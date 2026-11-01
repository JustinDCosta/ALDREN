using System.IO;
using System.Text;
using S6.Host;
using Xunit;

namespace Aldren.Spikes.Tests;

public class NativeMessagingTests
{
    [Fact]
    public void Frame_prefixes_little_endian_length()
    {
        var frame = NativeMessaging.Frame("{\"id\":1}");

        Assert.Equal(new byte[] { 8, 0, 0, 0 }, frame[..4]);
        Assert.Equal("{\"id\":1}", Encoding.UTF8.GetString(frame[4..]));
    }

    [Fact]
    public void Read_frame_returns_null_at_end_of_stream()
    {
        var input = new MemoryStream(NativeMessaging.Frame("{\"id\":2}"));

        Assert.Equal("{\"id\":2}", NativeMessaging.ReadFrame(input));
        Assert.Null(NativeMessaging.ReadFrame(input));
    }

    [Fact]
    public void Read_frame_rejects_messages_over_1_MB()
    {
        var header = BitConverter.GetBytes(1024 * 1024 + 1);
        var input = new MemoryStream([.. header, .. new byte[16]]);

        Assert.Throws<InvalidDataException>(() => NativeMessaging.ReadFrame(input));
    }
}
