using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PCL.Communist.Core;

public sealed record AccountSession(string Name, string Uuid, string AccessToken, string UserType, DateTimeOffset ExpiresAt)
{
    public static AccountSession Offline(string name)
    {
        if (!Regex.IsMatch(name, "^[A-Za-z0-9_]{3,16}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("离线名称应为 3–16 位字母、数字或下划线。");
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new(name, Convert.ToHexString(hash).ToLowerInvariant(), "0", "legacy", DateTimeOffset.MaxValue);
    }
}

public sealed record LaunchPlan(string Executable, string WorkingDirectory, IReadOnlyList<string> Arguments)
{
    public ProcessStartInfo CreateStartInfo()
    {
        var info = new ProcessStartInfo(Executable) { WorkingDirectory = WorkingDirectory, UseShellExecute = false };
        foreach (var argument in Arguments) info.ArgumentList.Add(argument);
        return info;
    }
}

public sealed record RuntimeCapability(bool CanLaunch, string Description);

// A future mobile backend must embed its JVM, native graphics surface and input bridge.
// It must never fall through to desktop Process.Start or report a successful launch without a running game.
public interface IGameRuntime
{
    RuntimeCapability Capability { get; }
    Task LaunchAsync(JsonObject metadata, AccountSession account, string? java, int memoryMb,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken);
}

public sealed class GameLauncher(PlatformInfo platform, LauncherPaths paths, JavaRuntimeService javaService) : IGameRuntime
{
    public RuntimeCapability Capability => platform.IsMobile
        ? new(false, "可管理版本、登录和下载游戏资源；本机游戏启动还需接入移动端 Java、图形与触控运行时。")
        : new(true, "原版游戏启动 · 自动选择对应系统和架构的 Java 与原生库");

    public async Task LaunchAsync(JsonObject metadata, AccountSession account, string? java, int memoryMb,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        if (!Capability.CanLaunch) throw new PlatformNotSupportedException(Capability.Description);
        var runtime = await javaService.ResolveAsync(metadata, platform, java, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var plan = BuildPlan(metadata, platform, paths, runtime.Executable, account, memoryMb);
        using var process = Process.Start(plan.CreateStartInfo()) ?? throw new IOException("未能创建游戏进程。");
        await Task.Delay(1500, cancellationToken);
        if (process.HasExited && process.ExitCode != 0)
            throw new IOException($"游戏进程提前退出（代码 {process.ExitCode}），请检查游戏目录内的日志。");
    }

    public static LaunchPlan BuildPlan(JsonObject metadata, PlatformInfo platform, LauncherPaths paths,
        string java, AccountSession account, int memoryMb)
    {
        if (platform.IsMobile) throw new PlatformNotSupportedException("移动端不能使用桌面启动命令。");
        if (account.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("登录已过期，请重新登录微软账号。");
        if (metadata["inheritsFrom"] is not null) throw new NotSupportedException("此移植版尚未支持加载器继承版本。");
        var id = metadata["id"]!.GetValue<string>();
        var libraries = metadata["libraries"]!.AsArray().Where(l => l is not null && MinecraftRules.Allows(l["rules"], platform)).ToArray();
        MinecraftRules.ValidateNativeArchitecture(metadata, platform);
        var classpath = libraries.Where(l => l?["downloads"]?["artifact"] is not null)
            .Select(l => LauncherPaths.SafeChild(paths.Libraries, l!["downloads"]!["artifact"]!["path"]!.GetValue<string>()))
            .Append(Path.Combine(paths.VersionDirectory(id), id + ".jar"));
        var assetId = metadata["assetIndex"]?["id"]?.GetValue<string>() ?? "legacy";
        var replacements = new Dictionary<string, string>
        {
            ["auth_player_name"] = account.Name, ["auth_uuid"] = account.Uuid,
            ["auth_access_token"] = account.AccessToken, ["auth_session"] = $"token:{account.AccessToken}:{account.Uuid}",
            ["user_type"] = account.UserType, ["user_properties"] = "{}", ["version_name"] = id,
            ["version_type"] = "PCL Communist", ["game_directory"] = paths.Root, ["assets_root"] = paths.Assets,
            ["assets_index_name"] = assetId, ["game_assets"] = LauncherPaths.SafeChild(Path.Combine(paths.Assets, "virtual"), assetId),
            ["natives_directory"] = Path.Combine(paths.VersionDirectory(id), "natives", platform.Kind + "-" + platform.Architecture),
            ["launcher_name"] = "PCL Communist", ["launcher_version"] = "0.1.0", ["classpath_separator"] = platform.ClasspathSeparator,
            ["library_directory"] = paths.Libraries, ["classpath"] = string.Join(platform.ClasspathSeparator, classpath),
            ["resolution_width"] = "1100", ["resolution_height"] = "700", ["clientid"] = MicrosoftLogin.ClientId, ["auth_xuid"] = ""
        };
        string Expand(string arg) => Regex.Replace(arg, @"\$\{([^}]+)\}", match =>
            replacements.TryGetValue(match.Groups[1].Value, out var value) ? value :
                throw new InvalidDataException($"未知的启动参数：{match.Value}"), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var arguments = new List<string> { $"-Xmx{Math.Clamp(memoryMb, 512, 32768)}M" };
        if (metadata["arguments"]?["jvm"] is JsonArray jvm) arguments.AddRange(ReadArguments(jvm, platform).Select(Expand));
        else arguments.AddRange(["-Djava.library.path=" + replacements["natives_directory"], "-cp", replacements["classpath"]]);
        if (platform.Kind == PlatformKind.MacOS && !arguments.Contains("-XstartOnFirstThread")) arguments.Add("-XstartOnFirstThread");
        if (metadata["logging"]?["client"] is JsonNode logging)
        {
            var path = LauncherPaths.SafeChild(Path.Combine(paths.Assets, "log_configs"), logging["file"]!["id"]!.GetValue<string>());
            arguments.Add(logging["argument"]!.GetValue<string>().Replace("${path}", path));
        }
        arguments.Add(metadata["mainClass"]!.GetValue<string>());
        if (metadata["arguments"]?["game"] is JsonArray game) arguments.AddRange(ReadArguments(game, platform).Select(Expand));
        else
        {
            // Legacy Mojang arguments contain quoted tokens but never require a shell.
            foreach (Match token in Regex.Matches(metadata["minecraftArguments"]!.GetValue<string>(), "\"([^\"]*)\"|([^\\s]+)", RegexOptions.None, TimeSpan.FromSeconds(1)))
                arguments.Add(Expand(token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value));
        }
        return new(java, paths.Root, arguments);
    }

    private static IEnumerable<string> ReadArguments(JsonArray arguments, PlatformInfo platform)
    {
        foreach (var argument in arguments)
        {
            if (argument is JsonValue value) yield return value.GetValue<string>();
            else if (argument is JsonObject conditional && MinecraftRules.Allows(conditional["rules"], platform))
            {
                if (conditional["value"] is JsonArray values)
                    foreach (var item in values) yield return item!.GetValue<string>();
                else yield return conditional["value"]!.GetValue<string>();
            }
        }
    }
}
