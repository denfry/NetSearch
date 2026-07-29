param(
    [string]$Version = "0.3.0"
)

$ErrorActionPreference = "Stop"
$architectures = @("win-x86", "win-x64", "win-arm64")

# Build self-contained single-file applications for every Windows 10+ architecture.
foreach ($rid in $architectures) {
    $output = Join-Path "publish" $rid
    dotnet publish src/NetSearch.App/NetSearch.App.csproj `
        -c Release -r $rid --self-contained true `
        -p:Version=$Version -o $output
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid with exit code $LASTEXITCODE"
    }
    Write-Host "Built $output/NetSearch.exe"
}

# Keep clearly named portable artifacts next to the installer.
New-Item -ItemType Directory -Force -Path dist | Out-Null
foreach ($rid in $architectures) {
    Copy-Item "publish/$rid/NetSearch.exe" "dist/NetSearch-$Version-$rid.exe" -Force
}

# 2) Locate the Inno Setup compiler.
$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    foreach ($p in @(
        "${env:LOCALAPPDATA}\Programs\Inno Setup 7\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { $iscc = $p; break }
    }
} else { $iscc = $iscc.Source }

if (-not $iscc) {
    Write-Warning "Inno Setup 6.3+ (ISCC.exe) not found. Install it, e.g.: winget install JRSoftware.InnoSetup"
    Write-Warning "Portable executables in dist/ are ready; skipping installer."
    exit 0
}

# Compile one architecture-aware installer into dist/.
& $iscc "/DAppVersion=$Version" "installer/netsearch.iss"
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}
Write-Host "Built dist/NetSearch-Setup-$Version.exe"

# The in-app updater refuses to execute an installer without this checksum.
Get-ChildItem "dist/NetSearch-Setup-$Version.exe" | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash *$($_.Name)" | Set-Content "$($_.FullName).sha256" -Encoding ascii
}
Write-Host "Built dist/NetSearch-Setup-$Version.exe.sha256"
