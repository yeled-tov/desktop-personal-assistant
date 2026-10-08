# Screen translation

`ScreenCaptureProvider` captures the Windows virtual desktop (all monitors, including negative origins). The assistant hides before capture, and the borderless topmost overlay displays the frozen image without changing the source application.

## Gemini Vision path

The user selects Gemini or Automatic and explicitly enables cloud translation. On first use, the app explains that the screenshot will be sent to Gemini. `GeminiAiProvider.TranslateScreenshotAsync` encodes a compressed JPEG and sends it as image `inlineData` with a structured-output schema. The prompt asks for multilingual OCR and Hebrew text regions with normalized `[0,1]` boxes. Coordinates are validated, empty or oversized text is discarded, and no more than 250 regions render.

The overlay uses each box to place a Hebrew RTL label near its source text, estimates font size from box height, and masks the text with a translucent background. Controls: ESC closes, F5 captures and translates again, O toggles translations, +/- changes text size. Errors leave the frozen screenshot visible and show a short message. No screenshot is uploaded if Gemini is not selected, its key is missing, or consent is declined.

## Local path

`IOcrProvider` and `ITranslationProvider` remain separate extension points. The local provider is currently unavailable; the app does not download OCR or translation models automatically. No large model is included in the installer.
