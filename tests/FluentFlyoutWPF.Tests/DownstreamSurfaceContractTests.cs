// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF;
using Xunit;

namespace FluentFlyoutWPF.Tests;

public sealed class DownstreamSurfaceContractTests
{
    [Fact]
    public void RetiredProductionTypesAreAbsent()
    {
        var typeNames = typeof(SettingsWindow).Assembly
            .GetTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var retiredTypes = new[]
        {
            "VolumeMixerPage",
            "LockKeysPage",
            "OnboardingWindow",
            "VolumeMixerWindow",
            "VolumeMixerViewModel",
            "AudioSessionModel",
            "LockWindow",
            "PremiumPurchaseButton",
            "AccentBadge"
        };

        foreach (var retiredType in retiredTypes)
        {
            Assert.DoesNotContain(retiredType, typeNames);
        }

        Assert.Contains("MediaFlyoutPage", typeNames);
    }

    [Fact]
    public void SearchIndexExcludesRetiredPages()
    {
        var targetPageNames = SettingsWindow.SearchItems
            .Select(item => item.TargetPageType.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var retiredPage in new[] { "VolumeMixerPage", "LockKeysPage" })
        {
            Assert.DoesNotContain(retiredPage, targetPageNames);
        }
    }

    [Fact]
    public void SearchIndexPreservesRetainedDeepLinks()
    {
        var targetPageNames = SettingsWindow.SearchItems
            .Select(item => item.TargetPageType.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var retainedPage in new[]
        {
            "TaskbarWidgetPage",
            "TaskbarVisualizerPage",
            "NextUpPage",
            "SystemPage",
            "AppFilteringPage",
            "AdvancedPage"
        })
        {
            Assert.Contains(retainedPage, targetPageNames);
        }
    }
}