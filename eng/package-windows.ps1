param(
    [Parameter(Mandatory = $true)][string]$FfmpegArchive,
    [string]$OutputDirectory = "artifacts/windows-package",
    [string]$ZipPath = "artifacts/StickerForge-Studio-win-x64.zip"
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$archive = (Resolve-Path -LiteralPath $FfmpegArchive).Path
$expectedArchive = 'E174AF1BC75771DB087A1E9AB789D1F004215CA5B545D5CE826E2C8EEC873EFB'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedArchive) { throw 'FFmpeg archive SHA-256 mismatch.' }
$package = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$zip = [IO.Path]::GetFullPath((Join-Path $root $ZipPath))
$extract = Join-Path $root 'artifacts/ffmpeg-extract'
New-Item -ItemType Directory -Force -Path $extract | Out-Null
& tar -xf $archive -C $extract
if ($LASTEXITCODE -ne 0) { throw 'Could not extract FFmpeg archive.' }
$upstream = Join-Path $extract 'ffmpeg-n9.0.2-62-gac1c6e003f-win64-lgpl-9.0'
$tools = Join-Path $package 'tools/ffmpeg/windows/x64'
New-Item -ItemType Directory -Force -Path $tools | Out-Null
$hashes = @{
    'ffmpeg.exe' = 'ACE36305D537A20868A776819BE530D3CE042CF93F70ED07A9CBDBBE7B03579C'
    'ffprobe.exe' = 'F7D94376A390D38BAAEB151D29E37E94E9E374B0664E0436C8C3BA69A7A95AAE'
}
foreach ($file in $hashes.Keys) {
    $source = Join-Path $upstream "bin/$file"
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $hashes[$file]) { throw "$file SHA-256 mismatch." }
}
& dotnet publish (Join-Path $root 'src/StickerForge.Desktop/StickerForge.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:PublishTrimmed=false -p:PublishSingleFile=false -o $package
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
foreach ($file in $hashes.Keys) { Copy-Item -LiteralPath (Join-Path $upstream "bin/$file") -Destination $tools -Force }
Set-Content -LiteralPath (Join-Path $package 'standalone-windows.txt') -Value 'StickerForge Windows x64 standalone package' -Encoding UTF8
$notice = Join-Path $package 'licenses'
New-Item -ItemType Directory -Force -Path $notice | Out-Null
Copy-Item -LiteralPath (Join-Path $upstream 'LICENSE.txt') -Destination (Join-Path $notice 'FFmpeg-LGPL-3.0.txt') -Force
Copy-Item -Path (Join-Path $root 'licenses/*.txt') -Destination $notice -Force
Copy-Item -LiteralPath (Join-Path $root 'docs/LICENSING.md') -Destination (Join-Path $notice 'PROJECT-LICENSING.md') -Force
Copy-Item -LiteralPath (Join-Path $root 'docs/WINDOWS-DISTRIBUTION.md') -Destination (Join-Path $package 'THIRD-PARTY-NOTICES.md') -Force
foreach ($required in @('StickerForge.Desktop.exe', 'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'libSkiaSharp.dll', 'standalone-windows.txt', 'tools/ffmpeg/windows/x64/ffmpeg.exe', 'tools/ffmpeg/windows/x64/ffprobe.exe', 'licenses/FFmpeg-LGPL-3.0.txt', 'THIRD-PARTY-NOTICES.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $required) -PathType Leaf)) { throw "Package missing $required" }
}
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip -Force -CompressionLevel Optimal
Add-Type -AssemblyName System.IO.Compression.FileSystem
$contents = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($contents.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($required in @('StickerForge.Desktop.exe', 'hostfxr.dll', 'coreclr.dll', 'tools/ffmpeg/windows/x64/ffmpeg.exe', 'tools/ffmpeg/windows/x64/ffprobe.exe', 'licenses/FFmpeg-LGPL-3.0.txt', 'THIRD-PARTY-NOTICES.md')) {
        if ($required -notin $names) { throw "ZIP missing $required" }
    }
} finally { $contents.Dispose() }
Write-Host "ZIP: $zip ($((Get-Item -LiteralPath $zip).Length) bytes)"
