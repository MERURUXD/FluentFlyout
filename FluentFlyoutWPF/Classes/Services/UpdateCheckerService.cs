// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes.Clients;
using FluentFlyoutWPF.Classes.Downstream;
using NLog;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace FluentFlyoutWPF.Classes.Services;

/// <summary>
/// Handles update metadata checks for the downstream and retained upstream channels.
/// </summary>
public static class UpdateCheckerService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly HttpClient DownstreamHttpClient = CreateDownstreamHttpClient();
    private const string ApiEndpoint = "newest-version";

    /// <summary>
    /// Result of an update check.
    /// </summary>
    public class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string NewestVersion { get; set; } = string.Empty;
        public string UpdateUrl { get; set; } = string.Empty;
        public DateTime CheckedAt { get; set; }
        public bool Success { get; set; }
    }

    /// <summary>
    /// Check for update metadata without downloading or installing an update.
    /// </summary>
    /// <param name="currentVersion">The current app version (e.g., "v2.5.0").</param>
    /// <returns>UpdateCheckResult with update information.</returns>
    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion)
    {
        var result = new UpdateCheckResult
        {
            CheckedAt = DateTime.Now
        };

        if (DownstreamPolicy.EnableDownstreamUpdateCheck)
            return await CheckDownstreamForUpdatesAsync(currentVersion);

        if (!DownstreamPolicy.EnableUpstreamUpdateCheck)
            return result;

        try
        {
            var response = await FluentFlyoutApiClient.GetStringAsync(ApiEndpoint);
            using var json = JsonDocument.Parse(response);

            result.NewestVersion = json.RootElement.GetProperty("version").GetString() ?? string.Empty;
            result.UpdateUrl = json.RootElement.GetProperty("url").GetString() ?? string.Empty;
            result.Success = true;

            result.IsUpdateAvailable = currentVersion != "debug" && IsNewerVersion(currentVersion, result.NewestVersion);

            Logger.Info($"Update check complete. Current: {currentVersion}, Newest: {result.NewestVersion}, Update available: {result.IsUpdateAvailable}");
        }
        catch (HttpRequestException ex)
        {
            Logger.Info($"Failed to check for updates - network error: {ex.Message}");
            result.Success = false;
        }
        catch (TaskCanceledException ex)
        {
            Logger.Info(ex, "Failed to check for updates - request timed out.");
            result.Success = false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error checking for updates");
            result.Success = false;
        }

        return result;
    }

    public static void OpenUpdateUrl(string url)
    {
        if (DownstreamPolicy.EnableDownstreamUpdateCheck)
        {
            // The downstream channel always opens its fixed release page. A URL from
            // external release metadata is never treated as an executable/download target.
            url = ProductIdentity.LatestReleaseUrl;
        }
        else if (!DownstreamPolicy.EnableUpstreamUpdateCheck || string.IsNullOrEmpty(url))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open update URL");
        }
    }

    private static async Task<UpdateCheckResult> CheckDownstreamForUpdatesAsync(string currentVersion)
    {
        var result = new UpdateCheckResult
        {
            CheckedAt = DateTime.Now
        };

        if (!TryParseVersion(currentVersion, out _))
        {
            Logger.Info($"Skipping downstream update check for non-release version: {currentVersion}");
            return result;
        }

        try
        {
            using var response = await DownstreamHttpClient.GetAsync(ProductIdentity.LatestReleaseApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Info($"Downstream update check returned HTTP {(int)response.StatusCode}.");
                return result;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!json.RootElement.TryGetProperty("tag_name", out var tagNameElement) ||
                tagNameElement.ValueKind != JsonValueKind.String)
            {
                Logger.Info("Downstream latest release did not contain a tag_name.");
                return result;
            }

            string newestVersion = tagNameElement.GetString() ?? string.Empty;
            if (!TryParseVersion(newestVersion, out _))
            {
                Logger.Info($"Ignoring malformed downstream release tag: {newestVersion}");
                return result;
            }

            result.NewestVersion = newestVersion;
            result.UpdateUrl = ProductIdentity.LatestReleaseUrl;
            result.IsUpdateAvailable = IsNewerVersion(currentVersion, newestVersion);
            result.Success = true;

            Logger.Info($"Downstream update check complete. Current: {currentVersion}, Newest: {newestVersion}, Update available: {result.IsUpdateAvailable}");
        }
        catch (HttpRequestException ex)
        {
            Logger.Info($"Failed to check downstream updates - network error: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            Logger.Info(ex, "Failed to check downstream updates - request timed out.");
        }
        catch (JsonException ex)
        {
            Logger.Info(ex, "Failed to parse downstream release metadata.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error checking downstream updates");
        }

        return result;
    }

    private static HttpClient CreateDownstreamHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ProductIdentity.UserAgentName);
        return client;
    }

    private static bool IsNewerVersion(string currentVersion, string newestVersion)
    {
        if (!TryParseVersion(currentVersion, out var current) ||
            !TryParseVersion(newestVersion, out var newest))
        {
            Logger.Info($"Unable to compare release versions: {currentVersion} vs {newestVersion}");
            return false;
        }

        return newest > current;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version();
        string normalized = value.Trim();
        if (normalized.StartsWith('v'))
            normalized = normalized[1..];

        string[] components = normalized.Split('.');
        if (components.Length is < 3 or > 4)
            return false;

        var numbers = new int[components.Length];
        for (int i = 0; i < components.Length; i++)
        {
            if (!int.TryParse(components[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]) || numbers[i] < 0)
                return false;
        }

        version = components.Length == 4
            ? new Version(numbers[0], numbers[1], numbers[2], numbers[3])
            : new Version(numbers[0], numbers[1], numbers[2]);
        return true;
    }
}