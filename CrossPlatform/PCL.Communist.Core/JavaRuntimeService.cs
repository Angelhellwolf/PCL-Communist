using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PCL.Communist.Core;

public sealed record JavaRuntime(string Executable, int Major, string Architecture);

public sealed class JavaRuntimeService(DownloadClient downloads, LauncherPaths paths)
{
    public const string IndexUrl = "https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    public static string RuntimePlatform(PlatformInfo platform) => (platform.Kind, platform.Architecture) switch
    {
        (PlatformKind.Windows, Architecture.X64) => "windows-x64",
        (PlatformKind.Windows, Architecture.X86) => "windows-x86",
        (PlatformKind.Windows, Architecture.Arm64) => "windows-arm64",
        (PlatformKind.MacOS, Architecture.X64) => "mac-os",
        (PlatformKind.MacOS, Architecture.Arm64) => "mac-os-arm64",
        (PlatformKind.Linux, Architecture.X64) => "linux",
        (PlatformKind.Linux, Architecture.X86) => "linux-i386",
        _ => throw new PlatformNotSupportedException("此平台需要单独提供匹配架构的 Java 运行时。")
    };

    public async Task<JavaRuntime> ResolveAsync(JsonObject metadata, PlatformInfo platform, string? configuredJava,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (platform.IsMobile) throw new PlatformNotSupportedException("移动端 Java 需要原生运行时接入。" );
        var major = metadata["javaVersion"]?["majorVersion"]?.GetValue<int>() ?? 8;
        if (!string.IsNullOrWhiteSpace(configuredJava))
        {
            var configured = await ProbeAsync(configuredJava, cancellationToken);
            if (configured is null || configured.Major != major || !MatchesArchitecture(configured, platform))
                throw new InvalidOperationException($"所选 Java 需要为 {platform.MinecraftArch} 架构的 Java {major}。");
            return configured;
        }
        foreach (var candidate in Candidates(platform))
        {
            var found = await ProbeAsync(candidate, cancellationToken);
            if (found?.Major == major && MatchesArchitecture(found, platform)) return found;
        }
        progress?.Report(new($"准备 Java {major} · {platform.DisplayName}…", 0, 1));
        var runtimePlatform = RuntimePlatform(platform);
        var index = await downloads.GetJsonAsync(IndexUrl, cancellationToken);
        var component = metadata["javaVersion"]?["component"]?.GetValue<string>() ?? "jre-legacy";
        var entry = index[runtimePlatform]?[component]?.AsArray().FirstOrDefault()
            ?? throw new PlatformNotSupportedException($"官方没有 {platform.DisplayName} 的 Java {major}。请安装对应 Java 并在界面指定路径。");
        var root = LauncherPaths.SafeChild(paths.Runtimes, runtimePlatform + "/" + component);
        var manifestPath = Path.Combine(root, "runtime-manifest.json");
        await downloads.FetchAsync(MinecraftInstaller.Artifact(entry["manifest"]!, manifestPath), cancellationToken);
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken))!["files"]!.AsObject();
        var files = manifest.Where(f => f.Value?["type"]?.GetValue<string>() == "file").ToArray();
        var completed = 0;
        await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken },
            async (file, token) =>
            {
                var target = LauncherPaths.SafeChild(root, file.Key);
                await downloads.FetchAsync(MinecraftInstaller.Artifact(file.Value!["downloads"]!["raw"]!, target), token);
                if (!OperatingSystem.IsWindows() && file.Value["executable"]?.GetValue<bool>() == true)
                    File.SetUnixFileMode(target, File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                progress?.Report(new($"安装 Java {major}…", Interlocked.Increment(ref completed), files.Length));
            });
        // Resolve only links inside the verified runtime. Copying these small files also works without Windows symlink privileges.
        foreach (var file in manifest.Where(f => f.Value?["type"]?.GetValue<string>() == "link"))
        {
            var target = LauncherPaths.SafeChild(root, file.Key);
            var source = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, file.Value!["target"]!.GetValue<string>()));
            if (!source.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Java 运行时的链接超出安装目录。");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(source))
            {
                if (!Directory.Exists(target)) Directory.CreateSymbolicLink(target, source);
            }
            else File.Copy(source, target, true);
        }
        var result = await ProbeAsync(Path.Combine(root, "bin", platform.JavaExecutable), cancellationToken)
            ?? await ProbeAsync(Path.Combine(root, "jre.bundle", "Contents", "Home", "bin", platform.JavaExecutable), cancellationToken);
        if (result?.Major != major || !MatchesArchitecture(result, platform)) throw new IOException("下载的 Java 无法正常运行，或架构不匹配。" );
        return result;
    }

    public static bool MatchesArchitecture(JavaRuntime runtime, PlatformInfo platform) => platform.Architecture switch
    {
        Architecture.X64 => runtime.Architecture is "amd64" or "x86_64",
        Architecture.Arm64 => runtime.Architecture is "aarch64" or "arm64",
        Architecture.X86 => runtime.Architecture is "x86" or "i386" or "i686",
        _ => false
    };

    private IEnumerable<string> Candidates(PlatformInfo platform)
    {
        if (Directory.Exists(paths.Runtimes))
            foreach (var file in Directory.EnumerateFiles(paths.Runtimes, platform.JavaExecutable, SearchOption.AllDirectories)) yield return file;
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome)) yield return Path.Combine(javaHome, "bin", platform.JavaExecutable);
        yield return platform.JavaExecutable;
        var parent = platform.Kind == PlatformKind.MacOS ? "/Library/Java/JavaVirtualMachines" : "/usr/lib/jvm";
        if (!Directory.Exists(parent)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(parent))
            yield return Path.Combine(directory, platform.Kind == PlatformKind.MacOS ? "Contents/Home/bin/java" : "bin/java");
    }

    public static async Task<JavaRuntime?> ProbeAsync(string executable, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            start.ArgumentList.Add("-XshowSettings:properties");
            start.ArgumentList.Add("-version");
            process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text = await stdout + "\n" + await stderr;
            if (process.ExitCode != 0) return null;
            var version = Regex.Match(text, @"java\.version\s*=\s*(?:1\.)?(\d+)", RegexOptions.None, TimeSpan.FromSeconds(1));
            var arch = Regex.Match(text, @"os\.arch\s*=\s*(\S+)", RegexOptions.None, TimeSpan.FromSeconds(1));
            return version.Success && arch.Success ? new(executable, int.Parse(version.Groups[1].Value), arch.Groups[1].Value) : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (IOException) { return null; }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited) process.Kill(true);
                process.Dispose();
            }
        }
    }
}
