# Security and privacy

The application requests `asInvoker`, never elevates, and does not execute shell commands. Startup registration is per-user and only occurs after the user enables the visible setting. No microphone capture or telemetry occurs. Settings and user memory are local JSON without provider secrets.

Gemini chat is off until enabled in Settings. The API key is kept in Windows Credential Manager and sent in a header; it is never logged or written to settings. Screenshot understanding and screen translation have separate opt-ins and first-use consent. Declining consent leaves the screenshot local. API error details are sanitized.

Consequential requests and risky UI button labels get a visible confirmation prompt. URL launching permits HTTP/HTTPS only. Agent tools are allowlisted; there is no arbitrary command execution. Do not add hidden persistence, silent capture/upload, or confirmation bypasses.
