using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace PCL.Communist.Core;

public sealed record DownloadFile(string Url, string Path, string? Sha1 = null, long? Size = null);
public sealed record InstallProgress(string Message, int Completed, int Total)
{
    public double Percent => Total == 0 ? 0 : Completed * 100d / Total;
}

public sealed class DownloadClient(HttpClient http, bool preferMirror = true)
{
    public async Task<JsonObject> GetJsonAsync(string url, CancellationToken cancellationToken = default)
    {
        Exception? failure = null;
        foreach (var source in Sources(url))
        {
            try
            {
                using var response = await http.GetAsync(source, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
                    ?? throw new InvalidDataException("下载的元数据不是 JSON 对象。");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException)
            { failure = ex; }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            { failure = ex; }
        }
        throw new IOException("下载元数据失败，请检查网络后重试。", failure);
    }

    public async Task FetchAsync(DownloadFile file, CancellationToken cancellationToken = default)
    {
        if (await IsValidAsync(file, cancellationToken)) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file.Path)!);
        Exception? failure = null;
        foreach (var source in Sources(file.Url))
        {
            var temporary = file.Path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    await response.Content.CopyToAsync(output, cancellationToken);
                if (!await IsValidAsync(file with { Path = temporary }, cancellationToken))
                    throw new InvalidDataException("下载文件的长度或 SHA1 不匹配。");
                File.Move(temporary, file.Path, true);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            { failure = ex; }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            { failure = ex; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        throw new IOException($"下载失败：{System.IO.Path.GetFileName(file.Path)}", failure);
    }

    public static async Task<bool> IsValidAsync(DownloadFile file, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(file.Path)) return false;
        if (file.Size is long size && new FileInfo(file.Path).Length != size) return false;
        if (string.IsNullOrEmpty(file.Sha1)) return true;
        await using var stream = File.OpenRead(file.Path);
        var actual = Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken));
        return actual.Equals(file.Sha1, StringComparison.OrdinalIgnoreCase);
    }

    public IEnumerable<Uri> Sources(string url)
    {
        var original = new Uri(url);
        if (original.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("只允许 HTTPS 下载源。");
        if (preferMirror)
        {
            var mirrorPath = original.Host switch
            {
                "piston-meta.mojang.com" or "launchermeta.mojang.com" or "piston-data.mojang.com" => original.PathAndQuery,
                "libraries.minecraft.net" => "/maven" + original.PathAndQuery,
                "resources.download.minecraft.net" => "/assets" + original.PathAndQuery,
                _ => null
            };
            if (mirrorPath is not null) yield return new Uri("https://bmclapi2.bangbang93.com" + mirrorPath);
        }
        yield return original;
    }
}
