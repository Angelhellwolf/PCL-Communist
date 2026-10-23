using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Communist.Core;

namespace PCL.Communist.ViewModels;

public sealed record LauncherTheme(string Name, string Color)
{
    public override string ToString() => Name;
}

public partial class MainViewModel : ViewModelBase
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly PlatformInfo _platform;
    private LauncherPaths _paths;
    private readonly string _settingsPath;
    private CancellationTokenSource? _operation;
    private AccountSession? _account;

    public MainViewModel() : this(PlatformInfo.Current, LauncherPaths.Default) { }

    public MainViewModel(PlatformInfo platform, LauncherPaths paths)
    {
        _platform = platform;
        _paths = paths;
        _settingsPath = Path.Combine(Path.GetDirectoryName(paths.Root)!, "launcher-settings.json");
        GameRoot = paths.Root;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PCL-Communist/0.1.0");
        SelectedTheme = Themes[0];
        LoadSettings();
        LoadInstalledVersions();
    }

    public string PlatformName => _platform.DisplayName;
    public bool IsDesktop => !_platform.IsMobile;
    public string RuntimeDescription => Runtime.Capability.Description;
    public string LaunchLabel => IsDesktop ? "启动游戏  →" : "本机运行时待接入";
    public string MobileNotice => "资源可以在本机下载和校验。游戏画面、Java 和触控运行时尚待接入，当前构建暂不能在手机或 iPad 上启动游戏。";
    public bool IsMobile => _platform.IsMobile;
    public ObservableCollection<MinecraftVersion> Versions { get; } = [];
    public IReadOnlyList<LauncherTheme> Themes { get; } =
    [
        new("苏维埃红", "#C93746"), new("天空蓝", "#3E87CD"), new("秋叶金", "#B98A35"),
        new("清水绿", "#348F86"), new("森林绿", "#4C9560"), new("星空紫", "#8062BE"),
        new("樱花粉", "#C96794"), new("落日橙", "#D7753B"), new("深海蓝", "#425AB0"),
        new("薄荷青", "#329D9C"), new("玫瑰红", "#BA476F"), new("丁香紫", "#A175BF"),
        new("琥珀金", "#AD7C2A"), new("青瓷绿", "#509285"), new("冰川蓝", "#548DB2"),
        new("石墨灰", "#78818F"), new("午夜紫", "#5F4D99")
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    public partial MinecraftVersion? SelectedVersion { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string Status { get; set; } = "选择已安装版本，或刷新列表下载游戏。";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string PlayerName { get; set; } = "Player";
    [ObservableProperty] public partial string AccountStatus { get; set; } = "离线账号 · 可登录微软正版账号";
    [ObservableProperty] public partial string LoginCode { get; set; } = "";
    [ObservableProperty] public partial bool ShowLoginCode { get; set; }
    [ObservableProperty] public partial string LoginAddress { get; set; } = "https://microsoft.com/link";
    [ObservableProperty] public partial string GameRoot { get; set; } = "";
    [ObservableProperty] public partial string JavaPath { get; set; } = "";
    [ObservableProperty] public partial decimal MemoryMb { get; set; } = 2048;
    [ObservableProperty] public partial LauncherTheme SelectedTheme { get; set; } = null!;
    public IBrush AccentBrush => Brush.Parse(SelectedTheme?.Color ?? "#C93746");

    partial void OnSelectedThemeChanged(LauncherTheme value) => OnPropertyChanged(nameof(AccentBrush));
    private bool CanWork() => !IsBusy;
    private bool CanInstall() => !IsBusy && SelectedVersion is not null;
    private bool CanLaunch() => CanInstall() && IsDesktop;
    private DownloadClient Downloads => new(_http);
    private MinecraftInstaller Installer => new(Downloads, _paths);
    private GameLauncher Runtime => new(_platform, _paths, new JavaRuntimeService(Downloads, _paths));
    private IProgress<InstallProgress> InstallProgress => new Progress<InstallProgress>(p => { Status = p.Message; Progress = p.Percent; });

    [RelayCommand(CanExecute = nameof(CanWork))]
    private Task RefreshAsync() => RunAsync(async token =>
    {
        Status = "获取可用版本…";
        var versions = await Installer.GetVersionsAsync(token);
        var selected = SelectedVersion?.Id;
        Versions.Clear();
        foreach (var version in versions.Where(v => v.Type == "release")) Versions.Add(version);
        SelectedVersion = Versions.FirstOrDefault(v => v.Id == selected) ?? Versions.FirstOrDefault();
        Status = $"已获取 {Versions.Count} 个正式版本。";
    });

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync() => RunAsync(async token =>
    {
        ApplySettings();
        if (SelectedVersion is { Url.Length: 0 } local)
            await Installer.RepairAsync(local.Id, _platform, InstallProgress, token);
        else await InstallSelectedAsync(token);
    });

    private async Task<JsonObject> InstallSelectedAsync(CancellationToken token)
    {
        var version = SelectedVersion ?? throw new InvalidOperationException("请先选择游戏版本。");
        // Complete cached installations work offline; interrupted downloads are repaired before launching.
        if (string.IsNullOrEmpty(version.Url))
            return await Installer.RepairAsync(version.Id, _platform, InstallProgress, token);
        return await Installer.InstallAsync(version, _platform, InstallProgress, token);
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private Task LaunchAsync() => RunAsync(async token =>
    {
        ApplySettings();
        var account = _account ?? AccountSession.Offline(PlayerName.Trim());
        var metadata = await InstallSelectedAsync(token);
        Status = "准备游戏运行时…";
        await Runtime.LaunchAsync(metadata, account, JavaPath.Trim(), (int)MemoryMb, InstallProgress, token);
        Status = "游戏进程已启动。";
    });

    [RelayCommand(CanExecute = nameof(CanWork))]
    private Task LoginAsync() => RunAsync(async token =>
    {
        Status = "正在准备微软登录…";
        try
        {
            _account = await new MicrosoftLogin(_http).LoginAsync(prompt =>
            {
                LoginCode = prompt.Code;
                LoginAddress = prompt.VerificationUri.AbsoluteUri;
                ShowLoginCode = true;
                Status = "打开登录网页，输入上方代码完成授权。";
            }, token);
            PlayerName = _account.Name;
            AccountStatus = "正版账号 · " + _account.Name;
            Status = "微软登录成功，本次会话可直接启动游戏。";
        }
        finally { ShowLoginCode = false; }
    });

    [RelayCommand(CanExecute = nameof(CanWork))]
    private void Logout()
    {
        _account = null;
        AccountStatus = "离线账号 · 可登录微软正版账号";
        Status = "已退出账号。";
    }

    [RelayCommand]
    private void Cancel() => _operation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanWork))]
    private void SaveSettings()
    {
        try { ApplySettings(); LoadInstalledVersions(); Status = "设置已保存。"; }
        catch (Exception ex) { Status = ex.Message; }
    }

    private void ApplySettings()
    {
        if (IsDesktop)
        {
            if (!Path.IsPathFullyQualified(GameRoot)) throw new ArgumentException("游戏目录需要填写绝对路径。");
            _paths = new LauncherPaths(GameRoot.Trim());
        }
        Directory.CreateDirectory(_paths.Root);
        MemoryMb = Math.Clamp(MemoryMb, 512, 32768);
        var settings = new JsonObject
        {
            ["gameRoot"] = _paths.Root, ["javaPath"] = JavaPath, ["memoryMb"] = (int)MemoryMb,
            ["theme"] = SelectedTheme.Name, ["playerName"] = _account is null ? PlayerName : "Player"
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, settings.ToJsonString());
        File.Move(temporary, _settingsPath, true);
    }

    private void LoadSettings()
    {
        if (!File.Exists(_settingsPath)) return;
        try
        {
            var settings = JsonNode.Parse(File.ReadAllText(_settingsPath))!;
            var root = settings["gameRoot"]?.GetValue<string>();
            if (IsDesktop && root is not null && Path.IsPathFullyQualified(root)) { _paths = new(root); GameRoot = root; }
            JavaPath = settings["javaPath"]?.GetValue<string>() ?? "";
            MemoryMb = Math.Clamp(settings["memoryMb"]?.GetValue<int>() ?? 2048, 512, 32768);
            PlayerName = settings["playerName"]?.GetValue<string>() ?? "Player";
            SelectedTheme = Themes.FirstOrDefault(t => t.Name == settings["theme"]?.GetValue<string>()) ?? Themes[0];
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
        { Status = "设置文件无法读取，已使用默认设置。"; }
    }

    private void LoadInstalledVersions()
    {
        Versions.Clear();
        SelectedVersion = null;
        if (!Directory.Exists(_paths.Versions)) return;
        foreach (var directory in Directory.EnumerateDirectories(_paths.Versions).OrderDescending())
        {
            var id = Path.GetFileName(directory);
            if (File.Exists(Path.Combine(directory, id + ".json")) && File.Exists(Path.Combine(directory, id + ".jar")))
                Versions.Add(new(id, "installed", "", ""));
        }
        SelectedVersion = Versions.FirstOrDefault();
    }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        Progress = 0;
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        try { await operation(cancellation.Token); }
        catch (OperationCanceledException) { Status = "操作已取消，可重新开始。"; }
        catch (Exception ex) { Status = ex.Message; }
        finally { _operation = null; IsBusy = false; }
    }
}
