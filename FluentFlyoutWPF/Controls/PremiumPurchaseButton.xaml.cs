// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Downstream;
using FluentFlyoutWPF.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace FluentFlyout.Controls;

public partial class PremiumPurchaseButton : UserControl
{
    public UserSettings UserSettings => SettingsManager.Current;

    public PremiumPurchaseButton()
    {
        InitializeComponent();

        if (!DownstreamPolicy.EnableUpstreamPurchaseUi)
            Visibility = Visibility.Collapsed;
    }

    private void PurchaseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DownstreamPolicy.EnableUpstreamPurchaseUi)
            return;

        LicenseManager.UnlockPremium(sender);
    }
}
