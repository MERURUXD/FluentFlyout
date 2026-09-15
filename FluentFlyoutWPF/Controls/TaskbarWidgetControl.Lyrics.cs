// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Windows;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes.Downstream.Lyrics;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Animation;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyout.Controls;

public partial class TaskbarWidgetControl
{
    private SpotifyLyricsService? _lyricsService;
    private HttpClient? _lyricsHttp;
    private MediaSession? _lyricsOwner;
    private string? _lyricsMetadataKey;
    private long _lyricsGeneration;
    private bool _lyricsClosed;
    private double _lyricsAvailableWidth = 420;
    private DateTimeOffset? _lyricsAnchorTime;
    private TimeSpan _lyricsAnchorPosition;
    private bool _lyricsNeedsTimeline;

    internal bool HasLyricsLayout => LyricsView.Visibility == Visibility.Visible && !_isVertical;

    private void InitializeLyrics()
    {
        Loaded += (_, _) => RefreshLyrics();
        IsVisibleChanged += (_, _) => RefreshLyrics();
        Unloaded += (_, _) => ReleaseLyrics();
        LyricsView.SizeChanged += (_, _) => (Window.GetWindow(this) as TaskbarWindow)?.RefreshLyricsPosition();
    }

    internal void SetLyricsAvailableWidth(double width)
    {
        double available = Math.Clamp(width, 100, 420);
        if (Math.Abs(available - _lyricsAvailableWidth) < 1) return;
        _lyricsAvailableWidth = available;
        ResizeLyrics(false);
    }

