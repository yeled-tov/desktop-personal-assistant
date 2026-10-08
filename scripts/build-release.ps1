[CmdletBinding()]
param([switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'DesktopAssistant.sln'
$app = Join-Path $root 'src\DesktopAssistant.App\DesktopAssistant.App.csproj'
$iconPath = Join-Path $root 'src\DesktopAssistant.App\app.ico'
$publishDir = Join-Path $root 'artifacts\portable'
$installerDir = Join-Path $root 'artifacts\installer'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'לא נמצא .NET SDK. יש להתקין .NET 10 SDK כדי לבנות את האפליקציה.' }
$installedSdks = dotnet --list-sdks
if ($LASTEXITCODE -ne 0 -or -not $installedSdks) { throw 'נמצא רק .NET Runtime ללא SDK. יש להתקין .NET 10 SDK כדי לשחזר, לבנות ולפרסם.' }
if (-not (Get-Command iscc -ErrorAction SilentlyContinue) -and -not $SkipInstaller) { throw 'לא נמצא Inno Setup Compiler (ISCC). התקן Inno Setup או הרץ עם ‎-SkipInstaller‎ כדי לפרסם EXE בלבד.' }

New-Item -ItemType Directory -Force -Path $publishDir, $installerDir | Out-Null
& (Join-Path $PSScriptRoot 'new-icon.ps1')
dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw 'שחזור החבילות נכשל.' }
dotnet build $solution -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'בנייה ב-Release נכשלה.' }
dotnet run --project (Join-Path $root 'tests\DesktopAssistant.Tests\DesktopAssistant.Tests.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'בדיקות הליבה נכשלו.' }
dotnet publish $app -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'פרסום EXE נכשל.' }
$exe = Join-Path $publishDir 'DesktopAssistant.exe'
if (-not (Test-Path $exe)) { throw "קובץ EXE לא נוצר: $exe" }
$publishedIcon = Join-Path $publishDir 'app.ico'
if (-not (Test-Path $publishedIcon)) { Copy-Item -LiteralPath $iconPath -Destination $publishedIcon -Force }
if (-not $SkipInstaller) {
  $iss = Join-Path $root 'installer\DesktopAssistant.iss'
  iscc "/DAppVersion=0.1.0" "/DSourceDir=$publishDir" "/DOutputDir=$installerDir" $iss
  if ($LASTEXITCODE -ne 0) { throw 'יצירת המתקין נכשלה.' }
  $setup = Join-Path $installerDir 'DesktopAssistant-Setup.exe'
  if (-not (Test-Path $setup)) { throw "קובץ המתקין לא נוצר: $setup" }
}
Write-Host "Portable EXE: $exe"
if (-not $SkipInstaller) { Write-Host "Installer: $(Join-Path $installerDir 'DesktopAssistant-Setup.exe')" }
