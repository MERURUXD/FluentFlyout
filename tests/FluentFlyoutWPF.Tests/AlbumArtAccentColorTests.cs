using FluentFlyout.Classes.Utils;
using System.Windows.Media;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class AlbumArtAccentColorTests
{
    [Fact]
    public void MutedBackgroundWinsOverSmallVividDetail()
    {
        byte[] pixels = Pixels((Color.FromRgb(145, 148, 158), 800),
            (Color.FromRgb(200, 105, 50), 200));
        Color color = AlbumArtAccentColor.Extract(pixels);
        Assert.Equal(Color.FromRgb(145, 148, 158), color);
        Assert.Equal(color, AlbumArtAccentColor.Extract(pixels));
    }

    [Fact]
    public void NearbyBackgroundShadesCombineAcrossBinBoundaries()
    {
        byte[] pixels = Pixels((Color.FromRgb(126, 130, 145), 300),
            (Color.FromRgb(135, 140, 155), 300),
            (Color.FromRgb(220, 100, 45), 400));
        Color color = AlbumArtAccentColor.Extract(pixels);
        Assert.True(color.B > color.R);
        Assert.InRange(color.R, 126, 135);
    }

    [Fact]
    public void ColorfulMajorityRetainsItsHue()
    {
        Color blue = Color.FromRgb(40, 85, 190);
        Assert.Equal(blue, AlbumArtAccentColor.Extract(
            Pixels((blue, 800), (Color.FromRgb(180, 180, 180), 200))));
    }

    [Fact]
    public void TransparentPixelsDoNotContribute()
    {
        Color blue = Color.FromRgb(40, 85, 190);
        Assert.Equal(blue, AlbumArtAccentColor.Extract(
            Pixels((Color.FromArgb(0, 255, 0, 0), 900), (blue, 1))));
    }

    [Fact]
    public void EmptyImageFallsBackToNeutral()
    {
        Assert.Equal(Color.FromRgb(128, 128, 128), AlbumArtAccentColor.Extract([]));
    }

    private static byte[] Pixels(params (Color Color, int Count)[] groups)
    {
        return groups.SelectMany(group => Enumerable.Range(0, group.Count)
            .SelectMany(_ => new[] { group.Color.B, group.Color.G, group.Color.R, group.Color.A })).ToArray();
    }
}
