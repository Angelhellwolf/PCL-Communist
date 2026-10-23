using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class Program {
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static Assembly launcher = null!;
    private static int checks;
    private static readonly List<string> report = new();
    private static Type T(string name) => launcher.GetType("PCL." + name, true)!;
    private static object? Field(string type, string name) => T(type).GetField(name, Static)!.GetValue(null);
    private static object? Call(string type, string name, params object?[] args) =>
        T(type).GetMethods(Static).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(null, args);
    private static object? CallOptional(string type, string name, params object?[] args) {
        var method = T(type).GetMethods(Static).Single(m => m.Name == name &&
            m.GetParameters()[0].ParameterType.IsInstanceOfType(args[0]));
        return method.Invoke(null, args.Concat(Enumerable.Repeat<object?>(Type.Missing, method.GetParameters().Length - args.Length)).ToArray());
    }
    private static void Set(string key, object value) => Call("Settings", "Set", key, value, null);
    private static object Control(object page, string name) =>
        page.GetType().GetProperty(name, Instance)?.GetValue(page) ??
        page.GetType().GetField(name, Instance)?.GetValue(page) ??
        throw new MissingMemberException(page.GetType().FullName, name);
    private static void Check(bool ok, string name) {
        if (!ok) throw new Exception("FAIL: " + name);
        checks++;
        report.Add("PASS: " + name);
    }
    private static void Pump(int milliseconds) {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until) {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }
    private static void Capture(FrameworkElement element, string path) {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) png.Save(stream);
    }
    [STAThread]
    private static void Main(string[] args) {
        string output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        string errorPath = Path.Combine(output, "smoke-error.txt");
        if (File.Exists(errorPath)) File.Delete(errorPath);
        var transcript = new StreamWriter(Path.Combine(output, "test-console.txt")) { AutoFlush = true };
        Console.SetOut(transcript);
        Console.SetError(transcript);
        try {
            launcher = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Check(!launcher.GetManifestResourceNames().Contains("PCL.RedBuildCredentials.json"), "Public executable excludes private build credentials");
            Check(launcher.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName == ".NETFramework,Version=v4.8",
                "Executable declares .NET Framework 4.8, including modern path handling");
            var app = (Application)Activator.CreateInstance(T("Application"), true)!;
            // Pumping WPF queues Application.Startup even in a host. Keep the host
            // isolated from the launcher's normal splash screen and startup path.
            var startup = (StartupEventHandler)Delegate.CreateDelegate(typeof(StartupEventHandler), app, T("Application").GetMethod("Application_Startup", Instance)!);
            app.Startup -= startup;
            T("Application").GetMethod("InitializeComponent")!.Invoke(app, null);
            // WPF rejects null through the setter. Only the test host clears the
            // backing field to prevent automatic navigation to FormMain.xaml.
            typeof(Application).GetField("_startupUri", Instance)!.SetValue(app, null);
            var unhandled = (DispatcherUnhandledExceptionEventHandler)Delegate.CreateDelegate(
                typeof(DispatcherUnhandledExceptionEventHandler), app,
                T("Application").GetMethod("Application_DispatcherUnhandledException", Instance)!);
            app.DispatcherUnhandledException -= unhandled;
            app.DispatcherUnhandledException += (_, error) => {
                File.WriteAllText(Path.Combine(output, "smoke-error.txt"), error.Exception.ToString());
                Console.Error.WriteLine(error.Exception);
                Environment.Exit(1);
            };
            var core = Assembly.Load("MeloongCore");
            core.GetType("MeloongCore.Main")!.GetMethod("Init")!.Invoke(null, new object[] { "PCL" });
            Assembly.Load("MeloongCore.Wpf").GetType("MeloongCore.Wpf.Main")!.GetMethod("Init")!.Invoke(null, null);
            Assembly.Load("PCLCS");
            using (var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Plain Craft Launcher 2;component/Images/Red/Splash.png")).Stream) {
                var splash = BitmapFrame.Create(resource, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Check(splash.PixelWidth == 256 && splash.PixelHeight == 256, "Hammer-and-sickle startup splash is embedded at 256 pixels");
            }
            using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(launcher.Location))
            using (var bitmap = icon!.ToBitmap()) {
                bitmap.Save(Path.Combine(output, "Executable-icon.png"), System.Drawing.Imaging.ImageFormat.Png);
                int gold = 0, red = 0;
                for (int x = 0; x < bitmap.Width; x++) for (int y = 0; y < bitmap.Height; y++) {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.R > 200 && pixel.G > 130 && pixel.B < 180) gold++;
                    if (pixel.R > 80 && pixel.G < 70 && pixel.B < 100) red++;
                }
                Check(gold > 5 && red > 5, "Executable icon contains the new red-and-gold emblem");
            }
            Check(((string)T("ModBase").GetField("VersionDisplay", Static)!.GetRawConstantValue()!).EndsWith("PCL Communist"), "Version text uses the requested product name");
            Check(launcher.GetCustomAttribute<AssemblyTitleAttribute>()?.Title == "PCL Communist" &&
                launcher.GetCustomAttribute<AssemblyProductAttribute>()?.Product == "PCL Communist", "Executable metadata uses PCL Communist");
            using (var licence = new StreamReader(launcher.GetManifestResourceStream("PCL.Licence.txt")!))
                Check(licence.ReadToEnd().Contains("PCL 分发有限许可"), "Distribution licence is embedded in the single executable");

            // Route every test setting to the isolated host directory, including registry settings.
            var entries = (IDictionary)T("Settings").GetField("Entries", Static)!.GetValue(null)!;
            foreach (DictionaryEntry item in entries) {
                object setting = item.Value;
                var source = setting.GetType().GetField("Source")!;
                if (source.GetValue(setting)!.ToString() == "Registry") source.SetValue(setting, Enum.Parse(source.FieldType, "Normal"));
            }
            Check((int)entries["UiLauncherTheme"]!.GetType().GetField("DefaultValue")!.GetValue(entries["UiLauncherTheme"])! == 15, "Soviet red is the fresh-install default");
            Check(!(bool)T("ModSecret").GetField("RestrictMcDownload", Static)!.GetRawConstantValue()!, "Minecraft downloads are unrestricted");
            Check(!string.IsNullOrWhiteSpace((string)Field("ModSecret", "OAuthClientId")!), "Microsoft public client ID is configured");
            Check((int)entries["ToolDownloadSource"]!.GetType().GetField("DefaultValue")!.GetValue(entries["ToolDownloadSource"])! == 0, "Mainland mirrors are preferred for files");
            Check((int)entries["ToolDownloadVersion"]!.GetType().GetField("DefaultValue")!.GetValue(entries["ToolDownloadVersion"])! == 0, "Mainland mirrors are preferred for manifests");

            bool Allowed(string url, string method = "GET", bool file = false) =>
                (bool)Call("RedPolicy", "IsNetworkAllowed", url, new HttpMethod(method), file)!;
            Check(Allowed("https://bmclapi2.bangbang93.com/mc/game/version_manifest.json"), "BMCLAPI manifest is allowed");
            Check(Allowed("https://piston-meta.mojang.com/v1/test.json"), "Mojang metadata fallback is allowed");
            Check(Allowed("https://cdn.example.org/pack.mrpack", file: true), "Custom pack file downloads are allowed");
            Check(Allowed("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", "POST"), "Microsoft device login is allowed");
            Check(Allowed("https://login.live.com/oauth20_token.srf", "POST"), "Microsoft token refresh is allowed");
            Check(Allowed("https://user.auth.xboxlive.com/user/authenticate", "POST"), "Xbox authentication is allowed");
            Check(Allowed("https://xsts.auth.xboxlive.com/xsts/authorize", "POST"), "XSTS authentication is allowed");
            Check(Allowed("https://api.minecraftservices.com/entitlements/mcstore"), "Minecraft ownership validation is allowed");
            Check(Allowed("https://api.minecraftservices.com/minecraft/profile"), "Authenticated Minecraft identity is allowed");
            Check(!Allowed("https://api.minecraftservices.com/minecraft/profile/skins", "POST"), "Skin uploads are blocked");
            Check(!Allowed("https://api.minecraftservices.com/minecraft/profile/capes/active", "PUT"), "Cape mutations are blocked");
            Check(!Allowed("https://sessionserver.mojang.com/session/minecraft/profile/id"), "Unrelated online skin lookups are blocked");
            Check(!Allowed("https://login.microsoftonline.com.evil.example/consumers/oauth2/v2.0/token", "POST"), "Lookalike authentication hosts are blocked");
            Check(!Allowed("https://pcl2-server-1253424809.file.myqcloud.com/ServerConfig.json", file: true), "Launcher config server is blocked even as a download");
            Check(!Allowed("https://ap-guangzhou.cls.tencentcs.com/track", file: true), "Telemetry is blocked even as a download");
            Check(!Allowed("https://example.org/Custom.xaml"), "Remote homepage requests are blocked");
            Check(!Allowed("https://auth.example.org/refresh", "POST"), "Third-party authentication is blocked");
            Check(!Allowed("ftp://example.org/pack.zip", file: true), "Unsupported URL schemes are rejected");
            Check(!Allowed("https://cdn.example.org/upload", "POST", true), "The file download channel cannot upload");
            Console.WriteLine("PASS: defaults and network policy");

            if (args.Skip(2).Contains("--download")) {
                var download = Task.Run(() => TestDownload(output));
                while (!download.IsCompleted) Pump(100);
                download.GetAwaiter().GetResult();
                File.WriteAllLines(Path.Combine(output, "download-results.txt"), report);
                Console.WriteLine("PASS: " + checks + " download and policy checks.");
                Environment.Exit(0);
            }

            Set("SystemLastVersionReg", 413);
            Set("UiLauncherTheme", 15);
            var window = (Window)Activator.CreateInstance(T("FormMain"))!;
            app.MainWindow = window;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000; window.Top = -10000;
            window.Width = 1000; window.Height = 650;
            window.Show();
            Pump(1000);
            Check(window.Title == "PCL Communist", "Main window uses the requested product name");
            Check(((Panel)Control(window, "PanMsg")).Children.Count == 0, "Startup reaches the main page without an agreement dialog");
            Call("ModSecret", "ThemeRefresh", 15);
            Check((int)Field("ModSecret", "ThemeNow")! == 15, "Default theme survives startup");
            Check(((FrameworkElement)Control(window, "CommunismBackdrop")).Visibility == Visibility.Visible, "Hammer-and-sickle background is visible");
            Check(((string)Call("ModMinecraft", "McSkinDownload", "https://textures.minecraft.net/texture/test")!).StartsWith("pack://"), "Online skins use bundled local fallback");
            Capture(window, Path.Combine(output, "Soviet-red-home.png"));

            var page = Activator.CreateInstance(T("PageSetupUI"))!;
            T("ModMain").GetField("FrmSetupUI", Static)!.SetValue(null, page);
            Call("ModSecret", "ThemeCheckAll", true);
            var ids = Enumerable.Range(0, 16).Concat(new[] { 42 }).ToArray();
            var colors = new HashSet<int>();
            foreach (int id in ids) {
                Check((bool)Call("ModSecret", "ThemeCheckOne", id)!, "Theme " + id + " is unlocked");
                Check(((UIElement)Control(page, "RadioLauncherTheme" + id)).IsEnabled, "Theme " + id + " is selectable");
                Call("ModSecret", "ThemeRefresh", id);
                Check((int)Field("ModSecret", "ThemeNow")! == id, "Theme " + id + " applies");
                colors.Add((int)Field("ModSecret", "ColorHue")!);
            }
            Check(colors.Count >= 13, "Theme selection changes actual colors");
            Check(!(bool)Call("ModSecret", "ThemeCheckOne", -1)!, "Invalid theme IDs are rejected");
            Set("UiLauncherHue", 123); Set("UiLauncherSat", 45);
            Set("UiLauncherLight", 30); Set("UiLauncherDelta", 10);
            Call("ModSecret", "ThemeRefresh", 14);
            Call("ModSecret", "ThemeRefresh", -1);
            Check((int)Field("ModSecret", "ColorHue")! == 123 && (int)Field("ModSecret", "ColorSat")! == 45 && (int)Field("ModSecret", "ColorLightAdjust")! == 10, "Custom HSL sliders affect the rendered palette");
            Call("ModSecret", "ThemeRefresh", 12);
            int hue = (int)Field("ModSecret", "ColorHue")!;
            Pump(100);
            Call("ModSecret", "ThemeRefresh", -1);
            Check((int)Field("ModSecret", "ColorHue")! != hue, "Rainbow theme changes over time");
            Call("ModSecret", "ThemeRefresh", 15);
            Call("ModSecret", "ThemeRefresh", 999);
            Check((int)Field("ModSecret", "ThemeNow")! == 15, "Invalid saved theme falls back to Soviet red");

            Set("UiLauncherTheme", 15);
            ((Border)Control(window, "PanMainRight")).Child = (UIElement)page;
            Pump(500);
            Capture(window, Path.Combine(output, "All-themes-unlocked.png"));
            var about = (UIElement)Activator.CreateInstance(T("PageOtherAbout"))!;
            ((Border)Control(window, "PanMainRight")).Child = about;
            Pump(200);
            Check(((TextBlock)Control(about, "TextBundledLicence")).Text.Contains("PCL 分发有限许可"), "About page shows bundled distribution licence without a sidecar file");
            File.WriteAllLines(Path.Combine(output, "smoke-results.txt"), report);
            Console.WriteLine("PASS: " + checks + " runtime checks; home and theme screenshots saved.");
            Environment.Exit(0);
        } catch (Exception error) {
            File.WriteAllText(Path.Combine(output, "smoke-error.txt"), error.ToString());
            Console.Error.WriteLine(error);
            Environment.Exit(1);
        }
    }

    private static void TestDownload(string output) {
        var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        Dictionary<string, object> Json(string url) => serializer.Deserialize<Dictionary<string, object>>(
            (string)CallOptional("ModNet", "NetRequestByClientRetry", url)!);
        Dictionary<string, object> Version(Dictionary<string, object> manifest) =>
            ((IEnumerable)manifest["versions"]).Cast<Dictionary<string, object>>().Single(v => (string)v["id"] == "1.21.1");
        var mirror = Json("https://bmclapi2.bangbang93.com/mc/game/version_manifest.json");
        Check(Version(mirror).ContainsKey("url"), "BMCLAPI manifest loads through compiled request engine");
        Console.WriteLine(report.Last());
        var official = Json("https://launchermeta.mojang.com/mc/game/version_manifest.json");
        var version = Version(official);
        Check(version.ContainsKey("url"), "Official manifest fallback is reachable");
        var sources = ((IEnumerable<string>)Call("ModDownload", "DlSourceLauncherOrMetaGet", version["url"])!).ToArray();
        Check(new Uri(sources.First()).Host == "bmclapi2.bangbang93.com" && sources.Last() == (string)version["url"], "Version metadata uses mirror first with official fallback");
        var metadata = Json(sources.First());
        var client = (Dictionary<string, object>)((Dictionary<string, object>)metadata["downloads"])["client"];
        var officialMetadata = Json((string)version["url"]);
        var officialClient = (Dictionary<string, object>)((Dictionary<string, object>)officialMetadata["downloads"])["client"];
        Check((string)client["sha1"] == (string)officialClient["sha1"], "Mirror client hash matches official metadata");
        string expectedHash = (string)officialClient["sha1"];
        long expectedSize = Convert.ToInt64(officialClient["size"]);
        var checkerType = Assembly.Load("MeloongCore").GetTypes().Single(t => t.Name == "FileChecker");
        object Checker() {
            var checker = Activator.CreateInstance(checkerType)!;
            checkerType.GetField("Hash")!.SetValue(checker, expectedHash);
            checkerType.GetField("ActualSize")!.SetValue(checker, expectedSize);
            return checker;
        }
        var urls = ((IEnumerable<string>)Call("ModDownload", "DlSourceLauncherOrMetaGet", officialClient["url"])!).ToArray();
        Check(new Uri(urls.First()).Host == "bmclapi2.bangbang93.com" && urls.Last() == (string)officialClient["url"], "Client files use mirror first with official fallback");
        string target = Path.Combine(output, "minecraft-1.21.1-client.jar");
        if (File.Exists(target)) File.Delete(target);
        Console.WriteLine("Downloading Minecraft 1.21.1 client through compiled multi-thread downloader...");
        CallOptional("ModNet", "NetDownloadByLoader", new[] { urls.First() }, target, null, Checker());
        using (var sha = SHA1.Create()) using (var stream = File.OpenRead(target)) {
            Check(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == expectedHash, "Actual BMCLAPI client download passes SHA1");
        }
        Check(new FileInfo(target).Length == expectedSize, "Downloaded client size matches official metadata");
        Console.WriteLine(report.Last());
        string fallback = Path.Combine(output, "minecraft-1.21.1-official-fallback.jar");
        if (File.Exists(fallback)) File.Delete(fallback);
        Console.WriteLine("Testing unavailable-source fallback to official client...");
        // A deliberately blocked first source fails locally, so the same engine
        // must advance to the real official client URL.
        CallOptional("ModNet", "NetDownloadByLoader", new[] {
            "https://pcl2-server-1253424809.file.myqcloud.com/disabled-client.jar", urls.Last()
        }, fallback, null, Checker());
        using (var sha = SHA1.Create()) using (var stream = File.OpenRead(fallback)) {
            Check(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == expectedHash, "Unavailable first source falls back to official client and passes SHA1");
        }
        report.Add("Minecraft 1.21.1 client: " + expectedSize + " bytes; SHA1 " + expectedHash);
    }
}
