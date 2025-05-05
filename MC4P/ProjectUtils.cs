using System.Diagnostics;
using System.Xml;

public static class ProjectUtils
{
    public static string EnvFileName = "MobileCompanionForPerformanceToolAndroidEnv.txt";
    public static string DotnetDiagnosticPortsEnv = "DOTNET_DiagnosticPorts=127.0.0.1:9000,suspend,connect";
    public static string DirectoryBuildPropsFileName = "Directory.Build.props";
    public static string BuildConfiguration = "Release";
    public static string CommonDotnetBuildArguments = $"-c {BuildConfiguration} -p:AndroidEnableProfiler=true -p:RunAOTCompilation=false -tl:false";
    public static string CommonDotnetTraceArguments = "--providers Microsoft-Windows-DotNETRuntime:0x1F000080018:5 --duration 00:00:00:15 --dsrouter android";

    public static Process CreateProcess(string fileName, string arguments, string workingDirectory)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return new Process { StartInfo = processStartInfo };
    }

    public static async Task<(string StandardOutput, string StandardError)> ExecuteProcessAsync(Process process)
    {
        Console.WriteLine($"Executing: {process.StartInfo.FileName} {process.StartInfo.Arguments}");
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await Task.WhenAll(outputTask, errorTask);

        return (outputTask.Result, errorTask.Result);
    }

    public static async Task<ProcessWrapper> DotnetBuild(string projectDir, string csprojPath, string binlogPath, string args)
    {
        var process = CreateProcess("dotnet", $"build \"{csprojPath}\" /bl:\"{binlogPath}\" {args}", projectDir);
        var (output, error) = await ExecuteProcessAsync(process);
        return ProcessWrapper.Create(process, output, error);
    }

    public static async Task<ProcessWrapper> DotnetClean(string projectDir, string csprojPath, string binlogPath, string args)
    {
        var process = CreateProcess("dotnet", $"clean \"{csprojPath}\" /bl:\"{binlogPath}\" {args}", projectDir);
        var (output, error) = await ExecuteProcessAsync(process);
        return ProcessWrapper.Create(process, output, error);
    }

    public static async Task<ProcessWrapper> DotnetTrace(string projectDir, string csprojPath, string traceFilePath, string args = "")
    {
        var process = CreateProcess("dotnet", $"trace collect --output \"{traceFilePath}\" {args}", projectDir);
        var (output, error) = await ExecuteProcessAsync(process);
        return ProcessWrapper.Create(process, output, error);
    }

    public static string AddAndroidEnvironmentFileForTracing(string projectDir)
    {
        var envFilePath = Path.Combine(projectDir, EnvFileName);
        // if the file exists assume everything is setup correctly
        if (!File.Exists(envFilePath))
        {
            File.WriteAllText(envFilePath, DotnetDiagnosticPortsEnv);
            var directoryBuildPropsFilePath = Path.Combine(projectDir, DirectoryBuildPropsFileName);
            if (File.Exists(directoryBuildPropsFilePath))
            {
                throw new Exception($"Error: {DirectoryBuildPropsFileName} file exists in the current directory. This setup is not supported.");
            }
            else
            {
                File.WriteAllText(directoryBuildPropsFilePath, @"
<Project>
<ItemGroup Condition=""'$(AndroidEnableProfiler)'=='true'"">
    <AndroidEnvironment Include="""+EnvFileName+@""" />
</ItemGroup>
</Project>
");
            }
            return $"Files: '{envFilePath}' and '{directoryBuildPropsFilePath}' created successfully!";
        }
        else
        {
            return $"Environment file '{envFilePath}' already exists.";
        }
    }

    public static async Task<ProcessWrapper> AdbRunApp(string projectDir, string packageName, string activityName, string args)
    {
        var process = CreateProcess("adb", $"shell am start -n {packageName}/{activityName} {args}", projectDir);
        var (output, error) = await ExecuteProcessAsync(process);
        return ProcessWrapper.Create(process, output, error);
    }

    public static (string, string) GetPackageName(string projectDir, string csprojPath)
    {
        // TODO: do not hard code obj directory, use info from project build
        var intermediateSearchDir = Path.Combine(projectDir, "obj", BuildConfiguration);
        var manifestFiles = Directory.GetFiles(intermediateSearchDir, "AndroidManifest.xml", SearchOption.AllDirectories)
            .Where(filePath => Path.GetFileName(Path.GetDirectoryName(filePath)) == "android")
            .FirstOrDefault();

        if (string.IsNullOrEmpty(manifestFiles))
            throw new FileNotFoundException("AndroidManifest.xml not found in the project directory.");

        return GetPackageAndActivityName(manifestFiles);
    }

    public static (string packageName, string activityName) GetPackageAndActivityName (string manifestPath)
    {
        Console.WriteLine ($"Parsing AndroidManifest.xml from: {manifestPath}");

        var doc = new XmlDocument ();
        doc.Load (manifestPath);

        var nsmgr = new XmlNamespaceManager (doc.NameTable);
        nsmgr.AddNamespace ("android", "http://schemas.android.com/apk/res/android");

        XmlNode? node = doc.DocumentElement?.SelectSingleNode ("//manifest", nsmgr);
        if (node?.Attributes == null)
            throw new Exception($"'manifest' element not found in {manifestPath}");

        string packageName = node.Attributes ["package"]?.Value ?? string.Empty;
        if (string.IsNullOrEmpty (packageName))
            throw new Exception($"'package' attribute not found on the 'manifest' element in {manifestPath}");

        string activityName = string.Empty;
        XmlNodeList? nodes = doc.DocumentElement?.SelectNodes ("//manifest/application/activity[@android:name]", nsmgr);
        if (nodes is null)
            throw new Exception($"No named activity nodes in {manifestPath}");

        foreach (XmlNode? activity in nodes)
        {
            if (activity is null)
                continue;

            XmlNode? intent = activity.SelectSingleNode ("./intent-filter/action[@android:name='android.intent.action.MAIN']", nsmgr);
            if (intent is null)
                continue;

            intent = activity.SelectSingleNode ("./intent-filter/category[@android:name='android.intent.category.LAUNCHER']", nsmgr);
            if (intent is null)
                continue;

            if (activity.Attributes is null)
                continue;

            activityName = activity.Attributes ["android:name"]?.Value ?? string.Empty;
            if (string.IsNullOrEmpty (activityName))
                throw new Exception($"Launcher activity has no 'android:name' attribute in {manifestPath}");
            
            break;
        }

        return (packageName, activityName);
    }
}

public class ProcessWrapper
{
    public required Process Process { get; set; }
    public required string StandardOutput { get; set; }
    public required string StandardError { get; set; }

    public static ProcessWrapper Create(Process process, string standardOutput, string standardError)
    {
        return new ProcessWrapper
        {
            Process = process,
            StandardOutput = standardOutput,
            StandardError = standardError
        };
    }
}