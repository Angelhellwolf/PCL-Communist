using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace PCL.Communist.Core;

public sealed record DeviceCodePrompt(string Code, Uri VerificationUri, string Message);

public sealed class MicrosoftLogin(HttpClient http)
{
    public static string ClientId => Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID") ?? "fe72edc2-3a6f-4280-90e8-e2beb64ce7e1";

    // Tokens live only in this session. A mobile secure-store implementation is required before persisting refresh tokens.
    public async Task<AccountSession> LoginAsync(Action<DeviceCodePrompt> prompt, CancellationToken cancellationToken = default)
    {
        var device = await FormAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
            new() { ["client_id"] = ClientId, ["scope"] = "XboxLive.signin offline_access" }, cancellationToken);
        prompt(new(device["user_code"]!.GetValue<string>(), new Uri(device["verification_uri"]!.GetValue<string>()), device["message"]?.GetValue<string>() ?? "请在浏览器完成登录。"));
        var interval = Math.Max(5, device["interval"]?.GetValue<int>() ?? 5);
        var expires = DateTimeOffset.UtcNow.AddSeconds(device["expires_in"]!.GetValue<int>());
        JsonObject? oauth = null;
        while (DateTimeOffset.UtcNow < expires)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            var result = await FormAsync("https://login.microsoftonline.com/consumers/oauth2/v2.0/token", new()
            {
                ["client_id"] = ClientId, ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = device["device_code"]!.GetValue<string>()
            }, cancellationToken, allowOAuthError: true);
            if (result["access_token"] is not null) { oauth = result; break; }
            switch (result["error"]?.GetValue<string>())
            {
                case "authorization_pending": continue;
                case "slow_down": interval += 5; continue;
                case "authorization_declined": throw new InvalidOperationException("你取消了微软登录。");
                case "expired_token": throw new InvalidOperationException("登录代码已过期，请重新登录。");
                default: throw new InvalidOperationException("微软登录失败：" + result["error"]?.GetValue<string>());
            }
        }
        if (oauth is null) throw new InvalidOperationException("登录代码已过期，请重新登录。");
        var xbl = await JsonAsync("https://user.auth.xboxlive.com/user/authenticate", new JsonObject
        {
            ["Properties"] = new JsonObject { ["AuthMethod"] = "RPS", ["SiteName"] = "user.auth.xboxlive.com", ["RpsTicket"] = "d=" + oauth["access_token"]!.GetValue<string>() },
            ["RelyingParty"] = "http://auth.xboxlive.com", ["TokenType"] = "JWT"
        }, cancellationToken);
        var xsts = await JsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new JsonObject
        {
            ["Properties"] = new JsonObject { ["SandboxId"] = "RETAIL", ["UserTokens"] = new JsonArray(xbl["Token"]!.GetValue<string>()) },
            ["RelyingParty"] = "rp://api.minecraftservices.com/", ["TokenType"] = "JWT"
        }, cancellationToken);
        var mc = await JsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", new JsonObject
        {
            ["identityToken"] = $"XBL3.0 x={xsts["DisplayClaims"]!["xui"]![0]!["uhs"]!.GetValue<string>()};{xsts["Token"]!.GetValue<string>()}"
        }, cancellationToken);
        var token = mc["access_token"]!.GetValue<string>();
        var entitlements = await AuthenticatedGetAsync("https://api.minecraftservices.com/entitlements/mcstore", token, cancellationToken);
        if (entitlements["items"] is not JsonArray items || items.Count == 0) throw new InvalidOperationException("此账号尚未购买 Minecraft Java，或 Game Pass 已到期。");
        var profile = await AuthenticatedGetAsync("https://api.minecraftservices.com/minecraft/profile", token, cancellationToken);
        return new(profile["name"]!.GetValue<string>(), profile["id"]!.GetValue<string>(), token, "msa",
            DateTimeOffset.UtcNow.AddSeconds(mc["expires_in"]!.GetValue<int>() - 60));
    }

    private async Task<JsonObject> FormAsync(string url, Dictionary<string, string> fields, CancellationToken token, bool allowOAuthError = false)
    {
        using var response = await http.PostAsync(url, new FormUrlEncodedContent(fields), token);
        if (!allowOAuthError) response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject();
    }

    private async Task<JsonObject> JsonAsync(string url, JsonObject body, CancellationToken token)
    {
        using var response = await http.PostAsync(url, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"账号服务返回 {(int)response.StatusCode}，请检查 Xbox 账号设置后重试。");
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject();
    }

    private async Task<JsonObject> AuthenticatedGetAsync(string url, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
    }
}
