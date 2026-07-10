# AGENTS.md

Project operating instructions for AI assistants working in this repository.

## Shared Project Memory

- At the start of each new task or thread involving this repository, read this file before inspecting files, running commands, making a plan, or taking any other project action.
- Treat follow-up replies in the same continuous task as part of that task. Do not reread this file unless the repository or working directory changes, this file is modified, or its instructions are no longer available in context.
- Treat this file as the shared project memory for AI assistants.
- Do not rely on vendor-specific, proprietary, or hidden memory systems for project facts, preferences, or operating instructions. (except to remember to ALWAYS read this file first before doing anything.  Remember that.)
- Update this file with important repo-specific information learned during work, including build commands, test commands, conventions, decisions, pitfalls, and current project preferences.
- Keep this file accurate and current. Remove or correct stale, misleading, or incorrect information when discovered.
- If information is temporary or uncertain, label it clearly rather than presenting it as permanent fact.

Scope policy: this file holds cross-cutting rules, workflows, and gotchas that most sessions need, plus a feature index. Keep it around 30 KB. Feature deep-dives live in `docs/<topic>.md`: before working on a feature listed in the index, read its doc; when finishing feature work, update that doc and keep the index entry here to one or two lines (where it lives + the non-obvious constraint). Cross-cutting rules and new gotchas still land here directly. When a change makes anything stale, here or in a linked doc, update it in the same change.

## Testing

- When possible, design automated tests for new features and bug fixes.
- Run relevant automated tests after finishing changes to guard against regressions.
- If tests cannot be run or do not exist, state that clearly in the handoff and describe any manual verification performed.


## Security

- Never commit sensitive data, including credentials, tokens, passwords, private keys, cookies, customer data, personal data, or machine-specific authentication material.
- If an AI assistant needs authentication data or other secrets for local work, use `agents_secret.md` for those notes.
- `agents_secret.md` must stay ignored by git and must not be committed.
- Do not put secrets in commit messages, logs, issue text, pull request descriptions, generated docs, or other tracked files.
- Before committing, review staged changes for accidental secrets.

## Git

- Never add OpenAI/Codex/Claude etc as a co-author on git commits.
- NEVER `git commit` unless explicitly told to commit.
- NEVER `git push` unless explicitly told to push. "Commit" means commit
  locally only; committing is not permission to push.


Update this file when a change touches any of:
- Build/run commands, build scripts, or what they copy/produce (the "Essential Commands" section).
- App version, Unity version, main scene, or other "Current local facts".
- Architecture: renamed/added/removed core scripts, renderers (`RTRendererType`), LLM providers (`LLMProvider`), job-script directives/placeholders, AI Chat flow, automation control endpoints, or experiment folders.
- Hard rules, directory layout, or CLI capabilities/limits.

When the user asks to commit, or when you finish a task, do a quick self-check: "did anything I changed make a statement in AGENTS.md wrong?" If yes, fix it here (and keep `CLAUDE.md` consistent). If you are unsure whether a fact is still true, verify against the code rather than copying the old claim forward. Keep edits concise and factual — this file is read at the start of every task, so brevity matters.

## Project Overview

Seth's AI Tools is a Unity 6 Windows application that provides a native front-end for ComfyUI workflows, image/video generation, LLM-assisted workflows, AI chat, and several interactive experiments.

Current local facts:
- Unity editor version: `6000.5.1f1` (`ProjectSettings/ProjectVersion.txt`)
- App version in code/version metadata: `3.04`
- Main scene: `Assets/Main.unity`
- Primary platform: Windows desktop; a limited Python CLI (Windows + Linux) also exists under `cli/`

### Bumping the app version

When the user says "bump the version", update ALL of these in the same task (do not stop after the code change):
1. `Assets/_Script/Config.cs` — `m_version` float (e.g. `3.02f`).
2. `latest_version_checker.json` (repo root) — `latest_version` number.
3. This file's "App version in code/version metadata" line above.
4. `README.md` — the `# Download` line (version number, date, and the zip size in MB) AND add a new `### V<x.yz> (<date>)` entry at the top of `## Recent changes` summarizing what changed since the last release (derive it from `git log <last-version-bump>..HEAD`).

Use the current date for README date strings. The download zip size changes per build; check the freshly built `SethsAIToolsWindows.zip` size and match it.

## Hard Rules

- Never automatically commit, push, or pull from git without explicit user directions.
- Do not read or edit files starting with `test_`, `Test_`, or `TEST_` unless the user explicitly names them or asks to work with test files.
- Treat ignored config and debug files as local/private unless the user explicitly asks for them. This includes `config.txt`, `config_llm.txt`, `config_preferences.txt`, `log.txt`, generated `*_json_*` files, `comfyui_workflow_to_send_api.json`, and cached ComfyUI API files.
- Do not revert unrelated work. This repo may already contain user edits in many files.
- Keep this file current: if a change you make invalidates anything documented here, update AGENTS.md in the same task (see "Keeping this file current").

## Essential Commands

```bat
BuildWin64.bat
```

Builds the Windows release. It calls `app_info_setup.bat`, deletes/recreates `build/win`, runs `GenerateBuildDate.bat`, invokes Unity with `Win64Builder.BuildRelease` and `Assets/Settings/Build Profiles/ReleaseBuildProfile.asset`, copies runtime folders with `UpdateBuildDirConfigFiles.bat`, signs binaries, and creates `SethsAIToolsWindows.zip`.

```bat
GenerateBuildDate.bat
UpdateBuildDirConfigFiles.bat
```

`GenerateBuildDate.bat` updates build-date data. `UpdateBuildDirConfigFiles.bat` copies `utils`, `web`, `Adventure`, `AIGuide`, `ComfyUI`, `Presets`, `aichat`, and local config files into `build/win`. `utils` includes runtime helper EXEs such as `RTClip` and the bundled FFmpeg/ffprobe helpers under `utils/ffmpeg/bin/`; those third-party FFmpeg binaries are copied as data and are not signed by the build scripts.

