'由于包含加解密等安全信息，本文件中的部分代码已被删除

Friend Module ModSecret

    '标注 PCL 的不同分支，仅用于替换标记
    Public Const VersionBranchMain As String = "Red"
    '在开源版的注册表与常规版的注册表隔离，以防数据冲突
    Public Const RegFolder As String = "PCLRed"
    '用于微软登录的 ClientId
    Public OAuthClientId As String = If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), "fe72edc2-3a6f-4280-90e8-e2beb64ce7e1")
    'CurseForge API Key
    Public CurseForgeAPIKey As String = ReadBuildCredential("CurseForgeAPIKey", "PCL_CURSEFORGE_API_KEY")

    Private Function ReadBuildCredential(Key As String, EnvironmentKey As String) As String
        Dim Override = Environment.GetEnvironmentVariable(EnvironmentKey)
        If Override IsNot Nothing Then Return Override
        Using Resource = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PCL.RedBuildCredentials.json")
            If Resource Is Nothing Then Return ""
            Using Reader As New StreamReader(Resource, Encoding.UTF8)
                Return If(JObject.Parse(Reader.ReadToEnd())(Key)?.ToString(), "")
            End Using
        End Using
    End Function
    '用于匿名数据收集的腾讯云日志服务上报 URL，形如 https://{region}.cls.tencentcs.com/track?topic_id={topic_id}
    Public Const ClsBaseUrl As String = ""

#Region "网络鉴权"

    Public Const RestrictMcDownload As Boolean = False
    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWithF("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function
    ''' <summary>
    ''' 设置 Headers 的 UA、Referer。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage, Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")  '#4951
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"PCL2/{VersionBaseName}.{CInt(BuildType)}")
            End If
        End If
        If Not SimulateBrowserHeaders Then Req.Headers.Add("Referer", $"http://{VersionCode}.open.pcl2.server/")
        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
    End Sub

#End Region

