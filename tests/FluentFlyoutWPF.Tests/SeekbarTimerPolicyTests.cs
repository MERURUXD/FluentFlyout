using FluentFlyoutWPF.Classes.Downstream;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class SeekbarTimerPolicyTests
{
    [Theory]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, true, true, true)]
    public void TimerDoesNotRunWhenAnyRequiredConditionIsMissing(
        bool enabled,
        bool visible,
        bool playing,
        bool supportsSeek,
        bool dragging)
    {
        Assert.False(SeekbarTimerPolicy.ShouldRun(enabled, visible, playing, supportsSeek, dragging));
    }

    [Fact]
    public void TimerRunsOnlyForAnEnabledVisiblePlayingSeekableSession()
    {
        Assert.True(SeekbarTimerPolicy.ShouldRun(true, true, true, true, false));
    }
}