$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '../.external/composa'
if (Test-Path $root) { throw "Destination already exists: $root" }
git clone --filter=blob:none --no-checkout https://github.com/dvdstelt/Composa.git $root
if ($LASTEXITCODE -ne 0) { throw 'Clone failed' }
git -C $root sparse-checkout set --no-cone /src/Composa.Core/ '!/src/Composa.Core/Models/' /Directory.Build.props /LICENSE
if ($LASTEXITCODE -ne 0) { throw 'Sparse checkout setup failed' }
git -C $root checkout 86529d3b5355d28ddc28ec3db2e4de1d4d00f63b
if ($LASTEXITCODE -ne 0) { throw 'Checkout failed' }
if (Test-Path (Join-Path $root 'src/Composa.Core/Models')) { throw 'Models must not be downloaded' }
