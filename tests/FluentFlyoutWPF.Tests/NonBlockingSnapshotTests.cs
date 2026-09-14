using FluentFlyoutWPF.Classes.Downstream;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class NonBlockingSnapshotTests
{
    [Fact]
    public void HungQueryDoesNotBlockOrSpawnRepeatedQueries()
    {
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        var cache = new NonBlockingSnapshot<string>();
        int calls = 0;
        try
        {
            Assert.Null(cache.Read("taskbar", () =>
            {
                Interlocked.Increment(ref calls);
                started.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return "bounds";
            }));
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            for (int i = 0; i < 100; i++) Assert.Null(cache.Read("taskbar", () => "wrong"));
            Assert.Equal(1, calls);
        }
        finally { release.Set(); }
        Assert.True(SpinWait.SpinUntil(() => cache.Read("taskbar", () => "wrong") == "bounds", TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ChangedTaskbarCannotReceiveOldPendingGeometry()
    {
        using var release = new ManualResetEventSlim();
        var cache = new NonBlockingSnapshot<string>();
        try
        {
            Assert.Null(cache.Read("old", () => { release.Wait(TimeSpan.FromSeconds(10)); return "old bounds"; }));
            Assert.Null(cache.Read("new", () => "new bounds"));
        }
        finally { release.Set(); }
        bool sawOld = false;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            var result = cache.Read("new", () => "new bounds");
            sawOld |= result == "old bounds";
            return result == "new bounds";
        }, TimeSpan.FromSeconds(5)));
        Assert.False(sawOld);
    }
}
