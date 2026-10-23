
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PCL.Communist.ViewModels;

namespace PCL.Communist.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private async void OpenLoginSite(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var uri = new Uri(model.LoginAddress);
            if (uri.Scheme != "https" || (uri.Host != "microsoft.com" && !uri.Host.EndsWith(".microsoft.com", StringComparison.Ordinal)))
                throw new InvalidOperationException("登录网页地址无法验证，请重新发起微软登录。");
            if (!await top.Launcher.LaunchUriAsync(uri)) model.Status = "请在浏览器打开显示的登录地址。";
        }
        catch (Exception ex) { model.Status = ex.Message; }
    }
}
