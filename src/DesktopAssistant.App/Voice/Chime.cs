using System.Media;
using DesktopAssistant.Core;

namespace DesktopAssistant.Voice;

/// <summary>A soft two-note "I'm listening" sound, generated in memory.</summary>
public static class Chime
{
    private static byte[]? _wav;

    public static void Play()
    {
        try
        {
            _wav ??= Build();
            new SoundPlayer(new MemoryStream(_wav)).Play();
        }
        catch (Exception ex) { AppLog.Warn("Chime failed: " + ex.Message); }
    }

    private static byte[] Build()
    {
        const int rate = 16000;
        var samples = new List<short>();
        void Note(double freq, double seconds)
        {
            var n = (int)(rate * seconds);
            for (var i = 0; i < n; i++)
            {
                var t = (double)i / rate;
                var envelope = Math.Min(1, i / (rate * 0.01)) * Math.Exp(-4.5 * t / seconds);
                samples.Add((short)(Math.Sin(2 * Math.PI * freq * t) * envelope * 9000));
            }
        }
        Note(784, 0.09);   // G5
        Note(1175, 0.16);  // D6
        var pcm = new byte[samples.Count * 2];
        Buffer.BlockCopy(samples.ToArray(), 0, pcm, 0, pcm.Length);
        return AudioRecorder.ToWav(pcm);
    }
}
