# AI Chat: media, routing and movie actions

Second half of the AI Chat deep-dive; core rules (prompt cache, no prompt-quality gates, Stop, anchors, error visibility) are in `aichat.md`. Related: `minimax_h3.md`, `web_media.md`, `audio_generation.md`, `media_import.md`, `workflows.md`.

## Model routing in skills

- `generate_image` defaults to `Prompt To Image (MOSS-Image).txt`; `Prompt To Image (MOSS-Image Turbo).txt` for turbo/fast/draft requests. MOSS is text-to-image only, so `image_to_image` and transparent assets (`Prompt To Transparent Image (Qwen Image 2.1).txt`) stay on Qwen-Image 2.1; Qwen text-to-image runs only when named. MOSS prompt shape (one to three observer paragraphs sized to the brief, exact quoted text, real copy) is in the skills; workflow details in `workflows.md`. `posters` builds new posters/covers as ONE `generate_image` with quoted text; the `add_border` / `draw_text` overlay chain is for existing images, memes, typography-only cards and non-MOSS presets.
- `krea` autoloads only when Krea is explicitly named (`Prompt To Image (Krea 2 Turbo).txt`).
- `scenario_storytelling` autoloads for story/roleplay: anchor-free narration, ~500 words, TWO visuals per turn interleaved with prose. GPU-aware: at most one movie (still -> movie pair) per IDLE GPU in the newest CURRENT STATE, stills only when all are busy. Explicit user counts override; anchors and `image_to_image` are opt-in.
- `comics` prefers one Ideogram 4 render for new whole strips/pages (`[y1,x1,y2,x2]` bboxes, wide speaker-side balloons, explicit tail endpoints); local `new_canvas` / `paste_image` / `draw_text` recipes are for existing-image assembly, exact anchor workflows and repairs.
- Logos/decals: literal stickers/watermarks use local `paste_image` alpha compositing; marks that should look physically integrated use a guide composite plus a 2-input `Image To Image (Qwen Image 2.1)` pass (`<image1>` guide, `<image2>` original mark), then anchor the result.

## Video routing

AI Chat video routing is H3-only; LTX/WAN presets stay usable manually but are deliberately not described in skills/prompts. H3 details (Ref2VA vs FL2VA, slots, durations, dialog verification via STT): `minimax_h3.md`.

