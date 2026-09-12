// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.ViewModels;
using System.Windows.Media;
using System.Xml.Linq;
using System.Xml.Serialization;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class VisualizerStyleTests
{
    private static readonly XmlSerializer Serializer = new(typeof(UserSettings));

    [Fact]
    public void NewSettingsDefaultToClassicBars()
    {
        Assert.Equal(0, new UserSettings().TaskbarVisualizerStyle);
    }

    [Fact]
    public void StyleSettingRoundTripsThroughXml()
    {
        var settings = new UserSettings { TaskbarVisualizerStyle = 1 };

        using StringWriter writer = new();
        Serializer.Serialize(writer, settings);

        var document = XDocument.Parse(writer.ToString());
        Assert.Contains(nameof(UserSettings.TaskbarVisualizerStyle), document.Root!.Elements().Select(element => element.Name.LocalName));

        using StringReader reader = new(writer.ToString());
        var restored = Assert.IsType<UserSettings>(Serializer.Deserialize(reader));

        Assert.Equal(1, restored.TaskbarVisualizerStyle);
    }

    [Fact]
    public void MissingStyleInLegacyXmlKeepsClassicDefault()
    {
        const string legacyXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <UserSettings>
              <TaskbarVisualizerEnabled>true</TaskbarVisualizerEnabled>
            </UserSettings>
            """;

        using StringReader reader = new(legacyXml);
        var settings = Assert.IsType<UserSettings>(Serializer.Deserialize(reader));

        Assert.Equal(0, settings.TaskbarVisualizerStyle);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    [InlineData(99, 0)]
    public void UnknownPersistedStyleFallsBackToClassicBars(int persistedValue, int expected)
    {
        Assert.Equal((VisualizerRenderStyle)expected, Visualizer.ResolveVisualizerStyle(persistedValue));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 1f)]
    public void SpectrumSamplingInterpolatesAndClamps(float normalizedX, float expected)
    {
        Assert.Equal(expected, RibbonVisualizerRenderer.SampleSpectrum([0f, 0.5f, 1f], normalizedX), precision: 5);
    }

    [Fact]
    public void SilentRibbonFrameDoesNotCreateProceduralPixels()
    {
        const int width = 152;
        const int height = 64;
        byte[] firstFrame = new byte[width * height * 4];
        byte[] secondFrame = new byte[width * height * 4];
        var options = new VisualizerRenderOptions(Color.FromRgb(10, 20, 30), false, false, 10, 4, 4, 12.5);
        var laterOptions = new VisualizerRenderOptions(Color.FromRgb(10, 20, 30), false, false, 10, 4, 4, 32.5);
        var renderer = new RibbonVisualizerRenderer();

        renderer.Render(firstFrame, width * 4, width, height, new float[7], in options);
        renderer.Render(secondFrame, width * 4, width, height, new float[7], in laterOptions);

        Assert.All(firstFrame, value => Assert.Equal(0, value));
        Assert.All(secondFrame, value => Assert.Equal(0, value));
    }

    [Fact]
    public void RibbonGeometryUsesSignedCenterlineAndThinThickness()
    {
        const int width = 152;
        const int height = 64;
        float[] amplitudes = [1f, 1f, 1f, 1f, 1f, 1f, 1f];
        float centerY = height * 0.5f;
        float[] centerline = Enumerable.Range(0, width)
            .Select(x => RibbonVisualizerRenderer.SampleCenterline(amplitudes, x / (width - 1f), 0, height, 0.37))
            .ToArray();

        Assert.Contains(centerline, value => value < centerY - 1f);
        Assert.Contains(centerline, value => value > centerY + 1f);

        float halfThickness = RibbonVisualizerRenderer.SampleHalfThickness(amplitudes, 0.5f, 0, height);
        Assert.InRange(halfThickness, 1.5f, 3f);
    }

    [Fact]
    public void RibbonCenterlineContainsAtLeastTwoWaveCycles()
    {
        const int width = 152;
        const int height = 64;
        float[] amplitudes = [1f, 1f, 1f, 1f, 1f, 1f, 1f];
        float[] centerline = Enumerable.Range(0, width)
            .Select(x => RibbonVisualizerRenderer.SampleCenterline(amplitudes, x / (width - 1f), 0, height, 0.37))
            .ToArray();
        int directionChanges = 0;
        int previousDirection = 0;

        for (int i = 1; i < centerline.Length; i++)
        {
            float delta = centerline[i] - centerline[i - 1];
            if (MathF.Abs(delta) < 0.02f)
                continue;

            int direction = delta > 0f ? 1 : -1;
            if (previousDirection != 0 && direction != previousDirection)
                directionChanges++;

            previousDirection = direction;
        }

        Assert.True(directionChanges >= 3);
    }

    [Fact]
    public void RibbonLayersRemainRelatedAndMotionIsLocal()
    {
        const int height = 64;
        float[] amplitudes = [1f, 1f, 1f, 1f, 1f, 1f, 1f];
        float[] firstTime = Enumerable.Range(0, 3)
            .Select(ribbon => RibbonVisualizerRenderer.SampleCenterline(amplitudes, 0.37f, ribbon, height, 0.25))
            .ToArray();
        float[] secondTime = Enumerable.Range(0, 3)
            .Select(ribbon => RibbonVisualizerRenderer.SampleCenterline(amplitudes, 0.37f, ribbon, height, 1.5))
            .ToArray();

        Assert.True(firstTime.Max() - firstTime.Min() > 0.25f);
        Assert.All(firstTime.Zip(secondTime), pair => Assert.InRange(MathF.Abs(pair.First - pair.Second), 0f, 1.5f));
    }

    [Fact]
    public void RibbonRendersAThickFilledFootprintAroundTheAnchoredCenterline()
    {
        const int width = 152;
        const int height = 64;
        byte[] buffer = new byte[width * height * 4];
        float[] amplitudes = [1f, 1f, 1f, 1f, 1f, 1f, 1f];
        var options = new VisualizerRenderOptions(Color.FromRgb(10, 20, 30), false, false, 10, 4, 4, 0.37);

        new RibbonVisualizerRenderer().Render(buffer, width * 4, width, height, amplitudes, in options);

        int columnsWithMultiplePixels = 0;
        bool hasUpperFootprint = false;
        bool hasLowerFootprint = false;

        for (int x = 0; x < width; x++)
        {
            int pixelsInColumn = 0;
            for (int y = 0; y < height; y++)
            {
                int index = (y * width + x) * 4;
                if (buffer[index + 3] == 0)
                    continue;

                pixelsInColumn++;
                hasUpperFootprint |= y < (height / 2) - 2;
                hasLowerFootprint |= y > (height / 2) + 2;
            }

            if (pixelsInColumn >= 3)
                columnsWithMultiplePixels++;
        }

        Assert.True(columnsWithMultiplePixels > width / 2);
        Assert.True(hasUpperFootprint);
        Assert.True(hasLowerFootprint);
    }

    [Fact]
    public void RibbonFftAmplitudeModulatesNearbyGeometry()
    {
        const int height = 64;
        float[] quietSpectrum = [0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f, 0.1f];
        float[] localPeakSpectrum = [0.1f, 0.1f, 0.1f, 1f, 0.1f, 0.1f, 0.1f];

        float localDelta = MathF.Abs(
            RibbonVisualizerRenderer.SampleHalfThickness(localPeakSpectrum, 0.5f, 1, height)
            - RibbonVisualizerRenderer.SampleHalfThickness(quietSpectrum, 0.5f, 1, height));
        float distantDelta = MathF.Abs(
            RibbonVisualizerRenderer.SampleHalfThickness(localPeakSpectrum, 0.02f, 1, height)
            - RibbonVisualizerRenderer.SampleHalfThickness(quietSpectrum, 0.02f, 1, height));

        Assert.True(localDelta > 0.3f);
        Assert.True(localDelta > distantDelta + 0.1f);
    }

    [Fact]
    public void RibbonEdgeEnvelopeRemainsGentle()
    {
        Assert.InRange(RibbonVisualizerRenderer.SampleEdgeEnvelope(0f), 0.64f, 0.66f);
        Assert.InRange(RibbonVisualizerRenderer.SampleEdgeEnvelope(1f), 0.64f, 0.66f);
        Assert.InRange(RibbonVisualizerRenderer.SampleEdgeEnvelope(0.5f), 0.99f, 1f);
    }
}