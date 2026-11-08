using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Aldren.Voice;

/// <summary>
/// The "ALDREN" wake word: openWakeWord's melspectrogram and embedding models (ONNX), then ALDREN's own classifier
/// (trained in tools/wakeword-trainer, saved as .npz) over the last 16 embedding frames. Fed 80 ms at a time; mirrors the
/// streaming feature extraction of openWakeWord's Python AudioFeatures so scores match the trainer's.
/// </summary>
public sealed partial class WakeWordDetector : IDisposable
{
    private const int Block = 1280;          // 80 ms
    private const int MelBins = 32;
    private const int EmbeddingWindow = 76;  // mel frames per embedding
    private const int Frames = 16;           // embeddings per classifier input
    private const int Cooldown = 25;         // blocks (2 s), as the field listener

    private static readonly string[] Spellings = ["aldrin", "aldren", "aldrine", "eldrin", "aldran", "oldrin", "alldrin", "al drin", "all drin"];

    private readonly InferenceSession _mel;
    private readonly InferenceSession _embedding;
    private readonly string _melInput;
    private readonly string _embeddingInput;
    private readonly Classifier _classifier;
    private readonly float _threshold;
    private readonly List<short> _raw = [];
    private readonly List<float[]> _melFrames = [];
    private readonly List<float[]> _embeddings = [];
    private readonly float[] _pending = new float[Block];
    private int _pendingCount;
    private int _cooldown;

