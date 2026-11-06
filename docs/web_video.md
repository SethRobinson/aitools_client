# AI Chat web video: yt-dlp clips, transcripts, YouTube sign-in

Part of the web media docs (index: `docs/web_media.md`). Covers `web_video`, `web_transcript`,
the speech check, cookie settings and the dedicated YouTube Firefox login.

## yt-dlp wrapper (`Web/YtDlpTool.cs`)

- Resolves `utils/yt-dlp/yt-dlp.exe` (then PATH). Bundled under the Unlicense; `utils/yt-dlp/README.txt`
  has the version. Release staging copies `utils`, so builds ship it.
- Auto-detects a JS runtime (deno / node / bun on PATH, passed as `--js-runtimes`). Without one yt-dlp
  warns, downloads may be throttled or have fewer formats; Settings > Web shows detection and suggests
  Deno or Node.js.
- Builds the exact command line: URL LAST, after a `--` end-of-options marker, quoted per Windows CRT
  rules by `FfmpegTool.QuoteArg`; runs via `FfmpegTool.RunProcessCancellable`, progress drained on the
  main thread. `--ignore-config` keeps machine-level yt-dlp options out. The model can supply URLs,
  language and clip parameters only, never cookie paths or downloader arguments.
- The URL must first pass `WebMediaDownloader.IsAllowedPublicHttpUrl` (see `docs/web_media.md`), which
  also rejects raw quote / backslash / whitespace / control characters because the URL reaches this
  command line.

## Range downloads

- `web_video` downloads only `start`..`start+duration` with `--download-sections` and
  `--force-keyframes-at-cuts`, capped at 480p. Segmented HLS (preferably H.264) is selected first to avoid
  direct DASH seek stalls; direct formats are a range-only fallback. There is NO whole-video fallback.
- FFmpeg network reads time out after 20 s; downloads after ten minutes. Stop/timeout also kills yt-dlp's
  FFmpeg child on Windows.
- Page URLs and direct video URLs share this path. `FfmpegTool.CreateClip` then normalizes the downloaded
  section from local time zero (soundtrack loudness-normalized to -16 LUFS when "Normalize imported clip
  audio" is on, the default; the trace shows the `AudioNote`). Caption and provenance keep the ORIGINAL
  source timestamps.
- The 250 MB cap applies to bytes written for the clip, not the source's metadata size.
- `max_source_minutes` defaults to 0 (no filter); a positive value filters source length.
- A `web_video url=` that ends in an audio extension is rerouted to `web_audio` with a silent note.
- Each failed source/cut contributes its ACTUAL error to both the compact Web bubble and the model recap.
  Source-duration rejection, missing captions and authentication failures are distinct messages (a
  generic recap made models invent bot-check explanations).
- Trace shows the range command (`--download-sections "*12-17"`, format selection, FFmpeg path),
  progress, exit status and failure; cookie-file paths and invalid cookie records are redacted.

## Ranking and vision verification

- `RankWebVideoCandidates`: interview / podcast / reaction / explained / news titles rank down;
  scene / clip / best-of / compilation titles and query-word matches rank up.
- `VerifyWebVideoClipCoroutine`: every cut is contact-sheeted and judged with `BuildWebVideoVerifyPrompt`
  ("people talking ABOUT the subject, title cards, intros, wrong subject -> UNSUITABLE"). The SUITABLE
  call's caption is applied directly (`AppendVideoClipBubble(..., autoCaption:false)` +
  `ApplyCaptionResultToPic`).
- `verify` defaults: search/result picks or explicit `criteria` verify; a plain `url=` does not.
- An UNSUITABLE cut from a searched source retries at +30 s and +90 s (`WebRequestLimits.VideoRetryOffsets`,
  separate range downloads), then the next ranked result (4 sources max).
- `web_video` auto-continues by default (`resume="false"` opts out); models otherwise ended their turn
  after "I'll find a clip first".

## Speech check (`Web/SpeechCheck.cs`)

Vision only sees frames, so a music-only clip would pass and H3 Ref2VA (which clones the reference
clip's audio) would produce a garbled voice. With `speech="true"` (also implied by criteria words like
talk / speak / dialog / voice) each accepted cut is audio-checked before it enters chat:

1. `FfmpegTool.ExtractAudioWav` (16 kHz mono), `FfmpegTool.MeasureMeanVolume` (below -50 dB = silent =
   definite no-speech).
2. Whisper-style transcription (`response_format=verbose_json`). Speech is present when >= 5 real words
   came back (clips under 4 s: >= 2 words; callers pass the clip duration to `SpeechCheck.Run`), the
   average segment `no_speech_prob` is <= 0.6, and the text is not a `[Music]` / `♪` marker.
