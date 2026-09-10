using FluentFlyoutWPF.Classes.Downstream;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class MediaSessionDisplayOwnershipTests
{
    [Fact]
    public void SameInstanceAndMetadataCanBeDeduplicated()
    {
        var ownership = new MediaSessionDisplayOwnership<object>();
        var session = new object();

        Assert.True(ownership.SetOwner(session));
        var token = ownership.Capture(session);
        Assert.True(ownership.TryRecord(token, "metadata", 42));

        Assert.True(ownership.HasSameSignature(token, "metadata"));
        Assert.True(ownership.IsDuplicate(token, "metadata", 42));
        Assert.False(ownership.IsDuplicate(token, "metadata", 43));
    }

    [Fact]
    public void AReplacementInstanceCannotInheritThePreviousDeduplicationKey()
    {
        var ownership = new MediaSessionDisplayOwnership<object>();
        var previous = new object();
        var replacement = new object();

        ownership.SetOwner(previous);
        var previousToken = ownership.Capture(previous);
        Assert.True(ownership.TryRecord(previousToken, "same metadata", 42));

        Assert.True(ownership.SetOwner(replacement));
        Assert.False(ownership.IsDuplicate(ownership.Capture(replacement), "same metadata", 42));
    }

    [Fact]
    public void ExplicitCloseInvalidationMakesAReusedInstanceEligibleAgain()
    {
        var ownership = new MediaSessionDisplayOwnership<object>();
        var session = new object();

        ownership.SetOwner(session);
        var token = ownership.Capture(session);
        Assert.True(ownership.TryRecord(token, "same metadata", 42));

        Assert.True(ownership.Invalidate(session));
        Assert.False(ownership.IsCurrent(session));
        Assert.True(ownership.SetOwner(session));
        Assert.False(ownership.IsDuplicate(ownership.Capture(session), "same metadata", 42));
    }

    [Fact]
    public void AStaleInstanceCannotInvalidateTheCurrentOwner()
    {
        var ownership = new MediaSessionDisplayOwnership<object>();
        var current = new object();
        var stale = new object();

        ownership.SetOwner(current);

        Assert.False(ownership.Invalidate(stale));
        Assert.True(ownership.IsCurrent(current));
    }

    [Fact]
    public void PreparedMetadataCannotMoveOwnershipBackToAnOlderInstance()
    {
        var ownership = new MediaSessionDisplayOwnership<object>();
        var previous = new object();
        var current = new object();

        ownership.SetOwner(previous);
        var staleToken = ownership.Capture(previous);
        ownership.SetOwner(current);

        Assert.False(ownership.TryRecord(staleToken, "stale", 1));
        Assert.True(ownership.IsCurrent(current));
    }

    [Fact]
    public void OwnerChangeSignalsNextUpTitleInvalidation()
    {
        var ownership = new MediaSessionDisplayOwnership<object>();
        var previous = new object();
        var current = new object();

        Assert.True(ownership.SetOwner(previous));
        var previousToken = ownership.Capture(previous);
        Assert.True(ownership.TryRecord(previousToken, "same title", 42));
        Assert.True(ownership.IsDuplicate(previousToken, "same title", 42));
        Assert.False(ownership.SetOwner(previous));

        Assert.True(ownership.SetOwner(current));
        var currentToken = ownership.Capture(current);
        Assert.False(ownership.IsDuplicate(currentToken, "same title", 42));

        Assert.True(ownership.Invalidate(current));
        Assert.True(ownership.SetOwner(current));
        Assert.False(ownership.IsDuplicate(ownership.Capture(current), "same title", 42));
    }

    [Fact]
    public void SameIdCloseRestartRejectsLatePreviousOwnerCallback()
    {
        var ownership = new MediaSessionDisplayOwnership<object>();
        var firstInstance = new object();
        var restartedInstance = new object();

        Assert.True(ownership.SetOwner(firstInstance));
        var firstToken = ownership.Capture(firstInstance);
        Assert.True(ownership.TryRecord(firstToken, "same metadata", 42));

        Assert.True(ownership.Invalidate(firstInstance));
        Assert.True(ownership.SetOwner(restartedInstance));
        var restartedToken = ownership.Capture(restartedInstance);

        Assert.False(ownership.IsDuplicate(restartedToken, "same metadata", 42));
        Assert.False(ownership.TryRecord(firstToken, "late metadata", 43));
        Assert.True(ownership.IsCurrent(restartedToken));
        Assert.True(ownership.TryRecord(restartedToken, "same metadata", 42));
        Assert.True(ownership.IsDuplicate(restartedToken, "same metadata", 42));
    }
}
