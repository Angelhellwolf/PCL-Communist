using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PCL.Communist;
using PCL.Communist.Core;
using PCL.Communist.ViewModels;
using PCL.Communist.Views;

var root = Path.Combine(Path.GetTempPath(), "pcl-cross-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var windows = new PlatformInfo(PlatformKind.Windows, Architecture.X64, "10.0");
var mac = new PlatformInfo(PlatformKind.MacOS, Architecture.X64, "14.0");
var apple = new PlatformInfo(PlatformKind.MacOS, Architecture.Arm64, "14.0");
var linux = new PlatformInfo(PlatformKind.Linux, Architecture.X64, "6.1");
var android = new PlatformInfo(PlatformKind.Android, Architecture.Arm64, "14");
var ios = new PlatformInfo(PlatformKind.IOS, Architecture.Arm64, "17");
var count = 0;
void Check(bool value, string message)
{
    if (!value) throw new Exception("FAIL: " + message);
    count++;
    Console.WriteLine("PASS: " + message);
}
void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, message); return; }
    throw new Exception("FAIL: " + message);
}
async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T) { Check(true, message); return; }
    throw new Exception("FAIL: " + message);
}
JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();
string Hash(byte[] bytes) => Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
JsonObject DownloadNode(string url, byte[] data, string? path = null)
{
    var node = new JsonObject { ["url"] = url, ["sha1"] = Hash(data), ["size"] = data.Length };
    if (path is not null) node["path"] = path;
    return node;
}
byte[] NativeZip(string fileName)
{
    using var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
    {
        using (var writer = new StreamWriter(zip.CreateEntry(fileName).Open())) writer.Write("native-fixture");
        using (var writer = new StreamWriter(zip.CreateEntry("META-INF/ignored").Open())) writer.Write("excluded");
    }
    return stream.ToArray();
}

