# Architecture

The WPF client is split into subsystem folders. `MainWindow` owns presentation and cancellation; `CommandRouter` handles deterministic local commands; `GeminiAgentLoop` provides bounded model/tool iteration. `AppServices` composes the launcher, Windows UI Automation, Gemini provider, typed tool registry and agent.

## Agent flow

Gemini receives a fixed allowlist of function declarations. It can request calls by name and typed JSON arguments. The app checks every name against `AgentToolRegistry`, applies action risk and confirmation, runs each tool with cancellation/timeout, and returns a structured function response. Original Gemini content is retained between turns, including call metadata. The agent stops after eight turns or two minutes. No arbitrary shell tool exists.

## Providers

`IAiProvider` and `IAgentModel` separate Gemini from the chat and agent. `IGeminiVisionProvider` handles screen analysis/translation. `ISpeechToTextProvider`, `IWakeWordProvider`, `ITextToSpeechProvider`, `IEmailProvider`, `ICalendarProvider`, `IOcrProvider`, and `ITranslationProvider` define future replaceable integrations. Audio and local OCR/translation are not currently implemented.

Settings are local JSON without secrets; API keys are stored in Windows Credential Manager. Cloud chat, screen understanding, and translation have separate user controls. No model downloads occur.
