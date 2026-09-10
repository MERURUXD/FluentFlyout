// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FluentFlyout.Controls;

/// <summary>
/// Interaction logic for TaskbarVisualizerControl.xaml
/// </summary>
public partial class TaskbarVisualizerControl : UserControl
{
    private const double DefaultTaskbarVisualizerHeight = 40;
    private const double SmallTaskbarVisualizerHeight = 28;

    private static Visualizer? visualizer;
    private static TaskbarVisualizerControl? currentControl;

    public TaskbarVisualizerControl()
    {
        InitializeComponent();
        currentControl = this;
        Unloaded += OnUnloaded;

        // Set DataContext for bindings
        DataContext = SettingsManager.Current;

        if (SettingsManager.Current.TaskbarVisualizerEnabled)
        {
            var instance = EnsureVisualizer();
            instance.Start();
            VisualizerContainer.Source = instance.Bitmap;
        }

        if (visualizer is { } existingVisualizer)
        {
            VisualizerContainer.Source = existingVisualizer.Bitmap;
        }

        // for hover animation
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
    }

    public void SetSmallTaskbarMode(bool isSmallTaskbar)
    {
        Height = isSmallTaskbar ? SmallTaskbarVisualizerHeight : DefaultTaskbarVisualizerHeight;
    }

    public static void OnTaskbarVisualizerEnabledChanged(bool value)
    {
        if (value)
        {
            if (currentControl == null)
                return;

            var instance = EnsureVisualizer();
            currentControl.VisualizerContainer.Source = instance.Bitmap;
            instance.Start();
        }
        else
        {
            ReleaseVisualizer();
        }
    }

    public static void StopVisualizer()
    {
        ReleaseVisualizer();
    }

    public static void DisposeVisualizer()
    {
        ReleaseVisualizer();
        currentControl = null;
    }

    private static void ReleaseVisualizer()
    {
        var instance = visualizer;
        visualizer = null;

        if (instance == null)
            return;

        var control = currentControl;
        if (control != null)
        {
            control.Dispatcher.BeginInvoke(() =>
            {
                // A disable/re-enable transition may have installed a new bitmap while
                // the old instance was being disposed. Only detach the bitmap we own.
                if (ReferenceEquals(control.VisualizerContainer.Source, instance.Bitmap))
                    control.VisualizerContainer.Source = null;
            });
        }

        instance?.Dispose();
    }

    private static Visualizer EnsureVisualizer()
        => visualizer ??= new Visualizer();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(currentControl, this))
            currentControl = null;

        Unloaded -= OnUnloaded;
    }

    // TODO: The following mouse events are almost the same as the ones in TaskbarWidgetControl.xaml.cs.
    // We should find a way to unify these methods instead of duplicating them.

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;

        SolidColorBrush targetBackgroundBrush;
        // hover effects with animations, hard-coded colors because I can't find the resource brushes
        WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
        bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;
        if (isDark)
        { // dark mode
            targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
            TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };
        }
        else
        { // light mode
            targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) { Opacity = 0.6 };
            TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 1 };
        }

        // Animate background
        var backgroundAnimation = new ColorAnimation
        {
            To = targetBackgroundBrush.Color,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = targetBackgroundBrush.Opacity,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // rare case where background is not a SolidColorBrush after SetupWindow
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        MainBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;

        // Animate back to transparent
        var backgroundAnimation = new ColorAnimation
        {
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        MainBorder.Background?.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background?.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);

        TopBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // only continue when the visualizer is clickable and actually has content
        // otherwise it would show an empty container to click on which is weird
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;

        // open settings when clicked
        SettingsWindow.ShowInstance("TaskbarVisualizerPage");
    }
}