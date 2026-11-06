# AI Chat web media: search, images, video, pages, audio

AI Chat can search the web and pull images, short clips and sound files into chat as ordinary `#N` /
`Movie #N` / `Audio #N` bubbles, so real references (people, characters, places, products) can feed H3
reference presets or Qwen Image 2.1 edits, and reads pages and transcripts as text for research.

Docs:
- this file: pieces, actions, rules, image fetching, flow/cancellation, trace bubble, recap, edge cases, tests
- `docs/web_video.md`: `web_video` range downloads, yt-dlp, speech check, `web_transcript`, cookies and the
  YouTube Firefox sign-in
- `docs/web_page.md`: `web_page` reader (`WebPageReader`) and `web_audio`

## Pieces

| piece | where | role |
|---|---|---|
| Brave client | `Assets/_Script/LLM/AIChat/Web/BraveSearchClient.cs` | `GET https://api.search.brave.com/res/v1/{images,videos,web}/search`, header `X-Subscription-Token`, SimpleJSON (every field optional), ~0.6 s spacing between calls |
| Downloader | `Web/WebMediaDownloader.cs` | `UnityWebRequest` to memory (images, 25 MB) or file (videos, 250 MB), browser UA, 30 s timeout, `Abort()` on cap/cancel, magic-byte sniffing, the URL gate below, hand-followed redirects |
| Image normalizer | `WebImageConverter` (same file) | PNG/JPEG decode-check via throwaway `Texture2D.LoadImage`; webp/gif/avif/bmp/tiff or >2048 px go through `FfmpegTool.ConvertImageToPng` |
| yt-dlp wrapper | `Web/YtDlpTool.cs` | see `docs/web_video.md` |
| YouTube sign-in | `Web/YouTubeLogin.cs`, `Web/YouTubeLoginDialog.cs` | see `docs/web_video.md` |
| Speech check | `Web/SpeechCheck.cs` | see `docs/web_video.md` |
| Page reader | `Web/WebPageReader.cs` | see `docs/web_page.md` |
| Trace bubble | `Web/WebTraceBubble.cs` + `AIChatPanel.BeginWebTrace` | the always-visible compact "Web" bubble (below) |
| Thumbnail strip | `Web/WebThumbStrip.cs` | 72 px thumbnails of every image / contact sheet the vision check saw |
| Host | `AIChatPanel.cs` "Web media fetch" region | busy gate (`_webFetchCount`, `_webCaptionInFlight`), epoch cancellation, the coroutines, search sessions `S1..`, page sessions `P1..` |
| Executor | `SkillActionExecutor.cs` `ExecuteWebSearch/Image/Video/Page/...` | argument parsing + aliases, `WebPreflight` (toggle / key / URL), deferred like `extract_still` |
| Settings | `AppSettingsPanel` Web tab, `Config.cs` | `set_brave_search_api_key`, `set_web_search_safesearch` (off by default, strict optional), `set_ytdlp_cookies_browser`, `set_ytdlp_cookies_file`, STT keys, in private `config.txt` |
| Prompts | `aichat/skills/web_image.md`, `web_video.md`, `web_search.md`, `web_page.md`, `web_transcript.md`, `web_audio.md`, `main_prompt.txt` | routing, cast recipe, research recipe |

## Hard rules

- **URL gate**: every model `url=` (and every redirect hop) must pass
  `WebMediaDownloader.IsAllowedPublicHttpUrl`: http/https only, no userinfo, no loopback / RFC1918 /
  link-local / `localhost` (trailing dots normalized), no single-label LAN names (hostnames are not
  DNS-resolved), no raw quote / backslash / whitespace / control characters (the URL reaches yt-dlp's
  command line, where it follows a `--` terminator). Redirects are followed by hand (`redirectLimit = 0`,
  max 5 hops, each `Location` re-gated), because UnityWebRequest would follow a public 302 into loopback.
- **Settings persistence**: the Brave key, SafeSearch, yt-dlp browser/cookie-file, YouTube-login and STT keys
  live in private `config.txt` and MUST stay emitted by `Config.BuildModernConfigText` (Settings Apply
  regenerates the whole file).
- **Web toggle**: the AI Chat header "Web" checkbox gates all six web skills (below).
- **Real people and named characters** are fetched with `web_image` (+ `web_video` for speakers) and used as
  anchors, never rendered from memory or as a text-described lookalike. A voice sample in chat is used via
  the H3 `audio=` reference; `ref_voice` cloning is never offered as an alternative.

## Model-facing actions

