using FluentFlyoutWPF.Classes.Downstream;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class MediaSessionSelectionPolicyTests
{
    [Theory]
    [InlineData(MediaSessionSelectionPolicy.Automatic)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void AutomaticAndUnknownModesDoNotEnableSpotifyOverride(int mode)
    {
        Assert.False(MediaSessionSelectionPolicy.IsSpotifyPreferred(mode));
    }

    [Fact]
    public void SpotifyPreferredModeEnablesSpotifyOverride()
    {
        Assert.True(MediaSessionSelectionPolicy.IsSpotifyPreferred(MediaSessionSelectionPolicy.SpotifyPreferred));
    }

    [Fact]
    public void NoAllowedSessionsDoNotProduceAnOverride()
    {
        Assert.Null(MediaSessionSelectionPolicy.TrySelectSpotifyPreferred([], null));
    }
}
