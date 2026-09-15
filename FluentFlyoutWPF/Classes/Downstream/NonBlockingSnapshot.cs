// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Downstream;

/// <summary>Dispatcher-owned cache for potentially hung shell queries. Never waits on its caller.</summary>
internal sealed class NonBlockingSnapshot<T>(TimeProvider? timeProvider = null) where T : class
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private Task<T?>? _pending;
    private object? _pendingContext;
    private object? _context;
    private T? _value;
    private DateTimeOffset _lastStart = DateTimeOffset.MinValue;

    internal T? Read(object context, Func<T?> query)
    {
        if (!Equals(context, _context)) { _context = context; _value = null; _lastStart = DateTimeOffset.MinValue; }
        if (_pending is { IsCompleted: true })
        {
            if (_pending.IsCompletedSuccessfully && Equals(_pendingContext, context))
                _value = _pending.Result;
            else if (_pending.IsFaulted)
                _ = _pending.Exception;
            _pending = null;
        }
        if (_pending == null && _clock.GetUtcNow() - _lastStart >= TimeSpan.FromSeconds(1.5))
        {
            _lastStart = _clock.GetUtcNow();
            _pendingContext = context;
            _pending = Task.Run(() =>
            {
                try { return query(); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
            });
        }
        return _value;
    }
}