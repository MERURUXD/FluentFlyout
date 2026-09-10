// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Reflection;
using Windows.ApplicationModel;

namespace FluentFlyoutWPF.Classes.Downstream;

/// <summary>
/// Resolves the displayed version for both packaged and downstream ZIP builds.
/// </summary>
public static class ProductVersion
{
    public static string Current
    {
        get
        {
            try
            {
                return Format(Package.Current.Id.Version);
            }
            catch (Exception)
            {
                // Package.Current is unavailable for an unpackaged WPF process.
            }

#if GITHUB_RELEASE
            Version? assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version;
            if (assemblyVersion is not null && assemblyVersion.Build >= 0)
                return Format(assemblyVersion);
#endif

            return "debug";
        }
    }

    public static string Display => Current == "debug" ? "debug version" : Current;

    private static string Format(PackageVersion version) =>
        $"v{version.Major}.{version.Minor}.{version.Build}";

    private static string Format(Version version) =>
        $"v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
}
