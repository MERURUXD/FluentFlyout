// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes.Downstream;
using NAudio.Wave;
using System.Collections.Concurrent;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class VisualizerAudioEngineTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(2, 1, 1)]
    [InlineData(999, 1, 0)]
    public async Task OnlySelectedSourcesAreCreatedAndDisableReleasesEverything(int mode, int desktop, int microphone)
    {
        var captures = new ConcurrentBag<FakeCapture>();
        var time = new ManualClock();
        using var engine = new VisualizerAudioEngine(source => AddCapture(captures, source), time);
        await engine.Configure(false, mode);
        Assert.Empty(captures);
        Assert.Equal(0, time.ActiveTimers);
        await engine.Configure(true, mode);
        Assert.Equal(desktop, captures.Count(c => c.Source == 0));
        Assert.Equal(microphone, captures.Count(c => c.Source == 1));
        await engine.Configure(false, mode);
        Assert.All(captures, c => Assert.True(c.Disposed && c.StopCount == 1 && c.SubscriberCount == 0));
        Assert.Equal(0, time.ActiveTimers);
    }

    [Fact]
    public async Task MissingMicrophoneHasBoundedRetriesAndDesktopStillProducesFrames()
    {
        int attempts = 0;
        var desktop = new FakeCapture(0);
        var time = new ManualClock();
        var frames = new ConcurrentQueue<VisualizerAudioFrame>();
        using var engine = new VisualizerAudioEngine(source =>
        {
            if (source == 0) return desktop;
            Interlocked.Increment(ref attempts);
            throw new UnauthorizedAccessException("Microphone access denied");
        }, time, TimeSpan.Zero);
        engine.FrameReady += frames.Enqueue;
        await engine.Configure(true, 2);
        Assert.Equal(5, attempts);
        Assert.False(desktop.Disposed);
        time.Advance(100);
        desktop.EmitTone();
        Assert.Contains(frames.Last().Bars, value => value > 0.01f);
        Assert.Equal(VisualizerSourceStatus.Unavailable, frames.Last().Microphone);
        Assert.Equal(VisualizerSourceStatus.Active, frames.Last().Desktop);
    }

    [Fact]
    public async Task MicrophoneOnlyFailureNeverCreatesDesktopCapture()
    {
        var requested = new ConcurrentBag<int>();
        using var engine = new VisualizerAudioEngine(source =>
        {
            requested.Add(source);
            throw new InvalidOperationException("Missing default device");
        }, retryDelay: TimeSpan.Zero);
        await engine.Configure(true, 1);
        Assert.Equal(5, requested.Count);
        Assert.All(requested, source => Assert.Equal(1, source));
    }

    [Fact]
    public async Task ExpiryClearsOnlyTheStaleSourceAndLateTimerCannotEraseFreshAudio()
    {
        var captures = new ConcurrentBag<FakeCapture>();
        var time = new ManualClock();
        var frames = new ConcurrentQueue<VisualizerAudioFrame>();
        using var engine = new VisualizerAudioEngine(source => AddCapture(captures, source), time);
        engine.FrameReady += frames.Enqueue;
        await engine.Configure(true, 2);
        var desktop = captures.Single(c => c.Source == 0);
        var microphone = captures.Single(c => c.Source == 1);
        time.Advance(100);
        desktop.EmitTone(100);
        time.Advance(100);
        microphone.EmitTone(3000);
        time.Advance(300);
        microphone.EmitTone(3000);
        time.Advance(100);
        Assert.True(frames.Last().Bars[8] > 0.01f);
        Assert.True(frames.Last().Bars[1] < 0.01f);
        time.FireAllIncludingStale();
        Assert.True(frames.Last().Bars[8] > 0.01f);
        time.Advance(500);
        Assert.All(frames.Last().Bars, value => Assert.Equal(0, value));
        Assert.All(captures, c => Assert.Equal(1, c.StartCount));
    }

    [Fact]
    public async Task ContinuousSilentPacketsDoNotRestartOrKeepBarsVisible()
    {
        var capture = new FakeCapture(1);
        var time = new ManualClock();
        var frames = new ConcurrentQueue<VisualizerAudioFrame>();
        using var engine = new VisualizerAudioEngine(_ => capture, time);
        engine.FrameReady += frames.Enqueue;
        await engine.Configure(true, 1);
        for (int i = 0; i < 20; i++)
        {
            time.Advance(100);
            capture.Emit(new byte[4096 * 4]);
        }
        Assert.Equal(1, capture.StartCount);
        Assert.All(frames.Last().Bars, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task ChangingDefaultInputReplacesOnlyMicrophoneAndRejectsOldCallbacks()
    {
        var captures = new ConcurrentBag<FakeCapture>();
        var time = new ManualClock();
        var frames = new ConcurrentQueue<VisualizerAudioFrame>();
        using var engine = new VisualizerAudioEngine(source => AddCapture(captures, source), time);
        engine.FrameReady += frames.Enqueue;
        await engine.Configure(true, 2);
        var desktop = captures.Single(c => c.Source == 0);
        var oldMic = captures.Single(c => c.Source == 1);
        var staleData = oldMic.DataAvailable;
        var staleStop = oldMic.RecordingStopped;
        long oldRevision = frames.Last().Revision;
        await engine.Restart(1);
        Assert.False(engine.IsCurrent(oldRevision));
        Assert.True(oldMic.Disposed);
        Assert.Equal(1, desktop.StartCount);
        Assert.False(desktop.Disposed);
        int framesBefore = frames.Count;
        staleData?.Invoke(oldMic, new WaveInEventArgs(VisualizerSpectrumTests.Tone(oldMic.WaveFormat, 1000), 4096 * 4));
        staleStop?.Invoke(oldMic, new StoppedEventArgs(new Exception("Late stop")));
        Assert.Equal(framesBefore, frames.Count);
        Assert.Equal(3, captures.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisableOrDisposeDuringStartRejectsProvisionalCapture(bool dispose)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var capture = new FakeCapture(1) { Starting = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); } };
        using var engine = new VisualizerAudioEngine(_ => capture, new ManualClock());
        var frames = new ConcurrentQueue<VisualizerAudioFrame>();
        engine.FrameReady += frames.Enqueue;
        var starting = engine.Configure(true, 1);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var disabled = engine.Configure(false, 1);
        var disposing = dispose ? Task.Run(engine.Dispose) : Task.CompletedTask;
        release.Set();
        await Task.WhenAll(starting, disabled, disposing).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(capture.Disposed);
        Assert.Equal(0, capture.SubscriberCount);
        Assert.DoesNotContain(frames, frame => frame.Microphone == VisualizerSourceStatus.Active);
    }

    [Fact]
    public async Task RapidSwitchDuringStartConvergesOnLatestMode()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var captures = new ConcurrentBag<FakeCapture>();
        using var engine = new VisualizerAudioEngine(source =>
        {
            var capture = AddCapture(captures, source);
            if (source == 0 && captures.Count(c => c.Source == 0) == 1)
                capture.Starting = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); };
            return capture;
        }, new ManualClock());
        var initial = engine.Configure(true, 0);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var mic = engine.Configure(true, 1);
        var both = engine.Configure(true, 2);
        var final = engine.Configure(true, 1);
        release.Set();
        await Task.WhenAll(initial, mic, both, final).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(captures.Where(c => c.Source == 0), c => Assert.True(c.Disposed));
        Assert.Single(captures, c => !c.Disposed && c.Source == 1);
    }

    [Fact]
    public async Task DisposeCancelsPendingRetriesAndLateRestartDoesNothing()
    {
        int attempts = 0;
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var engine = new VisualizerAudioEngine(_ =>
        {
            Interlocked.Increment(ref attempts);
            failed.TrySetResult();
            throw new InvalidOperationException();
        }, retryDelay: TimeSpan.FromSeconds(30));
        var starting = engine.Configure(true, 1);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        engine.Dispose();
        await starting.WaitAsync(TimeSpan.FromSeconds(5));
        await engine.Restart(1);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task FailureDuringStartCleansEveryProvisionalCapture()
    {
        var captures = new ConcurrentBag<FakeCapture>();
        var time = new ManualClock();
        using var engine = new VisualizerAudioEngine(source =>
        {
            var capture = AddCapture(captures, source);
            capture.Starting = () => throw new InvalidOperationException("Start failed");
            return capture;
        }, time, TimeSpan.Zero);
        await engine.Configure(true, 1);
        Assert.Equal(5, captures.Count);
        Assert.All(captures, capture => Assert.True(capture.Disposed && capture.SubscriberCount == 0));
        Assert.Equal(0, time.ActiveTimers);
    }

    [Fact]
    public async Task DisposeWaitsForInFlightAudioCallbackAndRejectsLateTimer()
    {
        var time = new ManualClock();
        var capture = new FakeCapture(1);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var disposeEntered = new ManualResetEventSlim();
        using var engine = new VisualizerAudioEngine(_ => capture, time);
        await engine.Configure(true, 1);
        engine.FrameReady += frame =>
        {
            if (!frame.Bars.Any(value => value > 0.01f)) return;
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
        };
        time.Advance(100);
        var callback = Task.Run(() => capture.EmitTone());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var disposing = Task.Run(() => { disposeEntered.Set(); engine.Dispose(); });
        Assert.True(disposeEntered.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(disposing.IsCompleted);
        Assert.False(capture.Disposed);
        release.Set();
        await Task.WhenAll(callback, disposing).WaitAsync(TimeSpan.FromSeconds(5));
        time.FireAllIncludingStale();
        Assert.True(capture.Disposed);
        Assert.Equal(0, time.ActiveTimers);
    }

    [Fact]
    public async Task SourceSwitchAndResizeClearOldSpectraAndInvalidateQueuedFrames()
    {
        var captures = new ConcurrentBag<FakeCapture>();
        var time = new ManualClock();
        var frames = new ConcurrentQueue<VisualizerAudioFrame>();
        using var engine = new VisualizerAudioEngine(source => AddCapture(captures, source), time);
        engine.FrameReady += frames.Enqueue;
        await engine.Configure(true, 0);
        time.Advance(100);
        captures.Single().EmitTone();
        long revision = frames.Last().Revision;
        await engine.Configure(true, 1);
        Assert.False(engine.IsCurrent(revision));
        Assert.All(frames.Last().Bars, value => Assert.Equal(0, value));
        engine.SetSpectrum(20, 3, 1);
        Assert.Equal(20, frames.Last().Bars.Length);
        time.Advance(100);
        captures.Single(capture => !capture.Disposed).EmitTone();
        Assert.Equal(20, frames.Last().Bars.Length);
        Assert.Contains(frames.Last().Bars, value => value > 0.01f);
    }

    private static FakeCapture AddCapture(ConcurrentBag<FakeCapture> captures, int source)
    {
        var capture = new FakeCapture(source);
        captures.Add(capture);
        return capture;
    }

    private sealed class FakeCapture(int source) : IVisualizerCapture
    {
        public int Source { get; } = source;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        public EventHandler<WaveInEventArgs>? DataAvailable;
        public EventHandler<StoppedEventArgs>? RecordingStopped;
        event EventHandler<WaveInEventArgs>? IVisualizerCapture.DataAvailable { add => DataAvailable += value; remove => DataAvailable -= value; }
        event EventHandler<StoppedEventArgs>? IVisualizerCapture.RecordingStopped { add => RecordingStopped += value; remove => RecordingStopped -= value; }
        public Action? Starting { get; set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public bool Disposed { get; private set; }
        public int SubscriberCount => (DataAvailable?.GetInvocationList().Length ?? 0) + (RecordingStopped?.GetInvocationList().Length ?? 0);
        public void Start() { StartCount++; Starting?.Invoke(); }
        public void Stop() { StopCount++; RecordingStopped?.Invoke(this, new StoppedEventArgs(null)); }
        public void Dispose() => Disposed = true;
        public void Emit(byte[] data) => DataAvailable?.Invoke(this, new WaveInEventArgs(data, data.Length));
        public void EmitTone(double frequency = 1000) => Emit(VisualizerSpectrumTests.Tone(WaveFormat, frequency));
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
        public int ActiveTimers { get { lock (_gate) return _timers.Count(timer => !timer.Disposed); } }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                var timer = new ManualTimer(this, callback, state);
                _timers.Add(timer);
                timer.Change(dueTime, period);
                return timer;
            }
        }
        public void Advance(int milliseconds)
        {
            ManualTimer[] due;
            lock (_gate)
            {
                _now += TimeSpan.FromMilliseconds(milliseconds);
                due = _timers.Where(timer => !timer.Disposed && timer.Due <= _now).ToArray();
                foreach (var timer in due) timer.Due = DateTimeOffset.MaxValue;
            }
            foreach (var timer in due) timer.Fire();
        }
        public void FireAllIncludingStale()
        {
            ManualTimer[] timers;
            lock (_gate) timers = _timers.ToArray();
            foreach (var timer in timers) timer.Fire();
        }
        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public bool Disposed { get; private set; }
            public DateTimeOffset Due { get; set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._gate)
                {
                    if (Disposed) return false;
                    Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : clock._now + dueTime;
                    return true;
                }
            }
            public void Fire() => callback(state);
            public void Dispose() { lock (clock._gate) Disposed = true; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