There is no current root `BuildWebGL.bat`. WebGL build support is present in `Assets/RT/Editor/WebGLBuilder.cs`, and upload scripts exist (`UploadWebGLRSync*.bat`), but do not document or call a missing WebGL build script unless it is reintroduced.

For editor development, open `Assets/Main.unity` in Unity and enter Play mode.

For the Python CLI subset:

```bash
python cli/aitools_cli.py "<prompt>" output.png -p "Prompt To Image (Z-Image)"
```

On Windows, use `cli\aitools_cli.bat` instead — the first run creates `cli\venv\` and installs dependencies automatically.

The CLI uses `cli/config.txt`, `../ComfyUI`, and `../Presets`. Dependencies are `requests`, `websocket-client`, and `Pillow` (`cli/requirements.txt`). See `cli/README.md` before changing CLI behavior.

AI agents (Claude, Codex, etc.) can and should use this CLI themselves — on Windows or Linux — to generate images and verify workflow/preset changes end-to-end against the user's ComfyUI servers, e.g.:

```bat
cli\aitools_cli.bat "a cat" out.png -p "Prompt To Image (Z-Image)" -v
cli\aitools_cli.bat "make the sky red" out.png -p "Image To Image Klein Edit 1 Input" -i input.png
cli\aitools_cli.bat "restyle this clip" out.mp4 -p "Video To Video (Bernini)" --video input.mp4
```

Text-to-image, single-step image-to-image presets (`-i` / `-i2` inputs), and single-step video-input presets (`--video`) work; multi-step chains and LLM presets error out by design.

## Current Architecture

### Core Unity App

- `Assets/_Script/GameLogic.cs` is the central UI/state coordinator. It owns prompts, negative prompts, generation parameters, selected renderer, preset/job-list text, temp image slots, global variables, and normal vs experiment mode.
- `Assets/_Script/Config.cs` loads `config.txt` and `config_cam.txt`, manages `GPUInfo` server entries, renderer selection, server busy state, per-server overrides, and the generated top-right server status rows.
- `Assets/_Script/ImageGenerator.cs` owns the global generation loop, GPU event queues, server selection, continuous generation, and throttling when job scripts start with LLM work before GPU work.
- `Assets/_Script/GUI/AppSettingsPanel.cs` is the unified Settings window. It owns the visible General, ComfyUI Settings, and Audio content tabs plus an "LLM Settings" launcher in the same tab strip; the old generate gear and Configuration button route into it. The "LLM Settings" tab is a launcher, not a content page: clicking it opens the standalone advanced `LLMSettingsPanel` dialog directly (there is no in-tab LLM summary). The standalone LLM Settings button (`GameLogic.OnLLMSettingsButtonClicked`) opens that dialog directly too, and `AppSettingsPanel.Show(AppSettingsTab.LLM)` / automation `/settings tab=llm` open the window on General and pop the advanced dialog. Its ComfyUI Settings and Audio tabs write the modern supported subset of `config.txt` (ComfyUI servers, tokens/names, VRAM annotations, image editor path, audio defaults, and Text To Speech settings) and reconnect through `Config.ProcessConfigString()` where needed. ComfyUI server rows have up/down priority controls; Apply/reconnect writes that order to `add_server` lines, and `Config.GetFreeGPU()` tries matching idle servers from top to bottom. If reconnect is blocked by active/queued GPU work, the panel shows a confirmation dialog that can force-cancel generation work, clear runtime GPU busy state, and reconnect.
- `Assets/_Script/GUI/MainToolPanelModeController.cs` toggles the scene-authored `CompactToolControls` and `ManualToolControls` groups on the main Tools panel without changing the underlying `GameLogic` state. It captures the scene-authored manual panel rect at startup, fits the panel to compact controls in compact mode, and restores the captured rect in manual mode.
- `Assets/_Script/PresetManager.cs` reads and writes `Presets/*.txt` files using `COMMAND_START|...COMMAND_END` blocks and `COMMAND_SET|...` lines.
- `Assets/_Script/VariableManager.cs` implements `%variable%` substitution for job scripts. Variables are local to a `PicMain` unless prefixed with `global_`.

### Per-Image Pipeline

- `Assets/_Script/Pic/PicMain.cs` is the main per-image controller. It stores textures, masks, temp images, job queues, job history, undo state, LLM manager references, and the job-script interpreter.
- `Assets/_Script/Pic/PicTextToImage.cs` loads ComfyUI workflow JSON, calls the ComfyUI workflow-to-API converter when needed, writes cached API JSON, applies `@replace` and placeholder substitution, sends `/prompt`, tracks progress, and writes debug JSON on errors.
- `Assets/_Script/Pic/PicGenerator.cs` handles continuous img2img/inpaint generation from an existing pic.
- Other `Pic/*` components are focused tools: mask editing, inpaint, upscale, interrogation, movie display, text-to-image status, info panels, size/menu nav, and target rectangles.

### Renderers and Servers

`RTRendererType` currently includes:
- `ComfyUI`
- legacy/compatibility values: `Any_Local`, `A1111`, `AI_Tools`, `AI_Tools_or_A1111`

The renderer dropdown currently exposes only `ComfyUI`. The legacy API image renderer path was removed; OpenAI LLM support is separate and still lives under the LLM provider stack. A1111 and AI Tools server support are legacy compatibility paths, not active primary targets.

ComfyUI servers must be reachable over HTTP and should be started with `--listen` or equivalent network binding. Current workflows are normal ComfyUI workflows in `ComfyUI/`; API-format cached files are generated beside them as `*_cached_api_version.json`.

### Runtime Helpers

- `utils/RTClip.*` is a small external helper already copied with releases.
- `utils/ffmpeg/bin/ffmpeg.exe` and `ffprobe.exe` are Windows-only external helpers for video import/clipping and Unity playback preview proxies. `Assets/_Script/LLM/AIChat/Video/FfmpegTool.cs` resolves them from the app root, calls them with `UseShellExecute=false` and redirected output, and surfaces missing-binary/process errors in chat or local movie UI. Successful ffprobe results are cached per (path, size, mtime) for the session, so movie reloads never re-spawn ffprobe for an unchanged file. `PicMovie` also staggers its lazy movie reloads globally (one per 0.1s slot) after the "\" unload-all hotkey, always calls `VideoPlayer.Stop()` on cleanup (Stop cancels an in-flight `Prepare()`, which is how a wedged player gets reset), and runs a first-prepare watchdog that cancels a `Prepare()` that never completes and retries with growing backoff; these guard against the Media Foundation decoder squeeze that left some movies permanently black on big canvases. The bundled build is GPL v3 with `libx264`; keep `utils/ffmpeg/NOTICE.txt` and `utils/ffmpeg/licenses/` with the binaries and follow https://www.ffmpeg.org/legal.html when distributing.

### Workflows, Presets, and Job Scripts

- `ComfyUI/*.json` contains the workflow files used by the app. There is no current `ComfyUI/FullWorkflowVersions/` folder.
- `Presets/*.txt` contains job scripts and default prompt settings. AutoPic presets are named `AutoPic*.txt` and are used by Adventure/AI-assisted flows.
- Workflow placeholders replaced by `PicTextToImage` include `<AITOOLS_PROMPT>`, `<AITOOLS_NEGATIVE_PROMPT>`, `<AITOOLS_AUDIO_PROMPT>`, `<AITOOLS_AUDIO_NEGATIVE_PROMPT>`, `<AITOOLS_SEGMENTATION_PROMPT>`, `<AITOOLS_INPUT_1>` through `<AITOOLS_INPUT_5>`, and `<AITOOLS_PROMPT_1>` through `<AITOOLS_PROMPT_8>`.
- Unique output filenames: any occurrence of the literal token `AITOOLS_UNIQUE_ID` in a workflow is replaced by `PicTextToImage` at submit time with a per-render tag (`g<gpu>_<yyyyMMdd_HHmmssfff>_<rand>`). Put it in a save node's `filename_prefix` (as the LTX `img_to_video_ltx23 workflow does) so simultaneous GPU renders sharing a ComfyUI output folder can't collide and return each other's file. The literal token is filename-safe, so running the workflow manually in ComfyUI still works with nothing to edit.
- Job scripts support workflow lines with `@` directives, `command` lines without a workflow, `call_llm`, `%var%="value"` assignments, and multi-line `@start`/`@end` or command blocks.
- Common directives include `@replace`, `@upload`, `@resize`, `@resize_if_larger`, `@copy`, `@add`, `@set`, `@setimage`, `@clear`, `@fill_mask_if_blank`, `@invert_alpha`, `@no_undo`, `@stopjob`, `@lock_gpu`, `@llm_*`, `@llm_add_image`, and `@parse_llm_prompts`.
- Built-in job variables include prompt/text buffers plus video helpers such as `%video_fps%`, `%video_fps_2x%`, `%video_fps_3x%`, `%video_fps_4x%`, and `%rife_output_fps%`; these probe the current workflow video source (`m_pendingVideoUploadPath` or loaded `PicMovie`) at job execution time, falling back to 16 fps if probing fails.
- `README.md` has the most complete human-facing job-script reference. Verify against `PicMain.cs` when behavior matters.
- Bernini-R (ByteDance, a Wan2.2-T2V-A14B in-context renderer) presets: `Image To Image (Bernini)` (instruction image edit) and `Video To Video (Bernini)` (restyle a loaded video; a commented `..._ref.json` variant adds a reference image). Their source workflows (`ComfyUI/img_to_img_bernini.json`, `video_to_video_bernini.json`, `video_to_video_bernini_ref.json`) are normal ComfyUI graph workflows, so the app converts/caches API format as needed at runtime. They use a distill-LoRA (turbo) dual-expert graph: `CLIPLoader` (umt5) + `VAELoader` (wan_2.1_vae) + two `UNETLoader` experts (`wan2.2_bernini_r_high_noise_fp16` / `..._low_noise_fp16`) each with `lightx2v_T2V_14B_cfg_step_distill_v2_lora_rank64_bf16`, a `BerniniConditioning` node (task inferred from inputs: source image -> img edit at `length:1`; source video [+ reference image via the `reference_images.reference_image_0` autogrow input] -> v2v/rv2v), `BasicScheduler`+`SplitSigmas`+two `SamplerCustom` passes, then `SaveImage` (i2i) or `VHS_VideoCombine` (v2v). V2V/RV2V saves at native 16 fps by default; 4x RIFE/64 fps interpolation is not in the default Bernini output path. Inputs load through `VHS_LoadImagePath` / `VHS_LoadVideoPath` (path-based, matching the app's temp-folder upload). ComfyUI 0.26.0+ supports the `BerniniConditioning` node natively (no custom node). In AI Chat, `image_to_image` uses Bernini only when the user explicitly says "Bernini"; `video_to_video` is the content-edit/restyle v2v path.
- Production WAN fast image/text-to-video workflows keep their legacy built-in `FL_RIFE` 4x interpolation and save at 64 fps; the default size path stays no-upscale unless a preset explicitly raises width/height. Bernini v2v does not include default RIFE. `ComfyUI/video_to_video_rife.json` and `Presets/Video To Video (RIFE Interpolation).txt` remain a separate optional utility for arbitrary Movie bubbles: they load a source video, run `FL_RIFE` 2x, preserve audio, and save MP4 with output fps from `%rife_output_fps%` (source fps x2). AI Chat exposes this as the `rife_video` action/skill for smoothing or FPS interpolation; content edits/restyles still use `video_to_video`/Bernini.

### LLM Systems

- `Assets/_Script/LLM/LLMSettingsData.cs`, `LLMSettingsManager.cs`, and `LLMInstanceManager.cs` manage `config_llm.txt`, active providers, multiple LLM instances, replicas, model lists, context limits, and sampling/reasoning settings.
- Per-instance job routing is two orthogonal axes: `jobMode` (text-job size only: `Any`/`BigJobsOnly`/`SmallJobsOnly`) plus `supportsVision` (can accept image jobs) and `visionOnly` (reserve for vision — no text). Vision jobs route to `visionOnly` instances first, then any `supportsVision` instance. New OpenAI/Anthropic/Gemini instances default `supportsVision` on; new llama.cpp/Ollama/OpenAI-compatible instances default it off because local text servers often reject image input unless a real vision model/mmproj is loaded. `config_llm.txt` carries a `schemaVersion`; pre-decoupling configs (where vision was encoded in `jobMode` via the legacy `VisionJobsOnly`/`NonVisionOnly` values) are migrated once on load by `LLMInstanceManager.MigrateJobModes()`. The AI Chat caption sidecar warns in-chat when an image needs vision but no active instance has `supportsVision`, and surfaces backend failure details when a marked-vision instance rejects the request.
- LLM requests do not impose an optional output-token budget: OpenAI/OpenAI-compatible, llama.cpp, Ollama, and Gemini requests omit their output-limit field, including AI Chat sidecars, PicMain `call_llm`, Adventure, and AI Guide. Legacy llama.cpp/Ollama `max_tokens`, `max_new_tokens`, `max_output_tokens`, `max_completion_tokens`, `n_predict`, and `num_predict` provider parameters are ignored. Anthropic's Messages API requires `max_tokens`, so it receives the known per-model maximum. Model context/output ceilings and server-side generation configuration still apply.
- `LLMProvider` currently supports `OpenAI`, `Anthropic`, `LlamaCpp`, `Ollama`, `Gemini`, and `OpenAICompatible`.
- `model_data.json` supplies shipped cloud model lists and default endpoints.
- `Assets/RT/AI/` contains provider/runtime managers: OpenAI text, Anthropic text, Gemini text, generic OpenAI-compatible/TextGen WebUI, ComfyUI file upload, speech-to-text, TTS, streaming download handlers, and main-thread dispatch.

### AI Chat

- `Assets/_Script/LLM/AIChatPanel.cs` builds the programmatic chat UI and routes chat requests through the same LLM instance/provider stack.
- AI Chat claims supported image drops (`.png`, `.jpg`, `.jpeg`, `.bmp`) anywhere over the visible AI Chat window as pending message attachments. The per-message cap is 99 images; the footer attachment strip scrolls horizontally and shows `Queued` / `Captioning` / `Ready` / `No caption` badges. Send remains blocked until every staged attachment is captioned or marked unavailable. Adventure uses its own narrower attachment zone and keeps the reusable default limit.
- AI Chat accepts dropped `.mov`, `.mp4`, and `.avi` files as video imports, not image attachments. `ChatImageAttachmentZone` claims those drops, `FfmpegTool` probes/transcodes them to short normalized MP4 clips (default 5s, source FPS with 16 fps fallback, max 832x480, audio included), and long sources show the top-level movable/resizable `ChatVideoClipChooser` for preview/scrub/start/duration/FPS/audio selection. The chooser's `Import still` button grabs the single frame at the current scrub position (via `FfmpegTool.ExtractStillFrame`, cut from the original source at native resolution) and appends it as a normal still `#N (you)` image bubble without closing the dialog, so several stills can be grabbed from different positions. The `Import still` button is shared: it appears both here (drag-drop import) and on a movie pic's `Process > Export movie clip` chooser (the same `ChatVideoClipChooser`), gated only by whether the caller passes an `onImportStill` callback. The drag-drop path routes through `AIChatPanel.ExtractAndAppendStillFrame` (epoch/busy-gated); the export path uses the static `AIChatPanel.AddLocalStillFrameToChat`; both share `AIChatPanel.AppendStillFrameFromSource`. If Unity/Windows cannot preview the source (common with HEVC/HDR iPhone MOVs), the chooser automatically builds a temporary H.264 preview proxy with FFmpeg and shows conversion progress in the dialog; final imported clips still transcode from the original source and map only the first normal audio stream. `PicMovie.PlayMovie` also probes workspace movie sources and creates the same temporary H.264 preview proxy before assigning Unity `VideoPlayer.url` when the source codec is known to be fragile on Windows. For workspace movie pics, `PicMovie.GetProcessingFileName()` returns that proxy when available, so `Process > Export movie clip`, workflow video uploads, AI Chat `clip_video`, and `video_to_video` reuse the already-normalized H.264 preview instead of decoding the original HEVC/MOV again; save/copy identity paths still use `GetFileName()` and preserve the original source. For AI Chat `video_to_video`, the executor probes the Movie source duration and overrides the next workflow's `length` and `frame_load_cap` to match the source at the Bernini loader's 16 fps, rounded up to the Wan/Bernini `4n+1` frame cadence (so an 8s clip queues 129 frames instead of the preset's 81-frame default). `PicMain` movie pics expose `Process > Export movie clip`, using the same chooser and automatically adding the exported MP4 to AI Chat as a `Movie #N` bubble. Imported/exported clips are loaded through `PicMovie.PlayMovie`, registered as chat media, and blocked like sidecar work while importing. Movie bubbles are captioned by an FFmpeg-sampled PNG contact sheet sent through the existing vision sidecar path; the app does not send native MP4 payloads to LLM providers. If FFmpeg/ffprobe is missing, the local chat notice includes the expected `utils/ffmpeg/bin` paths.
- AI Chat has a footer `Main LLM` dropdown. `Default` preserves normal big-job routing; selecting an active instance forces only the main chat turn to that instance, ignoring `jobMode`/`visionOnly` text gates and waiting for that instance if it is busy. Sidecar jobs such as attachment captions, `inspect_image`, compact summaries, `/applystyle` prompt rewrites, and skill delegation keep their own routing. If raw image data is in chat history, the forced main instance still must have `supportsVision`.
- AI Chat Settings has a session-only `Post user message` field. Non-empty text is appended to every main AI Chat user turn before the user bubble/history entry is created, so it is visible and remains in chat history. It is not saved to disk/PlayerPrefs and survives Clear until edited or the app closes.
- AI Chat's footer input keeps session-only command-line style prompt history. Up/Down recall recent accepted user-entered prompts only when the caret is already on the first/last visual line; normal multi-line caret movement, selections, and modifier shortcuts keep their default behavior. History is not saved and survives Clear until the app/editor session ends.
- AI Chat starts with a local welcome bubble and hides local `Info`/debug bubbles by default. AI Chat Settings has a `Show debug stuff` checkbox (default off) that makes those bubbles visible; hidden recap-eligible Info messages still queue for the next LLM turn. Real backend/LLM failures on the **main chat turn** are NOT debug-gated: they render as an always-visible red `Error` bubble (`AddErrorBubble` in `AIChatPanel.cs`), so an auth/quota/model error is never silent. Sidecar caption/`inspect_image` failures stay debug-gated `Info` bubbles. Cloud LLM dispatch no longer silently substitutes a default model when an instance has no model selected — AI Chat shows an `Error` bubble and the other subsystems (PicMain `call_llm`, Adventure, AI Guide, sidecars) surface the failure instead.
- AI Chat's Compact "Summarize" (settings panel, or automation `/chat_compact`) summarizes the older exchanges with the footer `Main LLM` override instance when one is selected (queuing on its least-loaded replica if busy, never switching instances), else normal big-job routing. The request STREAMS into a read-only live `Summary (generating...)` preview bubble so long compacts visibly progress, the status line shows an approximate sent-token count (chars/4), and the finished summary becomes an always-visible EDITABLE `Summary` bubble - a `COMPACT_SUMMARY_TAG`-tagged system-role history line whose bubble edits write straight back into the text the LLM reads. Per-image recap notes follow the "Image context limit" window plus named anchors (older unanchored images are summarized only in aggregate), so a 200-image story doesn't flood the recap with stale image descriptions. Auto-loaded skill bodies survive the compact: they are stripped from the summarizer's transcript input, and any body that lived only in the summarized-away lines is re-queued onto the next outgoing message's recap tail (after `RebuildChatBubblesFromHistory`, which clears pending recap messages). The one-shot has no output-token cap (max_tokens omitted; Anthropic gets its per-model max) and the watchdog scales with transcript size (5-30 min) because huge local-model prefills can take several minutes. Timeout/empty failures surface as always-visible red Error bubbles; pre-flight refusals ("nothing to compact", no LLM) are toasts. `SkillActionExecutor.DispatchOneShot` accepts optional `maxNewTokens` (0 = uncapped) and `onStreamChunk` (enables streaming) used by this path.
- Help/capability questions in AI Chat are answered by the LLM, not a local hardcoded shortcut. The autoloaded `help` skill (`aichat/skills/help.md`) gives the model the expected concise format, tells it to derive available areas from the live loaded SKILLS list, includes optional render-system hints (`zimage`, `krea`, `ideogram`, `ltx`, `wan`), and reminds users to ask for `use anchors` when they want consistency across images.
- The `image_to_movie` skill autoloads for image-to-video/animation requests and advertises `Image To Video (WAN) 5s.txt` as the user-facing WAN/Wan 2.2 preset name. 
- Text-only AI Chat video requests default to a chained `generate_image` (`Prompt To Image (Z-Image).txt`) -> `image_to_movie` pair, using `Image To Video (WAN) 5s.txt` when the user asks for WAN and `Image To Video (LTX) 5s.txt` otherwise. Direct `generate_movie` / text-to-video is reserved for explicit "text-to-video", "direct from text", "no still first", or named `Prompt To Video ...` requests.
- `Assets/_Script/LLM/AIChat/Context/ChatContextBuilder.cs` builds the stable system prompt in this order: optional `aichat/pre_prompt.txt`, `aichat/main_prompt.txt`, skill summaries, action protocol instructions, then optional `aichat/post_prompt.txt`; with the `test_` preset prefix, existing `test_pre_prompt.txt`, `test_main_prompt.txt`, `test_post_prompt.txt`, and `test_caption_prompt.txt` override their normal prompt files. The volatile per-turn state block adds GPU state plus a bounded recent-window CHAT IMAGES list with compact provenance/captions.
- `Assets/_Script/LLM/AIChat/Skills/SkillManager.cs` loads `aichat/skills/*.md` plus prompt files in `aichat/`.
- When `aichat/skills/*.md` reload changes available skill IDs, AI Chat emits a local-only `Info`/debug bubble listing added/removed IDs; it is visible only when `Show debug stuff` is enabled. Keyword autoload delivers full skill-reference bodies through the info-recap tail of the triggering user message (the same channel `read_skill` uses), NOT as a system-role interaction: `BuildPromptChat` folds system-role lines into the FRONT system message, and rewriting the prompt head mid-chat invalidated the server-side prompt cache for the entire history every time a new skill triggered (a ~40s full re-prefill on long llama.cpp chats). A skill counts as already-loaded while a body copy (keyword or `read_skill`) is still present in user-line history or queued unsent (`ComputeLiveAutoloadSkillIds` scans for the marker headers), so Rewind/Compact/Clear self-heal and a trimmed body simply re-triggers on the next keyword hit. Editing a live skill's file re-sends only the genuinely changed body on the next message. The local-only loaded/updated notice is not forwarded in the LLM recap.
- The `scenario_storytelling` skill auto-loads for story/roleplay requests. Ordinary roleplay defaults to anchor-free narration capped at ~500 words of prose with TWO visuals per turn, interleaved as short prose/dialog beats followed by their action tags. Stills vs movies is GPU-aware: the model counts IDLE GPUs in the newest CURRENT STATE GPUS block and emits at most one movie (generate_image -> image_to_movie chain pair) per IDLE GPU, filling the rest with fast stills; when every GPU is busy it emits stills only so the render queue catches up. Explicit user counts/"stills only"/"movies every beat" override; anchors and `image_to_image` remain opt-in for exact recurring identity and deliverables that require stable references.
- The `krea` skill auto-loads when the user explicitly asks for Krea/Krea 2/Krea Turbo and emits `generate_image` with `Prompt To Image (Krea 2 Turbo).txt`. The workflow `text_to_img_krea2_turbo.json` uses Krea 2 Turbo FP8, `qwen3vl_4b_fp8_scaled.safetensors`, `qwen_image_vae.safetensors`, 8-step Euler sampling, and no built-in prompt enhancer or LoRA branch.
- `SkillActionParser` and `SkillActionExecutor` parse and execute `<aitools_action .../>` tags. Image/movie skills reuse `PicMain.RunPresetByName()` and the existing preset/job pipeline. When AI Chat's preset prefix is active (for example `test_`), unprefixed requested preset names first try the prefixed file and fall back to the bare production file only if no prefixed preset exists. Preset names then resolve case-insensitively, then by unique punctuation/spacing-only canonicalization, before the conservative fuzzy fallback emits a correction bubble.
- `rife_video` is a promptless AI Chat action for 2x RIFE frame interpolation of Movie bubbles. It defaults to `Video To Video (RIFE Interpolation).txt`, accepts `chat_image="N"` or same-reply `chain="true"`, and optionally accepts `fps="N"` only for explicit fixed-FPS output. It is excluded from `/applystyle` prompt rewriting.
- `read_skill` loads a skill markdown body into the next LLM prompt and now requests one scoped synthetic `(continue)` turn automatically, so a mid-reply self-skill load can continue without the user clicking Send. Stop/Clear/new Send cancels the pending skill-load resume, and the injected reference text tells the model not to call `read_skill` again for that skill.
- `continue` is a model-invoked control action (`<aitools_action skill="continue"/>`, no preset/GPU/image side effect): the model emits it when it decides it needs another turn (e.g. it announced an edit but wants the spawned image to settle first, or it has more steps to run). It registers a scoped synthetic `(continue)` turn through the same auto-resume path as `read_skill`/`inspect_image resume`, but is bounded by a consecutive-self-continue cap (`MaxConsecutiveSelfContinues` in `AIChatPanel.cs`) that resets on a real user send; hitting the cap stops the loop with a local info/debug bubble, visible only when `Show debug stuff` is enabled. Stop/Clear/Rewind/new Send cancel a pending continue. The model is told not to add `continue` after `read_skill`/`inspect_image resume="true"` (those already auto-continue).
- `ChatPicMirror.cs` mirrors generated pics into chat image bubbles so later actions can reference `chat_image="N"`.
- The AI Chat action executor serializes skill actions and defers actions that need generated chat-image pixels or local video clip output, including `paste_image source_chat_image`, `inspect_image`'s implicit latest-image fallback, and `clip_video`, until the referenced media is ready. This avoids composing or inspecting the black placeholder texture from a same-reply source image and lets `clip_video` feed same-reply `video_to_video` via `chain="true"`.
- Chained AI Chat actions that carry `anchor="Name"` re-point the anchor registry to that same chained Pic, so multi-stage flows can rename a clean base into a final edited subject and later same-reply actions can reference the final anchor.
- AI Chat aborts an action immediately when a `chat_image*` / `source_chat_image` anchor name does not resolve, instead of stripping the bad reference and cascading into a misleading missing-input error.
- AI Chat annotation-style local composition ops (`draw_text`, `draw_shape`, `add_border`) capture a clean pre-overlay PNG the first time they mutate a generated/composed Pic. The volatile CHAT IMAGES block marks `clean_base=available`, and later composition actions can pass `clean_base="true"` with `chat_image="N"` to rebuild revised text/bubbles from that pre-overlay base instead of drawing over baked-in old overlays. Layout-building ops like `paste_image` and `crop_resize` are treated as base assembly, so multi-panel comics keep pasted panels in the clean base.
- AI Chat prompt recipes distinguish flat logos/decals/watermarks from material-integrated marks. Literal stickers/watermarks use local `paste_image` alpha compositing so uploaded pixels, colors, and transparency survive. Requests where the mark should look physically part of a subject use a guide flow: generate the clean subject, paste the uploaded mark only as a placement guide, then run Klein 2-input with the guide composite plus the original mark and anchor the final integrated result for downstream edits/videos.
- The `comics` skill prefers one Ideogram 4 render for new whole comic strips/pages by default; local `new_canvas`/`paste_image`/`draw_text` recipes remain for existing-image assembly, exact anchor workflows, single-panel overlays, and repairs. Ideogram comic prompt recipes use `[y1,x1,y2,x2]` bboxes plus wide horizontal speaker-side speech balloon/text regions with no manual line breaks, explicit speaker face/mouth tail endpoints, and negative non-speaker tail constraints, to avoid vertical stacked dialog and wrong-character balloon tails.
- AI Chat local composition records same-turn `paste_image` and probable top-band title `draw_text` rectangles, then injects a layout warning if a full-width title overlaps or sits too close to pasted panels. This is a narrow deterministic guard for comic/page title bugs, not a general layout validator.
- AI Chat prompt-cache preservation records the exact text last sent for each history line, including the appended per-turn CURRENT STATE block, in prompt-only cache fields on `GTPChatLine`. `BuildPromptChat()` reuses that exact text on later turns so llama.cpp-style prefix/KV caches can match prior requests; user edits to a bubble update the visible/display text and automatically invalidate the cached sent bytes for that line. User history lines keep a separate display string from the LLM payload so hidden info recaps remain hidden after Compact/Rewind rebuilds. Older CURRENT STATE copies can remain in prompt history as historical cached text; the model is told to use the newest CURRENT STATE block for live status. Old executed `<aitools_action .../>` XML is kept in outgoing prompts by default for the same cache reason; turning off "Keep old tool calls in prompt" is an explicit lean-mode tradeoff that strips old XML and breaks exact reuse through prior assistant tool calls. Generated images still use compact provenance instead of automatic caption sidecar calls unless "Auto-caption generated images" is enabled, and the "Image context limit" setting caps how many newest chat images are described in CHAT IMAGES (default 40, media remains locally available). User-pasted attachments are still captioned once for context, and when "Include image data" is enabled, raw base64 is sent only on the attachment's first outgoing turn and then cleared from stored chat history.
- AI Chat text bubbles have a right-click context menu. `Select this box` selects one bubble's text, `Speak` reads only the currently highlighted text through the configured Text To Speech provider or shows a local setup warning, `Copy all to clipboard` copies the visible transcript to the clipboard and shows a toast, and `Rewind to this spot` keeps the clicked user/assistant bubble while removing later prompt history plus later AI Chat media/context. The footer input has a smaller right-click menu with `Select all` and `Speak` for its selected text. AI Chat also has a footer speech status plus a Stop control that appears only during TTS request/playback. Rewind trims only the chat media list; world `PicMain` objects remain in the workspace.
- User-pasted attachment captions are queued behind available vision-capable LLM capacity. Attachment thumbnails show `Queued` / `Captioning` / `Ready` / `No caption`, the footer status line shows live caption progress, and Send remains blocked until every attachment is either captioned or marked unavailable. If the chosen vision sidecar fails at the backend, AI Chat emits an Info/debug bubble with the provider error, visible only when `Show debug stuff` is enabled, so the next main turn can account for it.
- AI Chat has an executable `inspect_image` skill for on-demand real vision inspection/captioning of an existing `chat_image`, current attachment, or the latest generated chat image when no explicit source is provided. Use it when the user asks the assistant to check its work or verify actual generated pixels; QA/layout inspections are prompted to return defect-first PASS/FAIL results. It shows the readable sidecar prompt in chat with image bytes elided, waits for generated-image pixels before dispatch, queues behind available vision-capable LLM capacity, shows footer progress, blocks Send/Continue until done, can be cancelled with Stop, and returns the result to chat/context. If the action includes `resume="true"`, AI Chat waits for all pending inspections and then sends one synthetic `(continue)` turn so the main assistant can answer from the result without the user clicking Send again. Its watchdog timeout is intentionally longer than attachment captions for slower local vision models.
- For `inspect_image`, PNGs with alpha are sent to the vision LLM as a checkerboard-composited inspection copy with an explicit transparency note, so hidden RGB pixels under alpha do not confuse transparency/cutout QA. The original chat image bytes remain unchanged for normal media actions.
- When `/applystyle` rewrites a render prompt, chat-image provenance and tool-call logging report only the original pre-style prompt text; the restyled prompt and the fact that `/applystyle` was used stay local-only and are not surfaced to future main-chat LLM turns.
- The optional `gpu="N"` action attribute is a SOFT hint: it sets `PicMain.m_requestedServerID` with `m_requestedServerIsPreference=true`, so if that GPU is busy when the pic is ready, the scheduler falls back to any free GPU (PicMain.UpdateJobs) instead of waiting. Adventure's per-server render uses the same field with the flag left false (hard pin, waits for that exact server). The model is told in `main_prompt.txt` to normally omit `gpu`/`llm` and let the scheduler load-balance — hand-assigning GPUs for bulk requests caused movie batches to collide on a couple GPUs while others sat idle.

**Debugging AI Chat:** the editor appends the whole exchange to `llm_aichat_log.json` in the working dir (project root in-editor). It is one chronological JSON array of `request` / `response` / `action` / `note` events: the outgoing LLM body, the raw reply with its inline `<aitools_action .../>` tags, each parsed tool call about to run (so `generate_image` prompts and `draw_text` rect/font/`chat_image` are visible), and per-action notes (e.g. `draw_text` auto-fit results: chosen font size, canvas size, overflow). It is truncated at the start of each play session and deleted on exit. Read this FIRST when a poster/book page comes out wrong — it shows exactly which Pic each action targeted and what the model emitted. Provider-agnostic last request/response files are split by job size: `llm_last_request_sent_big.json` / `llm_last_response_sent_big.json` for AI Chat, AI Guide, and Adventure-style big jobs, and `llm_last_request_sent_small.json` / `llm_last_response_sent_small.json` for PicMain `call_llm` jobs and AI Chat sidecars. Errors are similarly split as `llm_last_error_big.json` / `llm_last_error_small.json`. Source: `Assets/RT/AI/AIChatLog.cs` and `Assets/RT/AI/LLMDebugLog.cs`.

### Automation Control Server (editor testing harness)

A loopback HTTP control server lets external tools (AI agents, scripts) drive the editor and AI Chat for automated end-to-end testing. It survives C# domain reloads and play stop/start, so an external loop can edit code, recompile, and re-test without anyone clicking Play.

**Prefer this automation bridge for Unity/editor validation whenever it is enabled.** Before launching a separate Unity batch/editor process, check `GET /status`; if the bridge is ready, use `/rebuild`, `/play`, `/stop`, `/settings`, `/llm_settings`, `/server_settings`, `/focus_input`, `/open_chat`, `/chat`, `/chat_import_video`, `/screenshot`, `/save`, and `/chat_images` to compile, drive the app, and capture evidence. For UI/AI Chat work, verify with screenshots from `/screenshot` whenever possible rather than relying only on code inspection. Do not start a second Unity editor/batchmode instance against this project while the editor lockfile/server is active unless the bridge is unavailable or the task specifically requires batchmode.

If a change cannot be validated because the bridge lacks a needed command, add a narrow loopback-only automation endpoint or driver capability in the same task when practical, then document the new endpoint here. Keep new automation commands deterministic, local-only, and scoped to testing/inspection; avoid broad privileged operations.

- `Assets/_Script/Automation/AutomationBridge.cs` (runtime): static seam shared by the editor server and the play-mode driver (runtime can't reference editor types, so shared state lives here).
- `Assets/_Script/Automation/AutomationDriver.cs` (runtime MonoBehaviour): registers with the bridge; implements settings-open, chat send, the "fully idle" query, save-chat-image, and screenshot capture. In standalone builds it self-spawns when `-enable_automation` is on the command line; in the editor the controller spawns it on entering play.
- `Assets/_Script/Automation/Editor/AutomationController.cs` (editor, `[InitializeOnLoad]`): a raw `TcpListener` on `http://127.0.0.1:8772/` (TcpListener, not HttpListener, to avoid the Windows HTTP.sys URL-ACL/admin requirement). The "rebuild & restart" state machine persists across domain reloads via `SessionState` and advances on `afterAssemblyReload`. OFF by default — enable with **Tools > RT Automation > Enable Control Server** (EditorPrefs-persisted).

Endpoints (loopback only): `GET /status` (playing/compiling/driverReady/idle/chatActive/ready/stage), `POST /rebuild` (exit play → recompile → re-enter play, no clicks), `POST /play`, `POST /stop`, `POST /settings` (body `tab=<general|configuration|comfyui|audio|llm>`; opens the unified Settings panel), `POST /llm_settings` (opens the advanced LLM Settings panel), `POST /server_settings` (body `id=<serverID>`; opens that server's Overrides panel), `POST /focus_input` (body `name=<hierarchy-path substring>` matched case-insensitively against full transform paths like `AppSettingsPanel/.../UrlRow/Input`, optional `selectall=<true|false>`; focuses the first matching active `TMP_InputField`, returns the matched path plus `caretGraphic` = whether its caret/selection renderer exists), `POST /open_chat`, `POST /chat` (body = message text; opens chat + sends one turn; returns ok:false if a busy gate refused the send - poll `/status` idle and retry), `POST /chat_import_video` (body `path=<file>` plus optional `start=<seconds>`, `duration=<seconds>`, `fps=<n>`, and `audio=<true|false>`; opens AI Chat and imports a clipped local video as a Movie bubble), `POST /chat_compact` (body `mode=<summarize|truncate>` default summarize, `keep=<n>` exchanges kept verbatim default 2; runs AI Chat's Compact - summarize is async, poll `/status` idle), `GET /chat_images` (JSON: index/w/h/busy/movie/captionPending/captionShort/captionLong and `moviePath` for Movie bubbles), `POST /save` (body `index=<n|latest>` + `path=<file>`; writes a PNG via `PicMain.SaveFile`), `POST /screenshot` (body `path=<file>` + optional `x,y,w,h` top-left region; full game view if omitted). Request bodies for `/settings`, `/server_settings`, `/chat_import_video`, `/chat_compact`, `/save`, and `/screenshot` are `key=value` lines.

The `idle` flag ANDs every AI Chat busy gate (streaming, forced-main wait, compact summary, sidecar captions/inspections, auto-resume, the skill-action pump) plus any chat-generated `PicMain` still rendering — so it only reports idle once an image turn has finished rendering on the GPU, not merely when the text stream ends. `/save` and `/screenshot` write files asynchronously (screenshot waits for end-of-frame); poll for the file. Recommended editor pref: **Script Changes While Playing = Stop Playing And Recompile** for deterministic reloads.

### Experiments

Experiment code lives in `Assets/Experiments/`:
- `Adventure/` - interactive story, quiz, Twine/HTML export, AutoPic flows
- `ShootingGallery/` - paintball/gallery target experiment
- `Pizza/` - pizza generator experiment
- `Breakout/` - Breakout experiment
- `CrazyCam/` - older camera/photobooth code; README notes CrazyCam is currently disabled/replaced in practice by SpookyCam-style presets

Prompt/template text for higher-level modes also exists in root `Adventure/` and `AIGuide/`.

### Python CLI

The `cli/` folder is a separate Python command-line front-end for ComfyUI generation (Windows + Linux). It intentionally implements only a subset of the Unity app:
- `aitools_cli.py` - argparse entry point
- `aitools_cli.bat` - Windows launcher (auto-creates `cli/venv/`, installs `requirements.txt`)
- `config.py` - `cli/config.txt` parser
- `presets.py` - subset parser for `Presets/*.txt`
- `workflow.py` - workflow load/convert/cache, replacements, placeholders, seed override
- `servers.py`, `comfy_api.py`, `progress.py`, `images.py`, `util.py` - server probing, ComfyUI HTTP/WebSocket calls, image/video upload, image resizing, output save

Do not assume every Unity job-script feature works in the CLI. Multi-step chains, temp slots, most LLM commands, and many Unity-only image operations deliberately error or are unsupported there.

## Directory Map

- `Assets/_Script/` - main app logic
- `Assets/_Script/Pic/` - per-image workflow, image editing, job execution
- `Assets/_Script/GUI/` - app UI panels and controls
- `Assets/_Script/LLM/` - LLM settings, AI Chat, chat skills, model fetchers
- `Assets/RT/` - reusable runtime toolkit, editor builders, third-party utilities
- `Assets/RT/AI/` - API/service integration managers
- `Assets/Experiments/` - Adventure, ShootingGallery, Pizza, Breakout, CrazyCam
- `Assets/Settings/Build Profiles/` - Unity build profiles
- `ComfyUI/` - source workflows plus generated cached API workflow files
- `Presets/` - preset/job scripts
- `aichat/` - editable AI Chat prompts and skill markdown files
- `cli/` - Python CLI subset (Windows + Linux)
- `web/`, `utils/`, `Packaging/` - runtime/package support files, including bundled helper EXEs under `utils`
- `Media/` - README screenshots and media

Generated or local-only folders include `Library/`, `Temp/`, `Logs/`, `build/`, `output/`, `UserSettings/`, `Images/`, `OldVersions/`, and `TestFiles/`.

## Development Notes

- Prefer existing Unity/C# patterns in the repo: MonoBehaviours, TextMeshPro UI, `UnityWebRequest`, `SimpleJSON`, and the existing RT utility classes.
- Programmatic UI is common in the LLM panels and AI Chat. Match the local style when extending those panels.
- Runtime-built `TMP_InputField`s (e.g. via `TMP_DefaultControls.CreateInputField`) run `OnEnable` before `textComponent` is wired, so TMP never creates the caret/selection renderer: typing works but the caret, mouse highlight, and wheel-scroll forwarding are dead. Call `TMPInputFieldCaretFix.Apply(input)` (`Assets/RT/TMPInputFieldCaretFix.cs`) after the field is fully wired AND parented; AI Chat's own `AIChatCaretFixer` already handles its fields.
- When adding or moving Unity assets, keep `.meta` files with them.
- Normal ComfyUI workflow files are the source of truth; cached API JSON files are generated artifacts and should generally not be hand-edited.
- Build scripts and packaging scripts are Windows-oriented and may delete/recreate build output directories.
- No reliable automated test command was found. If validation is needed, prefer focused C# compile/build checks or targeted Unity editor validation requested by the user.
