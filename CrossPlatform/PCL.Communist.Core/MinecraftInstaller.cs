using System.IO.Compression;
using System.Text.Json.Nodes;

namespace PCL.Communist.Core;

public sealed record MinecraftVersion(string Id, string Type, string Url, string Sha1)
{
    public override string ToString() => Id;
}

public sealed class MinecraftInstaller(DownloadClient downloads, LauncherPaths paths)
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public async Task<IReadOnlyList<MinecraftVersion>> GetVersionsAsync(CancellationToken cancellationToken = default)
    {
        var manifest = await downloads.GetJsonAsync(ManifestUrl, cancellationToken);
        return manifest["versions"]!.AsArray().Select(node => new MinecraftVersion(
            node!["id"]!.GetValue<string>(), node["type"]!.GetValue<string>(),
            node["url"]!.GetValue<string>(), node["sha1"]!.GetValue<string>())).ToArray();
    }

    public async Task<JsonObject> InstallAsync(MinecraftVersion version, PlatformInfo platform,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(new("读取版本信息…", 0, 1));
        await downloads.FetchAsync(new(version.Url, paths.VersionJson(version.Id), version.Sha1), cancellationToken);
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(paths.VersionJson(version.Id), cancellationToken))!.AsObject();
        if (metadata["id"]?.GetValue<string>() != version.Id) throw new InvalidDataException("版本元数据的 ID 不一致。");
        return await InstallResourcesAsync(metadata, platform, progress, cancellationToken);
    }

    public async Task<JsonObject> RepairAsync(string versionId, PlatformInfo platform,
        IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(paths.VersionJson(versionId), cancellationToken))!.AsObject();
        if (metadata["id"]?.GetValue<string>() != versionId) throw new InvalidDataException("本地版本元数据的 ID 不一致。");
        return await InstallResourcesAsync(metadata, platform, progress, cancellationToken);
    }

    private async Task<JsonObject> InstallResourcesAsync(JsonObject metadata, PlatformInfo platform,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var id = metadata["id"]!.GetValue<string>();
        if (metadata["inheritsFrom"] is not null) throw new NotSupportedException("此移植版目前安装原版游戏；加载器继承版本尚待移植。");
        MinecraftRules.ValidateNativeArchitecture(metadata, platform);
        var files = new List<DownloadFile>();
        var nativeArchives = new List<(string File, string[] Exclude)>();
        files.Add(Artifact(metadata["downloads"]!["client"]!, Path.Combine(paths.VersionDirectory(id), id + ".jar")));

        // A mobile installation downloads portable resources only. Desktop natives are never substituted for a mobile runtime.
        if (!platform.IsMobile)
        {
            foreach (var library in metadata["libraries"]!.AsArray())
            {
                if (library is null || !MinecraftRules.Allows(library["rules"], platform)) continue;
                if (library["downloads"]?["artifact"] is JsonNode artifact)
                    files.Add(Artifact(artifact, LauncherPaths.SafeChild(paths.Libraries, artifact["path"]!.GetValue<string>())));
                var classifier = MinecraftRules.NativeClassifier(library, platform);
                if (classifier is null) continue;
                var native = library["downloads"]?["classifiers"]?[classifier]
                    ?? throw new PlatformNotSupportedException($"该版本未提供 {platform.DisplayName} 原生库：{library["name"]}");
                var path = LauncherPaths.SafeChild(paths.Libraries, native["path"]!.GetValue<string>());
                files.Add(Artifact(native, path));
                nativeArchives.Add((path, library["extract"]?["exclude"]?.AsArray().Select(e => e!.GetValue<string>()).ToArray() ?? ["META-INF/"]));
            }
        }

        if (metadata["assetIndex"] is JsonNode indexInfo)
        {
            var indexPath = LauncherPaths.SafeChild(Path.Combine(paths.Assets, "indexes"), indexInfo["id"]!.GetValue<string>() + ".json");
            await downloads.FetchAsync(Artifact(indexInfo, indexPath), cancellationToken);
            var index = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, cancellationToken))!;
            foreach (var asset in index["objects"]!.AsObject())
            {
                var hash = asset.Value!["hash"]!.GetValue<string>();
                if (hash.Length != 40 || hash.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("资源哈希格式不正确。");
                files.Add(new($"https://resources.download.minecraft.net/{hash[..2]}/{hash}",
                    LauncherPaths.SafeChild(Path.Combine(paths.Assets, "objects"), $"{hash[..2]}/{hash}"), hash, asset.Value["size"]!.GetValue<long>()));
            }
        }
        if (!platform.IsMobile && metadata["logging"]?["client"]?["file"] is JsonNode logging)
            files.Add(Artifact(logging, LauncherPaths.SafeChild(Path.Combine(paths.Assets, "log_configs"), logging["id"]!.GetValue<string>())));

        var uniqueFiles = files.DistinctBy(f => f.Path).ToArray();
        var completed = 0;
        await Parallel.ForEachAsync(uniqueFiles, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken },
            async (file, token) =>
            {
                await downloads.FetchAsync(file, token);
                progress?.Report(new($"下载资源 · {Path.GetFileName(file.Path)}", Interlocked.Increment(ref completed), uniqueFiles.Length));
            });
        var nativeDirectory = Path.Combine(paths.VersionDirectory(id), "natives", platform.Kind + "-" + platform.Architecture);
        foreach (var archive in nativeArchives) ExtractNatives(archive.File, nativeDirectory, archive.Exclude);
        await CopyLegacyAssetsAsync(metadata, cancellationToken);
        progress?.Report(new(platform.IsMobile ? "游戏资源已下载。移动端原生运行时尚未接入。" : "游戏安装完成。", uniqueFiles.Length, uniqueFiles.Length));
        return metadata;
    }

    private async Task CopyLegacyAssetsAsync(JsonObject metadata, CancellationToken cancellationToken)
    {
        var id = metadata["assetIndex"]?["id"]?.GetValue<string>();
        if (id is null) return;
        var index = JsonNode.Parse(await File.ReadAllTextAsync(LauncherPaths.SafeChild(Path.Combine(paths.Assets, "indexes"), id + ".json"), cancellationToken))!;
        var destinations = new List<string>();
        if (index["virtual"]?.GetValue<bool>() == true) destinations.Add(LauncherPaths.SafeChild(Path.Combine(paths.Assets, "virtual"), id));
        if (index["map_to_resources"]?.GetValue<bool>() == true) destinations.Add(Path.Combine(paths.Root, "resources"));
        foreach (var asset in index["objects"]!.AsObject())
        {
            if (destinations.Count == 0) break;
            var hash = asset.Value!["hash"]!.GetValue<string>();
            foreach (var destination in destinations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = LauncherPaths.SafeChild(destination, asset.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(LauncherPaths.SafeChild(Path.Combine(paths.Assets, "objects"), $"{hash[..2]}/{hash}"), target, true);
            }
        }
    }

    public static DownloadFile Artifact(JsonNode node, string path) => new(node["url"]!.GetValue<string>(), path,
        node["sha1"]?.GetValue<string>(), node["size"]?.GetValue<long>());

    public static void ExtractNatives(string archivePath, string destination, string[] exclude)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || exclude.Any(p => entry.FullName.StartsWith(p, StringComparison.Ordinal))) continue;
            var path = LauncherPaths.SafeChild(destination, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, true);
        }
    }
}