| skill | attributes | result |
|---|---|---|
| `web_search` | `query`, `kind=images\|videos\|web` (default images), `count` (max 20), `safesearch`, `resume` (default TRUE) | list only; stored as `S1`, `S2`... for `result="S1:3"` |
| `web_image` | one of `query` / `url` / `result`; `count` (max 4), `anchor` (count>1 -> `name`, `name_2`...), `min_width` (256), `criteria`, `verify` (default true), `safesearch`, `resume` (default false) | still bubble(s) `#N`, provenance `web image: "query" -> host/path`, vision-verified and captioned |
| `web_video` | one of `query` / `url` / `result`; `start`, `duration` (0.5..15, default 5), `max_source_minutes` (0 = no filter), `criteria`, `verify`, `speech`, `audio`, `anchor`, `resume` (default TRUE) | `Movie #N` from a <=480p range download (`docs/web_video.md`) |
| `web_page` | one of `url` / `result` (a `kind="web"` hit) / `query`; `max_chars` (500..20000, default 6000), `images`, `max_images` (1..40, default 12), `safesearch`, `resume` (default TRUE) | text in the info recap, images as `P1:N`, audio links as `P1:aN` (`docs/web_page.md`) |
| `web_transcript` | `url`, `language` (en), `offset` (0), `max_chars` (20000, up to 100000), `resume` (true) | caption text in the info recap, no download (`docs/web_video.md`) |
| `web_audio` | one of `url` / `result` (`P1:a2` or an S hit that is a sound file); `start`, `duration` (0 = whole, cap 300 s), `speech`, `anchor`, `resume` (default TRUE) | `Audio #N` bubble; no `query=` mode (`docs/web_page.md`) |

Aliases (`NormalizeSkillId`): `search_web`, `image_search`, `brave_search`... -> `web_search`;
`find_image`, `fetch_image`, `download_image`... -> `web_image`; `download_video`, `fetch_video`, `youtube`,
`web_clip`... -> `web_video`; `read_page`, `fetch_page`, `open_url`, `read_url`, `browse`, `visit`,
`web_fetch`... -> `web_page`; `download_audio`, `fetch_audio`, `find_sound`, `download_wav`, `web_sound`...
-> `web_audio`.

**Default cast recipe (Seth, 2026-08-31, prompt text only).** For a new video of a named show/film cast or any
real person with Web ON, reference fetching is the DEFAULT (the user never has to ask): per person ONE
`web_image count="2"` (anchors `name` + `name_2`, one `<Subject N>`), query `"<show> <character> scene
still"`, criteria rejecting interviews / talk shows / premieres / red carpet / photoshoots / headshots
(press photos give the wrong look and wardrobe); per SPEAKING character ONE
`web_video query="<show> <character> talking scene" speech="true"`; then a `Reference Video To Video` render
on the auto-continue turn (first two speakers' clips in clip slots, further speakers via `audio="name_clip"`,
stills after the clips), voices bound "styled like <Audio N>". Web OFF: use chat references or ask. Lives in
`main_prompt.txt`, `web_image.md`, `web_video.md`, `stitch_video.md`, `generate_movie.md`.

## Web toggle (AI Chat header)

`_webToggle`, PlayerPrefs `aichat_web_enabled` (default on), `AIChatPanel.GetWebEnabled()` / `SetWebEnabled()`.
When OFF:

- `BuildCurrentStateBlock(..., webEnabled)` writes `WEB ACCESS: OFF - ... disabled ...` (or `ON`) under the
  CURRENT STATE header (volatile tail, no prefix change per turn).
- The six skills (`BuiltInSkillIds.WebSkills`) are left out of the stable SKILLS block and keyword autoload
  (`AIChatPanel.HiddenSkillIdsForPrompt()` -> `ChatContextBuilder.Build(..., hiddenSkillIds)` /
  `GetAutoloadSkillsForMessage(msg, excludeIds)`): one cache miss per flip, never per turn.
- `SkillActionExecutor.WebPreflight` checks `IChatHost.IsWebAccessEnabled()` FIRST (so bare `url=` fetches are
  refused too), writes `Not started: Web access is OFF ...` into a Web bubble, injects a silent "do not retry
  until CURRENT STATE says ON" note and requests one bounded continue.
- Bridge: `POST /chat_web` `enabled=<true|false>` (omit to read) sets the pref AND checkbox
  (`AutomationSetWebEnabled`). It persists: restore the prior value after tests.

## web_image: ranking and vision verification