try
{
    Check(MinecraftRules.Allows(null, mac), "unconditional libraries are allowed");
    var rules = JsonNode.Parse("""[{"action":"allow"},{"action":"disallow","os":{"name":"windows"}}]""");
    Check(!MinecraftRules.Allows(rules, windows) && MinecraftRules.Allows(rules, mac), "rules use the last matching action");
    var armRules = JsonNode.Parse("""[{"action":"allow","os":{"name":"osx","arch":"aarch64"}}]""");
    Check(MinecraftRules.Allows(armRules, apple) && !MinecraftRules.Allows(armRules, mac), "Apple Silicon and Intel rules are distinct");
    var features = JsonNode.Parse("""[{"action":"allow","features":{"is_demo_user":true}}]""");
    Check(!MinecraftRules.Allows(features, linux), "demo and quick-play features default to disabled");
    var nativeDefinition = Json("""{"natives":{"windows":"natives-windows-${arch}","osx":"natives-osx","linux":"natives-linux"}}""");
    Check(MinecraftRules.NativeClassifier(nativeDefinition, mac) == "natives-osx", "macOS selects macOS natives");
    Check(MinecraftRules.NativeClassifier(nativeDefinition, windows) == "natives-windows-64", "Windows classifier architecture substitution");
    Throws<PlatformNotSupportedException>(() => MinecraftRules.NativeClassifier(nativeDefinition, android), "mobile cannot select desktop natives");
    Check(JavaRuntimeService.RuntimePlatform(apple) == "mac-os-arm64" && JavaRuntimeService.RuntimePlatform(mac) == "mac-os", "Java runtime keys distinguish Mac architectures");
    Throws<PlatformNotSupportedException>(() => JavaRuntimeService.RuntimePlatform(ios), "iOS cannot download a desktop JVM");
    Check(AccountSession.Offline("Notch").Uuid == "b50ad385829d3141a2167e7d7539ba7f", "offline UUID matches Java nameUUIDFromBytes");
    Throws<ArgumentException>(() => AccountSession.Offline("bad name;"), "offline player names are validated");

    foreach (var unsafePath in new[] { "../escape", "/absolute", "C:/windows", "dir\\file", "a/../../b" })
        Throws<InvalidDataException>(() => LauncherPaths.SafeChild(root, unsafePath), "reject path: " + unsafePath);

    var metadata = Json("""{"id":"fixture","mainClass":"net.minecraft.client.main.Main","libraries":[{"downloads":{"artifact":{"path":"example/lib/1/lib.jar"}}}],"arguments":{"jvm":["-Djava.library.path=${natives_directory}","-cp","${classpath}",{"rules":[{"action":"allow","os":{"name":"windows"}}],"value":"-Dos.name=Windows 10"}],"game":["--username","${auth_player_name}","--gameDir","${game_directory}",{"rules":[{"action":"allow","features":{"is_demo_user":true}}],"value":"--demo"}]}}""");
    var spacedPaths = new LauncherPaths(Path.Combine(root, "game with spaces"));
    var account = AccountSession.Offline("Player");
    var macPlan = GameLauncher.BuildPlan(metadata, mac, spacedPaths, "/jdk/bin/java", account, 2048);
    var windowsPlan = GameLauncher.BuildPlan(metadata, windows, spacedPaths, "java.exe", account, 2048);
    Check(macPlan.Arguments.Contains("-XstartOnFirstThread") && !macPlan.Arguments.Contains("-Dos.name=Windows 10"), "macOS starts Java on the first thread without Windows flags");
    Check(windowsPlan.Arguments.Contains("-Dos.name=Windows 10") && !windowsPlan.Arguments.Contains("-XstartOnFirstThread"), "Windows JVM arguments stay intact");
    Check(macPlan.Arguments[3].Contains(":") && windowsPlan.Arguments[3].Contains(";"), "classpath separators follow the target OS");
    Check(macPlan.CreateStartInfo().ArgumentList.Contains(spacedPaths.Root) && !macPlan.CreateStartInfo().UseShellExecute, "paths with spaces use argument lists without a shell");
    Check(!macPlan.Arguments.Contains("--demo") && !macPlan.Arguments.Any(a => a.Contains("${")), "conditional arguments and placeholders are resolved");
    Throws<PlatformNotSupportedException>(() => GameLauncher.BuildPlan(metadata, ios, spacedPaths, "java", account, 2048), "iOS is prevented from invoking desktop Java");
    Throws<InvalidOperationException>(() => GameLauncher.BuildPlan(metadata, mac, spacedPaths, "java", account with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }, 2048), "expired sessions require login");

    var bytes = Encoding.UTF8.GetBytes("verified content");
    var badBytes = Encoding.UTF8.GetBytes("corrupted mirror");
    var handler = new FakeHandler(request => request.RequestUri!.Host == "bmclapi2.bangbang93.com" ? badBytes : bytes);
    using (var http = new HttpClient(handler))
    {
        var downloader = new DownloadClient(http);
        var target = Path.Combine(root, "download.bin");
        await downloader.FetchAsync(new("https://libraries.minecraft.net/test", target, Hash(bytes), bytes.Length));
        Check(File.ReadAllBytes(target).SequenceEqual(bytes) && handler.Requests.Count == 2, "corrupt mirror falls back to verified official bytes");
        await downloader.FetchAsync(new("https://libraries.minecraft.net/test", target, Hash(bytes), bytes.Length));
        Check(handler.Requests.Count == 2, "valid cached downloads skip the network");
        Check(!Directory.EnumerateFiles(root, "*.part", SearchOption.AllDirectories).Any(), "downloads leave no partial files");
        Throws<InvalidDataException>(() => downloader.Sources("http://insecure.example/file").ToArray(), "insecure download URLs are rejected");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => downloader.FetchAsync(new("https://example.test/file", Path.Combine(root, "cancelled.bin")), cancel.Token), "cancelled downloads do not publish files");
    }

    var zipPath = Path.Combine(root, "natives.zip");
    await File.WriteAllBytesAsync(zipPath, NativeZip("liblwjgl.dylib"));
    var nativeRoot = Path.Combine(root, "native-output");
    MinecraftInstaller.ExtractNatives(zipPath, nativeRoot, ["META-INF/"]);
    Check(File.Exists(Path.Combine(nativeRoot, "liblwjgl.dylib")) && !Directory.Exists(Path.Combine(nativeRoot, "META-INF")), "native extraction honors exclusions");
    await File.WriteAllBytesAsync(zipPath, NativeZip("../escape.dll"));
    Throws<InvalidDataException>(() => MinecraftInstaller.ExtractNatives(zipPath, nativeRoot, []), "native ZIP cannot escape its destination");

    var assetBytes = Encoding.UTF8.GetBytes("asset-data");
    var assetHash = Hash(assetBytes);
    var indexBytes = Encoding.UTF8.GetBytes(new JsonObject { ["objects"] = new JsonObject { ["sound/example.ogg"] = new JsonObject { ["hash"] = assetHash, ["size"] = assetBytes.Length } }, ["virtual"] = true }.ToJsonString());
    var macZip = NativeZip("libfixture.dylib");
    var winZip = NativeZip("fixture.dll");
    var client = Encoding.UTF8.GetBytes("client-jar");
    var library = Encoding.UTF8.GetBytes("library-jar");
    var installMetadata = new JsonObject
    {
        ["id"] = "fixture", ["mainClass"] = "net.minecraft.client.main.Main", ["downloads"] = new JsonObject { ["client"] = DownloadNode("https://fixture.test/client", client) },
        ["assetIndex"] = DownloadNode("https://fixture.test/index", indexBytes),
        ["libraries"] = new JsonArray(new JsonObject { ["name"] = "fixture:natives:1", ["natives"] = nativeDefinition["natives"]!.DeepClone(), ["downloads"] = new JsonObject
        {
            ["artifact"] = DownloadNode("https://fixture.test/lib", library, "fixture/lib.jar"),
            ["classifiers"] = new JsonObject { ["natives-osx"] = DownloadNode("https://fixture.test/mac", macZip, "fixture/mac.jar"), ["natives-windows-64"] = DownloadNode("https://fixture.test/win", winZip, "fixture/win.jar") }
        } })
    };
    installMetadata["assetIndex"]!["id"] = "fixture";
    var metadataBytes = Encoding.UTF8.GetBytes(installMetadata.ToJsonString());
    var responses = new Dictionary<string, byte[]>
    {
        ["/metadata"] = metadataBytes, ["/client"] = client, ["/index"] = indexBytes,
        ["/lib"] = library, ["/mac"] = macZip, ["/win"] = winZip, [$"/{assetHash[..2]}/{assetHash}"] = assetBytes
    };
    var installHandler = new FakeHandler(request => responses[request.RequestUri!.AbsolutePath]);
    using (var http = new HttpClient(installHandler))
    {
        var downloader = new DownloadClient(http, false);
        var version = new MinecraftVersion("fixture", "release", "https://fixture.test/metadata", Hash(metadataBytes));
        var paths = new LauncherPaths(Path.Combine(root, "mac-install"));
        await new MinecraftInstaller(downloader, paths).InstallAsync(version, mac);
        Check(File.Exists(Path.Combine(paths.VersionDirectory("fixture"), "natives", "MacOS-X64", "libfixture.dylib")), "Mac installation extracts the correct native library");
        Check(!installHandler.Requests.Any(u => u.AbsolutePath == "/win"), "Mac installation never downloads Windows natives");
        Check(File.Exists(Path.Combine(paths.Assets, "virtual", "fixture", "sound", "example.ogg")), "legacy virtual assets are materialized");
        await File.WriteAllTextAsync(Path.Combine(paths.Libraries, "fixture", "lib.jar"), "corrupt cached library");
        await new MinecraftInstaller(downloader, paths).RepairAsync("fixture", mac);
        Check(File.ReadAllBytes(Path.Combine(paths.Libraries, "fixture", "lib.jar")).SequenceEqual(library), "repair revalidates and replaces corrupt local libraries");
        Throws<PlatformNotSupportedException>(() => MinecraftRules.ValidateNativeArchitecture(installMetadata, apple), "legacy Intel natives are rejected on Apple Silicon before installation");
        var beforeMobile = installHandler.Requests.Count;
        var mobilePaths = new LauncherPaths(Path.Combine(root, "mobile-install"));
        await new MinecraftInstaller(downloader, mobilePaths).InstallAsync(version, android);
        Check(!Directory.Exists(mobilePaths.Libraries) && !installHandler.Requests.Skip(beforeMobile).Any(u => u.AbsolutePath is "/win" or "/mac"), "mobile downloads resources without desktop libraries");
        var mobileRuntime = new GameLauncher(android, mobilePaths, new JavaRuntimeService(downloader, mobilePaths));
        Check(!mobileRuntime.Capability.CanLaunch, "mobile runtime capability reports unavailable honestly");
        await ThrowsAsync<PlatformNotSupportedException>(() => mobileRuntime.LaunchAsync(metadata, account, null, 2048, null, CancellationToken.None), "mobile launch fails before any Java execution");
    }

    var loginRequests = new List<string>();
    using (var http = new HttpClient(new FakeHandler(request =>
    {
        loginRequests.Add(request.RequestUri!.Host + request.RequestUri.AbsolutePath);
        var response = request.RequestUri.AbsolutePath switch
        {
            "/consumers/oauth2/v2.0/devicecode" => """{"user_code":"ABCD1234","device_code":"private-device","verification_uri":"https://microsoft.com/link","expires_in":600,"interval":1}""",
            "/consumers/oauth2/v2.0/token" => """{"access_token":"oauth-private"}""",
            "/user/authenticate" => """{"Token":"xbl-private"}""",
            "/xsts/authorize" => """{"Token":"xsts-private","DisplayClaims":{"xui":[{"uhs":"user-hash"}]}}""",
            "/authentication/login_with_xbox" => """{"access_token":"mc-private","expires_in":3600}""",
            "/entitlements/mcstore" => """{"items":[{"name":"game_minecraft"}]}""",
            "/minecraft/profile" => """{"name":"TestPlayer","id":"00000000000000000000000000000000"}""",
            _ => throw new Exception("Unexpected authentication request")
        };
        return Encoding.UTF8.GetBytes(response);
    })))
    {
        DeviceCodePrompt? received = null;
        var session = await new MicrosoftLogin(http).LoginAsync(prompt => received = prompt);
        Check(received?.Code == "ABCD1234" && session.Name == "TestPlayer" && session.UserType == "msa", "device-code login completes Xbox, entitlement and Minecraft profile exchanges");
        Check(loginRequests.IndexOf("api.minecraftservices.com/entitlements/mcstore") < loginRequests.IndexOf("api.minecraftservices.com/minecraft/profile"), "game ownership is checked before accepting the profile");
        Check(session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(50), "Microsoft sessions track token expiration");
    }

    if (args.Contains("--network"))
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var downloader = new DownloadClient(http, false);
        var installer = new MinecraftInstaller(downloader, new LauncherPaths(Path.Combine(root, "network")));
        var versions = await installer.GetVersionsAsync();
        var version = versions.Single(v => v.Id == "1.21.1");
        var versionPath = Path.Combine(root, "real-version.json");
        await downloader.FetchAsync(new(version.Url, versionPath, version.Sha1));
        var real = JsonNode.Parse(await File.ReadAllTextAsync(versionPath))!.AsObject();
        var clientPath = Path.Combine(root, "real-client.jar");
        await downloader.FetchAsync(MinecraftInstaller.Artifact(real["downloads"]!["client"]!, clientPath));
        Check(new FileInfo(clientPath).Length > 10000000, "real Minecraft 1.21.1 client download passes SHA1 validation");
        foreach (var platform in new[] { windows, mac, apple, linux })
        {
            var plan = GameLauncher.BuildPlan(real, platform, spacedPaths, "java", account, 2048);
            Check(plan.Arguments.Count > 15 && !plan.Arguments.Any(a => a.Contains("${")), "real launch arguments resolve for " + platform.DisplayName);
        }
        if (args.Contains("--java"))
        {
            var java = await new JavaRuntimeService(downloader, new LauncherPaths(Path.Combine(root, "runtime-test"))).ResolveAsync(real, PlatformInfo.Current, null);
            Check(java.Major == 21, "official Java 21 runtime installs and executes on the host");
        }
    }

    // Headless rendering exercises the same compiled XAML and bindings as every native entry point.
    AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont().SetupWithoutStarting();
    var output = Path.GetFullPath(args.FirstOrDefault(a => a.StartsWith("--screenshots="))?.Split('=', 2)[1] ?? Path.Combine(root, "screenshots"));
    Directory.CreateDirectory(output);
    foreach (var (platform, width, height, file) in new[] { (mac, 820, 1000, "desktop.png"), (android, 390, 1100, "android.png"), (ios, 390, 1100, "ios.png") })
    {
        var model = new MainViewModel(platform, new LauncherPaths(Path.Combine(root, file, "minecraft")));
        model.Versions.Add(new("1.21.1", "release", "https://example.test/version", ""));
        model.SelectedVersion = model.Versions[0];
        Check(model.LaunchCommand.CanExecute(null) == !platform.IsMobile, "launch button capability matches " + platform.DisplayName);
        Check(model.Themes.Count == 17, "all 17 theme choices are available on " + platform.DisplayName);
        var window = new Window { Width = width, Height = height, Content = new MainView { DataContext = model } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame() ?? throw new Exception("Could not render " + file);
        frame.Save(Path.Combine(output, file), PngBitmapEncoderOptions.Default);
        Check(frame.PixelSize.Width == width, "rendered responsive view: " + file);
        window.Close();
    }
    Console.WriteLine($"Completed {count} checks. Screenshots: {output}");
}
finally { Directory.Delete(root, true); }

sealed class FakeHandler(Func<HttpRequestMessage, byte[]> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] content;
        lock (Requests) { Requests.Add(request.RequestUri!); content = respond(request); }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }
}
