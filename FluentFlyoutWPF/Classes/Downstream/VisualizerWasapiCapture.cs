// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FluentFlyoutWPF.Classes.Downstream;

internal sealed class VisualizerWasapiCapture : IVisualizerCapture
{
    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;

    public VisualizerWasapiCapture(int source)
    {
        _device = (source == 0 ? AudioDeviceMonitor.Instance.GetDefaultRenderDevice()
            : AudioDeviceMonitor.Instance.GetDefaultCaptureDevice())
            ?? throw new InvalidOperationException("No default audio endpoint is available.");
        try
        {
            _capture = source == 0 ? new WasapiLoopbackCapture(_device) : new WasapiCapture(_device);
        }
        catch
        {
            _device.Dispose();
            throw;
        }
    }

    public WaveFormat WaveFormat => _capture.WaveFormat;
    public event EventHandler<WaveInEventArgs>? DataAvailable
    {
        add => _capture.DataAvailable += value;
        remove => _capture.DataAvailable -= value;
    }
    public event EventHandler<StoppedEventArgs>? RecordingStopped
    {
        add => _capture.RecordingStopped += value;
        remove => _capture.RecordingStopped -= value;
    }
    public void Start() => _capture.StartRecording();
    public void Stop() => _capture.StopRecording();
    public void Dispose()
    {
        try { _capture.Dispose(); }
        finally { _device.Dispose(); }
    }
}