using FluentFlyoutWPF.Classes.Downstream;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class ResourceLifecycleTests
{
    [Fact]
    public async Task CallbackDrainWaitsForTheCurrentCallbackBeforeStopping()
    {
        var drain = new CallbackDrain();
        Assert.True(drain.TryEnter());

        using var stopWaiting = new ManualResetEventSlim();
        using var stopCompleted = new ManualResetEventSlim();
        var stopTask = Task.Run(() =>
        {
            drain.StopAndWait(stopWaiting.Set);
            stopCompleted.Set();
        });

        Assert.True(stopWaiting.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(stopCompleted.IsSet);
        Assert.False(drain.TryEnter());

        drain.Exit();
        await stopTask;

        Assert.True(stopCompleted.IsSet);
        Assert.False(drain.TryEnter());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopOrDisposeBeforePublicationRejectsTheProvisionalResource(bool dispose)
    {
        var lifecycle = new ResourceLifecycle<TestResource>();
        Assert.True(lifecycle.TryBeginStart(out var startToken));

        using var publicationReady = new ManualResetEventSlim();
        using var allowPublication = new ManualResetEventSlim();
        var provisional = new TestResource();

        Task<(bool Published, TestResource? Rejected)> publishTask = Task.Run(() =>
        {
            publicationReady.Set();
            allowPublication.Wait();
            try
            {
                bool published = lifecycle.TryPublish(startToken, provisional, out var rejected);
                if (!published)
                    rejected?.Dispose();
                return (published, rejected);
            }
            finally
            {
                lifecycle.CompleteStart(startToken);
            }
        });

        Assert.True(publicationReady.Wait(TimeSpan.FromSeconds(5)));
        using var transitionStarted = new ManualResetEventSlim();
        Task<TestResource?> transitionTask = Task.Run(() =>
            dispose
                ? lifecycle.Dispose(transitionStarted.Set)
                : lifecycle.Stop(transitionStarted.Set));

        Assert.True(transitionStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(transitionTask.IsCompleted);

        allowPublication.Set();
        var result = await publishTask;
        Assert.Null(await transitionTask);

        Assert.False(result.Published);
        Assert.Same(provisional, result.Rejected);
        if (!dispose)
            lifecycle.CompleteStop();
        Assert.Equal(1, provisional.DisposeCount);
        Assert.False(lifecycle.IsRunning);
        Assert.Equal(dispose, lifecycle.IsDisposed);
    }

    [Fact]
    public void PublishedResourceIsDetachedExactlyOnceByStop()
    {
        var lifecycle = new ResourceLifecycle<TestResource>();
        Assert.True(lifecycle.TryBeginStart(out var startToken));
        var resource = new TestResource();

        Assert.True(lifecycle.TryPublish(startToken, resource, out var rejected));
        Assert.Null(rejected);
        lifecycle.CompleteStart(startToken);
        Assert.Same(resource, lifecycle.Current);

        Assert.Same(resource, lifecycle.Stop());
        lifecycle.CompleteStop();
        Assert.Null(lifecycle.Current);
        Assert.False(lifecycle.IsRunning);
        Assert.Null(lifecycle.Stop());

        resource.Dispose();
        Assert.Equal(1, resource.DisposeCount);
    }

    private sealed class TestResource : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}