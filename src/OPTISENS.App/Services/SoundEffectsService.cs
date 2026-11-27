using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OPTISENS.App.Services;

public static partial class SoundEffectsService
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;

    public static volatile bool Muted;

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundMemory(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    private static readonly byte[][] HitWavs = BuildHitWavs();
    private static readonly byte[] MissWav = SynthesizeTone(170, 95, 0.045, 0.35);
    private static readonly byte[] PhaseWav = SynthesizeChime(523.25, 783.99, 0.09, 0.25);

    private static readonly IntPtr[] HitBuffers = HitWavs.Select(PinWav).ToArray();
    private static readonly IntPtr MissBuffer = PinWav(MissWav);
    private static readonly IntPtr PhaseBuffer = PinWav(PhaseWav);

    private static readonly Lazy<string?> UnixPlayer = new(DetectUnixPlayer);
    private static readonly Lazy<string[]> HitFiles = new(() => HitWavs.Select((w, i) => WriteTempWav($"hit_{i}.wav", w)).ToArray());
    private static readonly Lazy<string> MissFile = new(() => WriteTempWav("miss.wav", MissWav));
    private static readonly Lazy<string> PhaseFile = new(() => WriteTempWav("phase.wav", PhaseWav));

    public static void PlayHit(int streak)
    {
        if (Muted) return;
        int idx = Math.Clamp(streak - 1, 0, HitBuffers.Length - 1);
        if (OperatingSystem.IsWindows())
        {
            try { PlaySoundMemory(HitBuffers[idx], IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT); }
            catch { /* ignore audio device absence */ }
            return;
        }
        PlayUnixAsync(HitFiles.Value[idx]);
    }

    public static void PlayMiss()
    {
        if (Muted) return;
        if (OperatingSystem.IsWindows())
        {
            try { PlaySoundMemory(MissBuffer, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT); }
            catch { /* ignore */ }
            return;
        }
        PlayUnixAsync(MissFile.Value);
    }

    public static void PlayPhaseChange()
    {
        if (Muted) return;
        if (OperatingSystem.IsWindows())
        {
            try { PlaySoundMemory(PhaseBuffer, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT); }
            catch { /* ignore */ }
            return;
        }
        PlayUnixAsync(PhaseFile.Value);
    }

    private static void PlayUnixAsync(string wavPath)
    {
        if (UnixPlayer.Value is not { } player || string.IsNullOrEmpty(wavPath)) return;
        try
        {
            string args = player == "aplay" ? $"-q \"{wavPath}\"" : $"\"{wavPath}\"";
            var p = Process.Start(new ProcessStartInfo(player, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p is not null)
            {
                p.Exited += (_, _) => p.Dispose();
                p.EnableRaisingEvents = true;
            }
        }
        catch { /* ignore audio playback failure */ }
    }

    private static string? DetectUnixPlayer()
    {
        if (OperatingSystem.IsMacOS()) return "afplay";
        foreach (string candidate in new[] { "paplay", "aplay" })
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("which", candidate)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                });
                p?.WaitForExit(500);
                if (p?.ExitCode == 0) return candidate;
            }
            catch { /* try next */ }
        }
        return null;
    }

    private static string WriteTempWav(string name, byte[] wav)
    {
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "optisens-sfx");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name);
            if (!File.Exists(path))
                File.WriteAllBytes(path, wav);
            return path;
        }
        catch { return ""; }
    }

    private static byte[][] BuildHitWavs()
    {
        var arr = new byte[8][];
        for (int i = 0; i < arr.Length; i++)
        {
            double f0 = 780.0 + i * 65.0;
            double f1 = f0 * 1.35;
            arr[i] = SynthesizeTone(f0, f1, 0.028, 0.40);
        }
        return arr;
    }

    private static IntPtr PinWav(byte[] wav)
    {
        var handle = GCHandle.Alloc(wav, GCHandleType.Pinned);
        return handle.AddrOfPinnedObject();
    }

    private static byte[] SynthesizeTone(double freqStartHz, double freqEndHz, double durationS, double volume)
    {
        const int sampleRate = 44100;
        int samples = Math.Max(1, (int)(sampleRate * durationS));
        var pcm = new short[samples];
        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            double u = i / (double)samples;
            double freq = freqStartHz + (freqEndHz - freqStartHz) * u;
            phase += 2.0 * Math.PI * freq / sampleRate;
            double env = Math.Sin(Math.PI * Math.Pow(u, 0.35)) * Math.Exp(-3.5 * u);
            pcm[i] = (short)Math.Clamp(Math.Sin(phase) * env * volume * short.MaxValue, short.MinValue, short.MaxValue);
        }
        return WrapRiffWav(pcm, sampleRate);
    }

    private static byte[] SynthesizeChime(double f1, double f2, double durationS, double volume)
    {
        const int sampleRate = 44100;
        int samples = Math.Max(1, (int)(sampleRate * durationS));
        var pcm = new short[samples];
        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)sampleRate;
            double u = i / (double)samples;
            double f = u < 0.45 ? f1 : f2;
            double env = Math.Exp(-4.0 * (u < 0.45 ? u : u - 0.45));
            pcm[i] = (short)Math.Clamp(Math.Sin(2.0 * Math.PI * f * t) * env * volume * short.MaxValue, short.MinValue, short.MaxValue);
        }
        return WrapRiffWav(pcm, sampleRate);
    }

    private static byte[] WrapRiffWav(short[] pcm, int sampleRate)
    {
        int dataBytes = pcm.Length * sizeof(short);
        using var ms = new MemoryStream(44 + dataBytes);
        using var bw = new BinaryWriter(ms);
        bw.Write("RIFF"u8);
        bw.Write(36 + dataBytes);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)1); // Mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write("data"u8);
        bw.Write(dataBytes);
        foreach (short s in pcm)
            bw.Write(s);
        return ms.ToArray();
    }
}
