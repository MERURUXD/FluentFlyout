// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using NAudio.Wave;

namespace FluentFlyoutWPF.Classes.Downstream;

internal interface IVisualizerCapture : IDisposable
{
    WaveFormat WaveFormat { get; }
    event EventHandler<WaveInEventArgs>? DataAvailable;
    event EventHandler<StoppedEventArgs>? RecordingStopped;
    void Start();
    void Stop();
}

internal enum VisualizerSourceStatus { Off, Starting, Active, Unavailable }

internal sealed record VisualizerAudioFrame(long Revision, float[] Bars, VisualizerSourceStatus Desktop, VisualizerSourceStatus Microphone);

/// <summary>
/// Each endpoint has its own serial resource owner. State and callbacks use a separate gate;
/// native Stop/Dispose never runs under that gate or on an audio callback thread.
/// </summary>
internal sealed class VisualizerAudioEngine : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly object _gate = new();
    private readonly object[] _operations = [new(), new()];
    private readonly Func<int, IVisualizerCapture> _factory;
    private readonly TimeProvider _time;
    private readonly TimeSpan _retryDelay;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _cancellation;
    private readonly Slot?[] _slots = new Slot?[2];
    private readonly int[] _versions = new int[2];
    private readonly int[] _failures = new int[2];
    private readonly VisualizerSourceStatus[] _status = new VisualizerSourceStatus[2];
    private bool _enabled;
    private bool _disposed;
    private int _mode;
    private long _revision;
    private int _barCount = 10;
    private int _sensitivity = 2;
    private int _peak = 3;
    private float[] _smoothed = new float[10];
    private DateTimeOffset _lastFrame = DateTimeOffset.MinValue;

    private sealed class Slot(IVisualizerCapture capture, VisualizerSpectrum spectrum, int version)
    {
        public IVisualizerCapture Capture { get; } = capture;
        public VisualizerSpectrum Spectrum { get; } = spectrum;
        public int Version { get; } = version;
        public CallbackDrain Callbacks { get; } = new();
        public ITimer? Timer { get; set; }
        public EventHandler<WaveInEventArgs>? DataHandler { get; set; }
        public EventHandler<StoppedEventArgs>? StoppedHandler { get; set; }
        public float[]? Bands { get; set; }
        public DateTimeOffset LastData { get; set; }
        public bool Stopped { get; set; }
    }

    // Subscribers must enqueue UI work rather than synchronously wait for the Dispatcher.
    public event Action<VisualizerAudioFrame>? FrameReady;

    public VisualizerAudioEngine(Func<int, IVisualizerCapture> factory, TimeProvider? time = null, TimeSpan? retryDelay = null)
    {
        _factory = factory;
        _time = time ?? TimeProvider.System;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);
        _cancellation = _lifetime.Token;
    }

    public bool IsCurrent(long revision)
    {
        lock (_gate)
            return !_disposed && revision == _revision;
    }

    public Task Configure(bool enabled, int mode)
    {
        lock (_gate)
        {
            if (_disposed)
                return Task.CompletedTask;
            mode = VisualizerSourcePolicy.Normalize(mode);
            if (_enabled == enabled && _mode == mode)
                return Task.CompletedTask;
            _enabled = enabled;
            _mode = mode;
            Array.Clear(_failures);
            Array.Clear(_smoothed);
            return Task.WhenAll(QueueRestartLocked(0), QueueRestartLocked(1));
        }
    }

    public Task Restart(int source)
    {
        lock (_gate)
        {
            if (_disposed || !Wanted(source))
                return Task.CompletedTask;
            _failures[source] = 0;
            return QueueRestartLocked(source);
        }
    }

    public void SetSpectrum(int count, int sensitivity, int peak)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            count = Math.Clamp(count, 1, 20);
            if (_barCount != count)
            {
                _barCount = count;
                _smoothed = new float[count];
                foreach (var slot in _slots)
                    if (slot != null)
                    {
                        slot.Bands = null;
                        slot.Spectrum.Reset();
                    }
                _revision++;
            }
            _sensitivity = Math.Clamp(sensitivity, 1, 3);
            _peak = Math.Clamp(peak, 1, 3);
            PublishLocked(true);
        }
    }

    private bool Wanted(int source) => _enabled && VisualizerSourcePolicy.Includes(_mode, source);

    private Task QueueRestartLocked(int source)
    {
        int version = ++_versions[source];
        _revision++;
        if (_slots[source] is { } slot)
            slot.Bands = null;
        _status[source] = Wanted(source) ? VisualizerSourceStatus.Starting : VisualizerSourceStatus.Off;
        PublishLocked(true);
        return Task.Run(() => RestartAsync(source, version));
    }

    private bool Valid(int source, int version) => !_disposed && _versions[source] == version;

    private async Task RestartAsync(int source, int version)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0)
            {
                try { await Task.Delay(_retryDelay, _cancellation).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }

            lock (_operations[source])
            {
                Slot? previous;
                lock (_gate)
                {
                    if (!Valid(source, version))
                        return;
                    previous = _slots[source];
                    _slots[source] = null;
                }
                Cleanup(previous);
                lock (_gate)
                    if (!Valid(source, version) || !Wanted(source) || _failures[source] >= 5)
                        return;

                Slot? candidate = null;
                IVisualizerCapture? partial = null;
                try
                {
                    partial = _factory(source);
                    candidate = new Slot(partial, new VisualizerSpectrum(partial.WaveFormat), version);
                    partial = null;
                    var owned = candidate;
                    owned.DataHandler = (_, e) => OnData(source, owned, e);
                    owned.StoppedHandler = (_, e) => OnStopped(source, owned, e);
                    owned.Capture.DataAvailable += owned.DataHandler;
                    owned.Capture.RecordingStopped += owned.StoppedHandler;
                    owned.Timer = _time.CreateTimer(_ => OnExpired(source, owned), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    owned.Capture.Start();
                    lock (_gate)
                    {
                        if (Valid(source, version) && Wanted(source) && !owned.Stopped)
                        {
                            owned.LastData = _time.GetUtcNow();
                            _slots[source] = owned;
                            owned.Timer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
                            _status[source] = VisualizerSourceStatus.Active;
                            PublishLocked(true);
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Visualizer source {0} failed to start (attempt {1})", source, attempt + 1);
                }
                Cleanup(candidate);
                if (partial != null)
                    Safely(partial.Dispose);
                lock (_gate)
                {
                    if (!Valid(source, version))
                        return;
                    _failures[source]++;
                    _status[source] = VisualizerSourceStatus.Unavailable;
                    PublishLocked(true);
                }
            }
        }
    }

    private void OnData(int source, Slot slot, WaveInEventArgs e)
    {
        if (!slot.Callbacks.TryEnter())
            return;
        try
        {
            lock (_gate)
            {
                if (!Valid(source, slot.Version) || !ReferenceEquals(_slots[source], slot) || e.BytesRecorded <= 0)
                    return;
                slot.LastData = _time.GetUtcNow();
                _failures[source] = 0;
                slot.Timer?.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
                var bands = slot.Spectrum.Add(e.Buffer, e.BytesRecorded, _barCount);
                if (bands != null)
                {
                    slot.Bands = bands;
                    PublishLocked(false);
                }
            }
        }
        catch (Exception ex)
        {
            OnStopped(source, slot, new StoppedEventArgs(ex));
        }
        finally { slot.Callbacks.Exit(); }
    }

    private void OnExpired(int source, Slot slot)
    {
        if (!slot.Callbacks.TryEnter())
            return;
        try
        {
            lock (_gate)
            {
                if (!Valid(source, slot.Version) || !ReferenceEquals(_slots[source], slot))
                    return;
                var remaining = TimeSpan.FromMilliseconds(500) - (_time.GetUtcNow() - slot.LastData);
                if (remaining > TimeSpan.Zero)
                {
                    slot.Timer?.Change(remaining, Timeout.InfiniteTimeSpan);
                    return;
                }
                slot.Bands = null;
                slot.Spectrum.Reset();
                PublishLocked(true);
            }
        }
        finally { slot.Callbacks.Exit(); }
    }

    private void OnStopped(int source, Slot slot, StoppedEventArgs e)
    {
        lock (_gate)
        {
            slot.Stopped = true;
            if (!Valid(source, slot.Version) || !ReferenceEquals(_slots[source], slot))
                return;
            Logger.Warn(e.Exception, "Visualizer source {0} stopped", source);
            _failures[source]++;
            _ = QueueRestartLocked(source);
            _status[source] = VisualizerSourceStatus.Unavailable;
            PublishLocked(true);
        }
    }

    private void PublishLocked(bool force)
    {
        var now = _time.GetUtcNow();
        if (!force && now - _lastFrame < TimeSpan.FromSeconds(1d / 30))
            return;
        _lastFrame = now;
        var desktop = _slots[0]?.Bands;
        var microphone = _slots[1]?.Bands;
        var merged = VisualizerSpectrum.Merge(desktop, microphone, _barCount, _sensitivity, _peak);
        for (int i = 0; i < _barCount; i++)
            _smoothed[i] = force ? merged[i]
                : merged[i] >= _smoothed[i] ? merged[i] : _smoothed[i] * 0.8f + merged[i] * 0.2f;
        try { FrameReady?.Invoke(new VisualizerAudioFrame(_revision, (float[])_smoothed.Clone(), _status[0], _status[1])); }
        catch (Exception ex) { Logger.Debug(ex, "Visualizer frame subscriber unavailable"); }
    }

    private static void Cleanup(Slot? slot)
    {
        if (slot == null)
            return;
        Safely(() => slot.Timer?.Dispose());
        slot.Callbacks.StopAndWait();
        slot.Capture.DataAvailable -= slot.DataHandler;
        slot.Capture.RecordingStopped -= slot.StoppedHandler;
        Safely(slot.Capture.Stop);
        Safely(slot.Capture.Dispose);
    }

    private static void Safely(Action action)
    {
        try { action(); }
        catch (Exception ex) { Logger.Debug(ex, "Visualizer resource cleanup failed"); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _enabled = false;
            _revision++;
        }
        _lifetime.Cancel();
        for (int source = 0; source < 2; source++)
        {
            lock (_operations[source])
            {
                Slot? slot;
                lock (_gate)
                {
                    slot = _slots[source];
                    _slots[source] = null;
                }
                Cleanup(slot);
            }
        }
        _lifetime.Dispose();
    }
}