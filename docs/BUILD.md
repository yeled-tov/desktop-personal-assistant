# Build

Install the .NET 10 SDK and Inno Setup on Windows x64. Run `scripts/build-release.ps1` in PowerShell. The script restores and builds the solution, runs its dependency-free logic, mocked agent and mocked vision checks, publishes a self-contained single-file `win-x64` EXE, then compiles the Inno Setup installer.

Use `-SkipInstaller` to produce only the portable EXE. Outputs: `artifacts/portable/DesktopAssistant.exe` and `artifacts/installer/DesktopAssistant-Setup.exe`. The installer offers desktop shortcut and startup tasks, supports uninstall, and leaves `%LOCALAPPDATA%\DesktopAssistant` settings intact.
