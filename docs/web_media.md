# AI Chat web media: Brave search + image / video download

AI Chat can search the web for images and short video clips and pull them into chat
as ordinary `#N` / `Movie #N` bubbles, so the model can use real reference photos
(people, characters, places, products, logos) as `chat_image2..9` photo references for
the MiniMax H3 Reference To Video preset or Klein edits, and reference clips as
`<Video 1>` for Reference Video To Video.

## Pieces

| piece | where | role |
|---|---|---|
| Brave client | `Assets/_Script/LLM/AIChat/Web/BraveSearchClient.cs` | `GET https://api.search.brave.com/res/v1/{images,videos,web}/search`, header `X-Subscription-Token`, SimpleJSON parse (every field optional), numbered result formatting, ~0.6 s spacing between calls |
| Downloader | `Web/WebMediaDownloader.cs` | `UnityWebRequest` to memory (images, 25 MB cap) or file (videos, 250 MB cap), browser User-Agent, 30 s timeout, `Abort()` on cap/cancel, magic-byte sniffing, public-host-only URL gate (`IsAllowedPublicHttpUrl`: http/https, no userinfo, no loopback / RFC1918 / link-local / `localhost`) |
| Image normalizer | `WebImageConverter` (same file) | PNG/JPEG decode-check with a throwaway `Texture2D.LoadImage` (real pixel size); webp/gif/avif/bmp/tiff or oversized (>2048 px) originals go through `FfmpegTool.ConvertImageToPng` |
| yt-dlp wrapper | `Web/YtDlpTool.cs` | resolves `utils/yt-dlp/yt-dlp.exe` (then PATH), auto-detects a JS runtime (deno / node / bun on PATH, passed as `--js-runtimes`), builds the exact command line, runs it via `FfmpegTool.RunProcessCancellable`, progress lines drained on the main thread. Downloads the WHOLE video capped at 480p (`--match-filters "duration<?N"`, `--max-filesize 250m`); the host cuts the section locally |
| Page reader | `Web/WebPageReader.cs` | pure C# (no Unity usings, compiles in a plain dotnet console app): charset decode (BOM > HTTP charset > `<meta charset>` > strict UTF-8 with windows-1252 fallback), single-pass HTML tag scanner -> readable text + candidate image list; see "web_page" below |
| Trace bubble | `Web/WebTraceBubble.cs` + `AIChatPanel.BeginWebTrace` | the always-visible "Web" bubble; plain text (only TMP angle brackets escaped, NO markdown pass), throttled status line, every line mirrored to `llm_aichat_log.json` as `note/web` |
| Host | `AIChatPanel.cs` "Web media fetch" region | busy gate (`_webFetchCount`, `_webCaptionInFlight`), epoch-based cancellation, the four coroutines, `AppendWebStillBubble`, caption tracking, search sessions `S1..`, page sessions `P1..` |
| Executor | `SkillActionExecutor.cs` `ExecuteWebSearch/Image/Video/Page` | argument parsing + aliases, Web-toggle / key / URL pre-flight (`WebPreflight`), defers the pump like `extract_still` |
| Web toggle | `AIChatPanel.CreateHeader` (`_webToggle`), `GetWebEnabled()` (`aichat_web_enabled`, default on) | header checkbox; see "Web toggle" below |
| Settings | `AppSettingsPanel` Web tab, `Config.cs` | `set_brave_search_api_key`, `set_web_search_safesearch` (strict/off), `set_ytdlp_cookies_browser` in `config.txt` |
| Prompts | `aichat/skills/web_image.md`, `web_video.md`, `web_search.md`, `web_page.md`, `main_prompt.txt` | routing, the Seinfeld-style multi-reference recipe, the RESEARCH recipe |

Bundled helper: `utils/yt-dlp/yt-dlp.exe` (Unlicense, `utils/yt-dlp/README.txt` has the
version). `UpdateBuildDirConfigFiles.bat` already copies `utils`, so builds ship it.

## Model-facing actions

