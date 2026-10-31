using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using S3.Voice.Audio;
using S3.Voice.Pipeline;

// S3 voice latency probe, CPU only. Nothing is ever played through the speakers.
//   tts                                  time to first audio and real-time factor for typical replies
//   synth                                endpoint delay, STT time and accuracy on English commands spoken by the TTS voice
//   files <session folder>               the same on the commands recorded with aldren-record (manifest.jsonl)
//   live --device <name> [--seconds N]   one pinned input in real time; stops with "Input device disappeared" if it goes
//   devices                              lists the active inputs
// Options: --threads N (default 2), --hangover seconds (default 0.4).

const string Models = @"E:\AldrenData\voice\models";
const string ResultsDir = @"E:\AldrenData\bench\s3";
string[] englishCommands =
[
    "Open Spotify.", "Set the volume to thirty percent.", "What's on my calendar tomorrow?", "Remind me to call mum at seven.",
    "Close all the browser windows.", "Turn on focus mode for an hour.", "What's the weather like this afternoon?", "Pause the music.",
    "Take a note: buy milk and eggs.", "Show me my downloads folder.", "Move this window to the left monitor.",
    "How long until my next meeting?", "Search the web for cheap flights to Lisbon.", "Mute notifications until six.",
    "What time is it in Tokyo?", "Start a five minute timer.", "Switch to gaming mode.", "What's on my screen right now?",
    "Open Visual Studio Code in my project folder.", "Skip to the next song.",
];

var mode = args.FirstOrDefault() ?? "tts";
if (mode == "devices")
{
    CaptureDevice.Inputs().ForEach(Console.WriteLine);
    return 0;
}

if (mode == "format")
{
    using var device = CaptureDevice.Open(Option("--device") ?? throw new ArgumentException("format needs --device"));
    Console.WriteLine($"{device.Name}: Windows shared-mode format {device.Format}");
    if (args.Contains("--measure"))
    {
        // Counts frames for a few seconds to compare the real delivery rate with the advertised one; keeps no audio.
        device.Start();
        Thread.Sleep(500);
        var (startFrames, clock) = (device.RawFrames, System.Diagnostics.Stopwatch.StartNew());
        Thread.Sleep(3000);
        Console.WriteLine($"WASAPI delivered {(device.RawFrames - startFrames) / clock.Elapsed.TotalSeconds / 1000:F1} kHz of frames");
    }

    return 0;
}

if (mode == "wavinfo")
{
    var samples = AudioUtil.ReadWav(args[1]);
    var peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);
    var rms = samples.Length == 0 ? 0 : Math.Sqrt(samples.Average(s => (double)s * s));
    Console.WriteLine($"{samples.Length} samples ({samples.Length / (double)AudioUtil.SampleRate:F2} s), peak {20 * Math.Log10(Math.Max(peak, 1e-9)):F1} dBFS, rms {20 * Math.Log10(Math.Max(rms, 1e-9)):F1} dBFS");
    return 0;
}

var threads = int.Parse(Option("--threads") ?? "2", CultureInfo.InvariantCulture);
var hangover = float.Parse(Option("--hangover") ?? "0.4", CultureInfo.InvariantCulture);
var report = new StringBuilder();
var englishOnly = args.Contains("--en-only");
var engine = Option("--tts") ?? "kokoro";
var fp32 = args.Contains("--fp32");
using var probe = new VoiceProbe(Models, threads, hangover, englishOnly, engine, fp32);
Line($"## S3 {mode}, {DateTime.Now:yyyy-MM-dd HH:mm} ({threads} CPU threads, {hangover * 1000:F0} ms VAD hangover, TTS {engine}{(engine == "kokoro" ? fp32 ? " fp32" : " int8" : "")}{(englishOnly ? ", English-only front end" : "")})");

var code = mode switch
{
    "tts" => Tts(),
    "synth" => Synth(),
    "files" => Files(args.ElementAtOrDefault(1) ?? throw new ArgumentException("files needs a session folder")),
    "live" => Live(Option("--device") ?? throw new ArgumentException("live needs --device"), int.Parse(Option("--seconds") ?? "60", CultureInfo.InvariantCulture)),
    _ => throw new ArgumentException($"Unknown mode '{mode}'."),
};

