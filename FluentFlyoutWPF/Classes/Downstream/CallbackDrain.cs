// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Downstream;

/// <summary>
/// Prevents an owner from disposing a resource while a callback is using it.
/// </summary>
internal sealed class CallbackDrain
{
    private readonly object _gate = new();
    private int _activeCallbacks;
    private bool _stopping;

    public bool TryEnter()
    {
        lock (_gate)
        {
            if (_stopping)
                return false;

            _activeCallbacks++;
            return true;
        }
    }

    public void Exit()
    {
        lock (_gate)
        {
            _activeCallbacks--;
            if (_stopping && _activeCallbacks == 0)
                Monitor.PulseAll(_gate);
        }
    }

    public void StopAndWait(Action? onWaiting = null)
    {
        lock (_gate)
        {
            _stopping = true;
            onWaiting?.Invoke();
            while (_activeCallbacks != 0)
                Monitor.Wait(_gate);
        }
    }
}