using System.Diagnostics;
using Aldren.Contracts;
using Aldren.Core.Ipc;
using Aldren.Core.Orchestration;
using Aldren.Core.Settings;
using Aldren.Inference;
using Aldren.Voice;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aldren.Core.Voice;

/// <summary>
/// The voice turn (ARCHITECTURE §10): push-to-talk or the wake word → listen until you stop → recognise → orchestrator →
/// speak sentence by sentence → an 8 s follow-up window without the wake word. Half-duplex: what the microphone hears
/// while ALDREN speaks is ignored. The hotkey, the wake word or "stop" interrupts.
/// </summary>
public sealed partial class VoiceLoop(
    VoiceOptions options,
    Orchestrator orchestrator,
    SettingsStore settings,
    ResidencyManager residency,
    AppConnections connections,
    ModeState modes,
    IServiceProvider services,
    ILogger<VoiceLoop> log) : BackgroundService
{
    private enum Mode
    {
        Idle,
        Listening,
        Busy,
        Speaking,
    }

    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private readonly Lock _audioLock = new();
    private SpeechRecognizer? _recognizer;
    private Speaker? _speaker;
    private WakeWordDetector? _wake;
    private Microphone? _microphone;
    private UtteranceCollector? _collector;
    private TaskCompletionSource<float[]?>? _utterance;
    private CancellationTokenSource? _speech;
    private volatile Mode _mode = Mode.Idle;
    private volatile bool _listenAgain;
    private volatile bool _interruptedByHotkey;
    private volatile bool _endTurn;
    private readonly Queue<float> _wakeHistory = new();
    private CancellationToken _stopping;

    /// <summary>The hotkey: interrupts speech, or starts a turn.</summary>
    public Task PushToTalkAsync()
    {
        if (_mode == Mode.Speaking)
        {
            _interruptedByHotkey = true;
            _speech?.Cancel();
            return Task.CompletedTask;
        }

        if (_mode == Mode.Listening)
        {
            // Already listening (often the follow-up window right after you stopped ALDREN): the press means "I'm about to
            // talk", so start the wait for speech again instead of ending the turn.
            _listenAgain = true;
            return Task.CompletedTask;
        }

        _ = Task.Run(() => RunTurnAsync(wokenByWakeWord: false, _stopping));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Something ALDREN says on its own (a reminder, the greeting). Spoken only when no conversation is under way and
    /// speaking is allowed (not gaming, not over a fullscreen app, replies not switched off); otherwise the notification
    /// that comes with it is enough. The Stop button, Escape and the hotkey stop it like any reply.
    /// </summary>
    public async Task<bool> AnnounceAsync(string text, CancellationToken ct)
    {
        if (_speaker is null || modes.Silent || !settings.Current.Voice.SpeakReplies || residency.GamingMode || GamingDetector.FullscreenAppRunning())
        {
            return false;
        }

        if (!await _turnGate.WaitAsync(0, ct))
        {
            return false;
        }

        try
        {
            using var speech = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _speech = speech;
            _mode = Mode.Speaking;
            await ShowAsync(VoiceState.Speaking, text);
            try
            {
                await _speaker.SpeakAsync(text, speech.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                log.LogInformation("Announcement stopped");
            }

            return true;
        }
        finally
        {
            _speech = null;
            _mode = Mode.Idle;
            _endTurn = false;
            await ShowAsync(VoiceState.Idle, "");
            _turnGate.Release();
        }
    }

    /// <summary>The Stop button or Escape: silence now, stop writing the reply, and end the turn (no follow-up).</summary>
    public Task StopSpeakingAsync()
    {
        if (_mode is Mode.Idle)
        {
            return Task.CompletedTask;
        }

        log.LogInformation("Stop requested");
        _endTurn = true;
        _speech?.Cancel();
        _utterance?.TrySetResult(null);
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        // Models load in the background so Core starts fast; the first voice turn waits for them if needed.
        await Task.Run(() =>
        {
            var clock = Stopwatch.StartNew();
            try
            {
                _recognizer = new SpeechRecognizer(options);
                _speaker = new Speaker(options);
                log.LogInformation("Voice models loaded in {Ms} ms", clock.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Voice models failed to load; voice is unavailable");
            }
        }, stoppingToken);

        // Watch the wake-word setting: the microphone is open continuously only while it is on.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var wanted = settings.Current.Voice.WakeWord && _recognizer is not null;
                if (wanted && _wake is null)
                {
                    StartWakeWord();
                }
                else if (!wanted && _wake is not null)
                {
                    StopWakeWord();
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "The wake word couldn't start");
                StopWakeWord();
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }

        StopWakeWord();
        CloseMicrophone();
        _recognizer?.Dispose();
        _speaker?.Dispose();
    }

    private void StartWakeWord()
    {
        _wake = new WakeWordDetector(options);
        EnsureMicrophone();
        log.LogInformation("Wake word on (threshold {Threshold}, transcript check {Check})", options.WakeThreshold, settings.Current.Voice.TranscriptCheck);
    }

    private void StopWakeWord()
    {
        var wake = _wake;
        _wake = null;
        wake?.Dispose();
        if (_mode == Mode.Idle)
        {
            CloseMicrophone();
        }

        if (wake is not null)
        {
            log.LogInformation("Wake word off");
        }
    }

    private void EnsureMicrophone()
    {
        lock (_audioLock)
        {
            if (_microphone is not null)
            {
                return;
            }

            _collector ??= new UtteranceCollector(options);
            var mic = Microphone.Open();
            mic.Samples += OnSamples;
            mic.Lost += reason =>
            {
                log.LogWarning("Microphone lost: {Reason}", reason);
                _utterance?.TrySetResult(null);
                _ = ShowAsync(VoiceState.Error, $"I lost the microphone ({mic.Name}).");
                CloseMicrophone();
            };
            mic.Start();
            _microphone = mic;
            log.LogInformation("Microphone open: {Name} ({Mode})", mic.Name, mic.Raw ? "raw, Windows effects bypassed" : $"processed by Windows effects: raw mode refused ({mic.RawUnavailableReason})");
        }
    }

    private void CloseMicrophone()
    {
        lock (_audioLock)
        {
            if (_microphone is null)
            {
                return;
            }

            _microphone.Samples -= OnSamples;
            _microphone.Dispose();
            _microphone = null;
            log.LogInformation("Microphone closed");
        }
    }

    /// <summary>On the capture thread: feed whichever stage is active.</summary>
    private void OnSamples(float[] samples)
    {
        switch (_mode)
        {
            case Mode.Listening when _collector is not null:
                if (_collector.Accept(samples) is { } utterance)
                {
                    _utterance?.TrySetResult(utterance);
                }

                break;
            case Mode.Idle when _wake is not null:
                KeepWakeHistory(samples);
                if (_wake.Accept(samples))
                {
                    var clip = _wakeHistory.ToArray();
                    _ = Task.Run(() => OnWakeAsync(clip, _wake?.LastScore ?? 0));
                }

                break;
            case Mode.Speaking when _wake is not null:
                // Half-duplex, except the wake word may interrupt (works best with headphones).
                if (_wake.Accept(samples))
                {
                    _speech?.Cancel();
                }

                break;
        }
    }

    private void KeepWakeHistory(float[] samples)
    {
        foreach (var s in samples)
        {
            _wakeHistory.Enqueue(s);
        }

        while (_wakeHistory.Count > 2 * VoiceOptions.SampleRate)
        {
            _wakeHistory.Dequeue();
        }
    }

    private async Task OnWakeAsync(float[] clip, float score)
    {
        if (_mode != Mode.Idle || _recognizer is null)
        {
            return;
        }

        if (settings.Current.Voice.TranscriptCheck)
        {
            var (text, _) = _recognizer.Transcribe(clip);
            if (!WakeWordDetector.TranscriptConfirms(text))
            {
                log.LogInformation("Wake word at {Score:F2} not confirmed by the transcript \"{Text}\"", score, text);
                return;
            }
        }

        log.LogInformation("Wake word ({Score:F2})", score);
        await RunTurnAsync(wokenByWakeWord: true, _stopping);
    }

    private async Task RunTurnAsync(bool wokenByWakeWord, CancellationToken ct)
    {
        if (!await _turnGate.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            if (_recognizer is null || _speaker is null)
            {
                await ShowAsync(VoiceState.Error, "My voice models aren't loaded. The log has the details.");
                return;
            }

            EnsureMicrophone();
            _endTurn = false;
            var followUp = false;
            while (!ct.IsCancellationRequested && !_endTurn)
            {
                var audio = await ListenAsync(modes.Dictating ? TimeSpan.FromSeconds(20) : followUp ? options.FollowUpWindow : options.NoSpeechTimeout, followUp, ct);
                if (audio is null)
                {
                    break;
                }

                var endOfSpeech = Stopwatch.StartNew();
                _mode = Mode.Busy;
                await ShowAsync(VoiceState.Transcribing, "…");
                var (text, recognition) = _recognizer.Transcribe(audio);
                SaveForDiagnosis(audio, text);
                log.LogInformation("Heard \"{Text}\" ({Ms} ms to recognise)", text, (int)recognition.TotalMilliseconds);
                if (text.Length > 0 && await DictationAsync(text, ct))
                {
                    followUp = true;
                    continue;
                }

                if (text.Length == 0 || IsStop(text))
                {
                    break;
                }

                await ShowAsync(VoiceState.Thinking, text);
                await RespondAsync(text, endOfSpeech, ct);
                followUp = true;
            }

            // Every turn ends by telling the island, including when the follow-up window runs out (it used to stay up).
            await ShowAsync(VoiceState.Idle, "");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Voice turn failed");
            await ShowAsync(VoiceState.Error, "Something went wrong with voice. The log has the details.");
        }
        finally
        {
            _endTurn = false;
            // Dictation lasts one voice turn at most, so a later "open Chrome" is never typed into a window by surprise.
            modes.Dictating = false;
            _mode = Mode.Idle;
            _wake?.Reset();
            if (_wake is null)
            {
                CloseMicrophone();
            }

            _turnGate.Release();
        }
    }

    private async Task<float[]?> ListenAsync(TimeSpan noSpeechTimeout, bool followUp, CancellationToken ct)
    {
        _collector!.Reset();
        _utterance = new TaskCompletionSource<float[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mode = Mode.Listening;
        // After you cut ALDREN off with the hotkey, you pressed it to talk: plain "Listening…", not "Anything else?".
        var prompt = followUp && !_interruptedByHotkey ? "Anything else?" : "Listening…";
        _interruptedByHotkey = false;
        await ShowAsync(VoiceState.Listening, prompt);

        var deadline = Stopwatch.StartNew();
        _listenAgain = false;
        while (!_utterance.Task.IsCompleted)
        {
            if (_listenAgain)
            {
                _listenAgain = false;
                if (!_collector.HeardSpeech)
                {
                    noSpeechTimeout = options.NoSpeechTimeout;
                    deadline.Restart();
                    await ShowAsync(VoiceState.Listening, "Listening…");
                }
            }

            if (!_collector.HeardSpeech && deadline.Elapsed > noSpeechTimeout)
            {
                _utterance.TrySetResult(null);
                break;
            }

            if (deadline.Elapsed > noSpeechTimeout + options.MaxUtterance)
            {
                _utterance.TrySetResult(null);
                break;
            }

            await Task.WhenAny(_utterance.Task, Task.Delay(100, ct));
        }

        _mode = Mode.Busy;
        return await _utterance.Task;
    }

    /// <summary>
    /// Asks the orchestrator and speaks the reply while it is still being written: each finished sentence goes to the
    /// speaker at once, so the first one plays while Qwen writes the rest.
    /// </summary>
    private async Task RespondAsync(string text, Stopwatch endOfSpeech, CancellationToken ct)
    {
        // No speech while you game, watch fullscreen or switch it off: the island shows the reply instead.
        var quiet = modes.Silent || !settings.Current.Voice.SpeakReplies || residency.GamingMode || GamingDetector.FullscreenAppRunning();
        if (quiet)
        {
            await ShowAsync(VoiceState.Speaking, await orchestrator.HandleAsync(text, ct));
            return;
        }

        using var speech = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _speech = speech;
        var sentences = new SentenceStream();
        var speakStarted = endOfSpeech.Elapsed;
        // The island switches to "speaking" (with the Stop button) the moment audio starts, not when the reply is done.
        var speaking = _speaker!.SpeakAsync(sentences.Reader, speech.Token, () => _ = ShowAsync(VoiceState.Speaking, sentences.Text));
        _mode = Mode.Speaking;
        try
        {
            // The speech token, not the turn's: stopping ALDREN also stops it writing the rest of the reply.
            var reply = await orchestrator.HandleAsync(text, speech.Token, sentences.Push);
            // Fast-path replies arrive whole; a hand-off replaces whatever the model began to say.
            if (!SameText(sentences.Text, Speakable(reply)))
            {
                if (sentences.Text.Trim().Length > 0)
                {
                    log.LogInformation("The final reply differs from the streamed text; speaking the final reply too");
                }

                sentences.Push(sentences.Text.Trim().Length > 0 ? " " + Speakable(reply) : Speakable(reply));
            }

            sentences.Complete();
            await ShowAsync(VoiceState.Speaking, reply);
            await speaking;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.LogInformation("Speech interrupted");
        }
        finally
        {
            sentences.Complete();
            _mode = Mode.Busy;
            _speech = null;
            if (_speaker.LastFirstAudio is { } first)
            {
                // Measured from the VAD closing the utterance, which itself comes 400 ms after your last word.
                log.LogInformation("First audio {Ms} ms after the end of the utterance (recognition took {RecognitionMs} ms of it)",
                    (int)(speakStarted + first).TotalMilliseconds, (int)speakStarted.TotalMilliseconds);
            }
        }
    }

    /// <summary>What is said aloud: research replies lose their source list, links and [n] marks (shown on screen instead).</summary>
    public static string Speakable(string reply)
    {
        var cut = reply.IndexOf(Orchestration.ResearchBrain.SourcesMarker, StringComparison.Ordinal);
        var text = cut >= 0 ? reply[..cut] : reply;
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s*\[\d+(?:\s*,\s*\d+)*\]", "").Trim();
    }

    private static bool SameText(string streamed, string reply) =>
        string.Equals(Squash(streamed), Squash(reply), StringComparison.Ordinal);

    private static string Squash(string text) => string.Concat(text.Replace("*", "").Where(c => !char.IsWhiteSpace(c)));

    /// <summary>
    /// Only while the folder <c>voice\debug</c> exists under the data root (you create and delete it): keeps the last ten
    /// utterances as WAV files with their transcripts, to diagnose recognition. Never uploaded, never committed.
    /// </summary>
    private void SaveForDiagnosis(float[] audio, string text)
    {
        var folder = Path.Combine(Path.GetDirectoryName(options.ModelsRoot)!, "debug");
        if (!Directory.Exists(folder))
        {
            return;
        }

        try
        {
            var name = $"utt-{DateTime.Now:HHmmss}";
            using (var file = File.Create(Path.Combine(folder, name + ".wav")))
            using (var w = new BinaryWriter(file))
            {
                w.Write("RIFF"u8); w.Write(36 + audio.Length * 2); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
                w.Write(VoiceOptions.SampleRate); w.Write(VoiceOptions.SampleRate * 2); w.Write((short)2); w.Write((short)16);
                w.Write("data"u8); w.Write(audio.Length * 2);
                foreach (var s in audio)
                {
                    w.Write((short)Math.Clamp(s * 32767f, short.MinValue, short.MaxValue));
                }
            }

            File.WriteAllText(Path.Combine(folder, name + ".txt"), text);
            foreach (var old in Directory.GetFiles(folder, "utt-*.wav").OrderByDescending(f => f).Skip(10))
            {
                File.Delete(old);
                File.Delete(Path.ChangeExtension(old, ".txt"));
            }
        }
        catch (IOException ex)
        {
            log.LogDebug(ex, "Couldn't save the diagnostic clip");
        }
    }

    /// <summary>
    /// Voice typing (Phase 9): "start dictation", then each utterance is typed into the focused field through the Tool Gateway
    /// (type_text), until "stop dictation" or the turn ends. Returns false when the utterance is a normal request.
    /// </summary>
    private async Task<bool> DictationAsync(string text, CancellationToken ct)
    {
        // People often say it in one breath: "Start dictation. I love mangoes. Stop dictation."
        var rest = text.Trim();
        var started = DictationStart().Match(rest);
        if (started.Success)
        {
            modes.Dictating = true;
            rest = started.Groups["rest"].Value;
        }
        else if (!modes.Dictating)
        {
            return false;
        }

        var stop = DictationStop().Match(rest);
        if (stop.Success)
        {
            rest = stop.Groups["before"].Value;
        }

        rest = rest.Trim();
        string message;
        if (rest.Length > 0)
        {
            var gateway = (Aldren.Security.Gateway.ToolGateway)services.GetService(typeof(Aldren.Security.Gateway.ToolGateway))!;
            var args = System.Text.Json.JsonSerializer.SerializeToElement(new { text = rest + " " });
            var outcome = await gateway.InvokeAsync(
                new Aldren.Security.Gateway.ToolCall("type_text", args, Guid.NewGuid().ToString("N")),
                new Aldren.Security.Gateway.CallContext(Aldren.Security.Gateway.CallOrigin.User, []),
                ct);
            log.LogInformation("Dictation: {Chars} characters, {Status}", rest.Length, outcome.Status);
            message = outcome.Status == Aldren.Security.Gateway.ToolOutcomeStatus.Succeeded
                ? stop.Success ? "Typed. Dictation off." : "Typed. Keep going, or say \"stop dictation\"."
                : outcome.Message;
        }
        else
        {
            message = stop.Success ? "Dictation off." : "Dictation on: I'll type what you say into the field you're in. Say \"stop dictation\" to finish.";
        }

        if (stop.Success)
        {
            modes.Dictating = false;
        }

        await ShowAsync(VoiceState.Speaking, message);
        return true;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(?:start dictation(?: mode)?|start dictating|dictation mode|take dictation|dictation on|type what i say)(?! +(?:off|of))[\s\.,!:;]*(?<rest>.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex DictationStart();

    [System.Text.RegularExpressions.GeneratedRegex(@"^(?<before>.*?)[\s\.,!;]*\b(?:(?:stop|end|exit|quit|finish|close)(?: the)? dictation(?: mode)?|stop dictating|dictation(?: mode)? (?:off|of|stop|end)|turn (?:off|of) (?:the )?dictation(?: mode)?|turn (?:the )?dictation(?: mode)? (?:off|of)|stop typing)[\s\.!]*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex DictationStop();

    private static bool IsStop(string text)
    {
        var t = FastPath.Normalize(text);
        return t is "stop" or "stop it" or "never mind" or "nevermind" or "cancel" or "thats all" or "thank you" or "thanks" or "no thanks" or "nothing";
    }

    private async Task ShowAsync(VoiceState state, string text)
    {
        if (connections.Current is not { } app)
        {
            return;
        }

        try
        {
            await app.App.ShowVoiceAsync(state, text, app.Disconnected);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log.LogDebug(ex, "The island didn't take the voice state");
        }
    }
}
