// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Downstream;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Collections.Concurrent;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class VisualizerOutputDeviceSelectionTests
{
    [Theory]
    [InlineData(Role.Console)]
    [InlineData(Role.Multimedia)]
    [InlineData(Role.Communications)]
    public async Task RenderNotificationRebindsDesktopToEventDeviceWhileMultimediaDefaultStaysOld(Role role)
    {
        var selection = new VisualizerOutputDeviceSelection();
        var created = new ConcurrentBag<Capture>();
        int defaultLookups = 0;
        using var engine = new VisualizerAudioEngine(source =>
        {
            string id = source == 1 ? "microphone" : selection.Select(id => id, () =>
            {
                Interlocked.Increment(ref defaultLookups);
                return "old-multimedia";
            })!;
            var capture = new Capture(id);
            created.Add(capture);
            return capture;
        });
        await engine.Configure(true, 2);
        var originalDesktop = created.Single(c => c.Id == "old-multimedia");
        var microphone = created.Single(c => c.Id == "microphone");

        await selection.OnDefaultDeviceChanged(new(DataFlow.Render, role, "new-output"), () => engine.Restart(0));

        Assert.True(originalDesktop.Disposed);
        Assert.False(microphone.Disposed);
        Assert.Single(created, c => c.Id == "new-output" && !c.Disposed);
        Assert.Equal(1, defaultLookups);
        Assert.Equal(3, created.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnresolvableNotificationFallsBackToMultimediaDefault(bool throws)
    {
        var selection = new VisualizerOutputDeviceSelection();
        await selection.OnDefaultDeviceChanged(new(DataFlow.Render, Role.Console, "removed"), () => Task.CompletedTask);
        int fallbackCalls = 0;
        string? requestedId = null;
        string? selected = selection.Select<string>(id =>
        {
            requestedId = id;
            if (throws) throw new InvalidOperationException("Endpoint removed");
            return null;
        }, () => { fallbackCalls++; return "multimedia"; });
        Assert.Equal("multimedia", selected);
        Assert.Equal("removed", requestedId);
        Assert.Equal(1, fallbackCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EmptyNotificationClearsPreviousPreference(string? id)
    {
        var selection = new VisualizerOutputDeviceSelection();
        await selection.OnDefaultDeviceChanged(new(DataFlow.Render, Role.Console, "previous"), () => Task.CompletedTask);
        await selection.OnDefaultDeviceChanged(new(DataFlow.Render, Role.Console, id!), () => Task.CompletedTask);
        int lookups = 0;
        Assert.Equal("default", selection.Select<string>(_ => { lookups++; return "old-device"; }, () => "default"));
        Assert.Equal(0, lookups);
    }

    [Fact]
    public async Task DisabledNotificationIsRememberedWithoutCreatingCapture()
    {
        var selection = new VisualizerOutputDeviceSelection();
        var created = new ConcurrentBag<Capture>();
        using var engine = new VisualizerAudioEngine(_ =>
        {
            var capture = new Capture(selection.Select(id => id, () => "old-default")!);
            created.Add(capture);
            return capture;
        });
        await selection.OnDefaultDeviceChanged(new(DataFlow.Render, Role.Console, "new-output"), () => engine.Restart(0));
        Assert.Empty(created);
        await engine.Configure(true, 0);
        Assert.Equal("new-output", Assert.Single(created).Id);
    }

    [Fact]
    public async Task CaptureNotificationCannotReplaceOutputPreferenceOrRestartDesktop()
    {
        var selection = new VisualizerOutputDeviceSelection();
        await selection.OnDefaultDeviceChanged(new(DataFlow.Render, Role.Console, "output"), () => Task.CompletedTask);
        await selection.OnDefaultDeviceChanged(new(DataFlow.Capture, Role.Multimedia, "input"),
            () => throw new InvalidOperationException("Must not restart desktop"));
        Assert.Equal("output", selection.Select(id => id, () => "default"));
    }

    private sealed class Capture(string id) : IVisualizerCapture
    {
        public string Id { get; } = id;
        public bool Disposed { get; private set; }
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;
        public void Start() => DataAvailable?.Invoke(this, new WaveInEventArgs([], 0));
        public void Stop() => RecordingStopped?.Invoke(this, new StoppedEventArgs(null));
        public void Dispose() => Disposed = true;
    }
}
