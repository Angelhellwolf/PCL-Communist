'仅保留下载（包括下载所需的版本/依赖元数据）与微软正版登录。
Public Module RedPolicy
    Public Const DefaultTheme As Integer = 15

    Private ReadOnly MetadataHosts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
        "launchermeta.mojang.com", "piston-meta.mojang.com", "launcher.mojang.com",
        "bmclapi2.bangbang93.com", "optifine.net", "files.minecraftforge.net",
        "maven.minecraftforge.net", "maven.neoforged.net", "meta.fabricmc.net",
        "dl.liteloader.com", "jenkins.liteloader.com", "api.modrinth.com",
        "api.curseforge.com", "mod.mcimirror.top", "files.mcimirror.top"
    }

    Public Function IsNetworkAllowed(Url As String, Method As HttpMethod, Optional IsFileDownload As Boolean = False) As Boolean
        Dim Address As Uri = Nothing
        If Not Uri.TryCreate(Url.Replace("{CDN}", ""), UriKind.Absolute, Address) Then Return False
        If Address.Scheme <> Uri.UriSchemeHttps AndAlso Address.Scheme <> Uri.UriSchemeHttp Then Return False
        Dim Host = Address.IdnHost
        Dim RequestMethod = If(Method, HttpMethod.Get).Method.ToUpperInvariant()
        Dim ReadOnlyRequest = RequestMethod = "GET" OrElse RequestMethod = "HEAD"
        '即使被当作文件下载，也不允许访问旧的公告、更新、遥测服务。
        If Host = "pcl2-server-1253424809.file.myqcloud.com" OrElse
           Host.EndsWith(".cls.tencentcs.com", StringComparison.OrdinalIgnoreCase) OrElse
           Host = "cls.tencentcs.com" Then Return False
        If IsFileDownload Then Return ReadOnlyRequest
        If Address.Scheme <> Uri.UriSchemeHttps Then Return False

        Select Case Host.ToLowerInvariant()
            Case "login.microsoftonline.com"
                Return RequestMethod = "POST" AndAlso
                    (Address.AbsolutePath = "/consumers/oauth2/v2.0/devicecode" OrElse Address.AbsolutePath = "/consumers/oauth2/v2.0/token")
            Case "login.live.com"
                Return RequestMethod = "POST" AndAlso Address.AbsolutePath = "/oauth20_token.srf"
            Case "user.auth.xboxlive.com"
                Return RequestMethod = "POST" AndAlso Address.AbsolutePath = "/user/authenticate"
            Case "xsts.auth.xboxlive.com"
                Return RequestMethod = "POST" AndAlso Address.AbsolutePath = "/xsts/authorize"
            Case "api.minecraftservices.com"
                Return (RequestMethod = "POST" AndAlso Address.AbsolutePath = "/authentication/login_with_xbox") OrElse
                    (ReadOnlyRequest AndAlso (Address.AbsolutePath = "/entitlements/mcstore" OrElse Address.AbsolutePath = "/minecraft/profile"))
        End Select
        If Not MetadataHosts.Contains(Host) Then Return False
        '资源平台使用 POST 批量查询文件哈希、依赖和下载版本。
        Return ReadOnlyRequest OrElse (RequestMethod = "POST" AndAlso
            (Host = "api.modrinth.com" OrElse Host = "api.curseforge.com" OrElse Host = "mod.mcimirror.top"))
    End Function

    Public Sub EnsureNetworkAllowed(Url As String, Method As HttpMethod, Optional IsFileDownload As Boolean = False)
        If Not IsNetworkAllowed(Url, Method, IsFileDownload) Then
            Throw New NotSupportedException("此修改版仅允许下载及微软正版登录，已禁用此联网功能。")
        End If
    End Sub
End Module
