using KillerPDF.Services;
using Xunit;

namespace KillerPDF.Tests;

public sealed class ProtocolRegistrarTests
{
    [Fact]
    public void ParsesEncodedHttpsPdfUrl()
    {
        Assert.True(ProtocolRegistrar.TryGetTargetUrl(
            "killerpdf://open?url=https%3A%2F%2Fexample.com%2Ffile.pdf%3Fx%3D1", out var target));
        Assert.Equal("https://example.com/file.pdf?x=1", target!.AbsoluteUri);
    }

    [Theory]
    [InlineData("killerpdf://open?url=http%3A%2F%2Fexample.com%2Ffile.pdf")]
    [InlineData("killerpdf://open?url=file%3A%2F%2Fc%3A%2Fsecret.pdf")]
    [InlineData("killerpdf://wrong?url=https%3A%2F%2Fexample.com%2Ffile.pdf")]
    [InlineData("https://example.com/file.pdf")]
    public void RejectsUnsafeOrUnrelatedLaunches(string value)
        => Assert.False(ProtocolRegistrar.TryGetTargetUrl(value, out _));
}
