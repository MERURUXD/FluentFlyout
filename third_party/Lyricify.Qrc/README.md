# Lyricify QRC source subset

`DESHelper.cs`, `Decrypter.cs` and `XmlUtils.cs` from
WXRIW/Lyricify-Lyrics-Helper commit a139e385b032b9abfca4c060627776cf9c3147ad
(the source revision of NuGet 0.2.0). Author: XY Wang / WXRIW.
https://github.com/WXRIW/Lyricify-Lyrics-Helper/tree/a139e385b032b9abfca4c060627776cf9c3147ad/Lyricify.Lyrics.Helper/Decrypter/Qrc

Apache-2.0: see LICENSE. These files are linked into the application build;
no other Lyricify components or CHTCHSConv binaries are distributed.
Keep this source separate from application formatting and retain attribution.

Downstream modification: Decrypter.cs adds an explicit System.IO import and nullable-annotation context; algorithms are unchanged.
