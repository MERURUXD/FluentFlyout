using FluentFlyout.Controls;
using FluentFlyoutWPF.Classes.Downstream.Lyrics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class LyricsPresentationTests
{
    private static LyricsDocument Sample => new("test", "1",
        [new("Hello world", 1000, 3000, [new("Hello ", 1000, 1800), new("world", 1800, 3000)]),
            new("Next line", 4000, 6000, [])], [new("你好，世界", 1000, 3000, [])]);

    [Fact]
    public void SeekChoosesLineInBothDirectionsAndHandlesIntro()
    {
        Assert.Equal(-1, LyricsPresentation.FindLine(Sample.Lines, 0));
        Assert.Equal(1, LyricsPresentation.FindLine(Sample.Lines, 4500));
        Assert.Equal(0, LyricsPresentation.FindLine(Sample.Lines, 1500));
        Assert.Equal(1, LyricsPresentation.FindLine(Sample.Lines, 4000));
    }

    [Fact]
    public void TranslationFallsBackToNextLineOnlyWhenUnavailableOrDisabled()
    {
        Assert.Equal("你好，世界", LyricsPresentation.SecondLine(Sample, 0, 1500, true));
        Assert.Equal("Next line", LyricsPresentation.SecondLine(Sample, 0, 1500, false));
        Assert.Equal("Next line", LyricsPresentation.SecondLine(Sample with { Translation = [] }, 0, 1500, true));
        Assert.Equal("", LyricsPresentation.SecondLine(Sample, 1, 4500, false));
        Assert.Equal("Hello world", LyricsPresentation.SecondLine(Sample with { Translation = [] }, -1, 0, true));
    }

    [Fact]
    public void HighlightFollowsWordTimingRatherThanAverageLineDuration()
    {
        var word = new LyricsWord("word", 1000, 3000);
        Assert.Equal(0, LyricsPresentation.WordProgress(word, 500));
        Assert.Equal(0.25, LyricsPresentation.WordProgress(word, 1500));
        Assert.Equal(1, LyricsPresentation.WordProgress(word, 5000));
        Assert.Equal(1, LyricsPresentation.WordProgress(word with { EndMs = 1000 }, 1000));
    }

    [Fact]
    public void WidthIsBoundedAndSurvivesMalformedImportedSettings()
    {
        Assert.Equal(216, LyricsPresentation.Width(200, false, 300, 420));
        Assert.Equal(300, LyricsPresentation.Width(200, true, 300, 420));
        Assert.Equal(180, LyricsPresentation.Width(800, false, 300, 180));
        Assert.Equal(260, LyricsPresentation.Width(200, true, double.NaN, 420));
    }

    [Fact]
    public void RendererDoesNotAllocateTimerBeforePlaybackAndStopsOnDisable()
    {
        RunSta(() =>
        {
            var view = new TaskbarLyricsView();
            Assert.False(view.HasFrameTimer);
            view.SetDocument(Sample);
            view.SetTimeline(1500, 6000, false, 1);
            Assert.False(view.HasFrameTimer);
            view.SetTimeline(1500, 6000, true, 1);
            view.SetDocument(null);
            Assert.False(view.IsFrameTimerRunning);
            view.Stop();
            Assert.False(view.IsFrameTimerRunning);
        });
    }

    [Theory]
    [InlineData(38, 20)]
    [InlineData(26, 14)]
    public void PausedRendererProducesStableTwoLinePixels(int height, int secondTop)
    {
        RunSta(() =>
        {
            var view = new TaskbarLyricsView { Width = 280, Height = height, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), Animate = false };
            view.SetDocument(Sample);
            view.SetTimeline(1400, 6000, false, 1);
            view.Measure(new Size(280, height));
            view.Arrange(new Rect(0, 0, 280, height));
            var bitmap = new RenderTargetBitmap(280, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var first = new byte[280 * height * 4];
            bitmap.CopyPixels(first, 280 * 4, 0);
            Assert.Contains(first, b => b > 0);
            Assert.Contains(first.Skip(280 * secondTop * 4), b => b > 0);
            view.InvalidateVisual();
            view.UpdateLayout();
            var second = new RenderTargetBitmap(280, height, 96, 96, PixelFormats.Pbgra32);
            second.Render(view);
            var pixels = new byte[first.Length];
            second.CopyPixels(pixels, 280 * 4, 0);
            Assert.Equal(first, pixels);
        });
    }

    [Fact]
    public void PauseAndResumeRetainTheLocallyAdvancedPosition()
    {
        RunSta(() =>
        {
            var view = new TaskbarLyricsView();
            view.SetTimeline(1500, 6000, true, 1);
            double pausedAt = view.CurrentPosition;
            view.SetTimeline(pausedAt, 6000, false, 1);
            Assert.Equal(pausedAt, view.CurrentPosition);
            Assert.False(view.IsFrameTimerRunning);
            view.SetTimeline(view.CurrentPosition, 6000, true, 1);
            Assert.InRange(view.CurrentPosition, pausedAt, pausedAt + 1000);
            view.Stop();
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void PlaybackEventDoesNotNeedNewTimelineOrLyricsToResume()
    {
        RunSta(() =>
        {
            var view = new TaskbarLyricsView();
            view.SetTimeline(2000, 10000, false, 1);
            view.SetPlayback(true, 1, 100);
            Assert.InRange(view.CurrentPosition, 2100, 2200);
            view.SetPlayback(false, 1);
            double paused = view.CurrentPosition;
            Assert.Equal(paused, view.CurrentPosition);
            Assert.False(view.IsFrameTimerRunning);
            view.SetPlayback(true, 1);
            Assert.True(view.CurrentPosition >= paused);
            view.Stop();
        });
    }
}
