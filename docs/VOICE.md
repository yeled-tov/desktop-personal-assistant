# Voice

Voice provider interfaces exist in `AI/ProviderContracts.cs`: speech-to-text, wake word and text-to-speech. No audio capture is active in this version. Wake-word listening is off and disabled in Settings. This avoids silent or continuous microphone access until a local detector, explicit permission UX, device selection, VAD, cancellation and visible listening state are implemented.

A future implementation should use local wake detection and only record after activation. Audio must not be stored by default. Check Hebrew language availability at runtime and show a clear unsupported-language message.
