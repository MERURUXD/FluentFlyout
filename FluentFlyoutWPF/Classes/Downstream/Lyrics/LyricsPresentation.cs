// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Downstream.Lyrics;

public static class LyricsPresentation
{
    public static int FindLine(IReadOnlyList<LyricsLine> lines, double positionMs)
    {
        int low = 0, high = lines.Count - 1, result = -1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (lines[middle].StartMs <= positionMs) { result = middle; low = middle + 1; }
            else high = middle - 1;
        }
        return result;
    }

    public static double WordProgress(LyricsWord word, double positionMs) =>
        word.EndMs <= word.StartMs ? (positionMs >= word.StartMs ? 1 : 0)
            : Math.Clamp((positionMs - word.StartMs) / (word.EndMs - word.StartMs), 0, 1);

    public static string SecondLine(LyricsDocument document, int index, double positionMs, bool translation)
    {
        if (translation && document.Translation.Count > 0)
        {
            int translated = FindLine(document.Translation, positionMs);
            return translated < 0 ? string.Empty : document.Translation[translated].Text;
        }
        return index + 1 < document.Lines.Count ? document.Lines[index + 1].Text : string.Empty;
    }

    public static double Width(double measured, bool fixedWidth, double configured, double available)
    {
        double desired = fixedWidth ? configured : measured + 16;
        if (!double.IsFinite(desired)) desired = 260;
        if (!double.IsFinite(available)) available = 420;
        return Math.Clamp(desired, 100, Math.Max(100, Math.Min(420, available)));
    }
}