Public Class MySkin

    '事件
    Public Event Click(sender As Object, e As MouseButtonEventArgs)

    '皮肤储存
    Private _Address As String
    Public Property Address As String
        Get
            Return _Address
        End Get
        Set(value As String)
            _Address = value
            ToolTip = If(_Address = "", "加载中", "点击更换皮肤（右键查看更多选项）")
        End Set
    End Property
    Public Loader As LoaderTask(Of (String, String), String)

    '控件动画
    Private Sub PanSkin_MouseEnter(sender As Object, e As MouseEventArgs) Handles Me.MouseEnter
        AniStart(AaOpacity(ShadowSkin, 0.8 - ShadowSkin.Opacity, 200, 100), "Skin Shadow")
    End Sub
    Private Sub PanSkin_MouseLeave(sender As Object, e As MouseEventArgs) Handles Me.MouseLeave
        AniStart(AaOpacity(ShadowSkin, 0.2 - ShadowSkin.Opacity, 200), "Skin Shadow")
        IsSkinMouseDown = False
        AniStart(AaScaleTransform(Me, 1 - CType(Me.RenderTransform, ScaleTransform).ScaleX, 60,, New AniEaseOutFluent), "Skin Scale")
    End Sub

    '点击
    Private IsSkinMouseDown As Boolean = False
    Private Sub PanSkin_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonDown
        IsSkinMouseDown = True
        AniStart(AaScaleTransform(Me, 0.9 - CType(Me.RenderTransform, ScaleTransform).ScaleX, 60,, New AniEaseOutFluent), "Skin Scale")
    End Sub
    Private Sub PanSkin_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs) Handles Me.MouseLeftButtonUp
        AniStart(AaScaleTransform(Me, 1 - CType(Me.RenderTransform, ScaleTransform).ScaleX, 60,, New AniEaseOutFluent), "Skin Scale")
        If IsSkinMouseDown Then
            IsSkinMouseDown = False
            RaiseEvent Click(sender, e)
        End If
    End Sub

    '保存皮肤
    Public Sub BtnSkinSave_Click() Handles BtnSkinSave.Click
        Save(Loader)
    End Sub
    Public Shared Sub Save(Loader As LoaderTask(Of (String, String), String))
        Dim Address = Loader.Output
        If Not Loader.State = LoadState.Finished Then
            Hint("皮肤正在获取中，请稍候！", HintType.Red)
            If Not Loader.State = LoadState.Loading Then Loader.Start()
            Return
        End If
        Try
            Dim FileAddress As String = Dialogs.SaveFile("选取保存皮肤的位置", PathUtils.GetLastPart(Address), filter:={("png", "皮肤图片文件")})
            If FileAddress Is Nothing Then Return
            If FileAddress.Contains("\") Then
                FileUtils.Delete(FileAddress)
                If Address.StartsWithF(PathImage) Then
                    Dim Image As New MyBitmap(Address)
                    Image.Save(FileAddress)
                Else
                    FileUtils.Copy(Address, FileAddress)
                End If
                Hint("皮肤保存成功！", HintType.Green)
            End If
        Catch ex As Exception
            Logger.Error(ex, "保存皮肤失败", LogBehavior.Toast)
        End Try
    End Sub
    Private Sub BtnSkinSave_Checked(sender As MyMenuItem, e As RoutedEventArgs) Handles BtnSkinSave.Checked
        sender.IsEnabled = Address = ""
    End Sub

    ''' <summary>
    ''' 载入皮肤。
    ''' </summary>
    Public Sub Load()
        Try
            '检查文件存在
            Address = Loader.Output
            If String.IsNullOrEmpty(Address) Then Throw New Exception("皮肤加载器 " & Loader.Name & " 没有输出")
            If Not Address.StartsWithF(PathImage) AndAlso Not FileUtils.Exists(Address) Then Throw New FileNotFoundException("皮肤文件未找到", Address)
            '加载
            Dim Image As MyBitmap
            Try
                Image = New MyBitmap(Address)
            Catch ex As Exception '#2272
                Logger.Error(ex, $"皮肤文件已损坏：{Address}", LogBehavior.Toast)
                FileUtils.Delete(Address)
                Return
            End Try
            ImgBack.Tag = Address
            '大小检查
            Dim Scale As Integer = Image.Pic.Width / 64
            If Image.Pic.Width < 32 OrElse Image.Pic.Height < 32 Then
                ImgFore.Source = Nothing : ImgBack.Source = Nothing
                Throw New Exception("图片大小不足，长为 " & Image.Pic.Height & "，宽为 " & Image.Pic.Width)
            End If
            '头发层（附加层）
            If Image.Pic.Width >= 64 AndAlso Image.Pic.Height >= 32 Then
                If (Image.Pic.GetPixel(1, 1).A = 0 OrElse '如果图片中有任何透明像素（避免纯色白底）
                    Image.Pic.GetPixel(Image.Pic.Width - 1, Image.Pic.Height - 1).A = 0 OrElse
                    Image.Pic.GetPixel(Image.Pic.Width - 2, Image.Pic.Height / 2 - 2).A = 0) OrElse
                   (Image.Pic.GetPixel(1, 1) <> Image.Pic.GetPixel(Scale * 41, Scale * 9) AndAlso '或是头部颜色和透明区均不一样
                    Image.Pic.GetPixel(Image.Pic.Width - 1, Image.Pic.Height - 1) <> Image.Pic.GetPixel(Scale * 41, Scale * 9) AndAlso
                    Image.Pic.GetPixel(Image.Pic.Width - 2, Image.Pic.Height / 2 - 2) <> Image.Pic.GetPixel(Scale * 41, Scale * 9)) Then
                    ImgFore.Source = Image.Clip(Scale * 40, Scale * 8, Scale * 8, Scale * 8)
                Else
                    ImgFore.Source = Nothing
                End If
            Else
                ImgFore.Source = Nothing
            End If
            '脸层
            ImgBack.Source = Image.Clip(Scale * 8, Scale * 8, Scale * 8, Scale * 8)
            Logger.Info($"载入头像成功：{Loader.Name}")
        Catch ex As Exception
            Logger.Error(ex, $"载入头像失败（{If(Address, "null")},{Loader.Name}）", LogBehavior.Toast)
        End Try
    End Sub
    ''' <summary>
    ''' 清空皮肤。
    ''' </summary>
    Public Sub Clear()
        Address = ""
        ImgFore.Source = Nothing
        ImgBack.Source = Nothing
    End Sub

    '刷新缓存
    Public Sub RefreshClick() Handles BtnSkinRefresh.Click
        RefreshCache(Loader)
    End Sub
    ''' <summary>
    ''' 刷新皮肤缓存。
    ''' </summary>
    Public Shared Sub RefreshCache(Optional sender As LoaderTask(Of (String, String), String) = Nothing)
        Dim HasLoaderRunning As Boolean = False
        For Each SkinLoader In PageLaunchLeft.SkinLoaders
            If SkinLoader.State = LoadState.Loading Then
                HasLoaderRunning = True : Exit For
            End If
        Next
        If FrmLaunchLeft IsNot Nothing AndAlso HasLoaderRunning Then
            '由于取消不是实时的，暂时不会释放文件，会导致删除报错，故只能取消执行
            Hint("有正在获取中的皮肤，请稍后再试！", HintType.Blue)
        Else
            RunInThread(
            Sub()
                Try
                    Hint("正在刷新皮肤……")
                    '清空缓存
                    Logger.Info("正在清空皮肤缓存")
                    If DirectoryUtils.Exists(PathTemp & "Cache\Skin") Then DirectoryUtils.Delete(PathTemp & "Cache\Skin")
                    If DirectoryUtils.Exists(PathTemp & "Cache\Uuid") Then DirectoryUtils.Delete(PathTemp & "Cache\Uuid")
                    IniClearCache(PathTemp & "Cache\Skin\IndexMs.ini")
                    IniClearCache(PathTemp & "Cache\Skin\IndexNide.ini")
                    IniClearCache(PathTemp & "Cache\Skin\IndexAuth.ini")
                    IniClearCache(PathTemp & "Cache\Uuid\Mojang.ini")
                    '刷新控件
                    For Each SkinLoader In If(sender IsNot Nothing, {sender}, {PageLaunchLeft.SkinLegacy, PageLaunchLeft.SkinMs})
                        SkinLoader.WaitForExit(IsForceRestart:=True)
                    Next
                    Hint("已刷新皮肤！", HintType.Green)
                Catch ex As Exception
                    Logger.Error(ex, "刷新皮肤缓存失败", LogBehavior.Alert)
                End Try
            End Sub)
        End If
    End Sub
    ''' <summary>
    ''' 在更换正版皮肤后，刷新正版皮肤。
    ''' </summary>
    ''' <param name="SkinAddress">新的正版皮肤完整地址。</param>
    Public Shared Sub ReloadCache(SkinAddress As String)
        RunInThread(
        Sub()
            Try
                '更新缓存
                WriteIni(PathTemp & "Cache\Skin\IndexMs.ini", Settings.Get(Of String)("CacheMsV2Uuid"), SkinAddress)
                Logger.Info($"已写入皮肤地址缓存 {Settings.Get(Of String)("CacheMsV2Uuid")} -> {SkinAddress}")
                '刷新控件
                For Each SkinLoader In {PageLaunchLeft.SkinMs, PageLaunchLeft.SkinLegacy}
                    SkinLoader.WaitForExit(IsForceRestart:=True)
                Next
                '完成提示
                Hint("更改皮肤成功！", HintType.Green)
            Catch ex As Exception
                Logger.Error(ex, "更改正版皮肤后刷新皮肤失败")
            End Try
        End Sub)
    End Sub

    '披风
    Public Property HasCape As Boolean
        Get
            Return BtnSkinCape.Visibility = Visibility.Collapsed
        End Get
        Set(value As Boolean)
            If value Then
                BtnSkinCape.Visibility = Visibility.Visible
            Else
                BtnSkinCape.Visibility = Visibility.Collapsed
            End If
        End Set
    End Property
    Private IsChanging As Boolean = False
    Public Sub BtnSkinCape_Click() Handles BtnSkinCape.Click
        Hint("此版本已移除在线披风管理。")
    End Sub

End Class
