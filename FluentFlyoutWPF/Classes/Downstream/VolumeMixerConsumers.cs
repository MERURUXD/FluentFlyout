// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Downstream;

[Flags]
internal enum VolumeMixerConsumer
{
    None = 0,
    VolumeControl = 1,
    Mixer = 2,
    TaskbarTooltip = 4,
    TaskbarScroll = 8
}

internal sealed class VolumeMixerConsumerRegistry
{
    private VolumeMixerConsumer _activeConsumers;

    public bool HasConsumers => _activeConsumers != VolumeMixerConsumer.None;

    public VolumeMixerConsumer ActiveConsumers => _activeConsumers;

    public bool Acquire(VolumeMixerConsumer consumer)
    {
        if (consumer == VolumeMixerConsumer.None)
            throw new ArgumentException("A concrete volume mixer consumer is required.", nameof(consumer));

        bool wasEmpty = !HasConsumers;
        _activeConsumers |= consumer;
        return wasEmpty;
    }

    public bool Release(VolumeMixerConsumer consumer)
    {
        _activeConsumers &= ~consumer;
        return !HasConsumers;
    }

    public void Clear() => _activeConsumers = VolumeMixerConsumer.None;
}