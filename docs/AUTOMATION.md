# Automation

Local deterministic tools include application/URL launch, web and YouTube search, arithmetic, named window close, and wait. The Gemini agent can additionally inspect accessible controls, read an active window, click an accessible control by name, type into a named non-password field, press an allowlisted keyboard shortcut, switch/minimize/maximize windows, scroll, and capture a local screenshot.

UI Automation is used before any fallback; no coordinate clicking is implemented. Tool schemas use concrete input records. Calls are allowlisted, bounded by step/time limits, cancellable, and return concise results. UI Automation is dependent on the target app exposing accessible controls; browser page access varies.

Risky button names such as Send, Delete, Submit, Continue, Save and their Hebrew equivalents require visible confirmation. There is no shell/PowerShell tool, direct email provider, purchase tool or install tool. Sending/deleting isn't available as a standalone integration.
