// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FluentFlyoutWPF.Classes.Downstream.Lyrics;

/// <summary>
/// QQ / NetEase protocol adapters based on Lyricify Lyrics Helper (Apache-2.0).
/// Uses caller-owned HTTP transport so cancellation reaches the actual request;
/// the library's global HTTP client and unrelated providers are never initialized.
/// </summary>
public sealed class OnlineLyricsProvider(HttpClient client, bool qqMusic) : ILyricsProvider
{
    public string Name => qqMusic ? "QQ Music" : "NetEase";

    public async Task<IReadOnlyList<LyricsCandidate>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        using var request = qqMusic
            ? new HttpRequestMessage(HttpMethod.Post, "https://u.y.qq.com/cgi-bin/musicu.fcg")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    req_1 = new
                    {
                        method = "DoSearchForQQMusicDesktop",
                        module = "music.search.SearchCgiService",
                        param = new { num_per_page = "20", page_num = "1", query, search_type = 0 }
                    }
                }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), Encoding.UTF8, "application/json")
            }
            : new HttpRequestMessage(HttpMethod.Get,
                $"https://music.163.com/api/search/get/web?s={Uri.EscapeDataString(query)}&type=1&offset=0&total=true&limit=20");
        using var json = JsonDocument.Parse(await SendAsync(request, cancellationToken).ConfigureAwait(false));
        var songs = qqMusic ? At(json.RootElement, "req_1", "data", "body", "song", "list")
            : At(json.RootElement, "result", "songs");
        var result = new List<LyricsCandidate>();
        if (songs.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var song in songs.EnumerateArray())
        {
            Add(song);
            if (qqMusic && At(song, "grp").ValueKind == JsonValueKind.Array)
                foreach (var grouped in song.GetProperty("grp").EnumerateArray()) Add(grouped);
        }
        return result;

        void Add(JsonElement song)
        {
            var artists = At(song, qqMusic ? "singer" : "artists");
            if (artists.ValueKind != JsonValueKind.Array)
                return;
            var duration = At(song, qqMusic ? "interval" : "duration");
            if (duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out int milliseconds)
                || milliseconds <= 0 || (qqMusic && milliseconds > int.MaxValue / 1000))
                return;
            if (qqMusic)
                milliseconds = checked(milliseconds * 1000);
            var track = new LyricsTrack(Text(song, qqMusic ? "title" : "name"),
                string.Join(" / ", artists.EnumerateArray().Select(a => Text(a, "name"))),
                Text(At(song, "album"), qqMusic ? "title" : "name"), milliseconds);
            var id = At(song, "id").ToString();
            if (id.Length > 0)
                result.Add(new(id, track));
        }
    }

    public async Task<LyricsDocument?> FetchAsync(string id, CancellationToken cancellationToken)
    {
        string? original;
        string? translation;
        if (qqMusic)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://c.y.qq.com/qqmusic/fcgi-bin/lyric_download.fcg")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["version"] = "15",
                    ["miniversion"] = "82",
                    ["lrctype"] = "4",
                    ["musicid"] = id
                })
            };
            var raw = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            // QQ returns a legacy malformed <miniversion="1" /> tag.
            raw = Regex.Replace(raw, "<miniversion=\"[^\"]*\"\\s*/>", "",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var xml = ParseXml(raw.Replace("<!--", "").Replace("-->", ""));
            original = Decode(xml.Descendants("content").FirstOrDefault()?.Value);
            translation = Decode(xml.Descendants("contentts").FirstOrDefault()?.Value);
        }
        else
        {
            // Line-synchronized fallback. QQ is the primary word-timing source for this stage.
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://music.163.com/api/song/lyric?id={Uri.EscapeDataString(id)}&lv=-1&kv=-1&tv=-1");
            using var json = JsonDocument.Parse(await SendAsync(request, cancellationToken).ConfigureAwait(false));
            original = Text(At(json.RootElement, "lrc"), "lyric");
            translation = Text(At(json.RootElement, "tlyric"), "lyric");
        }
        cancellationToken.ThrowIfCancellationRequested();
        var lines = LyricsParser.Parse(original);
        return lines.Count == 0 ? null : new(Name, id, lines, LyricsParser.Parse(translation));
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        request.Headers.Referrer = new Uri(qqMusic ? "https://c.y.qq.com/" : "https://music.163.com/");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/63.0.3239.132 Safari/537.36");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > 2 * 1024 * 1024)
                throw new InvalidDataException("Lyrics response exceeds the size limit.");
            output.Write(buffer, 0, count);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static JsonElement At(JsonElement value, params string[] path)
    {
        foreach (string key in path)
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out value))
                return default;
        return value;
    }

    private static string Text(JsonElement value, string key) => At(value, key).ValueKind == JsonValueKind.String
        ? At(value, key).GetString()! : string.Empty;

    private static XDocument ParseXml(string raw)
    {
        using var reader = XmlReader.Create(new StringReader(raw), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2 * 1024 * 1024
        });
        return XDocument.Load(reader);
    }

    private static string? Decode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (raw.TrimStart().StartsWith('['))
            return raw;
        // Guard the legacy block decoder before passing provider-controlled hex.
        if (raw.Length % 16 != 0 || !raw.All(Uri.IsHexDigit))
            return null;
        var decoded = Lyricify.Lyrics.Decrypter.Qrc.Decrypter.DecryptLyrics(raw);
        if (decoded?.Contains("<?xml", StringComparison.Ordinal) != true)
            return decoded;
        if (decoded.Length > 2 * 1024 * 1024 || decoded.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsupported lyrics XML.");
        // The upstream helper repairs unescaped ampersands/quotes and preserves line breaks
        // in the legacy LyricContent attribute, which a normalizing XML reader loses.
        var xml = Lyricify.Lyrics.Decrypter.Qrc.XmlUtils.Create(decoded);
        return xml.GetElementsByTagName("Lyric_1").OfType<XmlElement>().FirstOrDefault()?.GetAttribute("LyricContent");
    }
}