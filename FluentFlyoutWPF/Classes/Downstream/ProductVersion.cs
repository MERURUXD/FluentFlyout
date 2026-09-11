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
    public const string DevelopmentChannel = "development";
    public const string DevChannel = "dev";
    public const string StableChannel = "stable";

    public static string Current => ResolveCurrent().Current;

    public static string Display => ResolveCurrent().Display;

    public static bool IsStableIdentity(string? value) => TryParseStableVersion(value, out _);

    internal static BuildIdentity ResolveUnpackaged(
        string? channel,
        string? version,
        string? sourceRevision)
    {
        string normalizedChannel = string.Equals(channel?.Trim(), DevChannel, StringComparison.OrdinalIgnoreCase)
            ? DevChannel
            : DevelopmentChannel;

        string? normalizedRevision = NormalizeRevision(sourceRevision);
        if (string.Equals(channel?.Trim(), StableChannel, StringComparison.OrdinalIgnoreCase)
            && TryParseStableVersion(version, out var stableVersion))
        {
            string current = Format(stableVersion);
            return new BuildIdentity(current, current, true, current, null);
        }

        string revisionSuffix = normalizedRevision is null ? string.Empty : $"+{ShortRevision(normalizedRevision)}";
        string currentIdentity = normalizedChannel + revisionSuffix;
        string display = normalizedChannel == DevChannel
            ? BuildDevelopmentDisplay("dev build", version, normalizedRevision)
            : BuildDevelopmentDisplay("development build", version, normalizedRevision);

        return new BuildIdentity(currentIdentity, display, false, null, normalizedRevision);
    }

    internal static bool TryParseStableVersion(string? value, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];

        string[] components = normalized.Split('.');
        if (components.Length is < 3 or > 4)
            return false;

        if (components.Length == 4 && components[3] != "0")
            return false;

        var numbers = new int[3];
        for (int i = 0; i < numbers.Length; i++)
        {
            if (components[i].Length > 1 && components[i][0] == '0')
                return false;

            if (!int.TryParse(components[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out numbers[i])
                || numbers[i] < 0)
                return false;
        }

        version = new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    private static BuildIdentity ResolveCurrent()
    {
        try
        {
            return ResolveUnpackaged(StableChannel, Format(Package.Current.Id.Version), null);
        }
        catch (Exception)
        {
            // Package.Current is unavailable for an unpackaged WPF process.
        }

        var metadata = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal);

        return ResolveUnpackaged(
            GetMetadata(metadata, "DownstreamBuildChannel"),
            GetMetadata(metadata, "DownstreamBuildVersion"),
            GetMetadata(metadata, "DownstreamSourceRevision"));
    }

    private static string? GetMetadata(IReadOnlyDictionary<string, string?>? metadata, string key) =>
        metadata != null && metadata.TryGetValue(key, out string? value) ? value : null;

    private static string? NormalizeRevision(string? sourceRevision)
    {
        if (string.IsNullOrWhiteSpace(sourceRevision))
            return null;

        string normalized = sourceRevision.Trim();
        return string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static string BuildDevelopmentDisplay(string prefix, string? version, string? sourceRevision)
    {
        string versionPart = TryParseStableVersion(version, out var parsedVersion)
            ? $" {Format(parsedVersion)}"
            : string.Empty;
        string revisionPart = sourceRevision is null ? string.Empty : $" @ {ShortRevision(sourceRevision)}";
        return $"{prefix}{versionPart}{revisionPart}";
    }

    private static string ShortRevision(string revision) =>
        revision.Length <= 7 ? revision : revision[..7];

    private static string Format(PackageVersion version) =>
        $"v{version.Major}.{version.Minor}.{version.Build}";

    private static string Format(Version version) =>
        $"v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    internal sealed record BuildIdentity(
        string Current,
        string Display,
        bool IsStable,
        string? StableVersion,
        string? SourceRevision);
}