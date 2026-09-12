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
    public void RibbonUsesOnlyGlobalAudioAmplitude()
    {
        Assert.Equal(1f, RibbonVisualizerRenderer.ComputeGlobalAmplitude([1f, 0f, 0f, 0f, 0f, 0f, 0f]));
        Assert.Equal(1f, RibbonVisualizerRenderer.ComputeGlobalAmplitude([0f, 0f, 0f, 0f, 0f, 0f, 1f]));
        Assert.Equal(0f, RibbonVisualizerRenderer.ComputeGlobalAmplitude(new float[7]));
        Assert.Equal(0f, RibbonVisualizerRenderer.ComputeGlobalAmplitude([float.NaN, float.PositiveInfinity]));
        Assert.Equal(0.25f, RibbonVisualizerRenderer.ScaleGlobalAmplitude(0.1f), precision: 5);
        Assert.Equal(1f, RibbonVisualizerRenderer.ScaleGlobalAmplitude(0.5f));
    }

    [Fact]
    public void GlobalAttenuationMatchesIos9Reference()
    {
        Assert.Equal(1f, RibbonVisualizerRenderer.GlobalAttenuation(0f), precision: 5);
        Assert.Equal(0.4096f, RibbonVisualizerRenderer.GlobalAttenuation(1f), precision: 5);
        Assert.Equal(0.0625f, RibbonVisualizerRenderer.GlobalAttenuation(2f), precision: 5);
    }

    [Fact]
    public void RibbonRendersAThickMirroredFilledFootprint()
    {
        const int width = 152;
        const int height = 64;
        byte[] buffer = new byte[width * height * 4];
        float[] amplitudes = [1f, 1f, 1f, 1f, 1f, 1f, 1f];
        var renderer = new RibbonVisualizerRenderer();
        for (int frame = 0; frame <= 60; frame++)
        {
            Array.Clear(buffer);
            var options = new VisualizerRenderOptions(Color.FromRgb(10, 20, 30), false, false, 10, 4, 4, frame / 30d);
            renderer.Render(buffer, width * 4, width, height, amplitudes, in options);
        }

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

        Assert.True(columnsWithMultiplePixels > width / 8);
        Assert.True(hasUpperFootprint);
        Assert.True(hasLowerFootprint);
    }

    [Fact]
    public void RibbonAnimationChangesIndependentLayerState()
    {
        const int width = 152;
        const int height = 64;
        byte[] firstFrame = new byte[width * height * 4];
        byte[] secondFrame = new byte[width * height * 4];
        float[] amplitudes = [1f, 1f, 1f, 1f, 1f, 1f, 1f];
        var renderer = new RibbonVisualizerRenderer();
        for (int frame = 0; frame <= 60; frame++)
        {
            Array.Clear(firstFrame);
            var options = new VisualizerRenderOptions(Color.FromRgb(10, 20, 30), false, false, 10, 4, 4, frame / 30d);
            renderer.Render(firstFrame, width * 4, width, height, amplitudes, in options);
        }

        var laterOptions = new VisualizerRenderOptions(Color.FromRgb(10, 20, 30), false, false, 10, 4, 4, 2.5);
        renderer.Render(secondFrame, width * 4, width, height, amplitudes, in laterOptions);

        Assert.Contains(firstFrame.Zip(secondFrame), pair => pair.First != pair.Second);
    }
}