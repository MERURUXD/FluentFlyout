// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Downstream;

/// <summary>
/// Tracks short-lived display ownership and metadata deduplication for one session instance.
/// </summary>
internal sealed class MediaSessionDisplayOwnership<T> where T : class
{
    private readonly object _gate = new();
    private T? _owner;
    private string? _signature;
    private int _thumbnailHash;
    private long _epoch;

    public bool SetOwner(T? owner)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_owner, owner))
                return false;

            _owner = owner;
            _epoch++;
            ClearMetadata();
            return true;
        }
    }

    public bool IsCurrent(T owner)
    {
        lock (_gate)
        {
            return ReferenceEquals(_owner, owner);
        }
    }

    public OwnershipToken Capture(T owner)
    {
        lock (_gate)
        {
            return ReferenceEquals(_owner, owner)
                ? new OwnershipToken(owner, _epoch)
                : default;
        }
    }

    public bool IsCurrent(OwnershipToken token)
    {
        lock (_gate)
        {
            return token.IsValid
                && ReferenceEquals(_owner, token.Owner)
                && _epoch == token.Epoch;
        }
    }

    public bool HasSameSignature(OwnershipToken token, string signature)
    {
        lock (_gate)
        {
            return token.IsValid
                && ReferenceEquals(_owner, token.Owner)
                && _epoch == token.Epoch
                && string.Equals(_signature, signature, StringComparison.Ordinal);
        }
    }

    public bool IsDuplicate(OwnershipToken token, string signature, int thumbnailHash)
    {
        lock (_gate)
        {
            return token.IsValid
                && ReferenceEquals(_owner, token.Owner)
                && _epoch == token.Epoch
                && string.Equals(_signature, signature, StringComparison.Ordinal)
                && _thumbnailHash == thumbnailHash;
        }
    }

    public bool TryRecord(OwnershipToken token, string signature, int thumbnailHash)
    {
        lock (_gate)
        {
            if (!token.IsValid || !ReferenceEquals(_owner, token.Owner) || _epoch != token.Epoch)
                return false;

            _signature = signature;
            _thumbnailHash = thumbnailHash;
            return true;
        }
    }

    public bool Invalidate(T owner)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_owner, owner))
                return false;

            _owner = null;
            _epoch++;
            ClearMetadata();
            return true;
        }
    }

    internal readonly struct OwnershipToken(T? owner, long epoch)
    {
        internal T? Owner { get; } = owner;
        internal long Epoch { get; } = epoch;
        internal bool IsValid => Owner != null;
    }

    private void ClearMetadata()
    {
        _signature = null;
        _thumbnailHash = 0;
    }
}