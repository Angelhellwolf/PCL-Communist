param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$BinaryDirectory = '',
    [switch]$SourceOnly
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$repo = Split-Path -Parent $PSScriptRoot
if (-not $BinaryDirectory) { $BinaryDirectory = Join-Path $repo 'artifacts\PCL-Communist-2.99.0.0' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$excludedDirectories = @('.git', '.local', 'bin', 'obj', 'artifacts', '.vs', '.idea', '__pycache__')

function Add-PackageDirectory([IO.Compression.ZipArchive]$Archive, [string]$Directory, [string]$Prefix, [bool]$Source) {
    foreach ($item in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($item.PSIsContainer) {
            if ($excludedDirectories -contains $item.Name) { continue }
            Add-PackageDirectory $Archive $item.FullName ($Prefix + $item.Name + '/') $Source
        } else {
            if ($item.Name -in @('.git', 'build-credentials.json', 'decompiled.cs') -or
                $item.Name -like '.env*' -or $item.Extension -in @('.pfx', '.pem', '.key', '.snk', '.user', '.suo')) { continue }
            if ($Source -and $Directory -eq $repo -and $item.Extension -eq '.zip') { continue }
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($Archive, $item.FullName, $Prefix + $item.Name, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
}

function New-ProjectZip([string]$Path, [string]$Directory, [string]$Prefix, [bool]$Source) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create, [IO.FileAccess]::Write)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try { Add-PackageDirectory $archive $Directory $Prefix $Source }
        finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

$binaryZip = Join-Path $OutputDirectory 'PCL-Communist-2.99.0.0.zip'
$sourceZip = Join-Path $OutputDirectory 'PCL-Communist-2.99.0.0-source.zip'
if (-not $SourceOnly) { New-ProjectZip $binaryZip $BinaryDirectory 'PCL-Communist-2.99.0.0/' $false }
New-ProjectZip $sourceZip $repo 'PCL-Communist-source/' $true

$sourceArchive = [IO.Compression.ZipFile]::OpenRead($sourceZip)
try {
    if ($sourceArchive.Entries.FullName -match '(^|/)(\.git|\.local|bin|obj|artifacts)(/|$)' -or
        $sourceArchive.Entries.FullName -match '(build-credentials\.json|decompiled\.cs)$') {
        throw 'Unexpected private or generated file in source package.'
    }
    if (-not ($sourceArchive.Entries.FullName -contains 'PCL-Communist-source/MeloongCore/Shared/MeloongCore.csproj')) {
        throw 'Source package is missing the pinned MeloongCore source.'
    }
    Write-Output "Source package: $($sourceArchive.Entries.Count) files; private and generated directories excluded."
} finally { $sourceArchive.Dispose() }

$packagePaths = @($sourceZip)
if (-not $SourceOnly) { $packagePaths = @($binaryZip, $sourceZip) }
$hashes = $packagePaths | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 }
if (-not $SourceOnly) {
    $hashLines = $hashes | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" }
    [IO.File]::WriteAllLines((Join-Path $OutputDirectory 'PCL-Communist-2.99.0.0.sha256'), [string[]]$hashLines, (New-Object Text.UTF8Encoding($false)))
}
$hashes
