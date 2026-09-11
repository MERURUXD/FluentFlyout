// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Downstream;

/// <summary>
/// Immutable downstream policy switches. These are intentionally not user settings:
/// the fork must not accidentally re-enable upstream infrastructure at runtime.
/// </summary>
public static class DownstreamPolicy
{
    public static readonly bool EnableUpstreamTelemetry = false;
    public static readonly bool EnableUpstreamExperiments = false;
    public static readonly bool EnableUpstreamUpdateCheck = false;
    public static readonly bool EnableDownstreamUpdateCheck = true;

    public static bool UpdateChecksEnabled =>
        EnableDownstreamUpdateCheck || EnableUpstreamUpdateCheck;
}