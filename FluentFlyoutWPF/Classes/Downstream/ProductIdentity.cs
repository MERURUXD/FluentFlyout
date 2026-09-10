// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;

namespace FluentFlyoutWPF.Classes.Downstream;

/// <summary>
/// Runtime identity for this downstream product. Keep these values stable so the
/// downstream can coexist with the official FluentFlyout installation.
/// </summary>
public static class ProductIdentity
{
    public const string Slug = "FluentFlyoutDownstream";
    public const string DisplayName = "FluentFlyout Downstream";
    public const string ExecutableName = "FluentFlyoutDownstream";
    public const string IconPath = "/Resources/FluentFlyoutDownstream.ico";

    public const string PackageIdentityName = "MERURUXD.FluentFlyoutDownstream";
    public const string MutexName = "FluentFlyoutDownstream";
    public const string SettingsEventName = "FluentFlyoutDownstream_OpenSettings";
    public const string StartupRegistryValueName = "FluentFlyoutDownstream";
    public const string ToastActivatorClsid = "A4B7F5C8-5A1B-4B1F-9F54-4CF40A4E4E85";

    public const string RepositoryUrl = "https://github.com/MERURUXD/FluentFlyout";
    public const string ReleasesUrl = RepositoryUrl + "/releases";
    public const string LatestReleaseUrl = ReleasesUrl + "/latest";
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/MERURUXD/FluentFlyout/releases/latest";
    public const string IssuesUrl = RepositoryUrl + "/issues/new/choose";
    public const string UserAgentName = "FluentFlyoutDownstream";

    public const string LegacyAppDataDirectoryName = "FluentFlyout";
    public const string SettingsFileName = "settings.xml";

    public static string AppDataDirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Slug);

    public static string LegacyAppDataDirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        LegacyAppDataDirectoryName);

    public static string SettingsFilePath => Path.Combine(AppDataDirectoryPath, SettingsFileName);

    public static string LegacySettingsFilePath => Path.Combine(LegacyAppDataDirectoryPath, SettingsFileName);
}
