using System.Runtime.InteropServices;

namespace PCL.Communist.Core;

public enum PlatformKind { Windows, MacOS, Linux, Android, IOS }

public sealed record PlatformInfo(PlatformKind Kind, Architecture Architecture, string Version)
{
    public static PlatformInfo Current => new(
        OperatingSystem.IsAndroid() ? PlatformKind.Android :
        OperatingSystem.IsIOS() ? PlatformKind.IOS :
        OperatingSystem.IsMacOS() ? PlatformKind.MacOS :
        OperatingSystem.IsWindows() ? PlatformKind.Windows : PlatformKind.Linux,
        RuntimeInformation.ProcessArchitecture, Environment.OSVersion.Version.ToString());

    public bool IsMobile => Kind is PlatformKind.Android or PlatformKind.IOS;
    public string DisplayName => Kind switch
    {
        PlatformKind.MacOS => $"macOS · {(Architecture == Architecture.Arm64 ? "Apple Silicon" : "Intel")}",
        PlatformKind.IOS => "iOS / iPadOS",
        _ => $"{Kind} · {Architecture}"
    };
    public string MinecraftOs => Kind switch
    {
        PlatformKind.Windows => "windows",
        PlatformKind.MacOS => "osx",
        PlatformKind.Linux => "linux",
        _ => throw new PlatformNotSupportedException("移动端需要专用 Java、图形和输入运行时，不能使用桌面原生库。")
    };
    public string MinecraftArch => Architecture switch
    {
        Architecture.X64 => "x86_64", Architecture.X86 => "x86",
        Architecture.Arm64 => "aarch64", Architecture.Arm => "arm",
        _ => throw new PlatformNotSupportedException($"不支持的架构：{Architecture}")
    };
    public string ClasspathSeparator => Kind == PlatformKind.Windows ? ";" : ":";
    public string JavaExecutable => Kind == PlatformKind.Windows ? "java.exe" : "java";
}

public sealed class LauncherPaths
{
    public LauncherPaths(string root) => Root = Path.GetFullPath(root);
    public string Root { get; }
    public string Versions => Path.Combine(Root, "versions");
    public string Libraries => Path.Combine(Root, "libraries");
    public string Assets => Path.Combine(Root, "assets");
    public string Runtimes => Path.Combine(Root, "runtimes");
    public string VersionDirectory(string id) => SafeChild(Versions, id);
    public string VersionJson(string id) => Path.Combine(VersionDirectory(id), id + ".json");

    public static LauncherPaths Default => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCL Communist", "minecraft"));

    public static string SafeChild(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.StartsWith('/') || relative.Split('/').Any(p => p is "." or ".." or ""))
            throw new InvalidDataException($"不安全的相对路径：{relative}");
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("文件路径超出游戏目录。");
        return path;
    }
}
