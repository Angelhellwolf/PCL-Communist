param(
    [string]$DotnetPath = 'dotnet',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'artifacts\PCL-Communist-2.99.0.0' }
$project = Join-Path $repo 'Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj'
& $DotnetPath build $project -c Release -v:minimal -p:IncludePrivateBuildCredentials=false
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$binary = Join-Path $repo 'Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe'
Copy-Item -LiteralPath $binary -Destination (Join-Path $OutputDirectory 'PCL Communist.exe') -Force
Get-FileHash -LiteralPath (Join-Path $OutputDirectory 'PCL Communist.exe') -Algorithm SHA256
