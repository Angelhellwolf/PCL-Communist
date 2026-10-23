# PCL Communist 跨平台预览

这是独立于原 Windows WPF 程序的移植工程，位于 `CrossPlatform/`。共用 .NET 10 核心和 Avalonia 12.1.4 界面，并提供桌面、Android、iOS/iPadOS 原生入口。原 Windows 程序的全部功能尚未移植到此预览版。

## 当前能力

| 平台 | 应用工程与构建 | Minecraft Java 本机启动 |
| --- | --- | --- |
| Windows | 原 WPF 正式版继续可用；另有共用界面的桌面预览工程 | 正式版可用；预览版新增原版启动路径 |
| macOS Intel | 自包含桌面程序和 `.app` 打包脚本 | 已实现 Java、系统规则、原生库、classpath 与主线程参数；需 Mac 真机验证 |
| macOS Apple Silicon | 独立 `osx-arm64` 自包含构建 | 选择 ARM Java 与对应原生库；旧版 Intel 原生库会明确提示使用 Intel 构建与 Rosetta，需 Mac 真机验证 |
| Linux | 共用桌面工程，可生成自包含包 | 已实现原版启动路径；核心和界面测试在 Linux 执行 |
| Android arm64 | 原生应用工程，可生成预览 APK | **尚未接入移动端游戏运行时，不能在本机启动游戏** |
| iOS / iPadOS | iPhone/iPad 共用原生入口和模拟器构建工作流 | **尚未接入移动端游戏运行时，不能在本机启动游戏**；编译需要 Mac 与 Xcode |

预览界面提供 17 个开放的静态配色、正式版本列表、微软设备码登录、离线账号、游戏下载与 SHA1 校验、下载取消、已有游戏目录和设置保存。17 个配色是此界面的静态预设，尚未复制 WPF 版本的动态和自定义主题实现。

下载默认优先 BMCLAPI，失败或哈希不符时回退官方源；不按地区限制客户端下载。跨平台核心没有 WPF、Windows Forms、注册表或 `cmd.exe` 依赖。启动命令使用参数列表，路径包含空格也无需 shell。Java 从 Mojang 运行时清单按系统、CPU 架构和游戏所需版本安装，并保留运行时原有许可文件。

移动端目前只下载版本 JSON、客户端和资源文件，不会把 Windows、macOS、Linux 的桌面原生库当成手机运行时。启动按钮会明确禁用并说明缺少的运行时。`IGameRuntime` 预留本机运行时接口，接入时需要完成 JVM、游戏原生库、图形表面、输入与触控、生命周期，以及 iOS 的运行与设备安装条件；这些不包含在现有 APK 或 iOS 入口中。

暂未移植 Forge/Fabric/NeoForge 安装、继承版本、模组、整合包、皮肤和账号长期保存。微软访问令牌仅保留在当前会话内，未写入普通 JSON；关闭程序后需要重新登录。微软真实账号授权需用户在官方页面完成。

## 构建

桌面和测试需要 .NET 10 SDK；打包还需 Python 3.11+。运行时已包含在桌面发布包中，用户无需安装 .NET SDK。

```powershell
# 运行离线检查并输出桌面和移动端宽度的截图
pwsh ./tools/Build-CrossPlatform.ps1 -Target test

# 额外真实下载 Minecraft 1.21.1 客户端并验证 SHA1
pwsh ./tools/Build-CrossPlatform.ps1 -Target test -Network

# 分别打包 Intel Mac、Apple Silicon
pwsh ./tools/Build-CrossPlatform.ps1 -Target desktop -Runtime osx-x64
pwsh ./tools/Build-CrossPlatform.ps1 -Target desktop -Runtime osx-arm64

# Windows / Linux 同样使用 win-x64 / linux-x64
pwsh ./tools/Build-CrossPlatform.ps1 -Target desktop -Runtime win-x64
```

结果在 `artifacts/cross-platform/`；桌面归档和 SHA256 清单在 `packages/`。macOS `.app` 包含 ICNS 图标，可跨系统生成；在 Mac 打包时会使用本机 ad-hoc 签名，尚未进行开发者签名或 Apple 公证。

Android 需要 .NET Android workload、JDK 与 Android SDK：

```powershell
dotnet workload install android --skip-manifest-update
dotnet build CrossPlatform/PCL.Communist.Android -t:InstallAndroidDependencies -f net10.0-android -p:AndroidSdkDirectory=/absolute/android-sdk -p:JavaSdkDirectory=/absolute/jdk -p:AcceptAndroidSDKLicenses=True
pwsh ./tools/Build-CrossPlatform.ps1 -Target android -AndroidSdkDirectory /absolute/android-sdk -JavaSdkDirectory /absolute/jdk
```

生成的 `*-Signed.apk` 使用本机构建环境的调试密钥，仅用于预览。应用使用私有存储目录，要求 Android 6.0/API 23 或更高版本。

iOS 模拟器构建需要 Mac、兼容的 Xcode 和 .NET iOS workload：

```powershell
dotnet workload install ios --skip-manifest-update
pwsh ./tools/Build-CrossPlatform.ps1 -Target ios-simulator
```

模拟器产物不是可安装到 iPhone 的 IPA。真机构建还需匹配的设备签名与配置。工作流 `.github/workflows/cross-platform.yml` 分别构建 Windows、Linux、两种 Mac 架构、Android APK 和 iOS 模拟器应用；新增工作流本身不代表远程构建已经执行成功。

## 验证边界

自动化检查覆盖 OS/架构规则、条件参数、Mac 主线程启动、不同 classpath 分隔符、含空格路径、离线 UUID、会话过期、SHA1 与镜像回退、取消、ZIP 路径、正确的原生库、旧版虚拟资源目录、移动端禁止误用桌面启动流程，以及同一套 XAML 在桌面和手机宽度下的渲染。可加 `--network --java` 实际验证官方客户端和当前宿主的官方 Java 安装。

编译成功、界面截图和启动参数通过检查都不等于各平台已经实机进入 Minecraft。macOS 和移动端需要在对应设备上继续验收；移动端还需要实现上文列出的原生游戏运行时。

## 实现参考

- Avalonia 官方跨平台模板：<https://github.com/AvaloniaUI/avalonia-dotnet-templates>
- Mojang 版本清单：<https://piston-meta.mojang.com/mc/game/version_manifest_v2.json>
- Microsoft 设备码流程：<https://learn.microsoft.com/entra/identity-platform/v2-oauth2-device-code>

此目录新增代码适用仓库《共产共享协议》；Avalonia、.NET、游戏文件和 Java 运行时继续适用各自许可。游戏和 Java 按需下载，不包含在预览安装包内。
