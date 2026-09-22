// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using NAudio.CoreAudioApi;

namespace FluentFlyoutWPF.Classes.Downstream;

/// <summary>Preserves render notifications across roles until the next notification.</summary>
internal sealed class VisualizerOutputDeviceSelection
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private string? _deviceId;

    public Task OnDefaultDeviceChanged(DefaultDeviceChangedEventArgs e, Func<Task> restart)
    {
        if (e.DataFlow != DataFlow.Render)
            return Task.CompletedTask;

        // Store before queuing restart; capture construction runs on a worker thread.
        Volatile.Write(ref _deviceId, e.DeviceId);
        return restart();
    }

    public T? Select<T>(Func<string, T?> getDeviceById, Func<T?> getDefaultDevice) where T : class
    {
        string? deviceId = Volatile.Read(ref _deviceId);
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            try
            {
                var device = getDeviceById(deviceId);
                if (device != null)
                    return device;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Visualizer notified output endpoint unavailable; using multimedia default");
            }
        }
        return getDefaultDevice();
    }
}