| skill | attributes | result |
|---|---|---|
| `web_search` | `query`, `kind=images\|videos\|web` (default images), `count` (max 20), `safesearch`, `resume` (default TRUE) | list only; stored as `S1`, `S2`... for `result="S1:3"`; auto-continues with the list |
| `web_image` | one of `query` / `url` / `result`; `count` (max 4), `anchor` (count>1 -> `name`, `name_2`...), `min_width` (256), `criteria` (extra vision-check requirements), `verify` (default true), `safesearch`, `resume` (default false) | assistant still bubble(s) `#N`, kind `web image`, provenance `web image: "query" -> host/path`, vision-verified and captioned |
| `web_video` | one of `query` / `url` / `result`; `start`, `duration` (0.5..15, default 5), `max_source_minutes` (20), `criteria`, `verify` (default true), `audio`, `anchor`, `resume` (default TRUE) | `Movie #N` via yt-dlp whole-video download at <=480p (page URLs) or direct file download, then `FfmpegTool.CreateClip` cuts `start`..`start+duration`; each cut is vision-checked via a contact sheet, rejects retry +30 s / +90 s in the same source, then the next ranked result (4 sources max) |
| `web_page` | one of `url` / `result` (a `kind="web"` hit) / `query`; `max_chars` (500..20000, default 6000), `images` (default true), `max_images` (1..40, default 12), `safesearch`, `resume` (default TRUE) | no bubble: the page's readable text goes to the model via the info-recap tail, its image candidates are stored as `P1`, `P2`... for `web_image result="P1:3"`; the Web bubble shows URL / HTTP status / bytes / char counts / the image list |

Aliases (`NormalizeSkillId`): `search_web`, `image_search`, `brave_search`... -> `web_search`;
`find_image`, `fetch_image`, `download_image`... -> `web_image`;
`download_video`, `fetch_video`, `youtube`, `web_clip`... -> `web_video`;
`read_page`, `fetch_page`, `open_url`, `read_url`, `browse`, `visit`, `web_fetch` (moved here from `web_image`)... -> `web_page`.

## Choosing good images (ranking + vision verification)

The first version took the first download that decoded, which produced a wall of framed
portraits, a photo of a screen and an AI caricature for "Donald Trump portrait photo" while
the Wikimedia close-up sat at result 5. Two layers fix that:

1. **Metadata ranking** (`RankWebImageCandidates`): every Brave result gets a score before
   anything is downloaded. Wikimedia/Wikipedia +5, .gov/.edu/Britannica +3; AI-art, clipart,
   PNG-cutout, wallpaper, stock (watermarks), merch, Pinterest and meme hosts -4
   (`WebImageJunkHosts`); tell-tale title words (clipart, caricature, painting, wallpaper,
   poster, funko, "comment image"...) -3; "official portrait"/"headshot"/"press photo" +1;
   >=600 px +1, <300 px -1, banner aspect -2. Stable sort, ties keep Brave order. The trace
   prints `Download order by source quality: 5 (+6), 1 (+2), ...`. Results whose claimed
   width is already under `min_width` are skipped without downloading.
2. **Vision verification** (`VerifyWebImageCoroutine`, one call per download): the decoded
   image goes to the vision LLM with `BuildWebImageVerifyPrompt` (query + optional
   `criteria`), which answers `VERDICT: SUITABLE|UNSUITABLE`, `REASON: ...`, then the normal
   `SHORT:`/`LONG:` caption lines (the caption prompt is appended verbatim, and the existing
   `ParseCaptionResponse` ignores the leading VERDICT/REASON lines). UNSUITABLE -> the file is
   deleted, `-> vision check: UNSUITABLE - <reason>, skipped` is traced and the next candidate
   is tried (up to `MaxImageCandidates` = 12 downloads). SUITABLE -> the bubble is added and
   the caption from the same call is applied (`ApplyCaptionResultToPic`), so no second
   caption sidecar runs. No verdict (timeout / model ignored the format) -> accepted as
   unverified with the normal caption path. The check is skipped (and said so in the trace)
   when no active LLM accepts vision jobs or the action passed `verify="false"`.
   `TryCaptionBytes` grew an `onRawText` callback for this; the verify prompt deliberately
   allows scene stills / event photos where the subject is the clear main subject, and
   rejects reproductions (photo of a screen / framed picture), art, wrong subject, crowds.

Measured: "Donald Trump portrait photo face" now picks the Wikimedia close-up first, verified
and captioned in 6.5 s; "Cosmo Kramer ... portrait photo" count=2 rejected four painted /
scene-with-portrait stills before accepting a real publicity still.

