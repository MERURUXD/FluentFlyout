// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows.Media;

namespace FluentFlyout.Classes.Utils;

internal static class AlbumArtAccentColor
{
    internal static Color Extract(ReadOnlySpan<byte> pixels)
    {
        // Equal area weighting retains muted backgrounds. Neighboring bins share
        // support so gradual backgrounds do not lose to a small vivid detail.
        const int bins = 8;
        int[] counts = new int[bins * bins * bins];
        long[] reds = new long[counts.Length];
        long[] greens = new long[counts.Length];
        long[] blues = new long[counts.Length];
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 128) continue;
            int r = pixels[i + 2];
            int g = pixels[i + 1];
            int b = pixels[i];
            int index = (r >> 5) * bins * bins + (g >> 5) * bins + (b >> 5);
            counts[index]++;
            reds[index] += r;
            greens[index] += g;
            blues[index] += b;
        }

        long bestCount = 0;
        Color best = Color.FromRgb(128, 128, 128);
        for (int index = 0; index < counts.Length; index++)
        {
            if (counts[index] == 0) continue;
            int r = index / (bins * bins);
            int g = (index / bins) % bins;
            int b = index % bins;
            long count = 0;
            long red = 0;
            long green = 0;
            long blue = 0;
            for (int ri = Math.Max(0, r - 1); ri <= Math.Min(bins - 1, r + 1); ri++)
                for (int gi = Math.Max(0, g - 1); gi <= Math.Min(bins - 1, g + 1); gi++)
                    for (int bi = Math.Max(0, b - 1); bi <= Math.Min(bins - 1, b + 1); bi++)
                    {
                        int neighbor = ri * bins * bins + gi * bins + bi;
                        count += counts[neighbor];
                        red += reds[neighbor];
                        green += greens[neighbor];
                        blue += blues[neighbor];
                    }

            if (count <= bestCount) continue;
            bestCount = count;
            best = Color.FromRgb((byte)(red / count), (byte)(green / count), (byte)(blue / count));
        }

        return best;
    }
}