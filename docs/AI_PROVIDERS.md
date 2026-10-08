# AI providers

Gemini is the default online provider behind `IAiProvider`, `IAgentModel` and `IGeminiVisionProvider`. The chat/agent model and vision model are separately configurable; both default to `gemini-3.8-flash`. The REST client uses `generateContent`, Google function declarations, the `x-goog-api-key` header, image `inlineData`, and JSON response schema for translated regions.

Settings provide a masked key field, save/delete, a lightweight connection test, and a link to Google AI Studio. `CredentialStore` uses Windows Credential Manager; the JSON settings file never contains the API key. Error messages avoid response bodies and secrets.

Online text is sent only when the user enables cloud chat. Screenshot analysis and screen translation are separately opted in; first use explains the upload. Local mode is a graceful offline/local-tools path only; it does not download models. Other providers can implement the same interfaces.

The default model is `gemini-3.8-flash`, a current stable fast model documented by Google. Model identifiers are user configurable. See [Google's model list](https://ai.google.dev/gemini-api/docs/models) and [function-calling guide](https://ai.google.dev/gemini-api/docs/function-calling).
