using System.Text.Json;

namespace Aldren.Bench.Llama;

public static class LlamaBench
{
    /// <summary>Reads prompt-processing and generation speed from `llama-bench -o json` (one pp row, one tg row).</summary>
    public static (double PromptTokensPerSecond, double GenerationTokensPerSecond) Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        double prompt = 0, generation = 0;

        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var nPrompt = row.GetProperty("n_prompt").GetInt32();
            var nGen = row.GetProperty("n_gen").GetInt32();
            var rate = row.GetProperty("avg_ts").GetDouble();

            if (nPrompt > 0 && nGen == 0)
            {
                prompt = rate;
            }
            else if (nGen > 0 && nPrompt == 0)
            {
                generation = rate;
            }
        }

        return (prompt, generation);
    }
}
