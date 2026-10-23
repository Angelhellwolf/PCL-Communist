param(
    [ValidateSet('desktop', 'android', 'ios-simulator', 'test')][string]$Target = 'desktop',
    [ValidateSet('', 'win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64', 'android-arm64', 'android-x64', 'iossimulator-arm64', 'iossimulator-x64')][string]$Runtime = '',
    [string]$DotnetPath = 'dotnet',
    [string]$PythonPath = 'python',
    [string]$AndroidSdkDirectory = '',
    [string]$JavaSdkDirectory = '',
    [switch]$Network
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$cross = Join-Path $repo 'CrossPlatform'
$output = Join-Path $repo 'artifacts/cross-platform'

function Invoke-Dotnet([string[]]$Arguments) {
    & $DotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Cross-platform $Target failed (exit $LASTEXITCODE)." }
}

Push-Location $cross
try {
    switch ($Target) {
        'test' {
            $arguments = @('run', '--project', 'PCL.Communist.Tests', '-c', 'Release', '--', "--screenshots=$output/qa")
            if ($Network) { $arguments += '--network' }
            Invoke-Dotnet $arguments
        }
        'desktop' {
            if ($Runtime -notmatch '^(win|linux|osx)-(x64|arm64)$') { throw 'Specify a desktop -Runtime, for example osx-arm64.' }
            $publish = Join-Path $output $Runtime
            Invoke-Dotnet @('publish', 'PCL.Communist.Desktop', '-c', 'Release', '-r', $Runtime, '--self-contained', 'true', '-p:PublishTrimmed=false', '-o', $publish)
            & $PythonPath (Join-Path $PSScriptRoot 'package_cross_platform.py') --runtime $Runtime --input $publish --output (Join-Path $output 'packages')
            if ($LASTEXITCODE -ne 0) { throw 'Desktop packaging failed.' }
        }
        'android' {
            if (-not $Runtime) { $Runtime = 'android-arm64' }
            if ($Runtime -notmatch '^android-(arm64|x64)$') { throw 'Android requires android-arm64 or android-x64.' }
            $arguments = @('publish', 'PCL.Communist.Android', '-c', 'Release', '-r', $Runtime, '-p:AndroidKeyStore=false', '-p:RunAOTCompilation=false', '-o', (Join-Path $output $Runtime))
            if ($AndroidSdkDirectory) { $arguments += "-p:AndroidSdkDirectory=$AndroidSdkDirectory" }
            if ($JavaSdkDirectory) { $arguments += "-p:JavaSdkDirectory=$JavaSdkDirectory" }
            Invoke-Dotnet $arguments
            Write-Output 'Preview APK uses the local debug signing key. Production signing is configured separately.'
        }
        'ios-simulator' {
            if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) {
                throw 'An iOS simulator build requires macOS, Xcode and the .NET iOS workload.'
            }
            if (-not $Runtime) {
                $Runtime = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [Runtime.InteropServices.Architecture]::Arm64) { 'iossimulator-arm64' } else { 'iossimulator-x64' }
            }
            if ($Runtime -notmatch '^iossimulator-(arm64|x64)$') { throw 'Use an iOS simulator runtime.' }
            Invoke-Dotnet @('build', 'PCL.Communist.iOS', '-c', 'Debug', '-r', $Runtime, '-p:EnableCodeSigning=false', '-o', (Join-Path $output $Runtime))
        }
    }
} finally { Pop-Location }