    internal void RefreshLyricsSettings()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshLyricsSettings); return; }
        if (_lyricsClosed) return;
        RefreshLyrics();
        ResizeLyrics(false);
    }
    internal async void RefreshLyrics()
    {
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.BeginInvoke(RefreshLyrics); return; }
        var settings = SettingsManager.Current;
        var main = Application.Current.MainWindow as MainWindow;
        var owner = main?.GetActiveMediaSession();
        bool eligible = !_lyricsClosed && IsLoaded && IsVisible && !_isVertical && settings.TaskbarLyricsEnabled
            && settings.TaskbarWidgetEnabled && owner != null && LyricsMatchPolicy.IsSpotify(owner.Id);
        if (!eligible || owner == null)
        {
            InvalidateLyrics();
            return;
        }
        LyricsView.ShowTranslation = settings.TaskbarLyricsTranslation;
        LyricsView.OffsetMs = Math.Clamp(settings.TaskbarLyricsOffset, -5000, 5000);
        LyricsView.Animate = settings.TaskbarWidgetAnimated;
        string key = _actualTitle + "\n" + _actualArtist;
        if (ReferenceEquals(owner, _lyricsOwner) && key == _lyricsMetadataKey)
        {
            SyncLyricsTimeline();
            return;
        }
        DetachLyricsSession();
        _lyricsOwner = owner;
        _lyricsMetadataKey = key;
        long generation = ++_lyricsGeneration;
        if (_lyricsService == null)
        {
            _lyricsHttp = new HttpClient();
            _lyricsService = new SpotifyLyricsService(new OnlineLyricsProvider(_lyricsHttp, true), new OnlineLyricsProvider(_lyricsHttp, false));
        }
        var service = _lyricsService;
        await service.SelectAsync(false, null, null, null);
        LyricsView.SetDocument(null);
        LyricsView.SetTimeline(0, 0, false, 1);
        LyricsView.Visibility = Visibility.Collapsed;
        SongInfoStackPanel.Visibility = Visibility.Visible;
        UpdateMarquees();
        (Window.GetWindow(this) as TaskbarWindow)?.RefreshLyricsPosition();
        try
        {
            owner!.ControlSession.TimelinePropertiesChanged += LyricsTimelineChanged;
            owner.ControlSession.PlaybackInfoChanged += LyricsPlaybackChanged;
            var metadata = await owner.ControlSession.TryGetMediaPropertiesAsync();
            if (!LyricsOwnerCurrent(owner, generation)) return;
            var timeline = owner.ControlSession.GetTimelineProperties();
            var track = new LyricsTrack(metadata.Title ?? "", metadata.Artist ?? "", metadata.AlbumTitle ?? "",
                (int)Math.Clamp((timeline.EndTime - timeline.StartTime).TotalMilliseconds, 0, int.MaxValue));
            _lyricsNeedsTimeline = track.DurationMs <= 0;
            await service.SelectAsync(true, owner, owner.Id, track);
            if (!LyricsOwnerCurrent(owner, generation)) return;
            LyricsView.SetDocument(service.Current);
            if (service.Current == null) _lyricsMetadataKey = null;
            LyricsView.Visibility = service.Current == null ? Visibility.Collapsed : Visibility.Visible;
            SongInfoStackPanel.Visibility = HasLyricsLayout ? Visibility.Collapsed : Visibility.Visible;
            UpdateMarquees();
            ResizeLyrics(true);
            SyncLyricsTimeline();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (LyricsOwnerCurrent(owner, generation)) InvalidateLyrics();
            Logger.Debug("Lyrics unavailable ({0})", ex.GetType().Name);
        }
    }

    private bool LyricsOwnerCurrent(MediaSession owner, long generation) => !_lyricsClosed && IsVisible
        && SettingsManager.Current.TaskbarLyricsEnabled && generation == _lyricsGeneration
        && ReferenceEquals(owner, _lyricsOwner)
        && ReferenceEquals(owner, (Application.Current.MainWindow as MainWindow)?.GetActiveMediaSession());

    private void LyricsTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        => Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(sender, _lyricsOwner?.ControlSession)) return;
            if (_lyricsNeedsTimeline) RefreshLyrics();
            else SyncLyricsTimeline();
        });

    private void LyricsPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        try { NotifyLyricsPlayback(sender, sender.GetPlaybackInfo()); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ObjectDisposedException or InvalidOperationException) { }
    }

    internal void NotifyLyricsPlayback(GlobalSystemMediaTransportControlsSession sender,
        GlobalSystemMediaTransportControlsSessionPlaybackInfo? info)
    {
        if (info == null) return;
        long received = Stopwatch.GetTimestamp();
        bool playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        double rate = info.PlaybackRate ?? 1;
        Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(sender, _lyricsOwner?.ControlSession) || !LyricsOwnerCurrent(_lyricsOwner, _lyricsGeneration)) return;
            LyricsView.SetPlayback(playing, rate, Stopwatch.GetElapsedTime(received).TotalMilliseconds);
        }, System.Windows.Threading.DispatcherPriority.Send);
    }

    private void SyncLyricsTimeline()
    {
        if (_lyricsOwner == null) return;
        if (!LyricsOwnerCurrent(_lyricsOwner, _lyricsGeneration)) { InvalidateLyrics(); return; }
        try
        {
            var timeline = _lyricsOwner.ControlSession.GetTimelineProperties();
            var playback = _lyricsOwner.ControlSession.GetPlaybackInfo();
            bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            double rate = playback.PlaybackRate ?? 1;
            if (!double.IsFinite(rate) || rate <= 0) rate = 1;
            double position = (timeline.Position - timeline.StartTime).TotalMilliseconds;
            if (_lyricsAnchorTime == timeline.LastUpdatedTime && _lyricsAnchorPosition == timeline.Position)
                position = LyricsView.CurrentPosition;
            else if (playing)
                position += Math.Max(0, (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalMilliseconds) * rate;
            _lyricsAnchorTime = timeline.LastUpdatedTime;
            _lyricsAnchorPosition = timeline.Position;
            LyricsView.SetTimeline(position, (timeline.EndTime - timeline.StartTime).TotalMilliseconds, playing, rate);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { InvalidateLyrics(); }
    }

    private void ResizeLyrics(bool animate)
    {
        if (!HasLyricsLayout) { (Window.GetWindow(this) as TaskbarWindow)?.RefreshLyricsPosition(); return; }
        var settings = SettingsManager.Current;
        double width = LyricsPresentation.Width(LyricsView.MeasureDocument(), settings.TaskbarLyricsFixedWidth,
            settings.TaskbarLyricsWidth, _lyricsAvailableWidth);
        if (Math.Abs(width - LyricsView.Width) > 0.5)
        {
            // Layout changes once. Animate pixels, not Width (which repositions the shell every frame).
            LyricsView.BeginAnimation(WidthProperty, null);
            LyricsView.Width = width;
            if (animate && settings.TaskbarWidgetAnimated)
                LyricsView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        (Window.GetWindow(this) as TaskbarWindow)?.RefreshLyricsPosition();
    }

    private void DetachLyricsSession()
    {
        _lyricsAnchorTime = null;
        _lyricsNeedsTimeline = false;
        if (_lyricsOwner == null) return;
        var session = _lyricsOwner.ControlSession;
        _lyricsOwner = null;
        try { session.TimelinePropertiesChanged -= LyricsTimelineChanged; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ObjectDisposedException or InvalidOperationException) { }
        try { session.PlaybackInfoChanged -= LyricsPlaybackChanged; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ObjectDisposedException or InvalidOperationException) { }
    }

    private void InvalidateLyrics()
    {
        _lyricsGeneration++;
        DetachLyricsSession();
        _lyricsMetadataKey = null;
        _lyricsService?.SelectAsync(false, null, null, null);
        LyricsView.Stop();
        LyricsView.SetDocument(null);
        bool wasVisible = HasLyricsLayout;
        LyricsView.Visibility = Visibility.Collapsed;
        double heldWidth = LyricsView.Width;
        LyricsView.BeginAnimation(WidthProperty, null);
        LyricsView.Width = heldWidth;
        if (wasVisible)
        {
            SongInfoStackPanel.Visibility = _isVertical ? Visibility.Collapsed : Visibility.Visible;
            UpdateMarquees();
            (Window.GetWindow(this) as TaskbarWindow)?.RefreshLyricsPosition();
        }
    }

    internal void ReleaseLyrics(bool closed = false)
    {
        _lyricsClosed |= closed;
        InvalidateLyrics();
        _lyricsService?.Dispose();
        _lyricsService = null;
        _lyricsHttp?.Dispose();
        _lyricsHttp = null;
    }
}