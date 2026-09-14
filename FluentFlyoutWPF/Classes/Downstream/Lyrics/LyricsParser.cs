// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using System.Text.RegularExpressions;

namespace FluentFlyoutWPF.Classes.Downstream.Lyrics;

internal static class LyricsParser
{
    internal static IReadOnlyList<LyricsLine> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        var type = Regex.IsMatch(raw, @"^\[\d+,\d+\]", RegexOptions.Multiline,
            TimeSpan.FromMilliseconds(100)) ? LyricsRawTypes.Qrc : LyricsRawTypes.Lrc;
        var parsed = ParseHelper.ParseLyrics(raw, type);
        if (parsed?.Lines == null)
            return [];
        var lines = new List<LyricsLine>();
        foreach (var line in parsed.Lines)
        {
            if (line is SyllableLineInfo { Syllables.Count: 0 })
                continue;
            if (line.StartTime is not { } start || start < 0 || string.IsNullOrWhiteSpace(line.Text))
                continue;
            var words = line is SyllableLineInfo syllable
                ? syllable.Syllables.Where(s => s.StartTime >= 0 && s.EndTime >= s.StartTime)
                    .Select(s => new LyricsWord(s.Text, s.StartTime, s.EndTime)).ToArray()
                : [];
            lines.Add(new(line.Text, start, line.EndTime >= start ? line.EndTime : null,
                Array.AsReadOnly(words)));
        }
        return Array.AsReadOnly(lines.OrderBy(line => line.StartMs).ToArray());
    }
}