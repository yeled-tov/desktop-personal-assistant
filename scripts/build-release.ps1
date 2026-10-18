[CmdletBinding()]
param(
  [string]$Version = '2.0.0',
  [switch]$SkipInstaller,
  [switch]$SkipSmokeTest
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'src\DesktopAssistant.App\DesktopAssistant.App.csproj'
$publishDir = Join-Path $root 'artifacts\portable'
$installerDir = Join-Path $root 'artifacts\installer'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'לא נמצא .NET SDK. התקן .NET 10 SDK: https://dotnet.microsoft.com/download' }

Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDir, $installerDir | Out-Null

Write-Host "== Publishing v$Version ==" -ForegroundColor Cyan
dotnet publish $app -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false `
  -p:PublishReadyToRun=true `
  -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'הבנייה נכשלה.' }
$exe = Join-Path $publishDir 'DesktopAssistant.exe'
if (-not (Test-Path $exe)) { throw "קובץ EXE לא נוצר: $exe" }

if (-not $SkipSmokeTest) {
  Write-Host '== Smoke test: launching the real EXE ==' -ForegroundColor Cyan
  $marker = Join-Path $env:LOCALAPPDATA 'DesktopAssistant\smoke-result.txt'
  Remove-Item $marker -ErrorAction SilentlyContinue
  $p = Start-Process -FilePath $exe -ArgumentList '--smoke-test' -PassThru
  if (-not $p.WaitForExit(150000)) { Stop-Process -Id $p.Id -Force; throw 'Smoke test: the app hung.' }
  $result = if (Test-Path $marker) { Get-Content $marker -Raw } else { '(no result file)' }
  Write-Host $result
  if ($env:GITHUB_ACTIONS) { Write-Host ("::notice title=Smoke test::" + ($result -replace "`r","" -replace "`n","%0A")) }
  $log = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'DesktopAssistant\logs') -Filter 'app-*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
  if ($log) { Write-Host '--- app log ---'; Get-Content $log.FullName -Tail 60 }
  if ($p.ExitCode -ne 0 -or -not $result.StartsWith('SMOKE OK')) { throw "Smoke test failed (exit code $($p.ExitCode))." }
  Write-Host 'Smoke test passed.' -ForegroundColor Green
}

if (-not $SkipInstaller) {
  $makensis = (Get-Command makensis -ErrorAction SilentlyContinue).Source
  if (-not $makensis) { $makensis = @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1 }
  if (-not $makensis) { throw 'לא נמצא NSIS (makensis). התקן מ-https://nsis.sourceforge.io או: winget install NSIS.NSIS — או הרץ עם -SkipInstaller.' }
  Push-Location (Join-Path $root 'installer')
  try {
    & $makensis "-DVERSION=$Version" "-DSOURCE_DIR=$publishDir" "-DOUT_FILE=$(Join-Path $installerDir 'DesktopAssistant-Setup.exe')" 'DesktopAssistant.nsi'
    if ($LASTEXITCODE -ne 0) { throw 'יצירת המתקין נכשלה.' }
  } finally { Pop-Location }
  Write-Host "Installer: $(Join-Path $installerDir 'DesktopAssistant-Setup.exe')" -ForegroundColor Green
}
Write-Host "Portable EXE: $exe"