#Region "主题"

    Public Color1 As New MyColor(52, 61, 74)
    Public Color2 As New MyColor(11, 91, 203)
    Public Color3 As New MyColor(19, 112, 243)
    Public Color4 As New MyColor(72, 144, 245)
    Public Color5 As New MyColor(150, 192, 249)
    Public Color6 As New MyColor(213, 230, 253)
    Public Color7 As New MyColor(222, 236, 253)
    Public Color8 As New MyColor(234, 242, 254)
    Public ColorBg0 As New MyColor(150, 192, 249)
    Public ColorBg1 As New MyColor(190, Color7)
    Public ColorGray1 As New MyColor(64, 64, 64)
    Public ColorGray2 As New MyColor(115, 115, 115)
    Public ColorGray3 As New MyColor(140, 140, 140)
    Public ColorGray4 As New MyColor(166, 166, 166)
    Public ColorGray5 As New MyColor(204, 204, 204)
    Public ColorGray6 As New MyColor(235, 235, 235)
    Public ColorGray7 As New MyColor(240, 240, 240)
    Public ColorGray8 As New MyColor(245, 245, 245)
    Public ColorSemiTransparent As New MyColor(1, Color8)

    Public ThemeNow As Integer = -1
    Public ColorHue As Integer = 210, ColorSat As Integer = 85, ColorLightAdjust As Integer = 0, ColorHueTopbarDelta As OneOf(Of Integer, Integer()) = 0
    Public ThemeDontClick As Integer = 0
    Private ReadOnly LuckyThemeHue As Integer = RandomInteger(0, 359)

    Public Sub ThemeRefresh(Optional NewTheme As Integer = -1)
        Try
            If NewTheme >= 0 AndAlso Not ThemeCheckOne(NewTheme) Then NewTheme = DefaultTheme
            If ThemeNow = NewTheme AndAlso NewTheme >= 0 Then Return
            If NewTheme >= 0 Then ThemeNow = NewTheme
            If Not ThemeCheckOne(ThemeNow) Then ThemeNow = DefaultTheme

            ColorHue = 210
            ColorSat = 85
            ColorLightAdjust = 0
            ColorHueTopbarDelta = 0
            Select Case ThemeNow
                Case 1 '甜柠青
                    ColorHue = 170 : ColorSat = 75 : ColorLightAdjust = -5
                Case 2 '小草绿
                    ColorHue = 125 : ColorSat = 70 : ColorLightAdjust = -6
                Case 3 '菠萝黄
                    ColorHue = 50 : ColorSat = 90 : ColorLightAdjust = -7
                Case 4 '橡木棕
                    ColorHue = 28 : ColorSat = 55 : ColorLightAdjust = -9
                Case 5 '玄素黑
                    ColorSat = 5 : ColorLightAdjust = -12
                Case 6 '铁杆粉
                    ColorHue = 335 : ColorSat = 75
                Case 7 '神秘紫
                    ColorHue = 270 : ColorSat = 65
                Case 8 '秋仪金
                    ColorHue = 42 : ColorLightAdjust = -8 : ColorHueTopbarDelta = 15
                Case 9 '活跃橙
                    ColorHue = 25 : ColorSat = 95
                Case 10 '跳票红
                    ColorHue = 355 : ColorSat = 80
                Case 11 '极客蓝
                    ColorHue = 220 : ColorLightAdjust = -14
                Case 12 '滑稽彩：随时间平滑改变色相
                    ColorHue = CInt((Date.UtcNow.Ticks \ TimeSpan.TicksPerMillisecond \ 50) Mod 360)
                    ColorSat = 75 : ColorHueTopbarDelta = 35
                Case 13 '欧皇彩：每次启动随机配色
                    ColorHue = LuckyThemeHue : ColorSat = 75 : ColorHueTopbarDelta = 120
                Case 14 '自定义
                    ColorHue = Settings.Get(Of Integer)("UiLauncherHue")
                    ColorSat = Settings.Get(Of Integer)("UiLauncherSat")
                    ColorLightAdjust = Settings.Get(Of Integer)("UiLauncherLight") - 20
                    ColorHueTopbarDelta = Settings.Get(Of Integer)("UiLauncherDelta")
                Case 15 '苏维埃红
                    ColorHue = 0 : ColorSat = 90 : ColorLightAdjust = -6 : ColorHueTopbarDelta = 7
                Case 42 '纪念彩
                    ColorHue = 285 : ColorSat = 80 : ColorHueTopbarDelta = 120
            End Select

            Color1 = New MyColor().FromHSL2(ColorHue, ColorSat * 0.2, 25 + ColorLightAdjust * 0.3)
            Color2 = New MyColor().FromHSL2(ColorHue, ColorSat, 45 + ColorLightAdjust)
            Color3 = New MyColor().FromHSL2(ColorHue, ColorSat, 55 + ColorLightAdjust)
            Color4 = New MyColor().FromHSL2(ColorHue, ColorSat, 65 + ColorLightAdjust)
            Color5 = New MyColor().FromHSL2(ColorHue, ColorSat, 80 + ColorLightAdjust * 0.4)
            Color6 = New MyColor().FromHSL2(ColorHue, ColorSat, 91 + ColorLightAdjust * 0.1)
            Color7 = New MyColor().FromHSL2(ColorHue, ColorSat, 95)
            Color8 = New MyColor().FromHSL2(ColorHue, ColorSat, 97)
            ColorBg0 = Color4 * 0.4 + Color5 * 0.4 + ColorGray4 * 0.2
            ColorBg1 = New MyColor(190, Color7)

            ColorSemiTransparent = New MyColor(1, Color8)
            Application.Current.Resources("ColorBrush1") = New SolidColorBrush(Color1)
            Application.Current.Resources("ColorBrush2") = New SolidColorBrush(Color2)
            Application.Current.Resources("ColorBrush3") = New SolidColorBrush(Color3)
            Application.Current.Resources("ColorBrush4") = New SolidColorBrush(Color4)
            Application.Current.Resources("ColorBrush5") = New SolidColorBrush(Color5)
            Application.Current.Resources("ColorBrush6") = New SolidColorBrush(Color6)
            Application.Current.Resources("ColorBrush7") = New SolidColorBrush(Color7)
            Application.Current.Resources("ColorBrush8") = New SolidColorBrush(Color8)
            Application.Current.Resources("ColorBrushBg0") = New SolidColorBrush(ColorBg0)
            Application.Current.Resources("ColorBrushBg1") = New SolidColorBrush(ColorBg1)
            Application.Current.Resources("ColorObject1") = CType(Color1, Color)
            Application.Current.Resources("ColorObject2") = CType(Color2, Color)
            Application.Current.Resources("ColorObject3") = CType(Color3, Color)
            Application.Current.Resources("ColorObject4") = CType(Color4, Color)
            Application.Current.Resources("ColorObject5") = CType(Color5, Color)
            Application.Current.Resources("ColorObject6") = CType(Color6, Color)
            Application.Current.Resources("ColorObject7") = CType(Color7, Color)
            Application.Current.Resources("ColorObject8") = CType(Color8, Color)
            Application.Current.Resources("ColorObjectBg0") = CType(ColorBg0, Color)
            Application.Current.Resources("ColorObjectBg1") = CType(ColorBg1, Color)
            ThemeRefreshMain()
        Catch ex As Exception
            Logger.Error(ex, "刷新主题颜色失败", LogBehavior.Toast)
        End Try
    End Sub
    Public Sub ThemeRefreshMain()
        RunInUi(
        Sub()
            If FrmMain Is Nothing OrElse Not FrmMain.IsLoaded Then Return
            '顶部条背景
            Dim Brush = New LinearGradientBrush With {.EndPoint = New Point(1, 0), .StartPoint = New Point(0, 0)}
            Dim Deltas = ColorHueTopbarDelta.Switch(Function(d) New Integer() {-d, 0, d}, Function(d) d)
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0, .Color = New MyColor().FromHSL2(ColorHue + Deltas(0), ColorSat, 48 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 0.5, .Color = New MyColor().FromHSL2(ColorHue + Deltas(1), ColorSat, 54 + ColorLightAdjust)})
            Brush.GradientStops.Add(New GradientStop With {.Offset = 1, .Color = New MyColor().FromHSL2(ColorHue + Deltas(2), ColorSat, 48 + ColorLightAdjust)})
            If ThemeNow = 5 Then
                Brush.GradientStops.Clear()
                Brush.GradientStops.Add(New GradientStop(New MyColor().FromHSL2(ColorHue, ColorSat, 25), 0))
                Brush.GradientStops.Add(New GradientStop(New MyColor().FromHSL2(ColorHue, ColorSat, 15), 0.5))
                Brush.GradientStops.Add(New GradientStop(New MyColor().FromHSL2(ColorHue, ColorSat, 25), 1))
            ElseIf ThemeNow = DefaultTheme Then
                Brush.GradientStops.Clear()
                Brush.GradientStops.Add(New GradientStop(Color.FromRgb(142, 12, 24), 0))
                Brush.GradientStops.Add(New GradientStop(Color.FromRgb(198, 23, 38), 0.55))
                Brush.GradientStops.Add(New GradientStop(Color.FromRgb(163, 13, 26), 1))
            End If
            FrmMain.PanTitle.Background = Brush
            FrmMain.PanTitle.Background.Freeze()
            FrmMain.ImgTitle.Source = If(ThemeNow >= 5 AndAlso ThemeNow <> 14 AndAlso ThemeNow <> DefaultTheme, $"{PathImage}Themes/{ThemeNow}.png", Nothing)
            FrmMain.ImgTitle.Opacity = If(ThemeNow = 13, 0.25, 0.5)
            FrmMain.CommunismEmblem.Visibility = If(ThemeNow = DefaultTheme, Visibility.Visible, Visibility.Collapsed)
            FrmMain.CommunismRule.Visibility = FrmMain.CommunismEmblem.Visibility
            FrmMain.CommunismBackdrop.Visibility = FrmMain.CommunismEmblem.Visibility
            If FrmLaunchRight IsNot Nothing Then FrmLaunchRight.CommunismWelcome.Visibility = FrmMain.CommunismEmblem.Visibility
            '主页面背景
            If Settings.Get(Of Boolean)("UiBackgroundColorful") Then
                Brush = New LinearGradientBrush With {.EndPoint = New Point(0.1, 1), .StartPoint = New Point(0.9, 0)}
                Brush.GradientStops.Add(New GradientStop With {.Offset = -0.1, .Color = New MyColor().FromHSL2(ColorHue - 15, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 0.4, .Color = New MyColor().FromHSL2(ColorHue, ColorSat * 0.8, 91)})
                Brush.GradientStops.Add(New GradientStop With {.Offset = 1.1, .Color = New MyColor().FromHSL2(ColorHue + 15, ColorSat * 0.8, 91)})
                If ThemeNow = DefaultTheme Then
                    Brush.GradientStops.Clear()
                    Brush.GradientStops.Add(New GradientStop(Color.FromRgb(255, 249, 239), 0))
                    Brush.GradientStops.Add(New GradientStop(Color.FromRgb(247, 230, 219), 1))
                End If
                FrmMain.PanForm.Background = Brush
            Else
                FrmMain.PanForm.Background = New MyColor(245, 245, 245)
            End If
            FrmMain.PanForm.Background.Freeze()
        End Sub)
    End Sub
    Friend Sub ThemeCheckAll(EffectSetup As Boolean)
        RunInUi(
        Sub()
            If Not ThemeCheckOne(Settings.Get(Of Integer)("UiLauncherTheme")) Then Settings.Set("UiLauncherTheme", DefaultTheme)
            Const AllThemes As String = "0|1|2|3|4|5|6|7|8|9|10|11|12|13|14|15|42"
            If Settings.Get(Of String)("UiLauncherThemeHide2") <> AllThemes Then Settings.Set("UiLauncherThemeHide2", AllThemes)
            If Not EffectSetup OrElse FrmSetupUI Is Nothing Then Return
            For Each Control As UIElement In FrmSetupUI.PanLauncherTheme.Children
                If Not TypeOf Control Is MyRadioBox OrElse Control Is FrmSetupUI.RadioLauncherTheme5Gray Then Continue For
                Dim Radio = DirectCast(Control, MyRadioBox)
                Radio.IsEnabled = True
                Radio.Opacity = 1
                Radio.ToolTip = "所有主题均已开放"
            Next
            FrmSetupUI.RadioLauncherTheme5Gray.Visibility = Visibility.Collapsed
            FrmSetupUI.LabLauncherTheme5Unlock.Visibility = Visibility.Collapsed
            FrmSetupUI.LabLauncherTheme8Copy.Visibility = Visibility.Collapsed
            FrmSetupUI.LabLauncherTheme9Copy.Visibility = Visibility.Collapsed
            FrmSetupUI.LabLauncherTheme11Click.Visibility = Visibility.Collapsed
            FrmSetupUI.PanLauncherHide.Visibility = Visibility.Collapsed
        End Sub)
    End Sub
    Friend Function ThemeCheckOne(Id As Integer) As Boolean
        Return (Id >= 0 AndAlso Id <= DefaultTheme) OrElse Id = 42
    End Function
    Friend Function ThemeUnlock(Id As Integer, Optional ShowDoubleHint As Boolean = True, Optional UnlockHint As String = Nothing) As Boolean
        Return False
    End Function

