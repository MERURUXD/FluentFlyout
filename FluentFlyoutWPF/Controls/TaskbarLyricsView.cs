// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes.Downstream.Lyrics;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FluentFlyout.Controls;

/// <summary>Two cached text layouts; frame ticks move clipping, not media queries.</summary>
public sealed class TaskbarLyricsView : Control
{
    private DispatcherTimer? _frames;
    private readonly Stopwatch _clock = new();
    private readonly Stopwatch _transition = new();
    private LyricsDocument? _document;
    private FormattedText? _primary;
    private FormattedText? _secondary;
    private string _second = string.Empty;
    private double[] _wordEnds = [];
    private double _position;
    private double _rate = 1;
    private double _duration;
    private bool _playing;
    private int _line = -2;
    private double _scroll;
    private double _textHeight;
    private double PrimarySize => Height <= 28 ? 11.5 : 14;
    private double SecondarySize => Height <= 28 ? 10 : 12;
    private double SecondTop => Height <= 28 ? 14 : 20;

    public bool ShowTranslation { get; set; } = true;
    public bool Animate { get; set; } = true;
    public double OffsetMs { get; set; }
    public LyricsDocument? Document => _document;
    internal bool HasFrameTimer => _frames != null;
    internal bool IsFrameTimerRunning => _frames?.IsEnabled == true;
    internal double CurrentPosition => Math.Clamp(_position + (_playing ? _clock.Elapsed.TotalMilliseconds * _rate : 0),
        0, Math.Max(0, _duration));

    public TaskbarLyricsView()
    {
        ClipToBounds = true;
        Height = 38;
        IsVisibleChanged += (_, _) => UpdateFrames();
        Unloaded += (_, _) => Stop();
    }

    public void SetDocument(LyricsDocument? document)
    {
        if (ReferenceEquals(_document, document)) return;
        _document = document;
        _line = -2;
        _primary = _secondary = null;
        _scroll = 0;
        UpdateFrames();
        InvalidateVisual();
    }

    public double MeasureDocument()
    {
        if (_document == null) return 220;
        return _document.Lines.Concat(ShowTranslation ? _document.Translation : [])
            .Select(line => Text(line.Text, PrimarySize).WidthIncludingTrailingWhitespace).DefaultIfEmpty(220).Max();
    }

    public void SetTimeline(double position, double duration, bool playing, double rate)
    {
        _position = position;
        _duration = duration;
        _playing = playing;
        _rate = double.IsFinite(rate) && rate > 0 ? rate : 1;
        _clock.Restart();
        UpdateFrames();
        InvalidateVisual();
    }

    public void Stop()
    {
        _playing = false;
        _frames?.Stop();
        _clock.Stop();
    }

    internal void SetPlayback(bool playing, double rate, double queueDelayMs = 0)
    {
        double position = CurrentPosition;
        if (_playing && !playing) position -= queueDelayMs * _rate;
        else if (!_playing && playing) position += queueDelayMs * rate;
        SetTimeline(Math.Clamp(position, 0, Math.Max(0, _duration)), _duration, playing, rate);
    }

    private void UpdateFrames()
    {
        if (_playing && IsVisible && _document is { Lines.Count: > 0 })
        {
            if (_frames == null)
            {
                _frames = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000d / 30) };
                _frames.Tick += (_, _) => InvalidateVisual();
            }
            _frames.Start();
        }
        else _frames?.Stop();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty || e.Property == FontFamilyProperty)
        {
            _line = -2;
            _secondary = null;
            InvalidateVisual();
        }
    }

    private FormattedText Text(string text, double size) => new(text, CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
        size, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_document == null || ActualWidth <= 0) return;
        if (_textHeight != Height) { _textHeight = Height; _line = -2; _secondary = null; }
        double position = Math.Clamp(CurrentPosition + OffsetMs, 0, Math.Max(0, _duration));
        int index = LyricsPresentation.FindLine(_document.Lines, position);
        if (index != _line)
        {
            _line = index;
            _primary = index < 0 ? null : Text(_document.Lines[index].Text, PrimarySize);
            _wordEnds = index < 0 ? [] : _document.Lines[index].Words.Select((_, i) =>
                Text(string.Concat(_document.Lines[index].Words.Take(i + 1).Select(w => w.Text)), PrimarySize).WidthIncludingTrailingWhitespace).ToArray();
            _scroll = 0;
            _transition.Restart();
        }
        string second = LyricsPresentation.SecondLine(_document, index, position, ShowTranslation);
        if (_secondary == null || second != _second)
        {
            _second = second;
            _secondary = Text(second, SecondarySize);
        }
        double progress = Animate && _playing ? Math.Clamp(_transition.Elapsed.TotalMilliseconds / 180, 0, 1) : 1;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        dc.PushTransform(new TranslateTransform(0, (1 - progress) * 16));
        if (_primary != null && index >= 0)
        {
            var line = _document.Lines[index];
            double highlight = 0;
            for (int i = 0; i < line.Words.Count; i++)
            {
                double before = i == 0 ? 0 : _wordEnds[i - 1];
                highlight = Math.Max(highlight, before + ((_wordEnds[i] - before) * LyricsPresentation.WordProgress(line.Words[i], position)));
                if (position < line.Words[i].EndMs) break;
            }
            if (line.Words.Count == 0) highlight = _primary.WidthIncludingTrailingWhitespace;
            double maxScroll = Math.Max(0, _primary.WidthIncludingTrailingWhitespace - ActualWidth);
            double desired = line.Words.Count > 0 ? Math.Clamp(highlight - (ActualWidth * 0.65), 0, maxScroll)
                : Math.Min(maxScroll, Math.Max(0, position - line.StartMs - 1200) * 0.035);
            _scroll += (desired - _scroll) * (Animate && _playing ? 0.25 : 1);
            dc.PushOpacity(0.45);
            dc.DrawText(_primary, new Point(-_scroll, 0));
            dc.Pop();
            dc.PushClip(new RectangleGeometry(new Rect(-_scroll, 0, Math.Max(0, highlight), SecondTop)));
            dc.DrawText(_primary, new Point(-_scroll, 0));
            dc.Pop();
        }
        dc.PushOpacity(0.65 * progress);
        double secondOverflow = Math.Max(0, _secondary.WidthIncludingTrailingWhitespace - ActualWidth);
        double secondScroll = _line < 0 ? 0 : Math.Min(secondOverflow,
            Math.Max(0, position - _document.Lines[_line].StartMs - 1200) * 0.035);
        dc.DrawText(_secondary, new Point(-secondScroll, SecondTop));
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }
}