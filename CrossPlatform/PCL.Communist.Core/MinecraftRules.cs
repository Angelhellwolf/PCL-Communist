using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PCL.Communist.Core;

public static class MinecraftRules
{
    public static bool Allows(JsonNode? rules, PlatformInfo platform, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is not JsonArray entries) return true;
        var allowed = false;
        foreach (var entry in entries)
        {
            if (entry is null) continue;
            var os = entry["os"];
            if (os?["name"] is JsonNode name && name.GetValue<string>() != platform.MinecraftOs) continue;
            if (os?["arch"] is JsonNode arch && !Match(arch.GetValue<string>(), platform.MinecraftArch)) continue;
            if (os?["version"] is JsonNode version && !Match(version.GetValue<string>(), platform.Version)) continue;
            if (entry["features"] is JsonObject required && required.Any(f =>
                (features?.GetValueOrDefault(f.Key) ?? false) != (f.Value?.GetValue<bool>() ?? false))) continue;
            allowed = entry["action"]?.GetValue<string>() == "allow";
        }
        return allowed;
    }

    private static bool Match(string pattern, string value) =>
        Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string? NativeClassifier(JsonNode library, PlatformInfo platform)
    {
        if (platform.IsMobile) throw new PlatformNotSupportedException("桌面原生库不适用于移动端。");
        var classifier = library["natives"]?[platform.MinecraftOs]?.GetValue<string>();
        return classifier?.Replace("${arch}", platform.Architecture == System.Runtime.InteropServices.Architecture.X86 ? "32" : "64");
    }

    public static void ValidateNativeArchitecture(JsonObject metadata, PlatformInfo platform)
    {
        if (platform.IsMobile || platform.Architecture != System.Runtime.InteropServices.Architecture.Arm64) return;
        foreach (var library in metadata["libraries"]!.AsArray())
        {
            if (library is null || !Allows(library["rules"], platform)) continue;
            var classifier = NativeClassifier(library, platform);
            if (classifier is null || classifier.Contains("arm64", StringComparison.OrdinalIgnoreCase) || classifier.Contains("aarch64", StringComparison.OrdinalIgnoreCase)) continue;
            throw new PlatformNotSupportedException(platform.Kind == PlatformKind.MacOS
                ? "此旧版本仅提供 Intel Mac 原生库，请使用 Intel 构建与 Intel Java（需要 Rosetta），或选择提供 Apple Silicon 原生库的版本。"
                : "此版本的旧式原生库没有标明 ARM64 支持，请使用匹配的 x64 构建与 Java，或选择提供 ARM64 原生库的版本。");
        }
    }
}
