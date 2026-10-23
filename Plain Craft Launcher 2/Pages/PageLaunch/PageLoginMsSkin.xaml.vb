Public Class PageLoginMsSkin

    Public Sub New()
        InitializeComponent()
        Skin.Loader = PageLaunchLeft.SkinMs
    End Sub
    Private Sub PageLoginLegacy_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded
        Skin.Loader.Start()
    End Sub

    ''' <summary>
    ''' 刷新页面显示的所有信息。
    ''' </summary>
    Public Sub Reload(KeepInput As Boolean)
        TextName.Text = Settings.Get(Of String)("CacheMsV2Name")
        '皮肤在 Loaded 加载
    End Sub
    ''' <summary>
    ''' 获取当前页面的登录信息。
    ''' </summary>
    Public Shared Function GetLoginData() As McLoginMs
        If McLoginMsLoader.State = LoadState.Finished Then
            Return New McLoginMs With {.OAuthRefreshToken = Settings.Get(Of String)("CacheMsV2OAuthRefresh"), .UserName = Settings.Get(Of String)("CacheMsV2Name"), .AccessToken = Settings.Get(Of String)("CacheMsV2Access"), .Uuid = Settings.Get(Of String)("CacheMsV2Uuid"), .ProfileJson = Settings.Get(Of String)("CacheMsV2ProfileJson")}
        Else
            Return New McLoginMs With {.OAuthRefreshToken = Settings.Get(Of String)("CacheMsV2OAuthRefresh"), .UserName = Settings.Get(Of String)("CacheMsV2Name")}
        End If
    End Function

#Region "下边栏其他内容"

    '显示/隐藏控制
    Private Sub ShowPanel(sender As Object, e As MouseEventArgs) Handles PanData.MouseEnter
        AniStart(AaOpacity(PanButtons, 1 - PanButtons.Opacity, 120), "PageLoginMsSkin Button")
    End Sub
    Public Sub HidePanel(sender As Object, e As EventArgs) Handles PanData.MouseLeave
        If BtnEdit.ContextMenu.IsOpen OrElse BtnSkin.ContextMenu.IsOpen OrElse PanData.IsMouseOver Then Return
        AniStart(AaOpacity(PanButtons, -PanButtons.Opacity, 120), "PageLoginMsSkin Button")
    End Sub

    '修改账号信息
    Private Sub BtnEdit_Click(sender As Object, e As EventArgs) Handles BtnEdit.Click
        BtnEdit.ContextMenu.IsOpen = True
    End Sub

    '退出登录
    Private Sub BtnExit_Click() Handles BtnExit.Click
        Settings.Set("CacheMsV2OAuthRefresh", "")
        Settings.Set("CacheMsV2Access", "")
        Settings.Set("CacheMsV2ProfileJson", "")
        Settings.Set("CacheMsV2Uuid", "")
        Settings.Set("CacheMsV2Name", "")
        Settings.Set("CacheMsV2Expires", 0L)
        McLoginMsLoader.Cancel()
        FrmLaunchLeft.RefreshPage(False, True)
    End Sub

#End Region

#Region "皮肤/披风"

    '展开
    Private Sub BtnSkin_Click(sender As Object, e As RoutedEventArgs) Handles BtnSkin.Click
        BtnSkin.ContextMenu.IsOpen = True
    End Sub

    '修改皮肤
    Private IsChanging As Boolean = False
    Public Sub BtnSkinEdit_Click(sender As Object, e As RoutedEventArgs)
        '检查条件，获取新皮肤
        If IsChanging Then
            Hint("正在更改皮肤中，请稍候！")
            Return
        End If
        If McLoginLoader.State = LoadState.Failed Then
            Hint("登录失败，无法更改皮肤！", HintType.Red)
            Return
        End If
        Dim SkinInfo As McSkinInfo = McSkinSelect()
        If Not SkinInfo.IsVaild Then Return
        Hint("正在更改皮肤……")
        IsChanging = True
        '开始实际获取
        RunInNewThread(Sub() EditSkin(SkinInfo), "Ms Skin Upload")
    End Sub
    Private Sub EditSkin(SkinInfo As McSkinInfo)
        IsChanging = False
        Hint("此版本已移除在线皮肤上传。")
    End Sub

    '保存皮肤
    Public Sub BtnSkinSave_Click(sender As Object, e As RoutedEventArgs)
        Skin.BtnSkinSave_Click()
    End Sub

    '刷新头像
    Public Sub BtnSkinRefresh_Click(sender As Object, e As RoutedEventArgs)
        Skin.RefreshClick()
    End Sub

    '修改披风
    Public Sub BtnSkinCape_Click(sender As Object, e As RoutedEventArgs)
        Skin.BtnSkinCape_Click()
    End Sub

    '刷新披风
    Public Sub BtnSkinCapeRefresh_Click(sender As Object, e As RoutedEventArgs)
        RunInThread(
        Sub()
            Try
                Hint("正在刷新披风列表……")
                If McLaunchLoader.State = LoadState.Loading Then
                    McLoginMsLoader.WaitForExit()
                Else
                    McLoginMsLoader.WaitForExit(GetLoginData(), IsForceRestart:=True)
                End If
                Hint("已刷新披风列表！", HintType.Green)
            Catch ex As Exception
                Logger.Error(ex, "刷新披风列表失败", LogBehavior.Toast)
            End Try
        End Sub)
    End Sub

#End Region

End Class
