using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Protocol.Types;

// Prompts:
// Record startup trace of a .NET 9 Android app on Android device using MobileCompanionForPerformanceTool

[McpServerToolType]
public static class MobileCompanionForPerformanceTool
{
    [McpServerTool, Description("Record startup trace of a .NET 9 Android app on Android device using MobileCompanionForPerformanceTool")]
    public static List<ChatMessage> RecordStartupTraceOnAndroid()
    {
        var instructions = @"
You are a MobileCompanionForPerformanceTool assistant that helps the user to record startup trace on Android.
Here are the instructions you will follow:
1. Configure android project from the current workspace for tracing via MobileCompanionForPerformanceTool. If the tool succeeds continue to step 2.
2. Clean android project for tracing via MobileCompanionForPerformanceTool. If the tool succeeds continue to step 3.
3. Build android project for tracing via MobileCompanionForPerformanceTool. If the tool succeeds continue to step 4.
4. Run tracing on the android device via MobileCompanionForPerformanceTool. If the tool succeeds continue to step 5.
5. Ask the user to specify the number of methods to list from the startup trace. If the user provides a valid input continue to step 6.
6. List the methods from the startup trace using MobileCompanionForPerformanceTool. If the tool succeeds continue to step 7.
7. Say to the user that the process is finished.
Always start from step 1.
If any step fails, do not continue to the next step, instead, ask the user to try again from step 1.
For each step say to the user what are you about to do, but do not specify the step number.
Always display the output of the tool to the user.
";
        return new List<ChatMessage>() {
            new ChatMessage(ChatRole.Assistant, $"Lets follow the instructions step by step."),
            new ChatMessage(ChatRole.System, $"{instructions}"),
        };
    }

