using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

public class NetTraceParser
{
    class AssemblyLoadInfo
    {
        public int EventID { get; set; }
        public double TimeStampMs { get; set; }
        public string ProviderName { get; set; }
        public string ProcessName { get; set; }
        public int ProcessID { get; set; }
        public int ThreadID { get; set; }
        public long AssemblyID { get; set; }
        public long AppDomainID { get; set; }
        public string AssemblyName { get; set; }
    }

    class MethodInfo
    {
        public int EventID { get; set; }
        public double TimeStampMs { get; set; }
        public string ProviderName { get; set; }
        public string ProcessName { get; set; }
        public int ProcessID { get; set; }
        public int ThreadID { get; set; }
        public long MethodID { get; set; }
        public long ModuleID { get; set; }
        public string MethodName { get; set; }
        public string MethodNamespace { get; set; }
        public string MethodSignature { get; set; }
        public int ClrInstanceID { get; set; }
        public int ILSize { get; set; }
        public int MethodSize { get; set; }
        public double JitStartTime { get; set; }
        public double JitEndTime { get; set; }
        public ulong MethodStartAddress { get; set; }
        public string OptimizationTier { get; set; }
        
        public double JitDuration => JitEndTime > 0 && JitStartTime > 0 ? 
            JitEndTime - JitStartTime : -1;
    }

    public static string Parse(string filePath, int topN)
    {
        var output = new System.Text.StringBuilder();
        output.AppendLine($"Processing file: {filePath}");
        // Use a temporary ETLX file for processing
        string etlxFilePath = Path.ChangeExtension(filePath, ".etlx");
        
        // Convert the nettrace file to ETLX format if needed
        if (filePath.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
        {
            output.AppendLine("Converting .nettrace to ETLX format...");
            TraceLog.CreateFromEventPipeDataFile(filePath, etlxFilePath);
            output.AppendLine("Conversion complete.");
        }
        else
        {
            // Assume it's already in a compatible format
            etlxFilePath = filePath;
        }

        using var traceLog = TraceLog.OpenOrConvert(etlxFilePath);
        using var source = traceLog.Events.GetSource();

        int totalEvents = 0;
        int assemblyLoadEvents = 0;
        int methodDetailsEvents = 0;
        
        // Add specific handling for .NET runtime assembly load events
        var clrParser = new ClrTraceEventParser(source);
        
        // Handle Assembly Load events
        // Create a dictionary to store assembly load information indexed by AssemblyID
        var assemblyLoadInfos = new Dictionary<long, AssemblyLoadInfo>();
        
        clrParser.LoaderAssemblyLoad += delegate(AssemblyLoadUnloadTraceData data)
        {
            assemblyLoadEvents++;
            
            // Store assembly load information in dictionary using AssemblyID as key
            assemblyLoadInfos[data.AssemblyID] = new AssemblyLoadInfo
            {
                EventID = (int)data.ID,
                TimeStampMs = data.TimeStampRelativeMSec,
                ProviderName = data.ProviderName,
                ProcessName = data.ProcessName,
                ProcessID = data.ProcessID,
                ThreadID = data.ThreadID,
                AssemblyID = data.AssemblyID,
                AppDomainID = data.AppDomainID,
                AssemblyName = data.FullyQualifiedAssemblyName
            };
        };
        
        // Create a class to store method information
        var methodInfoDictionary = new Dictionary<long, MethodInfo>();
        
        // Track method JIT start times and collect IL sizes
        clrParser.MethodJittingStarted += delegate(MethodJittingStartedTraceData data)
        {
            totalEvents++;
            
            // Create or update method info in dictionary
            if (!methodInfoDictionary.TryGetValue(data.MethodID, out var methodInfo))
            {
                methodInfo = new MethodInfo { MethodID = data.MethodID };
                methodInfoDictionary[data.MethodID] = methodInfo;
            }
            
            // Record the start time of JIT compilation
            methodInfo.JitStartTime = data.TimeStampRelativeMSec;
            
            // Store IL size if available
            if (data.MethodILSize > 0)
            {
                methodInfo.ILSize = data.MethodILSize;
            }
        };
        
        // Handle Method JIT compilation events (MethodDetails)
        clrParser.MethodLoadVerbose += delegate(MethodLoadUnloadVerboseTraceData data)
        {
            methodDetailsEvents++;
            totalEvents++;
            
            // Create or update method info in dictionary
            if (!methodInfoDictionary.TryGetValue(data.MethodID, out var methodInfo))
            {
                methodInfo = new MethodInfo { MethodID = data.MethodID };
                methodInfoDictionary[data.MethodID] = methodInfo;
            }
            
            // Fill in all the method details
            methodInfo.EventID = (int)data.ID;
            methodInfo.TimeStampMs = data.TimeStampRelativeMSec;
            methodInfo.ProviderName = data.ProviderName;
            methodInfo.ProcessName = data.ProcessName;
            methodInfo.ProcessID = data.ProcessID;
            methodInfo.ThreadID = data.ThreadID;
            methodInfo.ModuleID = data.ModuleID;
            methodInfo.MethodName = data.MethodName;
            methodInfo.MethodNamespace = data.MethodNamespace;
            methodInfo.MethodSignature = data.MethodSignature;
            methodInfo.ClrInstanceID = data.ClrInstanceID;
            methodInfo.MethodSize = data.MethodSize;
            methodInfo.MethodStartAddress = data.MethodStartAddress;
            methodInfo.OptimizationTier = data.OptimizationTier.ToString();
            methodInfo.JitEndTime = data.TimeStampRelativeMSec;
        };
        
        output.AppendLine("Scanning for Assembly Load and Method Details events...");
        source.Process();
        output.AppendLine("Scan complete.");
        
        // Print summary information
        output.AppendLine($"Total events processed: {totalEvents}");
        output.AppendLine($"Assembly Load events found: {assemblyLoadEvents}");
        output.AppendLine($"Method Details events found: {methodDetailsEvents}");
        
        // Find the longest full method name (including signature)
        int maxMethodNameLength = methodInfoDictionary.Values
            .Select(m => $"{m.MethodNamespace}.{m.MethodName}.{m.MethodSignature}".Length)
            .DefaultIfEmpty(50) // Default to 50 if collection is empty
            .Max();

        // Add some padding and enforce minimum width
        int methodNameColumnWidth = Math.Max(50, maxMethodNameLength + 2);

        // Calculate total width for the separator line
        int totalWidth = methodNameColumnWidth + 15 + 20 + 15 + 15; // Added 15 for JIT time

        // Sort methods by timestamp and display top N
        output.AppendLine($"\nTop {topN} methods by compiled size:");
        output.AppendLine(new string('-', totalWidth));

        // Format the header with dynamic width, putting method name last
        output.AppendLine(string.Format("{0,-15} {1,-20} {2,-15} {3,-15} {4}", 
            "IL Size (bytes)", "Method Size (bytes)", "Timestamp (ms)", "JIT Time (ms)", "Method Name"));
        output.AppendLine(new string('-', totalWidth));

        var topMethods = methodInfoDictionary.Values
            .OrderByDescending(m => m.TimeStampMs)
            .Take(topN)
            .ToList();

        foreach (var method in topMethods)
        {
            string fullMethodName = $"{method.MethodNamespace}.{method.MethodName}.{method.MethodSignature}";
            output.AppendLine(string.Format("{0,-15} {1,-20} {2,-15:F2} {3,-15:F2} {4}", 
                method.ILSize, method.MethodSize, method.TimeStampMs, 
                method.JitDuration >= 0 ? method.JitDuration : 0, fullMethodName));
        }

        return output.ToString();
    }
}