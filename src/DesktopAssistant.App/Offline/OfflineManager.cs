using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using DesktopAssistant.AI;
using DesktopAssistant.Core;
using DesktopAssistant.Settings;

namespace DesktopAssistant.Offline;

/// <summary>
/// Optional offline mode: downloads (on request only) and runs local engines for speech-to-text and the AI brain.
/// Nothing here is part of the installer; the user picks what to download and sees the exact size first.
/// </summary>
public sealed class OfflineManager : IDisposable
{
    private readonly AppSettings _settings;
    private LocalServer? _whisper, _llama;
    private string? _whisperModel, _llamaModel;

    public static string Root { get; } = Path.Combine(AppLog.DataDirectory, "offline");
    private static string WhisperDir => Path.Combine(Root, "whisper");
    private static string LlamaDir => Path.Combine(Root, "llama");
    private static string ModelsDir => Path.Combine(Root, "models");

    public OfflineManager(AppSettings settings) => _settings = settings;

    // ---------------- state ----------------

    public OfflinePack? SpeechPack => IsInstalled(_settings.OfflineSpeechModel) ? OfflineCatalog.Find(_settings.OfflineSpeechModel) : null;
    public OfflinePack? BrainPack => IsInstalled(_settings.OfflineBrainModel) ? OfflineCatalog.Find(_settings.OfflineBrainModel) : null;
    public bool SpeechReady => SpeechPack is not null;
    public bool BrainReady => BrainPack is not null;

    public static bool IsInstalled(string packId)
    {
        var pack = OfflineCatalog.Find(packId);
        return pack is not null && pack.Files.All(f => IsFilePresent(pack, f));
    }

    private static bool IsFilePresent(OfflinePack pack, OfflineFile file) => file == OfflineCatalog.WhisperRuntime ? File.Exists(Path.Combine(WhisperDir, "whisper-server.exe"))
        : file == OfflineCatalog.LlamaRuntime ? File.Exists(Path.Combine(LlamaDir, "llama-server.exe"))
        : File.Exists(Path.Combine(ModelsDir, file.FileName)) && new FileInfo(Path.Combine(ModelsDir, file.FileName)).Length == file.Size;

    public static long FreeDiskBytes()
    {
        try { return new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace; } catch { return long.MaxValue; }
    }

