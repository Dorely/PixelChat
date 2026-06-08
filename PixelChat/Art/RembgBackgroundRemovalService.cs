using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PixelChat.Art;

public sealed class RembgBackgroundRemovalService(
    IHttpClientFactory httpClientFactory,
    IOptions<BackgroundRemovalOptions> options,
    ILogger<RembgBackgroundRemovalService> logger) : IBackgroundRemovalService
{
    private static readonly SemaphoreSlim SetupLock = new(1, 1);
    private static readonly IReadOnlyDictionary<string, string> DefaultUvChecksums = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["uv-x86_64-pc-windows-msvc.zip"] = "dd9d6d6554bfab265bfa98aa8e8a406c5c3a7b97582f93de1f4d48d9154a0395",
        ["uv-aarch64-pc-windows-msvc.zip"] = "e4f8e70eb21f0f4efd2eeb159ab289f9a16057d59881a4475758be4ce39bc8c5",
        ["uv-x86_64-unknown-linux-gnu.tar.gz"] = "74947fe2c03315cf07e82ab3acc703eddef01aba4d5232a98e4c6825ec116131",
        ["uv-aarch64-unknown-linux-gnu.tar.gz"] = "8c9d0f0ee98166ae6ab198747519ba6f25db29d185bd2ae5960ecebc91a5c22a",
        ["uv-x86_64-apple-darwin.tar.gz"] = "6b91ae3de155f51bd1f5b74814821c79f016a176561f252cd9ddfb976939af2e",
        ["uv-aarch64-apple-darwin.tar.gz"] = "2b25be1af546be330b340b0a76b99f989daa6d92678fdffb87438e661e9d88fb",
    };

    public async Task<BackgroundRemovalResult> RemoveBackgroundAsync(
        BackgroundRemovalRequest request,
        IProgress<BackgroundRemovalProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        if (!configured.Enabled)
            throw new InvalidOperationException("Local AI background removal is disabled.");

        var paths = SidecarPaths.Create(configured);
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(paths.TempRoot);

        var rembgExe = await EnsureSidecarAsync(paths, configured, progress, cancellationToken);
        var model = CleanModel(configured.ModelName);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(configured.TimeoutSeconds, 10, 3600));
        var tempDir = Path.Combine(paths.TempRoot, "export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            progress?.Report(new("model", $"Downloading {model} if needed..."));
            var inputPath = Path.Combine(tempDir, "input" + ExtensionForContentType(request.ContentType));
            var outputPath = Path.Combine(tempDir, "output.png");
            await File.WriteAllBytesAsync(inputPath, request.Data, cancellationToken);

            var arguments = new List<string> { "i", "-m", model };
            if (configured.AlphaMatting)
                arguments.Add("-a");
            arguments.Add(inputPath);
            arguments.Add(outputPath);

            progress?.Report(new("processing", $"Removing background with {model}..."));
            var command = await RunCommandAsync(
                rembgExe,
                arguments,
                tempDir,
                ProcessEnvironment(paths),
                timeout,
                cancellationToken);

            if (command.ExitCode != 0)
                throw new InvalidOperationException($"rembg failed with exit code {command.ExitCode}: {TrimCommandOutput(command)}");
            if (!File.Exists(outputPath))
                throw new InvalidOperationException($"rembg completed without writing an output PNG. {TrimCommandOutput(command)}");

            var output = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            progress?.Report(new("complete", $"Local AI background removal complete with {model}."));
            return new BackgroundRemovalResult(
                output,
                "image/png",
                "local-ai",
                model,
                $"Local AI background removed with {model}.");
        }
        finally
        {
            DeleteDirectoryInsideRoot(tempDir, paths.TempRoot);
        }
    }

    private async Task<string> EnsureSidecarAsync(
        SidecarPaths paths,
        BackgroundRemovalOptions configured,
        IProgress<BackgroundRemovalProgress>? progress,
        CancellationToken cancellationToken)
    {
        await SetupLock.WaitAsync(cancellationToken);
        try
        {
            progress?.Report(new("checking", "Checking local AI sidecar..."));
            Directory.CreateDirectory(paths.Downloads);
            Directory.CreateDirectory(paths.UvDirectory);
            Directory.CreateDirectory(paths.ModelCache);
            Directory.CreateDirectory(paths.HuggingFaceCache);
            Directory.CreateDirectory(paths.UvCache);

            var uvExe = await EnsureUvAsync(paths, configured, progress, cancellationToken);
            await EnsurePythonAsync(uvExe, paths, configured, progress, cancellationToken);
            return await EnsureRembgAsync(uvExe, paths, configured, progress, cancellationToken);
        }
        finally
        {
            SetupLock.Release();
        }
    }

    private async Task<string> EnsureUvAsync(
        SidecarPaths paths,
        BackgroundRemovalOptions configured,
        IProgress<BackgroundRemovalProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(paths.UvExecutable))
            return paths.UvExecutable;

        var platform = ResolveUvPlatform(configured);
        var archivePath = Path.Combine(paths.Downloads, platform.ArchiveName);
        var releaseBase = string.IsNullOrWhiteSpace(configured.UvReleaseBaseUrl)
            ? "https://releases.astral.sh/github/uv/releases"
            : configured.UvReleaseBaseUrl.TrimEnd('/');
        var url = $"{releaseBase}/download/{CleanRequired(configured.UvVersion, "uv version")}/{platform.ArchiveName}";

        progress?.Report(new("uv", $"Downloading uv {configured.UvVersion}..."));
        await DownloadFileAsync(url, archivePath, cancellationToken);
        await VerifySha256Async(archivePath, platform.Sha256, cancellationToken);

        progress?.Report(new("uv", "Installing managed uv..."));
        var extractDir = Path.Combine(paths.Downloads, "uv-extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractDir);
        try
        {
            if (platform.IsZip)
            {
                ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true);
            }
            else
            {
                await using var fileStream = File.OpenRead(archivePath);
                await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzipStream, extractDir, overwriteFiles: true);
            }

            var extractedUv = Directory
                .EnumerateFiles(extractDir, platform.ExecutableName, SearchOption.AllDirectories)
                .FirstOrDefault();
            if (extractedUv is null)
                throw new InvalidOperationException($"The uv archive did not contain {platform.ExecutableName}.");

            Directory.CreateDirectory(paths.UvDirectory);
            File.Copy(extractedUv, paths.UvExecutable, overwrite: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(paths.UvExecutable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            return paths.UvExecutable;
        }
        finally
        {
            DeleteDirectoryInsideRoot(extractDir, paths.Downloads);
        }
    }

    private async Task EnsurePythonAsync(
        string uvExe,
        SidecarPaths paths,
        BackgroundRemovalOptions configured,
        IProgress<BackgroundRemovalProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new("python", $"Installing managed Python {configured.PythonVersion} if needed..."));
        var setupTimeout = SetupTimeout(configured);
        var command = await RunCommandAsync(
            uvExe,
            ["python", "install", CleanRequired(configured.PythonVersion, "Python version"), "--managed-python"],
            paths.Root,
            ProcessEnvironment(paths),
            setupTimeout,
            cancellationToken);

        if (command.ExitCode != 0)
            throw new InvalidOperationException($"uv could not install Python {configured.PythonVersion}: {TrimCommandOutput(command)}");
    }

    private async Task<string> EnsureRembgAsync(
        string uvExe,
        SidecarPaths paths,
        BackgroundRemovalOptions configured,
        IProgress<BackgroundRemovalProgress>? progress,
        CancellationToken cancellationToken)
    {
        var expectedMarker = $"python={configured.PythonVersion};rembg={configured.RembgPackageVersion}";
        var rembgExe = RembgExecutable(paths.VenvDirectory);
        var markerPath = Path.Combine(paths.VenvDirectory, ".pixelchat-rembg-version");
        if (File.Exists(rembgExe)
            && File.Exists(markerPath)
            && string.Equals(await File.ReadAllTextAsync(markerPath, cancellationToken), expectedMarker, StringComparison.Ordinal))
        {
            return rembgExe;
        }

        if (Directory.Exists(paths.VenvDirectory))
            DeleteDirectoryInsideRoot(paths.VenvDirectory, paths.Root);

        progress?.Report(new("venv", "Creating local background-removal environment..."));
        var setupTimeout = SetupTimeout(configured);
        var createVenv = await RunCommandAsync(
            uvExe,
            ["venv", paths.VenvDirectory, "--python", CleanRequired(configured.PythonVersion, "Python version"), "--managed-python"],
            paths.Root,
            ProcessEnvironment(paths),
            setupTimeout,
            cancellationToken);
        if (createVenv.ExitCode != 0)
            throw new InvalidOperationException($"uv could not create the rembg environment: {TrimCommandOutput(createVenv)}");

        var venvPython = VenvPythonExecutable(paths.VenvDirectory);
        if (!File.Exists(venvPython))
            throw new InvalidOperationException("uv created the rembg environment without a Python executable.");

        progress?.Report(new("rembg", $"Installing rembg {configured.RembgPackageVersion}..."));
        var package = $"rembg[cpu,cli]=={CleanRequired(configured.RembgPackageVersion, "rembg version")}";
        var installRembg = await RunCommandAsync(
            uvExe,
            ["pip", "install", "--python", venvPython, package],
            paths.Root,
            ProcessEnvironment(paths),
            setupTimeout,
            cancellationToken);
        if (installRembg.ExitCode != 0)
            throw new InvalidOperationException($"uv could not install rembg: {TrimCommandOutput(installRembg)}");
        if (!File.Exists(rembgExe))
            throw new InvalidOperationException("rembg installed without creating a command-line executable.");

        await File.WriteAllTextAsync(markerPath, expectedMarker, cancellationToken);
        return rembgExe;
    }

    private async Task DownloadFileAsync(string url, string targetPath, CancellationToken cancellationToken)
    {
        var temporaryPath = targetPath + ".tmp";
        File.Delete(temporaryPath);
        try
        {
            using var client = httpClientFactory.CreateClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(temporaryPath);
            await source.CopyToAsync(target, cancellationToken);
            target.Close();
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task VerifySha256Async(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException($"Downloaded uv archive checksum mismatch. Expected {expectedSha256}, got {actual}.");
        }
    }

    private async Task<CommandResult> RunCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        foreach (var pair in environment)
            process.StartInfo.Environment[pair.Key] = pair.Value;

        logger.LogDebug(
            "Background removal sidecar command starting: file={FileName}, args={Arguments}",
            fileName,
            string.Join(' ', arguments.Select(SafeArgumentForLog)));

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            KillProcess(process);
            throw new TimeoutException($"Background removal command timed out after {timeout.TotalSeconds:N0} seconds.");
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        logger.LogDebug(
            "Background removal sidecar command finished: file={FileName}, exitCode={ExitCode}, elapsedMs={ElapsedMs}",
            fileName,
            process.ExitCode,
            stopwatch.ElapsedMilliseconds);
        return new CommandResult(process.ExitCode, stdout, stderr);
    }

    private static IReadOnlyDictionary<string, string> ProcessEnvironment(SidecarPaths paths) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["UV_CACHE_DIR"] = paths.UvCache,
            ["UV_PYTHON_INSTALL_DIR"] = paths.PythonInstallDirectory,
            ["UV_NO_MODIFY_PATH"] = "1",
            ["UV_NO_SYSTEM_CONFIG"] = "1",
            ["U2NET_HOME"] = paths.ModelCache,
            ["HF_HOME"] = paths.HuggingFaceCache,
            ["XDG_CACHE_HOME"] = paths.CacheRoot,
            ["PYTHONUTF8"] = "1",
        };

    private static UvPlatform ResolveUvPlatform(BackgroundRemovalOptions configured)
    {
        string archiveName;
        if (OperatingSystem.IsWindows())
        {
            archiveName = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "uv-aarch64-pc-windows-msvc.zip"
                : "uv-x86_64-pc-windows-msvc.zip";
        }
        else if (OperatingSystem.IsMacOS())
        {
            archiveName = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "uv-aarch64-apple-darwin.tar.gz"
                : "uv-x86_64-apple-darwin.tar.gz";
        }
        else if (OperatingSystem.IsLinux())
        {
            archiveName = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "uv-aarch64-unknown-linux-gnu.tar.gz"
                : "uv-x86_64-unknown-linux-gnu.tar.gz";
        }
        else
        {
            throw new PlatformNotSupportedException("Local AI background removal supports Windows, macOS, and Linux.");
        }

        var checksum = string.IsNullOrWhiteSpace(configured.UvArchiveSha256)
            ? DefaultUvChecksums[archiveName]
            : configured.UvArchiveSha256.Trim();
        return new UvPlatform(
            archiveName,
            checksum,
            OperatingSystem.IsWindows() ? "uv.exe" : "uv",
            archiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static string RembgExecutable(string venvDirectory) =>
        OperatingSystem.IsWindows()
            ? Path.Combine(venvDirectory, "Scripts", "rembg.exe")
            : Path.Combine(venvDirectory, "bin", "rembg");

    private static string VenvPythonExecutable(string venvDirectory) =>
        OperatingSystem.IsWindows()
            ? Path.Combine(venvDirectory, "Scripts", "python.exe")
            : Path.Combine(venvDirectory, "bin", "python");

    private static TimeSpan SetupTimeout(BackgroundRemovalOptions configured) =>
        TimeSpan.FromSeconds(Math.Max(600, configured.TimeoutSeconds));

    private static string CleanModel(string? model) =>
        string.IsNullOrWhiteSpace(model) ? "birefnet-general" : model.Trim();

    private static string CleanRequired(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label} must be configured.");
        return value.Trim();
    }

    private static string ExtensionForContentType(string contentType) =>
        contentType.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png",
        };

    private static string TrimCommandOutput(CommandResult result)
    {
        var output = string.Join(
            Environment.NewLine,
            new[] { result.StandardError, result.StandardOutput }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(output))
            return "No output was returned.";
        return output.Length <= 2000 ? output : output[..2000] + "...";
    }

    private static string SafeArgumentForLog(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? '"' + value + '"' : value;

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void DeleteDirectoryInsideRoot(string path, string root)
    {
        if (!Directory.Exists(path))
            return;

        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to remove sidecar path outside its root: {fullPath}");
        }

        Directory.Delete(fullPath, recursive: true);
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record UvPlatform(string ArchiveName, string Sha256, string ExecutableName, bool IsZip);

    private sealed record SidecarPaths(
        string Root,
        string Downloads,
        string ToolsRoot,
        string UvDirectory,
        string UvExecutable,
        string VenvDirectory,
        string CacheRoot,
        string UvCache,
        string ModelCache,
        string HuggingFaceCache,
        string PythonInstallDirectory,
        string TempRoot)
    {
        public static SidecarPaths Create(BackgroundRemovalOptions configured)
        {
            var root = string.IsNullOrWhiteSpace(configured.SidecarRoot)
                ? DefaultSidecarRoot()
                : configured.SidecarRoot.Trim();
            root = Path.GetFullPath(Environment.ExpandEnvironmentVariables(root));

            var toolsRoot = Path.Combine(root, "tools");
            var uvDirectory = Path.Combine(toolsRoot, "uv", CleanRequired(configured.UvVersion, "uv version"));
            var uvExecutable = Path.Combine(uvDirectory, OperatingSystem.IsWindows() ? "uv.exe" : "uv");
            var cacheRoot = Path.Combine(root, "cache");
            return new SidecarPaths(
                root,
                Path.Combine(root, "downloads"),
                toolsRoot,
                uvDirectory,
                uvExecutable,
                Path.Combine(root, "venv", "rembg"),
                cacheRoot,
                Path.Combine(cacheRoot, "uv"),
                Path.Combine(root, "models", "u2net"),
                Path.Combine(root, "models", "huggingface"),
                Path.Combine(root, "python"),
                Path.Combine(root, "temp"));
        }

        private static string DefaultSidecarRoot()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                return Path.Combine(localAppData, "PixelChat", "background-removal");

            return Path.Combine(AppContext.BaseDirectory, ".pixelchat", "background-removal");
        }
    }
}
