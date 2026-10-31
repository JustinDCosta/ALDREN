using Aldren.Bench.Commands;

namespace Aldren.Bench.Tests;

public class ExceptionTextTests
{
    [Fact]
    public void Chain_joins_inner_exception_messages()
    {
        var ex = new HttpRequestException("An error occurred while sending the request.",
            new IOException("Unable to read data from the transport connection.",
                new InvalidOperationException("Connection reset.")));

        Assert.Equal(
            "An error occurred while sending the request. → Unable to read data from the transport connection. → Connection reset.",
            ExceptionText.Chain(ex));
    }

    [Fact]
    public void Chain_of_single_exception_is_its_message()
    {
        Assert.Equal("boom", ExceptionText.Chain(new InvalidOperationException("boom")));
    }
}
