using Aldren.Bench.Llama;

namespace Aldren.Bench.Tests;

public class LlamaBenchTests
{
    // Trimmed from real `llama-bench -o json` output on this laptop (build 12023).
    private const string Output = """
        [
          {"build_number": 12023, "model_type": "qwen35 0.8B Q4_K - Medium", "n_gpu_layers": 99, "flash_attn": 1,
           "n_prompt": 64, "n_gen": 0, "avg_ns": 12990400, "avg_ts": 4926.715113, "stddev_ts": 0.000000},
          {"build_number": 12023, "model_type": "qwen35 0.8B Q4_K - Medium", "n_gpu_layers": 99, "flash_attn": 1,
           "n_prompt": 0, "n_gen": 16, "avg_ns": 69565700, "avg_ts": 229.998404, "stddev_ts": 0.000000}
        ]
        """;

    [Fact]
    public void Prompt_and_generation_rates_are_read()
    {
        var (prompt, generation) = LlamaBench.Parse(Output);

        Assert.Equal(4926.715113, prompt, 6);
        Assert.Equal(229.998404, generation, 6);
    }
}
