param(
    [string]$OutputDirectory = 'artifacts/fonts',
    [string]$SourceDirectory = ''
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $PSScriptRoot 'fonts-manifest.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath -Encoding UTF8 | ConvertFrom-Json
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $root 'artifacts/google-fonts-index' }
if (-not (Test-Path -LiteralPath (Join-Path $SourceDirectory '.git'))) {
    New-Item -ItemType Directory -Force -Path $SourceDirectory | Out-Null
    & git -C $SourceDirectory init --quiet
    & git -C $SourceDirectory remote add origin 'https://github.com/google/fonts.git'
    & git -C $SourceDirectory fetch --quiet --depth 1 --filter=blob:none origin $manifest.revision
    if ($LASTEXITCODE -ne 0) { throw 'Could not fetch the pinned Google Fonts revision.' }
    & git -C $SourceDirectory sparse-checkout init --cone
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'fonts-families.txt') | & git -C $SourceDirectory sparse-checkout set --stdin
    & git -C $SourceDirectory checkout --quiet --detach FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not check out the selected fonts.' }
}
$revision = & git -C $SourceDirectory rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $revision -ne $manifest.revision) { throw 'Unexpected Google Fonts source revision.' }
$destination = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) }
New-Item -ItemType Directory -Force -Path $destination | Out-Null
foreach ($family in $manifest.families) {
    if ($family.id -notmatch '^[a-z0-9]+$') { throw 'Invalid family identifier.' }
    $source = Join-Path $SourceDirectory "ofl/$($family.id)"
    $target = Join-Path $destination $family.id
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $license = Join-Path $source 'OFL.txt'
    if ((Get-FileHash -LiteralPath $license -Algorithm SHA256).Hash -ne $family.licenseSha256) { throw "License checksum mismatch: $($family.id)" }
    Copy-Item -LiteralPath $license -Destination (Join-Path $target 'OFL.txt') -Force
    foreach ($font in $family.fonts) {
        if ([IO.Path]::GetFileName($font.file) -ne $font.file) { throw 'Invalid font filename.' }
        $path = Join-Path $source $font.file
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $font.sha256) { throw "Font checksum mismatch: $($family.id)/$($font.file)" }
        Copy-Item -LiteralPath $path -Destination (Join-Path $target $font.file) -Force
    }
}
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $destination 'manifest.json') -Force
Write-Host "Prepared $($manifest.families.Count) verified font families in $destination"