    public static int TotalRamGb()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? (int)Math.Round(status.ullTotalPhys / 1024.0 / 1024 / 1024) : 0;
    }

    /// <summary>Bytes still to download for a pack (already-present files don't count).</summary>
    public static long RemainingBytes(OfflinePack pack) => pack.Files.Where(f => !IsFilePresent(pack, f)).Sum(f => f.Size);

    // ---------------- install / remove ----------------

    public async Task InstallAsync(OfflinePack pack, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        var missing = pack.Files.Where(f => !IsFilePresent(pack, f)).ToList();
        var total = missing.Sum(f => f.Size);
        if (FreeDiskBytes() < total + 500_000_000) throw new InvalidOperationException($"אין מספיק מקום פנוי בדיסק. צריך {OfflineCatalog.Size(total)} ועוד קצת מרווח.");
        long done = 0;
        foreach (var file in missing)
        {
            var isRuntime = file == OfflineCatalog.WhisperRuntime || file == OfflineCatalog.LlamaRuntime;
            var destination = isRuntime ? Path.Combine(Root, "downloads", file.FileName) : Path.Combine(ModelsDir, file.FileName);
            await Downloader.DownloadAsync(file, destination, progress, done, total, cancellationToken);
            if (isRuntime)
            {
                var target = file == OfflineCatalog.WhisperRuntime ? WhisperDir : LlamaDir;
                if (file == OfflineCatalog.WhisperRuntime) _whisper?.Stop(); else _llama?.Stop();
                Downloader.ExtractFlat(destination, target);
                File.Delete(destination);
            }
            done += file.Size;
        }
        if (pack.Kind == OfflineKind.Speech) { _settings.OfflineSpeechModel = pack.Id; _whisper?.Stop(); _whisper = null; }
        else { _settings.OfflineBrainModel = pack.Id; _llama?.Stop(); _llama = null; }
        SettingsStore.Save(_settings);
        AppLog.Info($"Offline pack installed: {pack.Id}");
    }

    public void Remove(OfflinePack pack)
    {
        if (pack.Kind == OfflineKind.Speech) { _whisper?.Dispose(); _whisper = null; } else { _llama?.Dispose(); _llama = null; }
        foreach (var file in pack.Files.Where(f => f != OfflineCatalog.WhisperRuntime && f != OfflineCatalog.LlamaRuntime))
        {
            try { File.Delete(Path.Combine(ModelsDir, file.FileName)); } catch (Exception ex) { AppLog.Warn("Remove: " + ex.Message); }
        }
        if (_settings.OfflineSpeechModel == pack.Id) _settings.OfflineSpeechModel = "";
        if (_settings.OfflineBrainModel == pack.Id) _settings.OfflineBrainModel = "";
        SettingsStore.Save(_settings);
    }

    // ---------------- speech ----------------

    private LocalServer Whisper()
    {
        var pack = SpeechPack ?? throw new InvalidOperationException("חבילת הדיבור האופליין לא מותקנת (הגדרות ← אופליין).");
        var model = Path.Combine(ModelsDir, pack.Files.Last().FileName);
        if (_whisper is not null && _whisperModel == model) return _whisper;
        _whisper?.Dispose();
        var threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
        _whisperModel = model;
        _whisper = new LocalServer("זיהוי דיבור", Path.Combine(WhisperDir, "whisper-server.exe"),
            port => $"-m \"{model}\" --host 127.0.0.1 --port {port} -t {threads} -l he",
            "/", TimeSpan.FromMinutes(5));
        return _whisper;
    }

    /// <summary>Keep the speech engine loaded (used while the wake word is on, so it answers instantly).</summary>
    public void KeepSpeechLoaded(bool keep)
    {
        if (!SpeechReady) return;
        Whisper().KeepAlive = keep;
    }

    public async Task<string> TranscribeAsync(byte[] wav, string languageCode, string? hint, CancellationToken cancellationToken)
    {
        var server = Whisper();
        await server.EnsureStartedAsync(TimeSpan.FromSeconds(90), cancellationToken);
        using var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "speech.wav");
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("0.0"), "temperature");
        if (!string.IsNullOrWhiteSpace(languageCode)) form.Add(new StringContent(languageCode), "language");
        if (!string.IsNullOrWhiteSpace(hint)) form.Add(new StringContent(hint), "prompt");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        using var response = await http.PostAsync(server.BaseUrl + "/inference", form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        server.Touch();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"זיהוי הדיבור המקומי נכשל ({(int)response.StatusCode}).");
        var text = JsonNode.Parse(body)?["text"]?.GetValue<string>() ?? "";
        return CleanTranscript(text);
    }

    /// <summary>whisper marks silence/noise with tokens like [BLANK_AUDIO] or (music) — drop them.</summary>
    private static string CleanTranscript(string text)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(text, @"\[[^\]]*\]|\([^\)]*\)|\*[^*]*\*", " ");
        return System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
    }

    // ---------------- brain ----------------

    private LocalServer Llama()
    {
        var pack = BrainPack ?? throw new AiException("המוח האופליין לא מותקן. הגדרות ← אופליין ← הורדה.");
        var model = Path.Combine(ModelsDir, pack.Files[1].FileName);
        var mmproj = Path.Combine(ModelsDir, pack.Files[2].FileName);
        if (_llama is not null && _llamaModel == model) return _llama;
        _llama?.Dispose();
        var threads = Math.Clamp(Environment.ProcessorCount / 2, 2, 12);
        _llamaModel = model;
        _llama = new LocalServer("מוח מקומי", Path.Combine(LlamaDir, "llama-server.exe"),
            port => $"-m \"{model}\" --mmproj \"{mmproj}\" --host 127.0.0.1 --port {port} -c 8192 -t {threads} --jinja --no-webui",
            "/health", TimeSpan.FromMinutes(10));
        return _llama;
    }

    /// <summary>An OpenAI-compatible chat model that talks to the local llama-server (started on first use).</summary>
    public IChatModel CreateBrainModel() => new LocalChatModel(this);

    internal async Task<string> EnsureBrainAsync(CancellationToken cancellationToken)
    {
        var server = Llama();
        await server.EnsureStartedAsync(TimeSpan.FromMinutes(4), cancellationToken);
        server.Touch();
        return server.BaseUrl + "/v1";
    }

    public void Dispose()
    {
        _whisper?.Dispose();
        _llama?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}

/// <summary>Wraps the OpenAI-compatible client and starts the local server before each call.</summary>
internal sealed class LocalChatModel(OfflineManager manager) : IChatModel
{
    public static readonly ProviderConfig OfflineConfig = new() { Id = "offline", Kind = ProviderKind.OpenAICompatible, DisplayName = "מוח מקומי (אופליין)", Model = "local", RequiresKey = false };

    public ProviderConfig Config => OfflineConfig;
    public bool CanTranscribe => false;

    public async Task<ModelReply> CompleteAsync(string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolSpec> tools, ModelOptions options, CancellationToken cancellationToken)
    {
        var baseUrl = await manager.EnsureBrainAsync(cancellationToken);
        var config = OfflineConfig.Clone();
        config.BaseUrl = baseUrl;
        return await new OpenAiCompatibleModel(config).CompleteAsync(system, messages, tools, options with { MaxOutputTokens = Math.Min(options.MaxOutputTokens, 4096) }, cancellationToken);
    }

    public Task<string> TranscribeAsync(byte[] wav, string languageCode, CancellationToken cancellationToken, string? hint = null) =>
        manager.TranscribeAsync(wav, languageCode, hint, cancellationToken);

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(["local"]);
}