Directory.CreateDirectory(ResultsDir);
File.WriteAllText(Path.Combine(ResultsDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{mode}.md"), report.ToString());
File.AppendAllText(Path.Combine(ResultsDir, "results.md"), report + Environment.NewLine);
return code;

int Tts()
{
    string[] replies =
    [
        "Sure, boss.",
        "Opening Spotify now, boss.",
        "Your next meeting is at three with the design team. Shall I set a reminder?",
    ];
    probe.Speak("Warming up.", 0);
    Line("| Reply | Voice | First audio p50 / max | Total p50 | Audio length | Real-time factor |");
    Line("|---|---|---|---|---|---|");
    foreach (var reply in replies)
    {
        // Kokoro v1.1: af_maple and bf_vale, two of its three English voices. Piper LibriTTS-R: two of its 904 speakers.
        foreach (var speaker in probe.TtsEngine == "piper" ? new[] { 0, 100 } : [0, 2])
        {
            var runs = Enumerable.Range(0, 5).Select(_ => probe.Speak(reply, speaker)).ToList();
            var first = runs.Select(r => r.FirstChunkMs).Order().ToList();
            var total = runs.Select(r => r.TotalMs).Order().ToList();
            var seconds = runs[0].Samples.Length / (double)probe.TtsSampleRate;
            Line($"| {reply} | {speaker} | {first[2]:F0} / {first[^1]:F0} ms | {total[2]:F0} ms | {seconds:F2} s | {total[2] / 1000 / seconds:F2} |");
        }
    }

    Line(probe.TtsEngine == "piper"
        ? "French: not measured; this Piper voice is English only."
        : "French: not measurable with this model; Kokoro v1.1 has no French voice.");
    return 0;
}

int Synth()
{
    using var vad = probe.CreateVad();
    var rows = new List<Row>();
    foreach (var command in englishCommands)
    {
        var speech = AudioUtil.Resample(probe.Speak(command, 2).Samples, probe.TtsSampleRate, AudioUtil.SampleRate);
        float[] audio = [.. AudioUtil.Silence(0.6), .. speech, .. AudioUtil.Silence(1.2)];
        rows.Add(Measure(vad, command, "en", audio));
    }

    Summarise(rows, "20 English commands spoken by the Kokoro voice (clean synthetic speech, so a pipeline check rather than the real accuracy)");
    return 0;
}

int Files(string folder)
{
    using var vad = probe.CreateVad();
    var rows = new List<Row>();
    foreach (var line in File.ReadAllLines(Path.Combine(folder, "manifest.jsonl")).Where(l => l.Trim().Length > 0))
    {
        using var entry = JsonDocument.Parse(line);
        var root = entry.RootElement;
        if (root.GetProperty("kind").GetString() == "command")
        {
            var audio = AudioUtil.ReadWav(Path.Combine(folder, root.GetProperty("file").GetString()!));
            var text = root.GetProperty("text").GetString()!;
            var lang = root.GetProperty("cue").GetString()!;
            rows.Add(args.Contains("--whole") ? MeasureWhole(text, lang, audio) : Measure(vad, text, lang, audio));
        }
    }

    Summarise(rows, $"recorded commands in {folder}");
    return 0;
}

int Live(string name, int seconds)
{
    using var vad = probe.CreateVad();
    using var device = CaptureDevice.Open(name);
    var chunks = new BlockingCollection<float[]>();
    string? lostReason = null;
    device.Samples += chunks.Add;
    device.Lost += reason => lostReason = reason;
    Line($"Listening on {device.Name} ({device.Format}) for {seconds} s.");
    device.Start();

    long heard = 0;
    var until = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < until)
    {
        if (lostReason is not null)
        {
            Console.Error.WriteLine($"Input device disappeared: {lostReason}");
            Line($"Input device disappeared: {lostReason}. Stopped without switching to another input.");
            return 2;
        }

        if (!chunks.TryTake(out var chunk, 100))
        {
            continue;
        }

        vad.AcceptWaveform(chunk);
        heard += chunk.Length;
        while (!vad.IsEmpty())
        {
            var segment = vad.Front();
            var delay = (heard - (segment.Start + segment.Samples.Length)) * 1000.0 / AudioUtil.SampleRate;
            var (text, ms) = probe.Transcribe(segment.Samples);
            Line($"- {segment.Samples.Length / (double)AudioUtil.SampleRate:F1} s of speech, endpoint {delay:F0} ms, STT {ms:F0} ms: {text}");
            vad.Pop();
        }
    }

    return 0;
}

Row Measure(SherpaOnnx.VoiceActivityDetector vad, string reference, string lang, float[] audio)
{
    var heard = probe.Endpoint(vad, audio);
    if (heard is null)
    {
        return new Row(reference, lang, 0, 0, double.NaN, double.NaN, "(no speech detected)", Wer.Words(reference).Length, Wer.Words(reference).Length);
    }

    var (text, ms) = probe.Transcribe(heard.Utterance);
    var (errors, words) = Wer.Count(reference, text);
    return new Row(reference, lang, heard.Utterance.Length / (double)AudioUtil.SampleRate, heard.Segments, heard.DelayMs, ms, text, errors, words);
}

// Diagnostic: the whole clip straight to recognition, no VAD.
Row MeasureWhole(string reference, string lang, float[] audio)
{
    var (text, ms) = probe.Transcribe(audio);
    var (errors, words) = Wer.Count(reference, text);
    return new Row(reference, lang, audio.Length / (double)AudioUtil.SampleRate, 1, double.NaN, ms, text, errors, words);
}

void Summarise(List<Row> rows, string source)
{
    Line($"Source: {source}.");
    foreach (var lang in rows.Select(r => r.Lang).Distinct())
    {
        var subset = rows.Where(r => r.Lang == lang).ToList();
        Line($"- Word error rate ({lang}): {100.0 * subset.Sum(r => r.Errors) / Math.Max(1, subset.Sum(r => r.Words)):F1}% over {subset.Count} commands");
    }

    var endpoints = rows.Select(r => r.EndpointMs).Where(d => !double.IsNaN(d)).Order().ToList();
    var stt = rows.Where(r => !double.IsNaN(r.SttMs)).ToList();
    var sttMs = stt.Select(r => r.SttMs).Order().ToList();
    var longer = stt.Where(r => r.Seconds is >= 3 and <= 5).Select(r => r.SttMs).Order().ToList();
    Line($"- Endpoint delay after the last word: p50 {Median(endpoints):F0} ms, max {(endpoints.Count > 0 ? endpoints[^1] : double.NaN):F0} ms; " +
         $"split by a pause: {rows.Count(r => r.Segments > 1)}; speech running to the end of the clip: {rows.Count(r => double.IsNaN(r.EndpointMs) && r.Segments > 0)}");
    Line($"- STT time: p50 {Median(sttMs):F0} ms, max {(sttMs.Count > 0 ? sttMs[^1] : double.NaN):F0} ms for {Median(stt.Select(r => r.Seconds).Order().ToList()):F1} s utterances (p50); " +
         $"3–5 s utterances: {(longer.Count > 0 ? $"p50 {Median(longer):F0} ms over {longer.Count}" : "none")}; real-time factor p50 {Median(stt.Select(r => r.SttMs / 1000 / r.Seconds).Order().ToList()):F3}");
    Line($"- Endpoint + STT (p50): {Median(endpoints) + Median(sttMs):F0} ms, before the reply's first audio");
    Line("");
    Line("| Said | Heard | Errors | Speech | Endpoint | STT |");
    Line("|---|---|---|---|---|---|");
    foreach (var r in rows)
    {
        Line($"| {r.Reference} | {r.Heard} | {r.Errors}/{r.Words} | {r.Seconds:F1} s | {r.EndpointMs:F0} ms | {r.SttMs:F0} ms |");
    }
}

static double Median(List<double> sorted) => sorted.Count == 0 ? double.NaN : sorted[sorted.Count / 2];

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

void Line(string text)
{
    Console.WriteLine(text);
    report.AppendLine(text);
}

internal sealed record Row(string Reference, string Lang, double Seconds, int Segments, double EndpointMs, double SttMs, string Heard, int Errors, int Words);
