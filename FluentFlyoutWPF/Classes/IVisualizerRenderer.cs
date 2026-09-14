// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Windows.Media;

namespace FluentFlyoutWPF.Classes;

internal enum VisualizerRenderStyle
{
    ClassicBars = 0,
    FluentRibbon = 1
}

internal readonly struct VisualizerRenderOptions
{
    public Color AccentColor { get; }
    public bool CenteredBars { get; }
    public bool Baseline { get; }
    public int BarCount { get; }
    public int BarSpacing { get; }
    public int BaselineHeight { get; }
    public double ElapsedSeconds { get; }

    public VisualizerRenderOptions(
        Color accentColor,
        bool centeredBars,
        bool baseline,
        int barCount,
        int barSpacing,
        int baselineHeight,
        double elapsedSeconds)
    {
        AccentColor = accentColor;
        CenteredBars = centeredBars;
        Baseline = baseline;
        BarCount = barCount;
        BarSpacing = barSpacing;
        BaselineHeight = baselineHeight;
        ElapsedSeconds = elapsedSeconds;
    }
}

internal interface IVisualizerRenderer
{
    void Render(
        Span<byte> buffer,
        int stride,
        int imageWidth,
        int imageHeight,
        ReadOnlySpan<float> amplitudes,
        in VisualizerRenderOptions options);
}