<#
  Builds CatSaver for Windows.

    .\build.ps1                 # self-contained single CatSaver.exe in artifacts\publish
    .\build.ps1 -Installer      # ...and CatSaver-Setup.exe (needs Inno Setup 6)
    .\build.ps1 -Test           # run unit tests first
    .\build.ps1 -InstallerOnly  # only (re)build the installer from artifacts\publish (e.g. after signing the exe)
#>
param(
    [switch]$Installer,
    [switch]$InstallerOnly,
    [switch]$Test,
    [string]$Version = "1.0.0"
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if ($InstallerOnly) { $Installer = $true }

if ($Test -and -not $InstallerOnly) {
    dotnet test tests\CatSaver.Tests -c Release
    if ($LASTEXITCODE) { throw "Tests failed" }
}

if (-not $InstallerOnly) {
    dotnet publish src\CatSaver\CatSaver.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:Version=$Version `
        -o artifacts\publish
    if ($LASTEXITCODE) { throw "Publish failed" }
    Write-Host "Built artifacts\publish\CatSaver.exe" -ForegroundColor Green
}

if ($Installer) {
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    $isccPath = if ($iscc) { $iscc.Source } else { "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
    if (-not (Test-Path $isccPath)) { throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isinfo.php or: winget install JRSoftware.InnoSetup" }
    & $isccPath "/DAppVersion=$Version" installer\CatSaver.iss
    if ($LASTEXITCODE) { throw "Installer build failed" }
    Write-Host "Built artifacts\CatSaver-Setup.exe" -ForegroundColor Green
}
