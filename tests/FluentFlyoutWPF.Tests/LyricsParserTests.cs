using FluentFlyoutWPF.Classes.Downstream.Lyrics;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class LyricsParserTests
{
    [Fact]
    public void QrcKeepsWordDurationsAndOffset()
    {
        var line = Assert.Single(LyricsParser.Parse("[offset:100]\n[1000,800]你(1000,300)好(1300,500)"));
        Assert.Equal("你好", line.Text);
        Assert.Equal(1100, line.StartMs);
        Assert.Equal(1900, line.EndMs);
        Assert.Equal(1400, line.Words[1].StartMs);
    }

    [Fact]
    public void LrcSupportsRepeatedTimestampsAndFractionPrecision()
    {
        var lines = LyricsParser.Parse("[ar:artist]\n[offset:-100]\n[00:01.2][00:02.345]hello\n[00:03]world");
        Assert.Equal(new[] { 1100, 2245, 2900 }, lines.Select(l => l.StartMs));
        Assert.All(lines, line => Assert.Empty(line.Words));
    }

    [Fact]
    public void UntimedTextDoesNotBecomeInventedSynchronizedLyrics()
    {
        Assert.Empty(LyricsParser.Parse("plain text\n[ar:artist]"));
        Assert.Empty(LyricsParser.Parse("[00:99]invalid seconds"));
    }

    [Theory]
    [InlineData("黃齡", "黄龄")]
    [InlineData("玄鳥", "玄鸟")]
    [InlineData("SAJI薩吉", "saji萨吉")]
    public void WindowsNormalizationPreservesTraditionalArtistMatching(string source, string expected)
        => Assert.Equal(expected, LyricsMatchPolicy.Normalize(source));
}
