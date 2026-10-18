namespace DesktopAssistant.Offline;

public enum OfflineKind { Speech, Brain }

public sealed record OfflineFile(string Url, string FileName, long Size);

/// <summary>A downloadable offline component. Sizes are exact (checked against the servers when this version was built).</summary>
public sealed record OfflinePack(string Id, OfflineKind Kind, string Title, string Description, int RamGb, OfflineFile[] Files)
{
    public long TotalBytes => Files.Sum(f => f.Size);
}

/// <summary>
/// Everything the offline mode can download. Engines are the open-source whisper.cpp (speech) and
/// llama.cpp (language + vision model), pinned to versions verified by CI on Windows.
/// </summary>
public static class OfflineCatalog
{
    public static readonly OfflineFile WhisperRuntime = new(
        "https://github.com/ggml-org/whisper.cpp/releases/download/b5454/whisper-bin-x64.zip", "whisper-bin-x64.zip", 8_928_640);

    public static readonly OfflineFile LlamaRuntime = new(
        "https://github.com/ggml-org/llama.cpp/releases/download/b11719/llama-b11719-bin-win-cpu-x64.zip", "llama-bin-win-cpu-x64.zip", 19_662_517);

    private const string WhisperHf = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
    private const string Qwen2B = "https://huggingface.co/Qwen/Qwen3-VL-2B-Instruct-GGUF/resolve/main/";
    private const string Qwen4B = "https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct-GGUF/resolve/main/";

    public static readonly IReadOnlyList<OfflinePack> Speech =
    [
        new("speech-small", OfflineKind.Speech, "בסיסי — קל ומהיר",
            "זיהוי דיבור Whisper Small. מהיר גם במחשב חלש; העברית סבירה.", 1,
            [WhisperRuntime, new(WhisperHf + "ggml-small-q5_1.bin", "ggml-small-q5_1.bin", 190_085_487)]),
        new("speech-turbo", OfflineKind.Speech, "מומלץ — עברית טובה",
            "Whisper Large-v3 Turbo (דחוס). איזון טוב בין דיוק למהירות.", 2,
            [WhisperRuntime, new(WhisperHf + "ggml-large-v3-turbo-q5_0.bin", "ggml-large-v3-turbo-q5_0.bin", 574_041_195)]),
        new("speech-ivrit", OfflineKind.Speech, "הכי מדויק בעברית",
            "מודל של ivrit.ai שאומן במיוחד על עברית. כבד ואיטי יותר במחשבים בלי כרטיס מסך.", 3,
            [WhisperRuntime, new("https://huggingface.co/ivrit-ai/whisper-large-v3-turbo-ggml/resolve/main/ggml-model.bin", "ggml-ivrit-turbo.bin", 1_624_555_275)]),
    ];

    public static readonly IReadOnlyList<OfflinePack> Brains =
    [
        new("brain-2b", OfflineKind.Brain, "קל — למחשבים עם 8GB זיכרון",
            "Qwen3-VL 2B: מבין טקסט ותמונות (מסך), מפעיל את המחשב. מהיר, אבל פחות חכם ופחות טוב בעברית.", 4,
            [LlamaRuntime, new(Qwen2B + "Qwen3VL-2B-Instruct-Q4_K_M.gguf", "Qwen3VL-2B-Instruct-Q4_K_M.gguf", 1_107_409_952),
             new(Qwen2B + "mmproj-Qwen3VL-2B-Instruct-Q8_0.gguf", "mmproj-Qwen3VL-2B-Instruct-Q8_0.gguf", 445_053_216)]),
        new("brain-4b", OfflineKind.Brain, "מומלץ — למחשבים עם 16GB זיכרון",
            "Qwen3-VL 4B: חכם יותר, עברית טובה יותר, רואה את המסך ומתרגם. איטי יותר במחשב בלי כרטיס מסך.", 6,
            [LlamaRuntime, new(Qwen4B + "Qwen3VL-4B-Instruct-Q4_K_M.gguf", "Qwen3VL-4B-Instruct-Q4_K_M.gguf", 2_497_281_664),
             new(Qwen4B + "mmproj-Qwen3VL-4B-Instruct-Q8_0.gguf", "mmproj-Qwen3VL-4B-Instruct-Q8_0.gguf", 453_974_304)]),
    ];

    public static OfflinePack? Find(string id) => Speech.Concat(Brains).FirstOrDefault(p => p.Id == id);

    public static string Size(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1_000_000_000.0:0.0}GB" : $"{bytes / 1_000_000.0:0}MB";
}