    public WakeWordDetector(VoiceOptions options)
    {
        var session = new SessionOptions { InterOpNumThreads = 1, IntraOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _mel = new InferenceSession(Path.Combine(options.WakeFeatures, "melspectrogram.onnx"), session);
        _embedding = new InferenceSession(Path.Combine(options.WakeFeatures, "embedding_model.onnx"), session);
        _melInput = _mel.InputMetadata.Keys.First();
        _embeddingInput = _embedding.InputMetadata.Keys.First();
        _classifier = Classifier.Load(options.WakeModel);
        _threshold = options.WakeThreshold;
        // openWakeWord starts its mel buffer with ones.
        for (var i = 0; i < EmbeddingWindow; i++)
        {
            _melFrames.Add(Enumerable.Repeat(1f, MelBins).ToArray());
        }
    }

    public float LastScore { get; private set; }

    /// <summary>Feeds 16 kHz audio; true when the wake word fired in it.</summary>
    public bool Accept(ReadOnlySpan<float> samples)
    {
        var fired = false;
        foreach (var s in samples)
        {
            _pending[_pendingCount++] = s;
            if (_pendingCount == Block)
            {
                _pendingCount = 0;
                fired |= ProcessBlock();
            }
        }

        return fired;
    }

    public void Reset()
    {
        _embeddings.Clear();
        _cooldown = Cooldown;
    }

    private bool ProcessBlock()
    {
        foreach (var s in _pending)
        {
            _raw.Add((short)Math.Clamp(s * 32767f, short.MinValue, short.MaxValue));
        }

        // Each block plus 3 hops of context yields 8 new mel frames (10 ms hop).
        var context = Block + 160 * 3;
        if (_raw.Count > context * 4)
        {
            _raw.RemoveRange(0, _raw.Count - context * 4);
        }

        var start = Math.Max(0, _raw.Count - context);
        var input = new float[_raw.Count - start];
        for (var i = 0; i < input.Length; i++)
        {
            input[i] = _raw[start + i];
        }

        using (var results = _mel.Run([NamedOnnxValue.CreateFromTensor(_melInput, new DenseTensor<float>(input, [1, input.Length]))]))
        {
            var spec = results[0].AsTensor<float>();
            var frames = spec.Dimensions[^2];
            for (var f = 0; f < frames; f++)
            {
                var frame = new float[MelBins];
                for (var b = 0; b < MelBins; b++)
                {
                    frame[b] = spec.GetValue((int)(f * MelBins + b) + 0) / 10f + 2f;
                }

                _melFrames.Add(frame);
            }
        }

        if (_melFrames.Count > EmbeddingWindow * 3)
        {
            _melFrames.RemoveRange(0, _melFrames.Count - EmbeddingWindow * 3);
        }

        var window = new float[EmbeddingWindow * MelBins];
        for (var f = 0; f < EmbeddingWindow; f++)
        {
            Array.Copy(_melFrames[_melFrames.Count - EmbeddingWindow + f], 0, window, f * MelBins, MelBins);
        }

        using (var results = _embedding.Run([NamedOnnxValue.CreateFromTensor(_embeddingInput, new DenseTensor<float>(window, [1, EmbeddingWindow, MelBins, 1]))]))
        {
            _embeddings.Add(results[0].AsEnumerable<float>().ToArray());
        }

        if (_embeddings.Count > Frames)
        {
            _embeddings.RemoveAt(0);
        }

        if (_cooldown > 0)
        {
            _cooldown--;
            return false;
        }

        if (_embeddings.Count < Frames)
        {
            return false;
        }

        LastScore = _classifier.Score(_embeddings);
        if (LastScore >= _threshold)
        {
            _cooldown = Cooldown;
            return true;
        }

        return false;
    }

    /// <summary>Second stage: the transcript must contain the wake word, give or take two letters.</summary>
    public static bool TranscriptConfirms(string text)
    {
        var joined = string.Join(' ', NonLetters().Replace(text.ToLowerInvariant(), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return Spellings.Any(joined.Contains) || joined.Split(' ').Any(w => Distance(w, "aldrin") <= 2);
    }

    [GeneratedRegex("[^a-z ]")]
    private static partial Regex NonLetters();

    private static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var previous = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var current = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1));
                previous = current;
            }
        }

        return row[b.Length];
    }

    public void Dispose()
    {
        _mel.Dispose();
        _embedding.Dispose();
    }

    /// <summary>The trainer's network: Linear → LayerNorm → ReLU, twice, then Linear → sigmoid.</summary>
    private sealed class Classifier
    {
        private readonly (Matrix W, float[] B, float[] Gamma, float[] Beta)[] _layers;
        private readonly Matrix _outW;
        private readonly float[] _outB;

        private Classifier((Matrix, float[], float[], float[])[] layers, Matrix outW, float[] outB)
        {
            _layers = layers;
            _outW = outW;
            _outB = outB;
        }

        public static Classifier Load(string path)
        {
            var arrays = Npz.Read(path);
            Matrix M(string name) => new(arrays[name].Data, arrays[name].Shape[0], arrays[name].Shape[1]);
            float[] V(string name) => arrays[name].Data;
            return new Classifier(
                [(M("1_weight"), V("1_bias"), V("2_weight"), V("2_bias")), (M("5_weight"), V("5_bias"), V("6_weight"), V("6_bias"))],
                M("9_weight"), V("9_bias"));
        }

        public float Score(IReadOnlyList<float[]> frames)
        {
            var x = frames.SelectMany(f => f).ToArray();
            foreach (var (w, b, gamma, beta) in _layers)
            {
                x = w.Apply(x, b);
                var mean = x.Average();
                var variance = x.Select(v => (v - mean) * (v - mean)).Average();
                var scale = 1f / MathF.Sqrt(variance + 1e-5f);
                for (var i = 0; i < x.Length; i++)
                {
                    x[i] = Math.Max(0f, (x[i] - mean) * scale * gamma[i] + beta[i]);
                }
            }

            var logit = _outW.Apply(x, _outB)[0];
            return 1f / (1f + MathF.Exp(-logit));
        }
    }

    /// <summary>Row-major weights of a torch Linear layer: out × in.</summary>
    private sealed record Matrix(float[] Data, int Rows, int Columns)
    {
        public float[] Apply(float[] input, float[] bias)
        {
            var output = new float[Rows];
            for (var r = 0; r < Rows; r++)
            {
                var sum = bias[r];
                var offset = r * Columns;
                for (var c = 0; c < Columns; c++)
                {
                    sum += Data[offset + c] * input[c];
                }

                output[r] = sum;
            }

            return output;
        }
    }

    /// <summary>Just enough of NumPy's .npz format for float32 arrays.</summary>
    private static partial class Npz
    {
        [GeneratedRegex(@"'shape':\s*\(([^)]*)\)")]
        private static partial Regex Shape();

        public static Dictionary<string, (float[] Data, int[] Shape)> Read(string path)
        {
            var result = new Dictionary<string, (float[], int[])>(StringComparer.Ordinal);
            using var zip = ZipFile.OpenRead(path);
            foreach (var entry in zip.Entries)
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                var bytes = memory.ToArray();
                // Magic (6) + version (2) + header length (2 or 4) + header.
                var major = bytes[6];
                var headerLength = major == 1 ? BitConverter.ToUInt16(bytes, 8) : (int)BitConverter.ToUInt32(bytes, 8);
                var headerStart = major == 1 ? 10 : 12;
                var header = Encoding.ASCII.GetString(bytes, headerStart, headerLength);
                if (!header.Contains("'<f4'"))
                {
                    throw new InvalidDataException($"{entry.Name}: only little-endian float32 arrays are supported.");
                }

                var shape = Shape().Match(header).Groups[1].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
                var data = new float[(bytes.Length - headerStart - headerLength) / 4];
                Buffer.BlockCopy(bytes, headerStart + headerLength, data, 0, data.Length * 4);
                result[Path.GetFileNameWithoutExtension(entry.Name)] = (data, shape);
            }

            return result;
        }
    }
}
