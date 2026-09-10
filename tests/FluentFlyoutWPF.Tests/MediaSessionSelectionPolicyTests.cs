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

    [Fact]
    public void PlayingSpotifyWinsOverAPlayingNonSpotifySession()
    {
        var browser = new Candidate("chrome", IsSpotify: false, IsPlaying: true);
        var spotify = new Candidate("spotify", IsSpotify: true, IsPlaying: true);

        var selected = MediaSessionSelectionPolicy.TrySelectSpotifyPreferredCore(
            [browser, spotify],
            browser,
            candidate => candidate.Id,
            candidate => candidate.IsSpotify,
            candidate => candidate.IsPlaying);

        Assert.Same(spotify, selected);
    }

    [Fact]
    public void PausedSpotifyYieldsToAPlayingNonSpotifySession()
    {
        var pausedSpotify = new Candidate("spotify", IsSpotify: true, IsPlaying: false);
        var playingBrowser = new Candidate("chrome", IsSpotify: false, IsPlaying: true);

        var selected = MediaSessionSelectionPolicy.TrySelectSpotifyPreferredCore(
            [pausedSpotify, playingBrowser],
            pausedSpotify,
            candidate => candidate.Id,
            candidate => candidate.IsSpotify,
            candidate => candidate.IsPlaying);

        Assert.Same(playingBrowser, selected);
    }

    [Fact]
    public void FocusedPlayingNonSpotifyWinsAmongMultiplePlayingFallbackCandidates()
    {
        var pausedSpotify = new Candidate("spotify", IsSpotify: true, IsPlaying: false);
        var firstPlayingBrowser = new Candidate("chrome-a", IsSpotify: false, IsPlaying: true);
        var focusedPlayingBrowser = new Candidate("chrome-b", IsSpotify: false, IsPlaying: true);

        var selected = MediaSessionSelectionPolicy.TrySelectSpotifyPreferredCore(
            [pausedSpotify, firstPlayingBrowser, focusedPlayingBrowser],
            focusedPlayingBrowser,
            candidate => candidate.Id,
            candidate => candidate.IsSpotify,
            candidate => candidate.IsPlaying);

        Assert.Same(focusedPlayingBrowser, selected);
    }

    [Fact]
    public void PausedOnlyCandidatesRetainTheNormalFocusedThenFirstFallback()
    {
        var pausedSpotify = new Candidate("spotify", IsSpotify: true, IsPlaying: false);
        var focusedBrowser = new Candidate("chrome", IsSpotify: false, IsPlaying: false);

        var preferredOverride = MediaSessionSelectionPolicy.TrySelectSpotifyPreferredCore(
            [pausedSpotify, focusedBrowser],
            focusedBrowser,
            candidate => candidate.Id,
            candidate => candidate.IsSpotify,
            candidate => candidate.IsPlaying);
        var normalFallback = MediaSessionSelectionPolicy.SelectFocusedOrFirst(
            [pausedSpotify, focusedBrowser],
            focusedBrowser,
            candidate => candidate.Id);

        Assert.Null(preferredOverride);
        Assert.Same(focusedBrowser, normalFallback);
    }

    [Fact]
    public void AutomaticFallbackChoosesTheFirstAllowedCandidateWithoutFocus()
    {
        var first = new Candidate("first", IsSpotify: false, IsPlaying: false);
        var second = new Candidate("second", IsSpotify: false, IsPlaying: true);
        var unavailableFocus = new Candidate("missing", IsSpotify: false, IsPlaying: true);

        var selected = MediaSessionSelectionPolicy.SelectFocusedOrFirst(
            [first, second],
            unavailableFocus,
            candidate => candidate.Id);

        Assert.Same(first, selected);
    }

    [Fact]
    public void AutomaticFallbackPreservesAnAllowedFocusedCandidate()
    {
        var first = new Candidate("first", IsSpotify: false, IsPlaying: false);
        var focused = new Candidate("focused", IsSpotify: false, IsPlaying: false);
        var last = new Candidate("last", IsSpotify: false, IsPlaying: true);

        var selected = MediaSessionSelectionPolicy.SelectFocusedOrFirst(
            [first, focused, last],
            focused,
            candidate => candidate.Id);

        Assert.Same(focused, selected);
    }

    [Fact]
    public void FilteredSpotifyIsAbsentFromThePreferenceCandidates()
    {
        var playingBrowser = new Candidate("chrome", IsSpotify: false, IsPlaying: true);

        var selected = MediaSessionSelectionPolicy.TrySelectSpotifyPreferredCore(
            [playingBrowser],
            playingBrowser,
            candidate => candidate.Id,
            candidate => candidate.IsSpotify,
            candidate => candidate.IsPlaying);

        Assert.Null(selected);
    }

    [Fact]
    public void FocusedWrapperMapsBackToTheCurrentCollectionMember()
    {
        var current = new Candidate("chrome", IsSpotify: false, IsPlaying: true);
        var staleFocused = new Candidate("chrome", IsSpotify: false, IsPlaying: true);

        var selected = MediaSessionSelectionPolicy.SelectFocusedOrFirst(
            [current],
            staleFocused,
            candidate => candidate.Id);

        Assert.Same(current, selected);
        Assert.NotSame(staleFocused, selected);
    }

    [Fact]
    public void AddingPausedSpotifyKeepsAPlayingBrowserAsPreferredFallback()
    {
        var pausedFocusedBrowser = new Candidate("browser-a", IsSpotify: false, IsPlaying: false);
        var playingBrowser = new Candidate("browser-b", IsSpotify: false, IsPlaying: true);
        var pausedSpotify = new Candidate("spotify", IsSpotify: true, IsPlaying: false);

        var initialOwner = MediaSessionSelectionPolicy.SelectFocusedOrFirst(
            [pausedFocusedBrowser, playingBrowser],
            pausedFocusedBrowser,
            candidate => candidate.Id);
        var ownerAfterOpen = MediaSessionSelectionPolicy.TrySelectSpotifyPreferredCore(
            [pausedFocusedBrowser, playingBrowser, pausedSpotify],
            pausedFocusedBrowser,
            candidate => candidate.Id,
            candidate => candidate.IsSpotify,
            candidate => candidate.IsPlaying);
        var displayOwnership = new MediaSessionDisplayOwnership<Candidate>();
        displayOwnership.SetOwner(ownerAfterOpen);

        Assert.Same(pausedFocusedBrowser, initialOwner);
        Assert.Same(playingBrowser, ownerAfterOpen);
        Assert.False(displayOwnership.Capture(pausedSpotify).IsValid);
        Assert.True(displayOwnership.IsCurrent(playingBrowser));
    }

    [Theory]
    [InlineData("chrome.exe", false)]
    [InlineData("Spotify.exe", true)]
    [InlineData("", false)]
    public void SpotifyClassificationUsesOnlyApplicationIdentity(string applicationId, bool expected)
    {
        Assert.Equal(expected, MediaSessionSelectionPolicy.IsSpotifyApplicationId(applicationId));
    }

    [Fact]
    public void BrowserMetadataDoesNotMakeAChromiumSessionSpotify()
    {
        var browser = new Candidate("chrome.exe", IsSpotify: false, IsPlaying: true, Metadata: "Spotify Web Player");
        var pausedSpotify = new Candidate("spotify.exe", IsSpotify: true, IsPlaying: false);

        var selected = MediaSessionSelectionPolicy.TrySelectSpotifyPreferredCore(
            [browser, pausedSpotify],
            browser,
            candidate => candidate.Id,
            candidate => MediaSessionSelectionPolicy.IsSpotifyApplicationId(candidate.Id),
            candidate => candidate.IsPlaying);

        Assert.Same(browser, selected);
    }

    private sealed record Candidate(string Id, bool IsSpotify, bool IsPlaying, string? Metadata = null);
}