#End Region

#Region "更新"

    Friend Sub UpdateCheckByButton()
        Hint("PCL Communist 已移除启动器远程更新和配置下发")
    End Sub

    Friend IsUpdateWaitingRestart As Boolean = False
    Public Sub UpdateRestart(TriggerRestartAndByEnd As Boolean)
    End Sub
    Public Sub UpdateReplace(ProcessId As Integer, OldFileName As String, NewFileName As String, TriggerRestart As Boolean)
    End Sub

    ''' <summary>
    ''' 确保 PathTemp/Latest.exe 是最新正式版的 PCL，它会被用于整合包打包。
    ''' 如果不是，则下载一个。
    ''' </summary>
    Friend Sub DownloadLatestPCL(Optional LoaderToSyncProgress As LoaderBase = Nothing)
        '整合包携带当前本地版本，不向远程服务器请求或替换程序。
        FileUtils.Copy(PathExe, PathTemp & "Latest.exe")
    End Sub

#End Region


#Region "赞助等级"

    Public ReadOnly Property CurrentRank As DonationRank
        Get
            Return DonationRank.None
        End Get
    End Property

    Public Sub InputPotatoCode(IsUpdating As Boolean)
    End Sub
    Friend Sub GeneratePotatoCode()
    End Sub

    ''' <summary>
    ''' 获取设备识别码。
    ''' </summary>
    Friend Function GetIdentify() As String
        Return "0000-0000-0000-0000"
    End Function

#End Region

End Module