3. No-speech cuts retry +30/+90 s then the next source. An accepted clip's LONG caption gets
   `Audio transcript: "..."` and the recap says it is usable as a voice reference.

Endpoint: Settings > Web "Speech-to-text", any OpenAI-compatible `/v1/audio/transcriptions` URL
(`set_stt_endpoint`, optional `set_stt_api_key`, `set_stt_model`, e.g. local faster-whisper / Speaches /
LocalAI / vLLM), falling back to api.openai.com `whisper-1` with the LLM Settings OpenAI key. With neither,
only silent cuts can be rejected and the trace + recap say the check was unavailable. Signal-only
heuristics were tried and rejected (a bass theme scored like speech). Known wrinkle: Whisper can
hallucinate subtitle-credit text on music, which may pass the heuristic.

`web_video speech="true"` transcribes only the fetched short clip; whole-video captions are `web_transcript`.

## web_transcript

- `web_transcript url="..." language="en"` uses manual subtitles when available, else automatic captions,
  with `--skip-download`. Needs neither a Brave key nor an STT service.
- VTT timestamps/markup and rolling-line duplicates are removed; decoded action tags are neutralized and
  the text is delivered as external source text in the info recap.
- `max_chars` default 20000 (1..100000); the response reports the next `offset` for longer tracks.
  Nonzero offsets reuse session-cached text; offset 0 refreshes; Clear drops the cache.
- A missing language/track is an explicit error, never a fabricated transcript. Two-minute timeout,
  10 MB subtitle file limit.

## Cookies and YouTube sign-in

Precedence for YouTube URLs: dedicated Firefox login (when enabled) > cookie file > cookie browser.

- **Cookie browser**: `set_ytdlp_cookies_browser` in private `config.txt`.
- **Cookie file**: Settings > Web > Cookie file persists `set_ytdlp_cookies_file` (alias
  `set_yt_dlp_cookies_file`): full path to a Netscape-format cookie export (e.g. from Brave browser).
  Applies to clips AND transcripts; clear it to return to browser extraction. Validated without echoing
  records; path redacted in traces; yt-dlp may update the file; expired logins need a fresh export.
  Brave Search API access is unrelated to the Brave browser session.
- All of these keys MUST stay emitted by `Config.BuildModernConfigText` (Settings Apply regenerates
  `config.txt`).

Dedicated login (`Web/YouTubeLogin.cs`, `Web/YouTubeLoginDialog.cs`):

- Only a real sign-in / age-verification / bot-verification / expired-session error pauses the action and
  offers **Sign in with Firefox** / **Cancel**. HTTP 403/429, missing captions, filters, missing formats and
  network failures alone do not imply authentication.
- Firefox launches only after the click, with `-no-remote -profile %LOCALAPPDATA%\SethsAITools\YouTubeFirefox`
  (outside the checkout, release package and tempCache; separate from everyday browser profiles). The
  destination is fixed to `https://www.youtube.com/`, never an error-message or model-supplied URL. Firefox
  must be installed in a standard location or on PATH; missing Firefox is reported, never auto-installed.
- After signing in the user closes Firefox and clicks **Done, retry**: the dialog checks the profile lock and
  cookie database, persists `set_youtube_firefox_login|true|`, and retries the EXACT clip URL/range or
  transcript URL/language ONCE. A second failure goes to the model with instructions not to retry.
  Declining stops further login prompts within that action. Stop/Clear cancels the dialog and late retries.
- Enabled sessions use `--cookies-from-browser firefox:<dedicated-profile>` for YouTube only; later launches
  reuse the profile; a missing profile offers sign-in again. No cookie values enter chat, no all-browser
  export is produced. The model has no action that opens Firefox or accepts a sign-in.
- Settings > Web: **Sign in / change account**, **Use remembered YouTube login** (disabling does not delete
  the profile; sign out inside its Firefox window), and **Test**: probes the private Liked videos playlist
  with only the dedicated profile, one metadata entry, no media download or cookie export, 45 s timeout,
  cancelled when Settings closes or changes tab. Success needs the playlist ID and a clean exit;
  expired-cookie and inconclusive network/helper failures have separate messages; raw account output is
  never shown or logged. Reuse need not be enabled to test.

## Checks

- `pwsh -File scripts/VerifyWebMedia.ps1` against an active compiled Play session (bridge): production
  command builders, VTT parsing, cookie validation/redaction, dedicated-profile routing, login retry and
  cancellation, an inactive login-dialog fixture, config serialization, and Web settings layout (Test button
  bounds, status fit, cancellation) under a disabled canvas. No network, browser launch, config writes or
  desktop input. Fixtures stay under ignored `build/web-media-validation-*`.
- `POST /chat_web_action` runs one parsed `web_video` or `web_transcript` action through the real executor
  with main-turn auto-resume disabled, for live checks.
