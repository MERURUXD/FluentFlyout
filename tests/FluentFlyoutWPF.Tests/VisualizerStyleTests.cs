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
        byte[] buffer = new byte[width * height * 4];
        var options = new VisualizerRenderOptions(Color.FromRgb(10, 20, 30), false, false, 10, 4, 4, 12.5);

        new RibbonVisualizerRenderer().Render(buffer, width * 4, width, height, new float[10], in options);

        Assert.All(buffer, value => Assert.Equal(0, value));
    }

    [Fact]
    public void RibbonFrameIsFilledAtCenterAndTapersAtEdges()
    {
        const int width = 152;
        const int height = 64;
        byte[] buffer = new byte[width * height * 4];
        var options = new VisualizerRenderOptions(Color.FromRgb(10, 20, 30), false, false, 10, 4, 4, 0);

        new RibbonVisualizerRenderer().Render(buffer, width * 4, width, height, new float[10].Select(_ => 1f).ToArray(), in options);

        Assert.Equal(0, buffer[3]);
        Assert.Equal(0, buffer[(width - 1) * 4 + 3]);
        Assert.True(buffer[(width / 2) * 4 + (height / 2) * width * 4 + 3] > 0);
    }

    [Fact]
    public void RibbonFrameIsMirroredAndPhaseChangesOnlyTheAudioShape()
    {
        const int width = 152;
        const int height = 64;
        float[] amplitudes = [0.1f, 0.8f, 0.2f, 0.7f, 0.1f];
        byte[] firstFrame = new byte[width * height * 4];
        byte[] secondFrame = new byte[width * height * 4];
        var firstOptions = new VisualizerRenderOptions(Color.FromRgb(10, 20, 30), false, false, 10, 4, 4, 0);
        var secondOptions = new VisualizerRenderOptions(Color.FromRgb(10, 20, 30), false, false, 10, 4, 4, 6);
        var renderer = new RibbonVisualizerRenderer();

        renderer.Render(firstFrame, width * 4, width, height, amplitudes, in firstOptions);
        renderer.Render(secondFrame, width * 4, width, height, amplitudes, in secondOptions);

        bool phaseChangedFrame = false;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;
                int mirroredIndex = ((height - 1 - y) * width + x) * 4;

                Assert.Equal(firstFrame[index + 3], firstFrame[mirroredIndex + 3]);
                if (firstFrame[index] != secondFrame[index]
                    || firstFrame[index + 1] != secondFrame[index + 1]
                    || firstFrame[index + 2] != secondFrame[index + 2]
                    || firstFrame[index + 3] != secondFrame[index + 3])
                    phaseChangedFrame = true;
            }
        }

        Assert.True(phaseChangedFrame);
    }
}
