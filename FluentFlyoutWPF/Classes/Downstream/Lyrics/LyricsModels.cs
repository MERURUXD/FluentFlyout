// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Downstream.Lyrics;

public sealed record LyricsTrack(string Title, string Artist, string Album, int DurationMs);
public sealed record LyricsCandidate(string Id, LyricsTrack Track);
public sealed record LyricsWord(string Text, int StartMs, int EndMs);
public sealed record LyricsLine(string Text, int StartMs, int? EndMs, IReadOnlyList<LyricsWord> Words);
public sealed record LyricsDocument(string Source, string TrackId, IReadOnlyList<LyricsLine> Lines,
    IReadOnlyList<LyricsLine> Translation);

public interface ILyricsProvider
{
    string Name { get; }
    Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<LyricsDocument?> FetchAsync(string id, CancellationToken cancellationToken);
}