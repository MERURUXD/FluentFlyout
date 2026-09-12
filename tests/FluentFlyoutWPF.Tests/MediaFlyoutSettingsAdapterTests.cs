// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.ViewModels;
using System.Xml.Linq;
using System.Xml.Serialization;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class MediaFlyoutSettingsAdapterTests
{
    private static readonly XmlSerializer Serializer = new(typeof(UserSettings));

    [Fact]
    public void DurationTextUpdatesDurationAndIsNotSerialized()
    {
        var settings = new UserSettings { Duration = 3000 };

        settings.DurationText = "12000";
        Assert.Equal(10000, settings.Duration);

        settings.DurationText = "not-a-number";
        Assert.Equal(3000, settings.Duration);

        using StringWriter writer = new();
        Serializer.Serialize(writer, settings);
        var elementNames = XDocument.Parse(writer.ToString())
            .Root!
            .Elements()
            .Select(element => element.Name.LocalName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(UserSettings.Duration), elementNames);
        Assert.DoesNotContain(nameof(UserSettings.DurationText), elementNames);
        Assert.DoesNotContain(nameof(UserSettings.IsDurationEditable), elementNames);
    }

    [Fact]
    public void AlwaysDisplayUpdatesDurationEditabilityAndNotifies()
    {
        var settings = new UserSettings { MediaFlyoutAlwaysDisplay = false };
        var propertyNames = new List<string?>();
        settings.PropertyChanged += (_, args) => propertyNames.Add(args.PropertyName);

        Assert.True(settings.IsDurationEditable);

        settings.MediaFlyoutAlwaysDisplay = true;

        Assert.False(settings.IsDurationEditable);
        Assert.Contains(nameof(UserSettings.IsDurationEditable), propertyNames);

        propertyNames.Clear();
        settings.MediaFlyoutAlwaysDisplay = false;

        Assert.True(settings.IsDurationEditable);
        Assert.Contains(nameof(UserSettings.IsDurationEditable), propertyNames);
    }
}