    [McpServerTool, Description("Configure android project for tracing via MobileCompanionForPerformanceTool")]
    public static List<ChatMessage> ConfigureAndroidProjectForTracing(string csprojPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(csprojPath) || !csprojPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Error: The provided path is not a valid .NET 9 Android C# project file: {csprojPath}");

            var projectDir = Path.GetDirectoryName(csprojPath) ?? 
                throw new Exception($"Error: Unable to determine the directory for the project file: {csprojPath}");

            // configure the project for tracing
            var response = ProjectUtils.AddAndroidEnvironmentFileForTracing(projectDir);
            Console.WriteLine(response);

            return new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, $"SUCCESS: {response}"),
            };
        }
        catch (Exception ex)
        {
            return new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, $"Error: {ex.Message}")
            };
        }
    }

    [McpServerTool, Description("Clean android project for tracing via MobileCompanionForPerformanceTool")]
    public static async Task<List<ChatMessage>> CleanAndroidProjectForTracing([Description("Input C# project file")]string csprojPath)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(csprojPath) ?? 
                throw new Exception($"Error: Unable to determine the directory for the project file: {csprojPath}");
            var formattedTime = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var cleanBinlogPath = Path.Combine(projectDir, $"clean_{formattedTime}.binlog");
            var cleanProcess = await ProjectUtils.DotnetClean(projectDir, csprojPath, cleanBinlogPath, args: string.Empty);
            if (cleanProcess.Process.ExitCode != 0)
            {
                var msg = $"[dotnet clean error]: {cleanProcess.StandardError}";
                Console.WriteLine(msg);
                throw new Exception(msg);
            }
            Console.WriteLine($"[dotnet clean]: {cleanProcess.StandardOutput}");

            return new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, $"SUCCESS: The project cleand successfully. The binlog is available at: {cleanBinlogPath}."),
            };
        }
        catch (Exception ex)
        {
            return new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, $"Error: {ex.Message}")
            };
        }
    }

    [McpServerTool, Description("Build android project for tracing via MobileCompanionForPerformanceTool")]
    public static async Task<List<ChatMessage>> BuildAndroidProjectForTracing([Description("Input C# project file")]string csprojPath)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(csprojPath) ?? 
                throw new Exception($"Error: Unable to determine the directory for the project file: {csprojPath}");
            var formattedTime = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var buildBinlogPath = Path.Combine(projectDir, $"build_{formattedTime}.binlog");

            // build the project
            var buildProcess = await ProjectUtils.DotnetBuild(projectDir, csprojPath, buildBinlogPath, args: ProjectUtils.CommonDotnetBuildArguments);
            if (buildProcess.Process.ExitCode != 0)
            {
                var msg = $"[dotnet build error]: {buildProcess.StandardError}";
                Console.WriteLine(msg);
                throw new Exception(msg);
            }
            Console.WriteLine($"[dotnet build]: {buildProcess.StandardOutput}");

            return new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, $"SUCCESS: The project build successfully. The binlog is available at: {buildBinlogPath}."),
            };
        }
        catch (Exception ex)
        {
            return new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, $"Error: {ex.Message}")
            };
        }
    }

    [McpServerTool, Description("Run tracing on the android device via MobileCompanionForPerformanceTool")]
    public static async Task<List<ChatMessage>> RunTracing([Description("Input C# project file")]string csprojPath)
    {
        try
        {
            var projectDir = Path.GetDirectoryName(csprojPath) ?? 
                throw new Exception($"Error: Unable to determine the directory for the project file: {csprojPath}");
            var formattedTime = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var installBinLogPath = Path.Combine(projectDir, $"install_{formattedTime}.binlog");
            
            // get the package name and activity name
            var (packageName, activityName) = ProjectUtils.GetPackageName(projectDir, csprojPath);
            Console.WriteLine($"Resolved: package name: {packageName} and activity name: {activityName}");

            // install the app
            var installProcess = await ProjectUtils.DotnetBuild(projectDir, csprojPath, installBinLogPath, args: $"{ProjectUtils.CommonDotnetBuildArguments} -t:Install");
            if (installProcess.Process.ExitCode != 0)
            {
                var msg = $"[dotnet install error]: {installProcess.StandardError}";
                Console.WriteLine(msg);
                throw new Exception(msg);
            }
            Console.WriteLine($"[dotnet install]: {installProcess.StandardOutput}");
                        
            var traceFilePath = Path.Combine(projectDir, $"startup_trace_{formattedTime}.nettrace");
            // Start the trace process first
            var traceProcessTask = ProjectUtils.DotnetTrace(projectDir, csprojPath, traceFilePath, args: ProjectUtils.CommonDotnetTraceArguments);
            // Start adb process to run the app
            var adbProcessTask = ProjectUtils.AdbRunApp(projectDir, packageName, activityName, args: "-S");
            // Wait for both tasks to complete
            await Task.WhenAll(adbProcessTask, traceProcessTask);
            
            // Now extract the actual process results from the tasks
            var adbProcess = adbProcessTask.Result;
            if (adbProcess.Process.ExitCode != 0)
            {
                var msg = $"[adb run error]: {adbProcess.StandardError}";
                Console.WriteLine(msg);
                throw new Exception(msg);
            }
            Console.WriteLine($"[adb run]: {adbProcess.StandardOutput}");

            var traceProcess = traceProcessTask.Result;
            if (traceProcess.Process.ExitCode != 0)
            {
                var msg = $"[dotnet trace error]: {traceProcess.StandardError}";
                Console.WriteLine(msg);
                throw new Exception(msg);
            }
            Console.WriteLine($"[dotnet trace]: {traceProcess.StandardOutput}");

            if (!File.Exists(traceFilePath))
            {
                throw new Exception($"Error: The trace file was not created at: {traceFilePath}");
            }

            return new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, $"SUCCESS: The tracing ran successfully. The trace file is available at: {traceFilePath}."),
            };
        }
        catch (Exception ex)
        {
            return new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, $"Error: {ex.Message}")
            };
        }
    }

    [McpServerTool, Description("List methods from the startup trace using MobileCompanionForPerformanceTool")]
    public static List<ChatMessage> ListNMethods([Description("The last startup trace")]string startupTrace, [Description("The number of methods to list")]int numberOfMethods)
    {
        try
        {
            var result = NetTraceParser.Parse(startupTrace, numberOfMethods);
            return new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, $"SUCCESS: Here is the output: {result}"),
            };
        }
        catch (Exception ex)
        {
            return new List<ChatMessage>
            {
                new ChatMessage(ChatRole.Assistant, $"Error: {ex.Message}")
            };
        }
    }
}