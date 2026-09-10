using FluentFlyoutWPF.Classes.Downstream;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class VolumeMixerConsumerTests
{
    [Fact]
    public void LastConsumerReleaseStopsTheSharedResourceButSharedReleaseDoesNot()
    {
        var consumers = new VolumeMixerConsumerRegistry();

        Assert.False(consumers.HasConsumers);
        Assert.True(consumers.Acquire(VolumeMixerConsumer.VolumeControl));
        Assert.False(consumers.Acquire(VolumeMixerConsumer.VolumeControl));
        Assert.False(consumers.Acquire(VolumeMixerConsumer.Mixer));
        Assert.False(consumers.Acquire(VolumeMixerConsumer.TaskbarTooltip));
        Assert.Equal(
            VolumeMixerConsumer.VolumeControl | VolumeMixerConsumer.Mixer | VolumeMixerConsumer.TaskbarTooltip,
            consumers.ActiveConsumers);

        Assert.False(consumers.Release(VolumeMixerConsumer.Mixer));
        Assert.True(consumers.HasConsumers);
        Assert.False(consumers.Release(VolumeMixerConsumer.VolumeControl));
        Assert.True(consumers.HasConsumers);
        Assert.True(consumers.Release(VolumeMixerConsumer.TaskbarTooltip));
        Assert.False(consumers.HasConsumers);
    }

    [Fact]
    public void ReleasingAnUnownedConsumerDoesNotStopAnActiveConsumer()
    {
        var consumers = new VolumeMixerConsumerRegistry();

        Assert.True(consumers.Acquire(VolumeMixerConsumer.Mixer));
        Assert.False(consumers.Release(VolumeMixerConsumer.TaskbarScroll));
        Assert.Equal(VolumeMixerConsumer.Mixer, consumers.ActiveConsumers);
    }
}