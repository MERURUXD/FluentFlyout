// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.ViewModels;
using System.Windows.Controls;
namespace FluentFlyoutWPF.Pages;

public partial class AboutPage : Page
{
    public AboutViewModel AboutViewModel { get; } = new();

    public AboutPage()
    {
        InitializeComponent();
        DataContext = this;
    }
}