namespace Aldren.Bench.Tests;

public class SmokeTests
{
    // llama.cpp, sherpa-onnx and ONNX Runtime ship x64 binaries only; a 32-bit test host would fail to load them.
    [Fact]
    public void Test_host_runs_as_64_bit_process()
    {
        Assert.True(Environment.Is64BitProcess);
    }
}