Video gets the same treatment (`RankWebVideoCandidates` + `VerifyWebVideoClipCoroutine`): the
first version cut seconds 5-10 of the top result for "Cosmo Kramer ... entrance scene", which
was a Rich Eisen talk-show interview (host intro), and accepted it blind. Now titles with
interview / podcast / reaction / explained / news words rank down, scene / clip / best-of /
compilation titles and query-word matches rank up, and every cut is contact-sheeted and judged
(`BuildWebVideoVerifyPrompt`: "people talking ABOUT the subject, title cards, intros, wrong
subject -> UNSUITABLE"). An UNSUITABLE cut from a searched source retries at +30 s and +90 s
(`WebRequestLimits.VideoRetryOffsets`) before the next source; a SUITABLE cut's caption comes
from the same call (`AppendVideoClipBubble(..., autoCaption:false)` + `ApplyCaptionResultToPic`).
`web_video` now auto-continues by default (resume="false" opts out) because the log also showed
the model ending its turn after "I'll find a clip first, then make the video" with no follow-up.

Unfinished-plan safety net (`SkillActionExecutor.TurnHadOnlyPreparatoryActions`, checked in
`AIChatPanel.FinalizeAssistantTurn`): actions are tallied at ENQUEUE time as preparatory
(`web_image`, `web_video`, `web_search`, `extract_still`, `clip_video`) or other. A reply whose
actions were all preparatory and that registered no resume/continue of its own gets one
`RegisterGenericContinueRequest` plus a silent note ("your previous reply only prepared media...
emit the render NOW or say it is complete"). This exists because the log showed "First, let me
extract a frame, then generate the video" followed by nothing, twice. The generic scheduler waits
for the fetch/extract to finish (sidecar work) before firing, and the consecutive-continue cap
still bounds it; a reply that only wanted the clip costs one short "done" turn. Measured: "find a
clip of Kramer and make a 5 s movie of him talking about Zelda" now goes web_video (ranked,
verified) -> auto-continue -> `video_to_video` with the H3 reference preset in 18 s, render lands ~5
min later.

Speech check (`Web/SpeechCheck.cs`, `web_video speech="true"`): vision sidecars only see frames, so a
music-only clip passed the verdict and H3 Ref2VA (which clones the reference clip's audio) produced a
garbled voice. With `speech="true"` (also implied by criteria words like talk/speak/dialog/voice) each
accepted cut is audio-checked before it enters chat: `FfmpegTool.ExtractAudioWav` (16 kHz mono),
`FfmpegTool.MeasureMeanVolume` (volumedetect; below -50 dB = silent = definite no-speech), then
OpenAI Whisper `whisper-1` with `response_format=verbose_json`; speech is present when >= 5 real
words came back, the average segment `no_speech_prob` is <= 0.6 and the text is not a `[Music]` /
`♪` marker. No-speech cuts retry +30/+90 s then the next source; an accepted clip's LONG caption gets
`Audio transcript: "..."` and the recap says it is usable as a voice reference. The transcription
endpoint is Settings > Web "Speech-to-text": any OpenAI-compatible `/v1/audio/transcriptions` URL
(`set_stt_endpoint`, optional `set_stt_api_key`, `set_stt_model`, e.g. a local faster-whisper /
Speaches / LocalAI / vLLM whisper server), falling back to api.openai.com with the LLM Settings OpenAI
key. With neither, only silent cuts can be rejected and the trace + recap say the check was
unavailable. A pure signal heuristic (envelope modulation, spectral flatness, pause ratio) was
prototyped on real clips and rejected: the Seinfeld bass theme scored like speech. A bigger vision
model would not help either; vision never hears the track.

Trace compaction (same change): the bubble shows `Searched Brave images for "...": 20 hits (0.9s)`
instead of the GET line + HTTP status + the full numbered list; the list and the ranking order go
to `llm_aichat_log.json` as `web_results` / `web_ranking` notes. Only the list-only `web_search`
skill still prints the numbered results in the bubble.

## web_page: reading a page (text + image list)

`web_search kind="web"` only returns Brave titles + snippets, and every HTML response used to be
sniffed as `MediaKind.Html` and rejected, so the model could not read an article or see which
pictures it contained. `web_page` (since 2026-08-24) fetches ONE page and:

1. **Fetch** (`AIChatPanel.WebPageCoroutine` -> `DownloadPageWithTrace`): `WebMediaDownloader.DownloadToMemory`
   with the new optional `accept` parameter (`HtmlAccept`; image downloads keep `ImageAccept`), browser UA, 30 s,
   5 MB cap (`WebRequestLimits.MaxPageBytes`), the `Handle` registered for Stop/Clear. `DownloadResult.Charset`
   now carries the `charset=` parameter that `NormalizeContentType` strips. Content gate: `text/html`,
   `application/xhtml+xml`, `text/plain`, `text/xml`, `application/xml`, `application/json`, or an empty /
   `application/octet-stream` type whose bytes sniff as `Html`. Anything else -> `-> HTTP 200 JPEG 184,233 bytes:
   not a readable page; use web_image url="..." for it` (video -> web_video, `application/pdf` -> "PDFs cannot be
   read") plus a silent note. `text/plain` / json skip the HTML extractor (verbatim, truncated).
2. **Sources**: `url=` (one fetch); `result="S1:3"` (must be a `kind="web"` session, otherwise "is a images
   result, not a page"); `query=` runs a Brave web search (stored as an S-session too, so `result="S2:N"` can read a
   different hit), ranks with `RankWebPageResults` (wikipedia/wikimedia +5; `.edu`/`.gov`/britannica/archive.org +3;
   reddit/quora/pinterest/facebook/x/twitter/instagram/tiktok/youtube/linkedin/tumblr -4; amazon/ebay/etsy/... -3;
   `.pdf` path -5; "top 10 / best / review / buy / cheap / coupon / deal / price / vs" titles -2; every query word in
   title+snippet +1; stable) and tries up to `MaxPageSearchAttempts` (3) hits until one yields readable text. That is
   a SEARCH fallback only: links inside a fetched page are never followed (one page per action, no crawling).
   Order goes to the log as `web_page_ranking`.
3. **Extract** (`WebPageReader`, run inside `Task.Run` because it is pure C#; the coroutine polls `IsCompleted`
   with epoch checks): `DecodeHtml` (BOM > HTTP charset > `<meta charset>` in the first 4 KB > strict UTF-8, and
   invalid UTF-8 falls back to windows-1252 / Latin-1; every `Encoding.GetEncoding` is guarded for stripped
   players), then `Extract`, a single-pass tag scanner with a frame stack (no DOM, no regex over the document,
   ~60 ms for the 676 KB Atari 2600 article, ~220 ms for a synthetic 2.5 MB page):
   - raw-skipped: `script style textarea template svg iframe math object canvas audio video`; `<title>` captured.
   - junk (text AND images dropped): `nav footer aside button select dialog menu`, `header` outside the main
     region, `hidden` / `aria-hidden` / `display:none` / navigation-type `role`s, `sup.reference`, and id/class
     TOKENS (never substrings, so `unavailable` is not `nav`) such as `navbox toc sidebar breadcrumb cookie
     comments share social related ads promo newsletter popup modal hatnote mw-editsection reflist references
     catlinks printfooter noprint portal-bar authority-control shortdescription metadata` plus prefixes
     (`vector- mw-jump navbox- share- comment- ...`) and id prefixes (`p- mw-navigation vector- footer catlinks`).
     `AllowTokens` protects Wikipedia's article wrappers (`vector-body`, `mw-body-header`, ...) from the prefix
     rules, which is how the `<h1>` inside `header.mw-body-header.vector-page-titlebar` survives.
   - `<noscript>` suppresses text but keeps `<img>` (lazy-load fallbacks).
   - main region: `main`, `article`, `[role=main]`, `[itemprop=articleBody]` or id/class hints (`mw-content-text
     mw-parser-output bodyContent content(id) main-content entry-content article-body ...`) record a (start, end)
     range in the one text buffer; the LONGEST region is used if it is >= 200 chars and >= 25% of the body, else
     the whole body (`Scope` = main | article | content | body | text). A content hint opening inside an
     unclosed junk element closes that junk element first.
   - rendering: `h1` `# `, `h2` `## `, `h3` `### `, `h4-6` `#### `; `li` -> indent + `- ` / `N. `; `br` newline;
     `hr` `---`; table cells ` | ` and infobox (`table.infobox`) `th` + `td` -> `Label: value`; `figcaption` /
     `.thumbcaption` -> `[caption] ...` (also used as the alt fallback of the figure's first image); `pre` verbatim;
     whitespace / entities / U+00A0 collapsed; at most one blank line; headings with no body (e.g. "References"
     after the list was dropped) removed; `<aitools_action` neutralised to `[aitools_action`.
   - implicit closes for unclosed `p li dt dd td th tr option`; unknown close tags ignored; uppercase tags and
     unquoted attribute values accepted; an unterminated tag stops the scan.
   - images (`wantImages`): `og:image` / `og:image:secure_url` / `twitter:image` / `link rel=image_src` first
     (with `og:image:width/height/alt`), then `<img>` in document order using the largest `srcset` entry
     (`w`, else `x`), a larger non-SVG `<picture><source>`, else `data-src` / `data-lazy-src` / `data-original` /
     `src`; resolved against `<base href>` / the page URL (protocol-relative `//` works), http(s) only, fragment
     stripped, deduped case-insensitively. Rejected: `data:` URIs, `.svg` / `.ico` paths, both `width`+`height`
     (or `data-file-width/height`) present and either < 100 px, `IconWords` in alt/class/id/URL (`icon avatar
     sprite tracking badge button emoji spinner loading blank 1x1 spacer arrow bullet oojs favicon wordmark
     gravatar captcha /static/ centralautologin placeholder transparent smiley`), `logo` in alt/class/id always
     and in the URL when the declared width is < 150 or unknown. Wikimedia: the `?utm_source=` query is dropped
     and `TryRewriteWikimediaThumb` turns `/wikipedia/<proj>/thumb/a/ab/Name.jpg/250px-Name.jpg` into the
     original `/wikipedia/<proj>/a/ab/Name.jpg` (dims from `data-file-width/height`; svg/tif/pdf/djvu/webm/ogv/
     gif/xcf originals keep the largest thumb instead, "vector/document original, largest thumb kept").
   - truncation: `TruncateAtBoundary` cuts at the last newline at or before `max_chars` (never before 60% of
     it), else the last space, else hard, and appends `[truncated, N more chars]`.
4. **Deliver**: page session `P<n>` (`WebPageSession { Id, Url, Title, Images }`, `_webPageSessions`, cleared on
   Clear like `S<n>`), trace lines, `AIChatLog.Note("web_page_text", ...)` with the full sent text, and ONE
   info-recap injection (`_infoMessages`, i.e. the same channel as `read_skill`; never a system-role line, which
   would rewrite the cached prompt prefix):
   ```
   [Web page: Atari 2600 - Wikipedia (en.wikipedia.org)] https://en.wikipedia.org/wiki/Atari_2600
   # Atari 2600
   Manufacturer: Atari, Inc.
   ...
   (Only the first 5863 of 39249 chars were sent; re-run web_page with max_chars up to 20000 to read more.)

   [Page images P1 - NOT downloaded yet; fetch one with web_image result="P1:N" anchor="name" (vision-checked and captioned like any web_image)]
   P1:1 https://upload.wikimedia.org/wikipedia/commons/0/02/Atari-2600-Wood-4Sw-Set.png (wikimedia thumb -> original)
   P1:4 https://upload.wikimedia.org/wikipedia/commons/4/4a/Atari-2600-Woody-FL.jpg (alt: "Starting in 1980, ...", 5850x3180, largest srcset, wikimedia thumb -> original (thumb 250x136))
   (This page was fetched once; links inside it were NOT followed. Quote or summarize from the text above only. Page ids expire on Clear.)
   ```
   `resume` defaults to true (`RequestAutoResumeAfterWebFetch`), so the text is in the very next turn.
5. **`web_image result="P1:3"`**: `WebImageCoroutine`'s result branch recognises a `P`+digit token
   (`TryResolveWebPageToken`) and builds one `WebImageCandidate` from the page list; `queryForProvenance` (the
   vision-check subject) becomes `<page title> - <alt>`; `Width` is deliberately left 0 so the claimed-width
   pre-skip never rejects a rewritten original whose `<img width>` said 250. Everything downstream (dedupe,
   ffmpeg normalize, `min_width` on decoded pixels, verify, caption, anchor, `alwaysIncludeCaption`) is shared.
   `ExecuteWebImage` no longer demands a Brave key for `result=` tokens (`needsSearch` is false for both S and P
   tokens). An S-token that points at a `kind="web"` hit now says "read it with web_page result=... first".

Trace bubble:
```
web_page  url="https://en.wikipedia.org/wiki/Atari_2600"  max_chars=6000  images=true  max_images=12
GET https://en.wikipedia.org/wiki/Atari_2600
  -> HTTP 200 text/html 676,548 bytes in 0.5s (utf-8)
Title: Atari 2600 - Wikipedia
Extracted 39,249 chars from <main>; sending 5,863 (truncated, 33,416 more)
Images (12 of 14 candidates; fetch one with web_image result="P1:N"):
  P1:1 https://upload.wikimedia.org/wikipedia/commons/0/02/Atari-2600-Wood-4Sw-Set.png (wikimedia thumb -> original)
  ...
Done in 0.7s.
```
query= adds `Searched Brave web for "...": N hits`, `Stored as S2 (...)`, `Fetch order by source quality: 3 (+6), 1 (+1)...`
and `GET <url>  (<title>)` per attempt. Failures: `-> HTTP 403 Forbidden; trying the next result`, `-> no readable
text (12 chars; the page is probably rendered by JavaScript or is an anti-bot page)`, `Result S1:2 is a images
result, not a page`, `Skip <url>: private / loopback ... not allowed`. Every failure path injects a note and, when
`resume="false"`, one bounded continue; cancel paths (Stop / Clear bump `_webFetchEpoch`) exit silently like the
other fetches. `web_page` counts as a preparatory action for the unfinished-plan safety net and as web work for
`HasPendingSidecarWork()` (Send / `/status idle` / auto-resume wait for it).

Measured (2026-08-24, Qwen 27B main LLM): "Research the Atari 2600 from its Wikipedia article: read the page, tell
me 3 facts, fetch two photos from that page as anchors, render the console on a shag carpet" -> `web_page url=`
(0.7 s), auto-continue with three facts quoted from the fetched text, `web_image result="P1:1"` (accepted),
`result="P1:4"` (vision check: "digital product mockup", rejected), `result="P1:3"` (accepted), then
`image_to_image` Klein 2-input with both anchors. Start to render submit: ~1 min.

Offline regression harness (not in the repo because `utils/` ships with builds): a dotnet console project that
`<Compile Include="...\Web\WebPageReader.cs">` and runs fixtures (unclosed p/li, uppercase unquoted attrs,
srcset/picture/figcaption, nested navbox, main-vs-body fallback, script containing `</div>`, noscript img,
sup.reference, infobox `th: td`, Wikimedia rewrites, `<aitools_action` neutralised, truncation rules, BOM/meta/http
charset precedence, invalid UTF-8) plus `--fetch <url> <file>` / `<file> <url>` to print title, scope, char counts
and the `P1:N` list for a saved page. Register `CodePagesEncodingProvider` in the harness (Unity's Mono has the
code pages built in; .NET 10 needs the provider for windows-1252).

## Web toggle (AI Chat header)

A "Web" checkbox in the AI Chat header (between the GPUs/LLMs pill and Settings; `_webToggle`, PlayerPrefs
`aichat_web_enabled`, default on; `AIChatPanel.GetWebEnabled()` / `SetWebEnabled()`) lets the user turn every
online feature off at a glance. When it is OFF:

- The model is told every turn: `BuildCurrentStateBlock(..., webEnabled)` writes `WEB ACCESS: ON (...)` or
  `WEB ACCESS: OFF - ... web_search, web_image, web_video and web_page are disabled and will fail; do not emit
  them ...` right under the CURRENT STATE header (volatile block, so a flip is seen on the next turn without
  touching the cached prefix).
- The four web skills are left out of the stable SKILLS block (`ChatContextBuilder.Build(keepOldToolCalls,
  hiddenSkillIds)` -> `SkillManager.BuildSkillSummariesBlock(excludeIds)`) and cannot keyword-autoload
  (`GetAutoloadSkillsForMessage(msg, excludeIds)`); `BuiltInSkillIds.WebSkills` is the set,
  `AIChatPanel.HiddenSkillIdsForPrompt()` the switch. This changes the prompt prefix once per flip (one cache
  miss), never per turn. `main_prompt.txt`'s REAL-people / RESEARCH rules say to obey the CURRENT STATE line.
- Every web action fails before any request: `SkillActionExecutor.WebPreflight` checks
  `IChatHost.IsWebAccessEnabled()` FIRST (before the Brave-key branch, so bare `url=` fetches are refused too),
  writes `Not started: Web access is OFF (the "Web" checkbox in the AI Chat header).` into a Web bubble, injects a
  silent note telling the model not to retry until CURRENT STATE says ON, and requests one bounded continue.
- Automation: `POST /chat_web` with `enabled=<true|false>` (omit the body to read) -> `{"ok":true,"webEnabled":...}`;
  it sets the pref AND the checkbox (`AutomationSetWebEnabled`, `SetIsOnWithoutNotify`). Restore the prior value
  after tests, it persists like the checkbox.

## Flow and gating (the non-obvious parts)

- Each action is a DEFERRED executor action (`_lastActionDeferred`), so later actions in
  the same reply wait for the download. That is what makes `web_image anchor="jerry"`
  followed by `image_to_movie chat_image2="jerry"` in one reply work with no extra turn.
- `HasPendingSidecarWork()` includes `_webFetchCount > 0 || _webCaptionInFlight.Count > 0`,
  so Send, `AutomationSendMessage`, `/status idle`, and every auto-resume scheduler wait
  for fetches AND the captions of what they spawned.
- `resume="true"` (default for `web_search`) reuses the inspect auto-resume slot
  (`RegisterInspectAutoResumeRequest`). Because captions count as sidecar work, the
  synthetic `(continue)` turn's CHAT IMAGES block already contains the captions.
- `FinishWebFetch` / web caption completion call `PokeAutoResumeSchedulers()`
  (inspect / skill-load / generic continue). The same poke was added to
  `FinishVideoImport` / `FinishVideoCaption`, which previously never re-poked them.
- `ChatImageRecord.alwaysIncludeCaption`: captions of non-attachment bubbles are only
  described in CHAT IMAGES when "Auto-caption generated images" is on; web images (and
  extracted stills) set this flag so the model always sees what arrived.
- Cancellation: `_webFetchEpoch` is bumped on BOTH Stop and Clear (`CancelAllWebFetches`),
  in-flight `UnityWebRequest`s are aborted, yt-dlp is killed via its `CancelToken`,
  coroutines check the epoch after every yield and exit silently, active trace bubbles
  get a "Cancelled." line. The video-import epoch, by contrast, is only bumped on Clear.
- Dedupe: a URL fetched earlier this session is reused (`_webFetchedUrlToPic`, Pic
  reference because chat numbers shift on trim); the anchor is re-bound.
- Search sessions and the dedupe map are cleared on Clear.
- `SkillActionParser.MakeSentinel` lists the web ids (and `extract_still`) so no
  `[skill: web_image]` marker leaks into the transcript.

## Trace bubble format

```
web_image  query="Jerry Seinfeld portrait photo"  count=1  min_width=256  safesearch=strict  anchor="jerry"
Search: Brave images  GET /images/search?q=Jerry%20Seinfeld%20portrait%20photo&count=10&safesearch=strict&spellcheck=1
  -> HTTP 200 in 0.6s (3.4 KB)
Results (10):
 1. Jerry Seinfeld - Wikipedia | 1200x1600 | en.wikipedia.org | https://upload.wikimedia.org/...jpg
 2. ...
Download 1/10: https://upload.wikimedia.org/...jpg  (claimed 1200x1600)
  -> HTTP 200 image/jpeg 184,233 bytes in 0.4s, JPEG 1200x1600, saved web_3f9a12.jpg
  -> added as #7 (anchor "jerry"), captioning...
Done: 1 of 1 image added (#7) in 1.3s.
#7 caption: "Middle-aged man in a dark blazer smiling..."
```
Failure lines: `-> HTTP 403 Forbidden; retry via Brave thumbnail https://imgs.search.brave.com/...`,
`-> HTTP 200 text/html (a web page, not an image), skipped`, `-> timed out after 30s, skipped`,
`WEBP -> ffmpeg -> PNG 1024x768`.

yt-dlp: the full command line (`"...\utils\yt-dlp\yt-dlp.exe" --no-playlist --newline --restrict-filenames
--no-part --js-runtimes node --match-filters "duration<?1200" --max-filesize 250m
-f "bv*[height<=480][ext=mp4]+ba[ext=m4a]/bv*[height<=480]+ba/b[height<=480]/b" --merge-output-format mp4
--ffmpeg-location "...\utils\ffmpeg\bin" -o "...\tempCache\aichat_web_videos\ytdlp_ab12cd34.%(ext)s"
[--cookies-from-browser firefox] "URL"`), the live `[download] 63.1% ...` line, `exit 0 in 9.4s ->
ytdlp_ab12cd34.mp4 (36 MB)`, the last 8 output lines (15 on failure, verbatim, e.g. "Sign in to confirm you're
not a bot"), then `ffmpeg normalize: 12-17s of source, 832x468, audio -> clip_....mp4 (1.2 MB)` and
`Added as Movie #10 (anchor "clip1"), captioning...`.

Why whole-video: yt-dlp's `--download-sections` hands the stream URL to ffmpeg, and YouTube throttles ffmpeg's
plain HTTP reads to ~5 KiB/s (a 5 s cut of a 10 min video took 5m24s in testing), while yt-dlp's own chunked
downloader with a JS runtime runs at 30-58 MiB/s (the same 10 min video at 480p: 9 s). Without a JS runtime
(deno / node / bun on PATH) YouTube throttles or hides formats; the Settings > Web status line says which
runtime was detected, and the yt-dlp warning is visible in the trace.

## Recap sent to the model

- `[web_search S1 images "..." -> 10 results]` + one line per result + how to use `result="S1:N"`.
- `(web_image "<query>" added #7 (1200x1600, upload.wikimedia.org, anchor "jerry"). The vision check judged it a suitable reference: <reason>.)` followed by a separate `(Full description of #7 (web image) anchor="jerry" - describe it from this, not from outside knowledge: <LONG caption>)` note. That second note comes from `AIChatPanel.ForwardFullDescriptionOnce`, called by `ApplyCaptionResultToPic` for EVERY captioned chat image or movie (generated, extracted, imported/cut clips, web fetches; pasted attachments already carry theirs in the paste header), deduped per Pic + text: the full description is paid for once in cached history, while the per-turn CHAT IMAGES list keeps only the SHORT caption. web_video's transcript is appended to the LONG caption, so it arrives the same way. Both verify prompts forbid naming people from general knowledge (only the query's subject name, only for the person who visibly matches); a session log showed the sidecar calling Kramer "Jerry Seinfeld in a white lab coat" in the LONG caption while its own VERDICT said Kramer.
- `(web_image "<query>": no usable image in N attempts (download failed x3 not an image x2 ...). Try a different query, a lower min_width, or a direct url=.)`
- `(web_video added Movie #10: "<title>" youtube.com 12s for 5s. Reference it via chat_image="10" ...)`
- No key / rejected key / quota: red Error bubble for the user naming Settings > Web (plus the HTTP error) and a note telling the model to relay it and not retry.

## Temp files

`tempCache/aichat_web_images/web_<id>.png|jpg` (plus transient `src_*` inputs for ffmpeg conversion),
`tempCache/aichat_web_videos/ytdlp_<id>.mp4` (whole source at <=480p) / `direct_<id>.<ext>` (deleted after the clip is cut), and the
normalized clip in `tempCache/aichat_video_clips/` (auto-deleted with the Pic). `tempCache` is wiped on quit;
save a bubble to keep it.

## Edge cases

| case | behavior |
|---|---|
| no Brave key | Error bubble naming Settings > Web; `url=` fetches still work |
| Brave 401/403 (bad key) | trace line + an always-visible red Error bubble (`ReportWebSearchFailure`) telling the user to enter a valid key in Settings > Web; the model is told to stop using web skills until the user confirms |
| Brave 429 / quota | same Error bubble shape, pointing at the Brave dashboard / credit; model told not to retry this turn |
| other Brave failures | trace line + Error bubble with the HTTP error; recap says do not retry blindly |
| hotlink 403 / timeout / non-image original | fall back to `thumbnail.src` (500 px Brave proxy), then the next result |
| HTML / JSON body | magic sniff -> "a web page, not an image", skipped |
| over the byte cap | aborted mid-stream, "skipped (over 25 MB)" |
| webp/gif/avif/bmp/tiff | ffmpeg -> PNG (GIF frame 1); animated GIF as a clip only via `web_video url=` |
| decoded width < `min_width` | skipped with the real size shown |
| model-invented / unsafe URL | rejected before any request (must be public http/https) |
| yt-dlp missing | trace + injection naming `utils/yt-dlp/yt-dlp.exe` |
| no JS runtime | yt-dlp warning in the trace; downloads throttled / fewer formats; Settings > Web suggests installing Deno or Node.js |
| source longer than `max_source_minutes` / over 250 MB | yt-dlp skips it (`does not pass filter` / `max-filesize`), reported as such in the trace |
| bot check / sign-in | stderr tail verbatim; suggest Settings > Web "cookies from browser" (Firefox most reliable; Chromium app-bound cookie encryption often breaks Chrome/Brave extraction) |
| Stop / Clear mid-fetch | requests aborted, processes killed, "Cancelled." in the bubble, partial files left in tempCache |

## Testing recipe (automation bridge)

1. No key: blank the key in Settings > Web, `POST /chat` "find a photo of a golden retriever and add it to chat"
   -> red Error bubble, `/chat_images` unchanged, `/status` idle.
2. Happy path with a key: same message -> Web bubble with results + download lines + `#N` + caption line;
   `/chat_images` shows the new still with `captionShort`.
3. `url=` to a `.webp` -> "WEBP -> ffmpeg -> PNG"; `url="http://127.0.0.1:8188/x.png"` -> rejected.
4. yt-dlp: "grab 5 seconds starting at 30s from <YouTube URL> as a clip" -> command line, progress, `Movie #N`.
   Stop mid-download: watch `llm_aichat_log.json` for the `yt-dlp:` note, then `POST /chat_stop` -> "Cancelled." line, process killed, `/status` idle.
5. Resume: "find a photo of the Eiffel Tower and then tell me what is in it" -> `(continue)` arrives after the
   caption line and the answer matches it.
6. Settings round-trip: Apply on the ComfyUI tab, reopen Web tab, the key survived (`BuildModernConfigText` emits it).
7. Full run: "make a Seinfeld episode where they play retro games" -> 4 anchored `web_image` + one `image_to_movie`
   with `Reference To Video (MiniMax H3) 5s.txt` and `<Picture 1>..<Picture 4>`.
8. web_page: "Research the Atari 2600 from its Wikipedia article: read the page, tell me 3 facts, fetch two good
   photos of the console from that page as anchors and render it on a shag carpet" -> Web bubble with
   `GET` / `HTTP 200 text/html 676,548 bytes` / `Title:` / `Extracted ... from <main>` / the `P1:N` list;
   `llm_aichat_log.json` gets a `web_page_text` note and the next `request` carries `[Web page: Atari 2600 -
   Wikipedia (en.wikipedia.org)]` + `[Page images P1 ...]`; then `web_image result="P1:N"` actions and a render
   from the anchors. `query=` mode: "look up the history of the Sega Genesis online" -> `Searched Brave web`,
   `Fetch order by source quality: 1 (+6)...`, Wikipedia read. Non-page URL: force
   `web_page url="<a .png>"` -> `not a readable page; use web_image url=...`.
9. Web toggle: `POST /chat_web` body `enabled=false`, then `/chat` "find a photo of the Eiffel Tower" -> no
   action, no Web bubble, the reply says the Web checkbox is off; the outgoing request has `WEB ACCESS: OFF` and
   no `- web_image:` / `- web_page:` summary lines. `enabled=true` restores both on the next turn.
