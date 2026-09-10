// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Downstream;

internal static class SeekbarTimerPolicy
{
    public static bool ShouldRun(
        bool enabled,
        bool visible,
        bool playing,
        bool supportsSeek,
        bool dragging) => enabled && visible && playing && supportsSeek && !dragging;
}