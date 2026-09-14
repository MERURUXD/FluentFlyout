// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows.Media;

namespace FluentFlyoutWPF.Classes;

internal sealed class BarsVisualizerRenderer : IVisualizerRenderer
{
    private const int Supersample = 2;

    public void Render(
        Span<byte> buffer,
        int stride,
        int imageWidth,
        int imageHeight,
        ReadOnlySpan<float> amplitudes,
        in VisualizerRenderOptions options)
    {
        int barCount = Math.Min(options.BarCount, amplitudes.Length);
        if (barCount <= 0)
            return;

        byte b = options.AccentColor.B;
        byte g = options.AccentColor.G;
        byte r = options.AccentColor.R;

        bool centeredBars = options.CenteredBars;
        int barBaseline = options.Baseline ? options.BaselineHeight : 0;
        int centerY = imageHeight / 2;

        ComputeLayout(imageWidth, barCount, options.BarSpacing, out int barWidth, out int offsetX);

        float baseRadius = GetCornerRadius(options.BarCount);
        const float aa = 1.25f;
        float invAA = 1f / aa;

        for (int i = 0; i < barCount; i++)
        {
            int barX = offsetX + i * (barWidth + options.BarSpacing);
            int barHeight = GetBarHeight(amplitudes[i], barBaseline, imageHeight);

            if (barHeight <= 0)
                continue;

            ComputeVertical(centeredBars, centerY, barHeight, imageHeight, out int barY, out int barEndY);

            float radius = ClampRadius(baseRadius, barWidth, barHeight);
            float radiusSq = radius * radius;

            RasterizeBar(
                buffer,
                stride,
                imageWidth,
                imageHeight,
                barX,
                barWidth,
                barY,
                barEndY,
                centeredBars,
                radius,
                radiusSq,
                invAA,
                b,
                g,
                r);
        }
    }

    private static void ComputeLayout(
        int imageWidth,
        int barCount,
        int spacing,
        out int barWidth,
        out int offsetX)
    {
        int totalSpacing = (barCount - 1) * spacing;
        int availableWidth = imageWidth - totalSpacing - 1;

        barWidth = availableWidth / barCount;

        int usedWidth = barWidth * barCount + totalSpacing;
        offsetX = (imageWidth - usedWidth) >> 1;
    }

    private static void ComputeVertical(
        bool centered,
        int centerY,
        int height,
        int imageHeight,
        out int y,
        out int endY)
    {
        if (centered)
        {
            int half = height >> 1;
            y = centerY - half;
            endY = centerY + half;
        }
        else
        {
            y = imageHeight - height;
            endY = imageHeight;
        }
    }

    private static int GetBarHeight(float value, int baseline, int imageHeight)
    {
        return Math.Max((int)(Math.Clamp(value, 0f, 1f) * imageHeight), baseline);
    }

    private static float GetCornerRadius(int barCount)
    {
        return (2f * Supersample) / MathF.Max(1f, barCount / 10f);
    }

    private static float ClampRadius(float radius, int width, int height)
    {
        float max = MathF.Min(width, height) * 0.5f;
        return radius > max ? max : radius;
    }

    private static void RasterizeBar(
        Span<byte> buffer,
        int stride,
        int imageWidth,
        int imageHeight,
        int barX,
        int barWidth,
        int barY,
        int barEndY,
        bool centeredBars,
        float radius,
        float radiusSq,
        float invAA,
        byte b,
        byte g,
        byte r)
    {
        float left = barX;
        float right = barX + barWidth;
        float top = barY;
        float bottom = barEndY;

        float innerLeft = left + radius;
        float innerRight = right - radius;
        float innerTop = top + radius;
        float innerBottom = bottom - radius;

        for (int y = barY; y < barEndY && y < imageHeight && y >= 0; y++)
        {
            int row = y * stride;
            float py = y + 0.5f;

            for (int x = barX; x < barX + barWidth && x < imageWidth; x++)
            {
                int index = row + (x << 2);
                if (index + 3 >= buffer.Length)
                    continue;

                float px = x + 0.5f;

                if (px >= innerLeft && px <= innerRight)
                {
                    WritePixel(buffer, index, b, g, r, 255);
                    continue;
                }

                if (py >= innerTop && py <= innerBottom)
                {
                    WritePixel(buffer, index, b, g, r, 255);
                    continue;
                }

                if (!centeredBars && py >= innerBottom)
                {
                    WritePixel(buffer, index, b, g, r, 255);
                    continue;
                }

                float cx = px < innerLeft ? innerLeft : (px > innerRight ? innerRight : px);
                float cy = py < innerTop ? innerTop : (py > innerBottom ? innerBottom : py);

                float dx = px - cx;
                float dy = py - cy;
                float distSq = dx * dx + dy * dy;
                float sdf = (distSq - radiusSq) / (2f * radius);
                float alpha = 0.5f - sdf * invAA;

                if (alpha <= 0f)
                    continue;

                if (alpha > 1f)
                    alpha = 1f;

                WritePixel(buffer, index, b, g, r, (byte)(255 * alpha));
            }
        }
    }

    private static void WritePixel(Span<byte> buffer, int index, byte b, byte g, byte r, byte a)
    {
        buffer[index] = b;
        buffer[index + 1] = g;
        buffer[index + 2] = r;
        buffer[index + 3] = a;
    }
}