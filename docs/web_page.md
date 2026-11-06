# AI Chat web pages and audio: web_page, web_audio

Part of the web media docs (index: `docs/web_media.md`).

## web_page: reading one page (text + image / audio lists)

`web_search kind="web"` only returns titles + snippets. `web_page` fetches ONE page, extracts readable text
and candidate images, and hands them to the model. Links inside the page are never followed (no crawling).

### Fetch and sources

- `AIChatPanel.WebPageCoroutine` -> `DownloadPageWithTrace` -> `WebMediaDownloader.DownloadToMemory` with
  `accept` = `HtmlAccept`, browser UA, 30 s, 5 MB cap (`WebRequestLimits.MaxPageBytes`), handle registered
  for Stop/Clear. `DownloadResult.Charset` carries the `charset=` that `NormalizeContentType` strips.
- Content gate: `text/html`, `application/xhtml+xml`, `text/plain`, `text/xml`, `application/xml`,
  `application/json`, or empty / `application/octet-stream` that sniffs as `Html`. Anything else is refused
  with a pointer (`not a readable page; use web_image url="..."`, video -> web_video, PDF -> "PDFs cannot be
  read"). `text/plain` / JSON skip the HTML extractor (verbatim, truncated).
- Sources: `url=`; `result="S1:3"` (must be a `kind="web"` session, else "is a images result, not a page");
  `query=` runs a Brave web search (stored as an S-session so `result="S2:N"` can read another hit), ranks
  with `RankWebPageResults` (wikipedia/wikimedia +5; `.edu` / `.gov` / britannica / archive.org +3; social and
  Q&A sites -4; shopping -3; `.pdf` -5; listicle / review / price titles -2; +1 per query word) and tries up
  to `MaxPageSearchAttempts` (3) hits until one yields readable text. Order is logged as `web_page_ranking`.

### Extraction (`Web/WebPageReader.cs`)

Pure C# (no Unity usings; compiles in a plain dotnet console app), run in `Task.Run`; the coroutine polls
`IsCompleted` with epoch checks.

- `DecodeHtml`: BOM > HTTP charset > `<meta charset>` in the first 4 KB > strict UTF-8, invalid UTF-8 falls
  back to windows-1252. Every `Encoding.GetEncoding` is guarded (stripped players).
- `Extract`: single-pass tag scanner with a frame stack (no DOM, no regex over the document; ~60 ms for a
  676 KB Wikipedia article).
  - Raw-skipped: `script style textarea template svg iframe math object canvas audio video`; `<title>` captured.
  - Junk (text AND images dropped): `nav footer aside button select dialog menu`, `header` outside the main
    region, hidden / `aria-hidden` / `display:none` / navigation `role`s, `sup.reference`, and id/class TOKENS
    (never substrings) such as navbox, toc, sidebar, cookie, comments, share, related, ads, reflist, catlinks,
    plus prefix rules. `AllowTokens` protects Wikipedia's article wrappers (`vector-body`, `mw-body-header`)
    from the prefix rules.
  - `<noscript>` suppresses text but keeps `<img>` (lazy-load fallbacks).
  - Main region: `main`, `article`, `[role=main]`, `[itemprop=articleBody]` or id/class hints
    (`mw-content-text`, `entry-content`, ...). The LONGEST region is used if >= 200 chars and >= 25% of the
    body, else the whole body (`Scope` = main | article | content | body | text). A content hint opening inside
    an unclosed junk element closes that element first.
  - Rendering: headings to `#`..`####`, list items indented `- ` / `N. `, table cells ` | `, infobox `th`+`td`
    -> `Label: value`, `figcaption` / `.thumbcaption` -> `[caption] ...` (also the alt fallback of the figure's
    first image), `pre` verbatim, whitespace/entities collapsed, empty headings removed, `<aitools_action`
    neutralised to `[aitools_action`.
  - Tolerant parsing: implicit closes for `p li dt dd td th tr option`, unknown close tags ignored, uppercase
    tags and unquoted attributes accepted, an unterminated tag stops the scan.
  - Images: `og:image` / `twitter:image` / `link rel=image_src` first, then `<img>` in document order using the
    largest `srcset` entry, a larger non-SVG `<picture><source>`, else `data-src` / `data-lazy-src` /
    `data-original` / `src`; resolved against `<base href>` / page URL, http(s) only, deduped. Rejected:
    `data:` URIs, `.svg` / `.ico`, declared size < 100 px, `IconWords` in alt/class/id/URL, `logo` (always in
    alt/class/id; in the URL when declared width < 150 or unknown). Wikimedia: `?utm_source=` dropped and
    `TryRewriteWikimediaThumb` rewrites `/thumb/.../250px-Name.jpg` to the original (vector/document/animated
    originals keep the largest thumb).
  - Audio links: `<a href>` targets ending in an audio extension plus `<audio src>`, deduped, capped at 30,
    labelled with the link text (else file name), junk-gated like images.
  - `TruncateAtBoundary`: cut at the last newline at or before `max_chars` (never before 60% of it), else the
    last space, else hard; appends `[truncated, N more chars]`.

### Delivery

- Page session `P<n>` (`WebPageSession { Id, Url, Title, Images }` plus audio links, `_webPageSessions`,
  cleared on Clear). `AIChatLog.Note("web_page_text", ...)` logs the full sent text.
- ONE info-recap injection (`_infoMessages`, same channel as `read_skill`; never a system-role line, which
  would rewrite the cached prefix): `[Web page: <title> (<host>)] <url>`, the text, a "first N of M chars"
  note, then `[Page images P1 - NOT downloaded yet; fetch one with web_image result="P1:N" ...]` lines and
  `P1:aN` audio lines (`FormatAudioLine`), ending with "links were NOT followed; quote only the text above".
- `resume` defaults true (`RequestAutoResumeAfterWebFetch`), so the text is in the next turn.
- `web_image result="P1:3"`: `TryResolveWebPageToken` builds one candidate; the vision-check subject becomes
  `<page title> - <alt>`; `Width` is left 0 so the claimed-width pre-skip never rejects a rewritten original.
  Everything downstream is shared with normal `web_image`. No Brave key is needed for `result=` tokens. An
  S-token pointing at a `kind="web"` hit says "read it with web_page result=... first".
- Bubble shows title, `Title:` / `Extracted N chars` / `Images:` counts and `Done`; the `[details]` trace
  holds the URL, HTTP status, bytes, ranking and the image/audio lists. Failure paths inject a note (and one
  bounded continue when `resume="false"`); cancel paths exit silently.
- `web_page` counts as preparatory for the unfinished-plan safety net and as web work for
  `HasPendingSidecarWork()`.

Offline harness (not in the repo): a dotnet console project that `<Compile Include>`s `WebPageReader.cs` and
runs fixtures, or `--fetch <url> <file>` to print title, scope, counts and the `P1:N` list. Register
`CodePagesEncodingProvider` there (.NET needs it for windows-1252; Unity's Mono has the code pages).

## web_audio: bare sound files

Fetches a direct sound-file URL into a playable `Audio #N` bubble.

- **Sniffing**: `SniffMagic` knows Wav (RIFF+WAVE), Mp3 (ID3 or frame sync, checked after JPEG/containers),
  Flac, Ogg, M4a; `IsAudioKind`.
- **Sources**: `url=` (direct file; `YtDlpTool.LooksLikeDirectAudioUrl`), `result="P1:a2"` (page audio link
  via `TryResolveWebPageAudioToken`, which also accepts bare `P1:3`), or an `S` web hit whose URL is a sound
  file. NO `query=` mode (no audio search vertical): the executor refuses it with the recipe
  `web_search kind="web"` -> `web_page` -> `web_audio result="P1:aN"`. Soundboard pages are indexed by web
  search; their .wav links are not.
- **Gate**: no vision check. Accept = `FfmpegTool.ProbeAudio` finds an audio stream and NO video stream
  (mp3/flac cover art, `attached_pic`, does not count). HTML / image / video sniffs are refused with a pointer
  to the right skill (a video's soundtrack: web_video, then `set_video_audio`). A URL that NAMES a sound file
  but returns HTML is a hotlink/scraper decoy: the note says the HOST is protected, do not retry its links,
  prefer archive.org.
- **Speech**: `SpeechCheck` always runs (transcript into caption and recap); with `speech="true"` silent or
  music-only files are rejected (voice-reference case); without it any sound passes.
- **Trim**: `start` / `duration` via `ExtractAudioWavSection` (full-quality WAV); sources over
  `MaxAudioSeconds` (300 s) auto-trim from `start`. Byte cap `MaxAudioBytes` 50 MB.
- **Landing**: `AppendAudioBubble` (same seam as dropped/generated audio, `docs/audio_generation.md`): teal
  `AudioColorWeb` waveform preview, lossless `audioPath`, `alwaysIncludeCaption`, synthesized caption,
  provenance `web audio: "label" host/path`, dedupe via `_webFetchedUrlToPic`, anchors, epoch cancellation.
- The recap names its uses: `set_video_audio` and the H3 standalone `audio=` reference as THE voice path.
  Seth's rule (2026-08-30): assume the H3 reference; never ask about or offer `generate_speech ref_voice`
  cloning as an alternative.
