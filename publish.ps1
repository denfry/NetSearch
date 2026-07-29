$ErrorActionPreference = "Stop"

# 1) Publish the portable single-file exe (existing behavior).
dotnet publish src/NetSearch.App/NetSearch.App.csproj -c Release -o publish
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}
Write-Host "Built publish/NetSearch.exe"

# 2) Locate the Inno Setup compiler.
$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    foreach ($p in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { $iscc = $p; break }
    }
} else { $iscc = $iscc.Source }

if (-not $iscc) {
    Write-Warning "Inno Setup (ISCC.exe) not found. Install it, e.g.: winget install JRSoftware.InnoSetup"
    Write-Warning "Portable publish/NetSearch.exe is ready; skipping installer."
    exit 0
}

# 3) Compile the installer into dist/.
New-Item -ItemType Directory -Force -Path dist | Out-Null
& $iscc "installer/netsearch.iss"
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}
Write-Host "Built dist/NetSearch-Setup-0.2.0.exe"
