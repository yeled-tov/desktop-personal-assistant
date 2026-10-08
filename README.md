# Desktop Personal Assistant (עוזר אישי למחשב)

Version 0.1.0 · Windows 10/11 x64 · WPF / .NET 10

This is a Hebrew-first Windows tray assistant with optional Gemini Online, a bounded multi-step tool agent, Windows UI Automation, and Gemini Vision screen translation.

## Implemented

- Tray icon, chat window, first-run welcome, RTL Hebrew-first interface, and global hotkeys: `Ctrl+Alt+Shift+A` and `Ctrl+Alt+Shift+T`.
- Application and website launching, YouTube/Google search, basic arithmetic, and closing a named visible window.
- Settings, per-user startup choice, editable local memory, Hebrew/English labels for the main panel.
- Gemini API key entry, secure Windows Credential Manager storage, connection test, configurable chat/vision models, and explicit cloud-text opt-in.
- Bounded Gemini agent loop: maximum eight model turns, 2-minute deadline, 20-second tool timeout, progress, cancellation, UI Automation tools, and risky-click confirmation.
- Gemini Vision screenshot analysis and screen translation with separate privacy opt-ins. Translation requests return structured normalized text boxes, which are validated and rendered over the frozen screenshot. Controls: ESC close, F5 recapture/retranslate, O toggle translated overlay, +/- resize text.
- Offline local actions remain usable without an API key. The app does not download local models.

## Limitations

Speech recognition, local wake-word detection, text-to-speech, Gemini Live, local OCR/translation, direct Gmail/Outlook APIs, coordinate clicking, and arbitrary command execution are not implemented. Browser email workflows depend on the accessible UI exposed by the browser and may need user intervention. Local translation mode currently freezes the screenshot and reports that no local OCR engine is installed. The English UI coverage is partial.

## Architecture

The WPF app is split into subsystem folders under `src/DesktopAssistant.App`: `Automation`, `AI`, `ScreenTranslation`, `Configuration`, `Localization`, and `Infrastructure`. `GeminiAgentLoop` uses typed tool contracts and an allowlisted registry. The AI provider, voice, email, calendar, OCR and translation boundaries remain independently replaceable. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Build and test

Prerequisites: Windows 10/11 x64, .NET 10 SDK, PowerShell, and Inno Setup. Run `scripts/build-release.ps1` to restore, build Release, run the dependency-free logic/mock checks, publish self-contained win-x64, and compile the installer. Use `-SkipInstaller` to publish only the portable EXE. The GitHub Actions workflow builds the same outputs on a hosted Windows runner.

Outputs: `artifacts/portable/DesktopAssistant.exe` and `artifacts/installer/DesktopAssistant-Setup.exe`. Settings remain under `%LOCALAPPDATA%\DesktopAssistant` and are preserved by uninstall.

## Gemini setup

In Settings, choose Gemini Online, paste the key into the masked field, select **שמור מפתח**, then **בדוק חיבור**. The key is stored in Windows Credential Manager, not the settings file. **פתח Google AI Studio** opens [the official API-key page](https://aistudio.google.com/app/apikey). Enable cloud chat to send typed messages. Screenshot understanding and screen translation each have a separate opt-in and consent prompt. Model names for chat/agent and vision can be configured separately; the default is `gemini-3.8-flash`.

## Screen translation

The capture provider captures the virtual desktop. Gemini receives a compressed image only after screen-translation opt-in. Its structured response contains multilingual text and normalized boxes; invalid boxes are discarded. The overlay does not alter the underlying application. See [docs/SCREEN_TRANSLATION.md](docs/SCREEN_TRANSLATION.md).

## Hotkeys, privacy and safety

`Ctrl+Alt+Shift+A` opens the assistant; `Ctrl+Alt+Shift+T` opens screen translation. The app checks whether Windows registered each shortcut and reports conflicts in its status line. Startup errors are recorded in `%LOCALAPPDATA%\DesktopAssistant\startup.log`. The wake-word setting is disabled because local detection is not implemented; no microphone is used. Gemini requests use an API key in an HTTP header. No secret is written to settings or logs. Risky UI buttons require confirmation. The app runs as the current user, never elevates, and only adds startup behavior after the visible user choice. See [docs/SECURITY.md](docs/SECURITY.md).

## מדריך התקנה קצר

1. הפעל את `DesktopAssistant-Setup.exe` והתקן מתפריט ההתקנה.
2. אפשר להמשיך ללא Gemini לפעולות המקומיות, או לפתוח הגדרות ולחבר מפתח Gemini.
3. `Ctrl+Alt+Shift+A` פותח את העוזר. `Ctrl+Alt+Shift+T` מפעיל תרגום מסך; `Esc` סוגר.
4. צילומי מסך נשלחים ל-Gemini רק לאחר הסכמה נפרדת לתרגום או לניתוח.
5. קול ותרגום מקומי עדיין אינם זמינים; האפליקציה לא מורידה מודלים גדולים.
