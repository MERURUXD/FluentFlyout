// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace FluentFlyoutWPF.Classes.Downstream.Lyrics;

public static class LyricsMatchPolicy
{
    // Deliberately narrower than the existing Spotify Preferred selection policy.
    // A browser identity or a title containing "Spotify" must never authorize network work.
    public static bool IsSpotify(string? applicationId) =>
        string.Equals(applicationId, "Spotify.exe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(applicationId, "Spotify", StringComparison.OrdinalIgnoreCase)
        || string.Equals(applicationId, "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string value) =>
        string.Concat(ToSimplified(value.Normalize(NormalizationForm.FormKC))
            .Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string ToSimplified(string value)
    {
        if (value.Length == 0) return value;
        const uint simplifiedChinese = 0x02000000;
        int length = LCMapStringEx("zh-CN", simplifiedChinese, value, value.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var output = new char[length];
        int written = LCMapStringEx("zh-CN", simplifiedChinese, value, value.Length, output, length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (written == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return new string(output, 0, written);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(string localeName, uint flags, string source, int sourceLength,
        [Out] char[]? destination, int destinationLength, IntPtr version, IntPtr reserved, IntPtr sortHandle);

    public static string SearchTitle(string title) => Regex.Replace(title,
        @"[（(][^()（）]*(?:插曲|主题曲|主題曲|片尾曲|片头曲|片頭曲)[^()（）]*[）)]", "",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim();

    public static bool Matches(LyricsTrack requested, LyricsTrack candidate)
    {
        if (requested.DurationMs <= 0 || candidate.DurationMs <= 0
            || Math.Abs((long)requested.DurationMs - candidate.DurationMs) > 2000)
            return false;

        // Keep Live, remix, demo and remaster labels: do not strip arbitrary parentheses.
        if (Normalize(SearchTitle(requested.Title)) != Normalize(SearchTitle(candidate.Title)))
            return false;

        static string Artist(string value) => Normalize(value) switch
        {
            "saji萨吉" => "萨吉",
            var artist => artist
        };
        return Artist(requested.Artist) == Artist(candidate.Artist);
    }

    public static LyricsCandidate? Select(LyricsTrack requested, IReadOnlyList<LyricsCandidate> candidates)
    {
        var matches = candidates.Where(c => Matches(requested, c.Track)).ToList();
        var album = Normalize(requested.Album);
        var albumMatches = matches.Where(c => album.Length > 0 && Normalize(c.Track.Album) == album).ToList();
        if (albumMatches.Count > 0)
            matches = albumMatches;
        // Ambiguous releases require a manual mapping in a later UI stage.
        return matches.DistinctBy(c => c.Id).Count() == 1 ? matches[0] : null;
    }
}