// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Downstream;

/// <summary>
/// Serializes resource ownership state while leaving external cleanup to the caller.
/// </summary>
internal sealed class ResourceLifecycle<T> where T : class
{
    private readonly object _gate = new();
    private T? _current;
    private int _generation;
    private bool _startInProgress;
    private bool _isRunning;
    private bool _isDisposed;
    private bool _stopInProgress;
    private TaskCompletionSource? _startCompletion;

    public bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _isDisposed;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _isRunning;
            }
        }
    }

    public int Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    public T? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool TryBeginStart(out StartToken token)
    {
        lock (_gate)
        {
            if (_isDisposed || _stopInProgress || _startInProgress || _isRunning)
            {
                token = default;
                return false;
            }

            _generation++;
            _startInProgress = true;
            _startCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token = new StartToken(_generation, _startCompletion);
            return true;
        }
    }

    public bool TryPublish(StartToken token, T resource, out T? rejected)
    {
        ArgumentNullException.ThrowIfNull(resource);

        lock (_gate)
        {
            if (_startInProgress && token.Generation == _generation && !_isDisposed)
            {
                _current = resource;
                _isRunning = true;
                _startInProgress = false;
                rejected = null;
                return true;
            }

            if (token.Generation == _generation)
                _startInProgress = false;

            rejected = resource;
            return false;
        }
    }

    public void CancelStart(StartToken token)
    {
        lock (_gate)
        {
            if (token.Generation == _generation)
                _startInProgress = false;
        }
    }

    public void CompleteStart(StartToken token)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_startCompletion, token.Completion))
                _startCompletion = null;
        }

        token.Completion.TrySetResult();
    }

    public T? Stop(Action? onWaiting = null)
    {
        Task? startCompletion;
        T? current;
        lock (_gate)
        {
            while (_stopInProgress)
                Monitor.Wait(_gate);

            _stopInProgress = true;
            _generation++;
            _startInProgress = false;
            _isRunning = false;

            current = _current;
            _current = null;
            startCompletion = _startCompletion?.Task;
            onWaiting?.Invoke();
        }

        startCompletion?.GetAwaiter().GetResult();
        return current;
    }

    public void CompleteStop()
    {
        lock (_gate)
        {
            _stopInProgress = false;
            Monitor.PulseAll(_gate);
        }
    }

    public T? Dispose(Action? onWaiting = null)
    {
        Task? startCompletion;
        T? current;
        lock (_gate)
        {
            while (_stopInProgress)
                Monitor.Wait(_gate);

            if (_isDisposed)
                return null;

            _isDisposed = true;
            _generation++;
            _startInProgress = false;
            _isRunning = false;

            current = _current;
            _current = null;
            startCompletion = _startCompletion?.Task;
            onWaiting?.Invoke();
        }

        startCompletion?.GetAwaiter().GetResult();
        return current;
    }

    internal readonly struct StartToken(int generation, TaskCompletionSource completion)
    {
        internal int Generation { get; } = generation;
        internal TaskCompletionSource Completion { get; } = completion;
    }
}