using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System.Text;

public class NetTraceParser
{
    // Adding SortMode enumeration to define different sorting methods
    public enum SortMode
    {
        SortBySize,
        SortByJitTime,
        SortByTimeToReach
    }

    public class ParseResult
    {
        public Dictionary<long, AssemblyLoadInfo> AssemblyLoadInfos { get; set; }
        public Dictionary<long, MethodInfo> MethodInfoDictionary { get; set; }
        public int TotalEvents { get; set; }
        public int AssemblyLoadEvents { get; set; }
        public int MethodDetailsEvents { get; set; }
    }

    public class AssemblyLoadInfo
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

    public class MethodInfo
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

    public static ParseResult Parse(string filePath)
    {
        // Use a temporary ETLX file for processing
        string etlxFilePath = Path.ChangeExtension(filePath, ".etlx");
        
        // Convert the nettrace file to ETLX format if needed
        if (filePath.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase))
        {
            TraceLog.CreateFromEventPipeDataFile(filePath, etlxFilePath);
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
        
        // Process all events in the trace
        source.Process();
        
        // Return the parsed data
        return new ParseResult
        {
            AssemblyLoadInfos = assemblyLoadInfos,
            MethodInfoDictionary = methodInfoDictionary,
            TotalEvents = totalEvents,
            AssemblyLoadEvents = assemblyLoadEvents,
            MethodDetailsEvents = methodDetailsEvents
        };
    }

    public static string ListTopMethods(string filePath, int topN, SortMode sortMode = SortMode.SortBySize)
    {
        var output = new StringBuilder();
        output.AppendLine($"Processing file: {filePath}");
        
        // Parse the trace file first
        var parseResult = Parse(filePath);
        
        // Print summary information
        output.AppendLine("Scan complete.");
        output.AppendLine($"Total events processed: {parseResult.TotalEvents}");
        output.AppendLine($"Assembly Load events found: {parseResult.AssemblyLoadEvents}");
        output.AppendLine($"Method Details events found: {parseResult.MethodDetailsEvents}");
        
        var methodInfoDictionary = parseResult.MethodInfoDictionary;
        
        // Find the longest full method name (including signature)
        int maxMethodNameLength = methodInfoDictionary.Values
            .Select(m => $"{m.MethodNamespace}.{m.MethodName}.{m.MethodSignature}".Length)
            .DefaultIfEmpty(50) // Default to 50 if collection is empty
            .Max();

        // Add some padding and enforce minimum width
        int methodNameColumnWidth = Math.Max(50, maxMethodNameLength + 2);

        // Calculate total width for the separator line
        int totalWidth = methodNameColumnWidth + 15 + 20 + 15 + 15; // Added 15 for JIT time

        // Sort methods based on the provided sort mode
        string sortDescription;
        IEnumerable<MethodInfo> sortedMethods;
        
        switch (sortMode)
        {
            case SortMode.SortBySize:
                sortDescription = "compiled size";
                sortedMethods = methodInfoDictionary.Values
                    .OrderByDescending(m => m.MethodSize);
                break;
            case SortMode.SortByJitTime:
                sortDescription = "JIT compilation time";
                sortedMethods = methodInfoDictionary.Values
                    .OrderByDescending(m => m.JitDuration);
                break;
            case SortMode.SortByTimeToReach:
                sortDescription = "time to reach";
                sortedMethods = methodInfoDictionary.Values
                    .OrderByDescending(m => m.TimeStampMs);
                break;
            default:
                sortDescription = "compiled size";
                sortedMethods = methodInfoDictionary.Values
                    .OrderByDescending(m => m.MethodSize);
                break;
        }

        // Select top N methods after sorting
        var topMethods = sortedMethods.Take(topN).ToList();

        // Display top N methods based on selected sorting
        output.AppendLine($"\nTop {topN} methods by {sortDescription}:");
        output.AppendLine(new string('-', totalWidth));

        // Format the header with dynamic width, putting method name last
        output.AppendLine(string.Format("{0,-15} {1,-20} {2,-15} {3,-15} {4}", 
            "IL Size (bytes)", "Method Size (bytes)", "Timestamp (ms)", "JIT Time (ms)", "Method Name"));
        output.AppendLine(new string('-', totalWidth));

        foreach (var method in topMethods)
        {
            string fullMethodName = $"{method.MethodNamespace}.{method.MethodName}.{method.MethodSignature}";
            output.AppendLine(string.Format("{0,-15} {1,-20} {2,-15:F2} {3,-15:F2} {4}", 
                method.ILSize, method.MethodSize, method.TimeStampMs, 
                method.JitDuration >= 0 ? method.JitDuration : 0, fullMethodName));
        }

        return output.ToString();
    }

    public static string DisplayMethodStats(string filePath, string methodName)
    {
        var output = new StringBuilder();
        output.AppendLine($"Processing file: {filePath}");
        output.AppendLine($"Looking for method: {methodName}");
        
        // Parse the trace file first
        var parseResult = Parse(filePath);
        
        // Print summary information
        output.AppendLine("Scan complete.");
        output.AppendLine($"Total events processed: {parseResult.TotalEvents}");
        output.AppendLine($"Assembly Load events found: {parseResult.AssemblyLoadEvents}");
        output.AppendLine($"Method Details events found: {parseResult.MethodDetailsEvents}");
        
        // Find all methods matching the provided method name
        var matchingMethods = parseResult.MethodInfoDictionary.Values
            .Where(m => (m.MethodName?.Contains(methodName, StringComparison.OrdinalIgnoreCase) ?? false) || 
                        (m.MethodNamespace?.Contains(methodName, StringComparison.OrdinalIgnoreCase) ?? false) || 
                        ($"{m.MethodNamespace}.{m.MethodName}".Contains(methodName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        
        if (matchingMethods.Count == 0)
        {
            output.AppendLine($"No methods found matching '{methodName}'.");
            return output.ToString();
        }
        
        // Find the longest full method name (including signature)
        int maxMethodNameLength = matchingMethods
            .Select(m => $"{m.MethodNamespace}.{m.MethodName}.{m.MethodSignature}".Length)
            .DefaultIfEmpty(50) // Default to 50 if collection is empty
            .Max();

        // Add some padding and enforce minimum width
        int methodNameColumnWidth = Math.Max(50, maxMethodNameLength + 2);

        // Calculate total width for the separator line
        int totalWidth = methodNameColumnWidth + 15 + 20 + 15 + 15; // Added 15 for JIT time
        
        output.AppendLine($"\nFound {matchingMethods.Count} methods matching '{methodName}':");
        output.AppendLine(new string('-', totalWidth));

        // Format the header with dynamic width, putting method name last
        output.AppendLine(string.Format("{0,-15} {1,-20} {2,-15} {3,-15} {4}", 
            "IL Size (bytes)", "Method Size (bytes)", "Timestamp (ms)", "JIT Time (ms)", "Method Name"));
        output.AppendLine(new string('-', totalWidth));

        foreach (var method in matchingMethods)
        {
            string fullMethodName = $"{method.MethodNamespace}.{method.MethodName}.{method.MethodSignature}";
            output.AppendLine(string.Format("{0,-15} {1,-20} {2,-15:F2} {3,-15:F2} {4}", 
                method.ILSize, method.MethodSize, method.TimeStampMs, 
                method.JitDuration >= 0 ? method.JitDuration : 0, fullMethodName));
        }

        return output.ToString();
    }
}