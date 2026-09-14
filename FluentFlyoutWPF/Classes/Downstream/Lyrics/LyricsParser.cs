// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.Text.RegularExpressions;

namespace FluentFlyoutWPF.Classes.Downstream.Lyrics;

internal static class LyricsParser
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    internal static IReadOnlyList<LyricsLine> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        var lines = new List<LyricsLine>();
        int offset = 0;
        var offsetMatch = Regex.Match(raw, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase, MatchTimeout);
        if (offsetMatch.Success) int.TryParse(offsetMatch.Groups[1].Value, out offset);
        foreach (string source in raw.Split('\n'))
        {
            string line = source.Trim();
            var qrc = Regex.Match(line, @"^\[(\d+),(\d+)\](.*)$", RegexOptions.None, MatchTimeout);
            if (qrc.Success)
            {
                var words = new List<LyricsWord>();
                foreach (Match word in Regex.Matches(qrc.Groups[3].Value, @"(.*?)\((\d+),(\d+)\)", RegexOptions.None, MatchTimeout))
                {
                    int start = checked(int.Parse(word.Groups[2].Value, CultureInfo.InvariantCulture) + offset);
                    int end = checked(start + int.Parse(word.Groups[3].Value, CultureInfo.InvariantCulture));
                    if (start >= 0) words.Add(new(word.Groups[1].Value, start, end));
                }
                if (words.Count > 0)
                    lines.Add(new(string.Concat(words.Select(w => w.Text)), words[0].StartMs, words[^1].EndMs, words.AsReadOnly()));
                continue;
            }
            var stamps = Regex.Matches(line, @"\[(\d+):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.None, MatchTimeout);
            if (stamps.Count == 0 || stamps[0].Index != 0) continue;
            string text = line[(stamps[^1].Index + stamps[^1].Length)..].Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            foreach (Match stamp in stamps)
            {
                int minutes = int.Parse(stamp.Groups[1].Value, CultureInfo.InvariantCulture);
                int seconds = int.Parse(stamp.Groups[2].Value, CultureInfo.InvariantCulture);
                if (seconds >= 60) continue;
                string fraction = stamp.Groups[3].Value;
                int milliseconds = fraction.Length == 0 ? 0 : int.Parse(fraction.PadRight(3, '0'), CultureInfo.InvariantCulture);
                int start = checked((minutes * 60000) + (seconds * 1000) + milliseconds + offset);
                if (start >= 0) lines.Add(new(text, start, null, []));
            }
        }
        return Array.AsReadOnly(lines.OrderBy(line => line.StartMs).ToArray());
    }
}