- Default image-to-video preset: `Image To Video (MiniMax H3 Turbo Cache) 5s.txt`. `duration="N"` works on every H3 preset (nearest grid step, unclamped). Longer clips only on explicit request.
- A supplied Movie used as a reference for a NEW movie stays the primary `video_to_video` source with `Reference Video To Video (MiniMax H3)`, including its soundtrack; this beats photo references even when an extracted still exists. A 7 s reference is not shortened for a 5 s output. Face stills supplement via `chat_image2+`, never replace it. Single-frame animation is an explicit user choice only.
- `Reference To Video (MiniMax H3) 5s.txt` (Ref2VA) makes the SUBJECT of a source image do something new (prompt calls it `<Picture 1>`, up to 9 photos via `chat_image2..9`). A video starring several existing people routes here directly (each person's photos as references), not through a composite still.
- The executor warns via system injection when an action stages more reference slots than the preset's `@upload` lines consume, blocks H3 Reference actions whose prompt doesn't use every staged `<Picture N>` / `<Video N>` tag (structural gate, correction turn with a slot->tag map), and CHAT IMAGES marks busy Pics "STILL RENDERING".
- Dialog must FILL each clip (10-13 quoted words per 5 s, sequential two-line exchange for two people, one speaker at a time, mouth in frame). The rule lives in the video skills and `main_prompt.txt` (VIDEO DIALOG RULE). Verify dialog changes by transcribing the clip (`minimax_h3.md`), never by reading the prompt.
- Text-only video requests default to a chained `generate_image` (MOSS) -> `image_to_movie` pair with the SAME `width`/`height` on both (864x480 / 480x864 / 640x640). This is prompt-enforced because actions enqueue as they stream (`OnSkillActionParsed` -> `EnqueueAction`), so no look-ahead is possible. Animating an EXISTING image omits width/height and `PicMain.SetWorkflowAspectSource` matches the source aspect at the preset's pixel budget.
- Chained still -> movie reads the source's dimensions from the prior step's QUEUED workflow dims (`PicMain.LastQueuedWorkflowWidth/Height`) before the live texture, which is still `Awake`'s 512x512 placeholder while the still renders. Video sources (`video_to_video`, `rife_video`) take aspect from an ffprobe of the clip (`SkillActionExecutor.TryGetVideoAspectSource`, rotation-aware), never the Movie Pic's texture; `/chat_images` reports probed clip dimensions for the same reason.
- Direct `generate_movie` (text-to-video) is only for explicit "text-to-video" / "no still first" / named `Prompt To Video ...` requests.

## Execution order, deferral and the gpu hint

- The executor serializes actions and defers those needing generated pixels or local clip output (`paste_image source_chat_image`, `inspect_image`'s implicit latest fallback, `clip_video`) until the media is ready, so same-reply sources work and `clip_video` can feed `video_to_video` via `chain="true"`.
- `gpu="N"` is a SOFT hint (`PicMain.m_requestedServerID` with `m_requestedServerIsPreference=true`): a busy GPU falls back to any free one. Adventure leaves the flag false (hard pin). `main_prompt.txt` tells the model to normally omit `gpu` / `llm`.

## Local composition

- `draw_text`, `draw_shape`, `add_border` capture a clean pre-overlay PNG the first time they mutate a Pic; CHAT IMAGES shows `clean_base=available` and later actions pass `clean_base="true"` to rebuild overlays. `paste_image` and `crop_resize` count as base assembly, so comic panels stay in the clean base.
- Same-turn `paste_image` and top-band title `draw_text` rects are recorded; a full-width title overlapping pasted panels injects a layout warning (narrow guard, not a general validator).

## Movie actions: extract_still, rife_video, stitch_video

- `extract_still`: one frame from a Movie bubble as a new still (local FFmpeg): `chat_image="N"` + `time="S"` + optional `anchor`. Mainly for identity refs before H3 same-people regenerations.
- `rife_video`: 2x RIFE interpolation (`Video To Video (RIFE Interpolation).txt`), `chat_image="N"` or `chain="true"`, `fps="N"` only for explicit fixed-FPS output. Excluded from `/applystyle`.
- `stitch_video`: joins 2-60 Movie bubbles with local FFmpeg (`FfmpegTool.StitchClips`: fit+letterbox to the majority size, highest fps, `anullsrc` for silent clips, `concat` or `xfade`/`acrossfade` for `crossfade="S"`). Sources: `chat_images="scene1,4,6-9,all"` or `chat_image` + `chat_image2..10` (a list in `chat_image` is moved to `chat_images`).
  - Intended flow is ONE reply: N clip actions with `anchor="sceneN"` and `duration` on each, then the stitch. Real/named/anchored cast -> each clip is one Reference / Reference Video To Video action; only invented characters use still -> movie pairs. Default per-clip `duration="5"` (Seth: longer only on explicit request), and every clip prompt restates setting/outfits/characters in full (clips share no memory). Rules live in `stitch_video.md` / `image_to_movie.md` / `main_prompt.txt`.
  - Slot gotcha: only `chat_image` (and `chat_image2` on Reference Video To Video) are clip slots; a Movie in another `chat_imageN` is coerced to its display frame by `ResolveExtraInputBytes`, which injects a NOTE.
  - `AIChatPanel.StitchVideoActionCoroutine` WAITS for still-rendering sources (polls `PicMain.IsBusy` + clip file; 2 h cap, 30 s grace) without counting as a video import, so Send stays enabled; only Stop/Clear cancel. Resume is `_turnEpoch`-guarded and uses `AppendVideoClipBubble(updateChainTarget:false)` when a later turn is current.
  - Failure injects a note + `RequestContinueTurn()`, including the ComfyUI execution error the source hit (`PicMain.m_lastRenderError`) and a recovery hint. Success adds a Movie bubble and a recap-eligible "Stitched N clips" Info.
  - On success it DELETES the generated pieces it consumed (`DeleteStitchedSourcePieces` -> `SafelyKillThisPic`); their bubbles and `Movie #N` numbers stay (collapsed via `ChatPicMirror.MarkSourceDeleted`, "not reusable" in CHAT IMAGES) so nothing renumbers, then the canvas is compacted (`ImageGenerator.ReorganizePics(bResetCamera: false)`). Kept: user imports, web downloads, Audio bubbles, locked/busy Pics, and Pics registered under another anchor. `keep_sources="true"` / `delete_sources="false"` opts out.

## inspect_image, captions and /applystyle

- `inspect_image` runs real vision QA on a `chat_image`, attachment, or the latest generated image. `InspectImageSystemPrompt` is describe-first / anti-sycophancy (literal DESCRIPTION before a defect-first PASS/FAIL; checklist items are claims to verify), because verdict-first prompts passed leading questions while describing the opposite. Keep thinking enabled on vision instances used for QA. It waits for pixels, queues behind vision capacity, blocks Send until done, is cancellable with Stop, and `resume="true"` sends one synthetic continue after all inspections finish.
- Alpha PNGs are sent as a checkerboard-composited copy with a transparency note; the original bytes are unchanged.
- Timeouts: `INSPECT_IMAGE_TIMEOUT_SECONDS` and `CAPTION_TIMEOUT_SECONDS` are both 300 s (thinking vision models can spend 90-150 s per caption). All vision sidecars use the instance's "Vision reasoning" level (`LLMProviderSettings.visionReasoningEffort`); No-think is the fast option. A reply after the watchdog is dropped: when captions show `No caption` with no error, check `log.txt` for "caption request didn't return" and `usage.reasoning_tokens` in `llm_last_response_sent_small.json`.
- `/applystyle`: provenance and tool-call logging report only the original prompt; the restyled prompt stays local and never reaches later main turns.

## Web media and audio (index; deep-dives are `web_media.md` and `audio_generation.md`)

- Six web skills: `web_search`, `web_image`, `web_video`, `web_audio`, `web_page`, `web_transcript` (`web_fetch` aliases to `web_page`). The header "Web" checkbox (`aichat_web_enabled`, default on) gates all of them: OFF writes `WEB ACCESS: OFF` to CURRENT STATE, hides the summaries/autoload (one cache miss per flip), and `SkillActionExecutor.WebPreflight` refuses every web action. `POST /chat_web enabled=<bool>` flips it for tests.
- Model `url=` values must pass `WebMediaDownloader.IsAllowedPublicHttpUrl` (public http/https only; redirects followed by hand and re-gated; no raw quote/backslash/whitespace since the URL reaches yt-dlp after a `--` terminator). The Brave key and yt-dlp cookie settings live in private `config.txt` and MUST stay emitted by `Config.BuildModernConfigText`.
- Web fetches and the captions they spawn count as `HasPendingSidecarWork()`, so `resume="true"` continues only once captions are in CHAT IMAGES. `ChatImageRecord.alwaysIncludeCaption` keeps web/extracted-still captions visible even with auto-captioning off. Fetches have their own epoch, bumped on Stop and Clear; vision verify jobs (`_webVerifyJobs`) are cancelled on Stop.
- Real people and named characters go through `web_image` + anchors, never rendered from memory. With Web on, a named show/film cast is fetched without asking (Seth's default); Web off -> ask for references, never a lookalike.
- Seth's rule (2026-08-30): a voice sample in chat is used via the H3 `audio=` reference, ALWAYS. Never offer `generate_speech ref_voice` cloning as an alternative.
- Audio: an Audio bubble IS a Movie bubble (waveform MP4 carrying AAC); `IsChatImageMovie` is true for it, so code that must tell video from sound checks `IChatHost.IsChatImageAudio`. The record's `audioPath` is the original lossless file. The three `generate_*` skills are hidden until a gateway URL is configured. Generation runs under the video-import gate and defers the pump so same-reply `set_video_audio audio="anchor"` works. Movie entries in CHAT IMAGES carry clip length. Saving an Audio bubble copies the original file beside the preview movie (`PicMovie.SetCompanionAudioFile`).

