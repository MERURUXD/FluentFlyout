using FluentFlyoutWPF.Classes.Downstream;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class ProductVersionTests
{
    [Fact]
    public void DevelopmentWithoutVersionDoesNotPretendToBeSdkStableVersion()
    {
        var identity = ProductVersion.ResolveUnpackaged(
            ProductVersion.DevelopmentChannel,
            "unknown",
            "unknown");

        Assert.False(identity.IsStable);
        Assert.Equal("development", identity.Current);
        Assert.Equal("development build", identity.Display);
    }

    [Fact]
    public void DevelopmentIdentityIncludesTheInjectedSourceRevision()
    {
        var identity = ProductVersion.ResolveUnpackaged(
            ProductVersion.DevelopmentChannel,
            "2.15.0",
            "abcdef0123456789");

        Assert.False(identity.IsStable);
        Assert.Equal("development+abcdef0", identity.Current);
        Assert.Equal("development build v2.15.0 @ abcdef0", identity.Display);
    }

    [Fact]
    public void RollingDevIdentityIncludesTheInjectedSourceRevisionButIsNotStable()
    {
        var identity = ProductVersion.ResolveUnpackaged(
            ProductVersion.DevChannel,
            "2.15.0",
            "abcdef0123456789");

        Assert.False(identity.IsStable);
        Assert.Equal("dev+abcdef0", identity.Current);
        Assert.Equal("dev build v2.15.0 @ abcdef0", identity.Display);
    }

    [Fact]
    public void StableIdentityUsesTheExplicitManifestDerivedVersion()
    {
        var identity = ProductVersion.ResolveUnpackaged(
            ProductVersion.StableChannel,
            "2.15.0",
            "abcdef0123456789");

        Assert.True(identity.IsStable);
        Assert.Equal("v2.15.0", identity.Current);
        Assert.Equal("v2.15.0", identity.Display);
        Assert.Equal("v2.15.0", identity.StableVersion);
        Assert.Null(identity.SourceRevision);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("development", false)]
    [InlineData("development+abcdef0", false)]
    [InlineData("dev+abcdef0", false)]
    [InlineData("v2.15.0-preview", false)]
    [InlineData("v2.15.0.1", false)]
    [InlineData("v01.2.3", false)]
    [InlineData("v2.15.0", true)]
    [InlineData("2.15.0.0", true)]
    public void StableParserAcceptsOnlyStableSemanticVersions(string value, bool expected)
    {
        Assert.Equal(expected, ProductVersion.IsStableIdentity(value));
    }

    [Fact]
    public void InvalidStableMetadataFailsSafeToDevelopmentIdentity()
    {
        var identity = ProductVersion.ResolveUnpackaged(
            ProductVersion.StableChannel,
            "not-a-version",
            "abcdef0123456789");

        Assert.False(identity.IsStable);
        Assert.Equal("development+abcdef0", identity.Current);
        Assert.Equal("development build @ abcdef0", identity.Display);
    }
}