1. **Metadata ranking** (`RankWebImageCandidates`, before any download): Wikimedia/Wikipedia +5,
   .gov/.edu/Britannica +3; AI-art, clipart, cutout, wallpaper, stock, merch, Pinterest, meme hosts -4
   (`WebImageJunkHosts`); title words (clipart, caricature, painting, wallpaper, poster, funko...) -3;
   "official portrait" / "headshot" / "press photo" +1; >=600 px +1, <300 px -1, banner aspect -2. Stable sort.
   Claimed width under `min_width` is skipped without downloading.
2. **Vision verification** (`VerifyWebImageCoroutine`, one call per download, `BuildWebImageVerifyPrompt` with
   query + `criteria`): answers `VERDICT: SUITABLE|UNSUITABLE`, `REASON:`, then `SHORT:` / `LONG:` caption
   lines (`ParseCaptionResponse` skips the verdict lines). UNSUITABLE -> next candidate (up to
   `MaxImageCandidates` = 12 downloads). SUITABLE -> bubble added and the same call's caption applied
   (`ApplyCaptionResultToPic`), no second caption sidecar. No verdict -> accepted unverified with the normal
   caption path. Skipped (and traced) when no active LLM accepts vision or `verify="false"`. The prompt allows
   scene stills where the subject is clearly the main subject; rejects reproductions (screens, framed
   pictures), art, wrong subject, crowds. Both image and video verify prompts forbid naming people from
   general knowledge (only the query's subject, only for the person who visibly matches).
3. **Hotlink fallback**: 403 / timeout / non-image original -> retry via the Brave `thumbnail.src` (500 px proxy),
   then the next result.
4. **Dedupe**: `_webFetchedUrlToPic` reuses a URL fetched earlier in the session (Pic reference, since chat
   numbers shift) and re-binds the anchor. `_webFetchedUrlToQuery` records which query accepted each URL: a
   query-driven fetch skips a candidate accepted for a DIFFERENT query (trace `Skip <url>: already in chat as
   #N from a different search`, no attempt consumed), so one castmate's photo is never handed to another.
   Explicit `url=` / `result=` always reuse. Video/audio dedupe is URL-only.

### Thumbnails of everything the vision check saw

- `WebTraceBubble.AddThumb(pngBytes, filePath, label, title)` lazily creates a `WebThumbStrip` (wrapping
  `GridLayoutGroup` of 72 px cells + a hint line) for every decoded `web_image` download and every `web_video`
  contact sheet. List-only bubbles (`web_search`, `web_page`) have none. Thumbnails are downscaled copies
  (max 128 px) owned by the strip and destroyed with the bubble.
- Badges (`SetThumbVerdict`): green check = SUITABLE, red X = rejected (vision, `min_width`, audio check, load
  failure), amber ? = accepted unverified, grey - = checking. Hover writes the verdict, reason, size and URL
  into the hint line. Labels: download ordinal (images) or cut range (`5-10s`, video).
- Click (`AIChatPanel.OnWebThumbClicked`): accepted media pans to its Pic (`ChatPicMirror.FocusCameraOnPic`);
  a rejected candidate or contact sheet is loaded into a NEW plain canvas `PicMain`
  (`ImageGenerator.AddImageByFileName`, stored as `SpawnedPic` so later clicks focus it; `chat_image`
  numbering unaffected).
- So rejected image files stay in `tempCache/aichat_web_images` and contact sheets in
  `tempCache/aichat_video_captions` for the session; rejected clip files are deleted.

## Flow and gating

- Every web action is DEFERRED (`_lastActionDeferred`): later actions in the same reply wait for the download,
  so `web_image anchor="jerry"` followed by `image_to_movie chat_image2="jerry"` works in one reply.
- `HasPendingSidecarWork()` includes `_webFetchCount > 0 || _webCaptionInFlight.Count > 0`, so Send,
  `AutomationSendMessage`, `/status idle` and every auto-resume scheduler wait for fetches AND their captions.
- `resume="true"` reuses the inspect auto-resume slot (`RegisterInspectAutoResumeRequest`); since captions
  are sidecar work, the `(continue)` turn's CHAT IMAGES already contains them. `FinishWebFetch` and web
  caption completion call `PokeAutoResumeSchedulers()`.
- `ChatImageRecord.alwaysIncludeCaption` is set on web images (and extracted stills) so CHAT IMAGES always
  describes them even when "Auto-caption generated images" is off.
- Unfinished-plan safety net (`SkillActionExecutor.TurnHadOnlyPreparatoryActions`, checked in
  `FinalizeAssistantTurn`): actions are tallied at enqueue as preparatory (`web_image`, `web_video`,
  `web_search`, `web_page`, `extract_still`, `clip_video`) or other. A reply with only preparatory actions and
  no resume of its own gets one `RegisterGenericContinueRequest` plus a silent "emit the render NOW or say it
  is complete" note. Bounded by the consecutive-continue cap.
- `SkillActionParser.MakeSentinel` lists the web ids (and `extract_still`) so no `[skill: web_image]` marker
  leaks into the transcript.
- Search sessions, page sessions and the dedupe maps are cleared on Clear.

### Cancellation

- `_webFetchEpoch` is bumped on BOTH Stop and Clear (`CancelAllWebFetches`): in-flight requests aborted,
  yt-dlp killed via its `CancelToken`, coroutines check the epoch after every yield and exit silently, active
  trace bubbles get "Cancelled.". (The video-import epoch is only bumped on Clear.)
- Stop stays enabled for the whole fetch through the single rule `ShouldStopBeInteractable()`; `SetBusyUI`
  must never compute its own. A Stop after the stream ended also calls `ResetPerTurnExecutionState()` before
  `SetBusyUI`, otherwise `ResumePumpAfterDeferredComplete` would run the rest of the reply's queued actions
  when the cancelled coroutine reports `onDone(false)`.
- The vision checks take the caller's epoch, end their waits on a bump, and register their `CaptionJob` in
  `_webVerifyJobs` so `CancelAllWebFetches` can `CancelCaptionJob` it (LLM slot freed at once; the late result
  is dropped). `CaptionWebStillBubble` skips trace lines after a bump.

## Trace bubble format

The Web bubble is COMPACT (Seth, 2026-09-13: "just show the images it looks at"). It renders only:

- a one-line title per skill (`web_image "<query>"  x2  anchor "kramer"`, `web_video "..."  5s`,
  `web_page en.wikipedia.org/wiki/...`, `web_search images "..."`, `web_audio host/path`; `WebCompactSource`
  = host/path of a `url=`, `result S1:3`, or the quoted query) with a clickable `[details]` marker (TMP link
  `webtrace`) once the full trace holds a hidden line;
- OUTCOME lines written with `WebTraceBubble.AppendSummaryLine` (`Done: 2 of 2 images added (#3, #4) in 12.3s.`,
  `No usable image in N attempts (...)`, `Added as Movie #10`, `Added as Audio #N`, web_page `Title:` /
  `Extracted` / `Images:` counts, the hit-count line for list-only `web_search`, `Result lookup failed`,
  `No vision-capable LLM is active`, the speech-check-unavailable warning, Brave failures, `yt-dlp: <error>`,
  `Cancelled.`);
- the throttled status line (`downloading 63%`, `checking suitability with the vision LLM...`) and the
  thumbnail strip.

Everything else (`AppendLine`: the raw action line, Brave result/ranking lines, `Skip` / `Download N/M` /
HTTP lines, verdicts, captions, yt-dlp command + output, page image/audio lists) is in the FULL trace only.
`[details]` opens it in the floating `ChatThinkingWindow` (kind `WebTrace`, live via
`AIChatPanel.RefreshOpenWebTraceWindow` until `WebTraceBubble.Finish()`), found through the `WebTraceHolder`
component on the bubble root. Plain text (only TMP angle brackets escaped, no markdown pass).

Rule: `AppendSummaryLine` is the ONLY way to put text in the bubble; when adding a trace line, decide whether
it is an outcome the user must see without clicking. New lines default to details-only. One-line notices
(`AddWebTraceNotice`, e.g. Web OFF) have no marker. Every line is mirrored to `llm_aichat_log.json` as
`note/web`; full Brave lists and ranking go there as `web_results` / `web_ranking`.

Bridge: `POST /chat_web_trace action=open|close|toggle|status` (latest Web bubble; reports `compact`, `lines`,
`summaryLines`, `thumbs`, `finished`).

Typical full-trace failure lines: `-> HTTP 403 Forbidden; retry via Brave thumbnail ...`,
`-> HTTP 200 text/html (a web page, not an image), skipped`, `-> timed out after 30s, skipped`,
`WEBP -> ffmpeg -> PNG 1024x768`.

## Recap sent to the model

- `[web_search S1 images "..." -> 10 results]` + one line per result + how to use `result="S1:N"`.
- `(web_image "<query>" added #7 (1200x1600, host, anchor "jerry"). The vision check judged it suitable: <reason>.)`
  then a separate `(Full description of #7 ... : <LONG caption>)` note from `AIChatPanel.ForwardFullDescriptionOnce`.
  That helper runs for EVERY captioned chat image or movie via `ApplyCaptionResultToPic`, deduped per Pic +
  text: the full description is paid once in cached history while per-turn CHAT IMAGES keeps only the SHORT
  caption. web_video transcripts ride the LONG caption.
- `(web_image "<query>": no usable image in N attempts (...). Try a different query, a lower min_width, or a direct url=.)`
- When chat holds any Audio bubble, the web_image recap also says to stage the speaking character's voice
  sample on the SAME reference action (`audio="K"`): the freshest concrete recipe wins, so the pairing reminder
  must ride it.
- `(web_video added Movie #10: "<title>" youtube.com 12s for 5s. Reference it via chat_image="10" ...)`
- No key / rejected key / quota: red Error bubble naming Settings > Web plus a note telling the model to relay
  it and not retry.

## Temp files

- `tempCache/aichat_web_images/web_<id>.png|jpg` (plus transient `src_*` ffmpeg inputs).
- `tempCache/aichat_web_videos/ytdlp_<id>.*` / `direct_<id>.<ext>` (downloaded section, deleted after the cut);
  the normalized clip in `tempCache/aichat_video_clips/` (deleted with the Pic).
- web_audio: `tempCache/aichat_audio/web_<id>.<ext>` / `web_cut_<id>.wav`, preview in
  `tempCache/aichat_audio_previews/`.
- `tempCache` is wiped on quit; a bubble's S saves a persistent copy (`docs/media_import.md` "Saving movies").
- Bulk cleanup uses `RTSafeFileSystem`: the fixed `tempCache` child resolved from `Application.dataPath` (never
  the working directory), skipped with a warning on any symlink/junction; yt-dlp failure cleanup only accepts
  `ytdlp_<8 lowercase hex digits>.*` in `tempCache/aichat_web_videos`. Fixtures: `scripts/VerifyDeleteSafety.ps1`.

## Edge cases

| case | behavior |
|---|---|
| no Brave key | Error bubble naming Settings > Web; `url=` fetches still work |
| Brave 401/403 | red Error bubble (`ReportWebSearchFailure`): enter a valid key; model told to stop using web skills until the user confirms |
| Brave 429 / quota | same shape, pointing at the Brave dashboard; no retry this turn |
| other Brave failures | Error bubble with the HTTP error; recap says do not retry blindly |
| HTML / JSON body for an image | sniffed as "a web page, not an image", skipped |
| over the byte cap | aborted mid-stream, "skipped (over 25 MB)" |
| webp/gif/avif/bmp/tiff | ffmpeg -> PNG (GIF frame 1); animated GIF as a clip only via `web_video url=` |
| decoded width < `min_width` | skipped with the real size shown |
| unsafe / invented URL | rejected before any request |
| yt-dlp missing | trace + injection naming `utils/yt-dlp/yt-dlp.exe` |
| source-duration filter / clip over 250 MB | specific failure in bubble and recap |
| YouTube bot check / sign-in | Firefox sign-in offer, one retry (`docs/web_video.md`) |
| Stop / Clear mid-fetch | requests aborted, processes killed, "Cancelled.", partial files left in tempCache |

web_audio-specific cases are in `docs/web_page.md`.

## Testing recipe (automation bridge)

1. No key: blank the key in Settings > Web, `POST /chat` "find a photo of a golden retriever and add it to chat"
   -> red Error bubble, `/chat_images` unchanged, `/status` idle.
2. With a key: same message -> Web bubble with outcome + thumbnails, `#N` with `captionShort` in `/chat_images`.
3. `url=` to a `.webp` -> "WEBP -> ffmpeg -> PNG"; `url="http://127.0.0.1:8188/x.png"` -> rejected.
4. yt-dlp: "grab 5 seconds starting at 30s from <YouTube URL> as a clip" -> `Movie #N`. Stop mid-download
   (`POST /chat_stop`) -> "Cancelled.", process gone, `/status` idle, no follow-up render.
5. Resume: "find a photo of the Eiffel Tower and then tell me what is in it" -> `(continue)` after the caption.
6. Settings round-trip: Apply on the ComfyUI tab, reopen Web tab, the key survived.
7. Full run: "make a Seinfeld episode where they play retro games" -> anchored `web_image`s + one H3 reference
   render.
8. web_page: "Research the Atari 2600 from its Wikipedia article ... fetch two photos from that page as anchors
   and render it" -> `web_page_text` log note, `[Web page: ...]` + `[Page images P1 ...]` in the next request,
   `web_image result="P1:N"`, render. Non-page URL -> `not a readable page; use web_image url=...`.
9. Toggle: `POST /chat_web enabled=false`, then "find a photo of the Eiffel Tower" -> no action, request has
   `WEB ACCESS: OFF` and no web skill summaries. Restore with `enabled=true`.
10. Offline: `pwsh -File scripts/VerifyWebMedia.ps1` (`docs/web_video.md` "Checks").
