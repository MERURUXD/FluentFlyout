// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.ViewModels;
using System.Xml.Linq;
using System.Xml.Serialization;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class RetiredSettingsCompatibilityTests
{
    private static readonly XmlSerializer Serializer = new(typeof(UserSettings));

    [Fact]
    public void LegacyRetiredElementsAreIgnoredWhileCoreSettingsSurvive()
    {
        const string legacyXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <UserSettings>
              <LockKeysEnabled>true</LockKeysEnabled>
              <LockKeysDuration>5000</LockKeysDuration>
              <VolumeControlEnabled>true</VolumeControlEnabled>
              <VolumeControlAboveMediaFlyout>true</VolumeControlAboveMediaFlyout>
              <VolumeControlDuration>5000</VolumeControlDuration>
              <VolumeMixerEnabled>true</VolumeMixerEnabled>
              <VolumeMixerHighlightActiveApps>true</VolumeMixerHighlightActiveApps>
              <VolumeMixerAcrylicWindowEnabled>false</VolumeMixerAcrylicWindowEnabled>
              <TaskbarWidgetScrollVolumeMode>2</TaskbarWidgetScrollVolumeMode>
              <TaskbarWidgetEnabled>true</TaskbarWidgetEnabled>
              <TaskbarVisualizerEnabled>true</TaskbarVisualizerEnabled>
              <NextUpEnabled>true</NextUpEnabled>
              <MediaFlyoutEnabled>true</MediaFlyoutEnabled>
              <MediaSessionSelectionMode>1</MediaSessionSelectionMode>
            </UserSettings>
            """;

        using StringReader reader = new(legacyXml);
        var settings = Assert.IsType<UserSettings>(Serializer.Deserialize(reader));

        Assert.True(settings.TaskbarWidgetEnabled);
        Assert.True(settings.TaskbarVisualizerEnabled);
        Assert.True(settings.NextUpEnabled);
        Assert.True(settings.MediaFlyoutEnabled);
        Assert.Equal(1, settings.MediaSessionSelectionMode);
    }

    [Fact]
    public void RetiredElementsAreNotWrittenBack()
    {
        var settings = new UserSettings
        {
            TaskbarWidgetEnabled = true,
            TaskbarVisualizerEnabled = true,
            NextUpEnabled = true,
            MediaFlyoutEnabled = true,
            MediaSessionSelectionMode = 1
        };

        using StringWriter writer = new();
        Serializer.Serialize(writer, settings);
        var document = XDocument.Parse(writer.ToString());
        var elementNames = document.Root!.Elements().Select(element => element.Name.LocalName).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("LockKeysEnabled", elementNames);
        Assert.DoesNotContain("VolumeControlEnabled", elementNames);
        Assert.DoesNotContain("VolumeControlAboveMediaFlyout", elementNames);
        Assert.DoesNotContain("VolumeControlDuration", elementNames);
        Assert.DoesNotContain("VolumeMixerEnabled", elementNames);
        Assert.DoesNotContain("VolumeMixerHighlightActiveApps", elementNames);
        Assert.DoesNotContain("VolumeMixerAcrylicWindowEnabled", elementNames);
        Assert.DoesNotContain("TaskbarWidgetScrollVolumeMode", elementNames);
        Assert.Contains("TaskbarWidgetEnabled", elementNames);
        Assert.Contains("TaskbarVisualizerEnabled", elementNames);
        Assert.Contains("NextUpEnabled", elementNames);
        Assert.Contains("MediaFlyoutEnabled", elementNames);
        Assert.Contains("MediaSessionSelectionMode", elementNames);
    }
}