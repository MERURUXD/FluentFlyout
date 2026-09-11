// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Downstream;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.ViewModels;
using NLog;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace FluentFlyoutWPF.Pages;

public partial class HomePage : Page
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public HomePage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;

        VersionTextBlock.Text = ProductVersion.Display;

        UpdateLastCheckedText();

        if (!DownstreamPolicy.UpdateChecksEnabled)
        {
            ViewUpdatesButton.Visibility = Visibility.Collapsed;
            UpdateCheckButton.Visibility = Visibility.Collapsed;
        }

    }

    private void UpdateLastCheckedText()
    {
        if (UpdateState.Current.LastUpdateCheck != default)
        {
            LastCheckedText.Text = string.Format(
                Application.Current.FindResource("LastChecked")?.ToString(),
                UpdateState.Current.LastCheckedText);
        }
        else
        {
            LastCheckedText.Text = string.Empty;
        }
    }

    private void ViewUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!DownstreamPolicy.UpdateChecksEnabled)
            return;

        Notifications.OpenChangelogInBrowser();
    }

    private long _lastChecked = 0;

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!DownstreamPolicy.UpdateChecksEnabled)
            return;

        // prevent multiple clicks within 1 second
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _lastChecked < 1)
        {
            return;
        }

        _lastChecked = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (UpdateState.Current.IsUpdateAvailable)
        {
            string url = UpdateState.Current.UpdateUrl;
            UpdateCheckerService.OpenUpdateUrl(url);
        }
        else
        {
            await CheckForUpdatesAsync();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!DownstreamPolicy.UpdateChecksEnabled)
            return;

        try
        {
            UpdateStatusText.Text = Application.Current.FindResource("CheckingForUpdates")?.ToString();

            var result = await UpdateCheckerService.CheckForUpdatesAsync(SettingsManager.Current.LastKnownVersion);

            if (result.Success)
            {
                UpdateState.Current.IsUpdateAvailable = result.IsUpdateAvailable;
                UpdateState.Current.NewestVersion = result.NewestVersion;
                UpdateState.Current.UpdateUrl = result.UpdateUrl;
                UpdateState.Current.LastUpdateCheck = result.CheckedAt;

                UpdateLastCheckedText();

                _ = Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        await Task.Delay(500); // slight delay for better UX

                        if (result.IsUpdateAvailable)
                        {
                            UpdateStatusText.Text = Application.Current.FindResource("UpdateAvailableNotificationTitle")?.ToString();
                        }
                        else
                        {
                            UpdateStatusText.Text = Application.Current.FindResource("UpToDate")?.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error while updating update status text on UI thread");
                    }
                });
            }
            else
            {
                UpdateStatusText.Text = "Unable to check for updates";
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to check for updates from HomePage");
            UpdateStatusText.Text = "Unable to check for updates"; // not localized
        }
    }

    private void TaskbarWidget_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(TaskbarWidgetPage));
    }

    private void NextUp_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(NextUpPage));
    }

    private void TaskbarVisualizer_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(TaskbarVisualizerPage));
    }

    private void System_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(SystemPage));
    }

    private void ViewLogs_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            Process.Start("explorer.exe", FileSystemHelper.GetLogsPath());
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open logs folder");
        }
    }

    private void ReportBug_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ProductIdentity.IssuesUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open bug report page");
        }
    }
}