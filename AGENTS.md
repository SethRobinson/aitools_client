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

Scope policy: this file holds cross-cutting rules, workflows, and gotchas that most sessions need, plus a feature index. Keep it around 30 KB. Feature deep-dives live in `docs/<topic>.md` (`aichat.md`, `automation_bridge.md`, `llm_providers.md`, `media_import.md`, `settings.md`, `workflows.md`, `minimax_h3.md`, `web_media.md`, `audio_generation.md`): before working on a feature listed in the index, read its doc; when finishing feature work, update that doc and keep the index entry here to one or two lines (where it lives + the non-obvious constraint). Cross-cutting rules and new gotchas still land here directly. When a change makes anything stale, here or in a linked doc, update it in the same change.

## Testing

- When possible, design automated tests for new features and bug fixes.
- Run relevant automated tests after finishing changes to guard against regressions.
- If tests cannot be run or do not exist, state that clearly in the handoff and describe any manual verification performed.

## Security

- Never commit sensitive data, including credentials, tokens, passwords, private keys, cookies, customer data, personal data, or machine-specific authentication material.
- If an AI assistant needs authentication data or other secrets for local work, use `agents_secret.md` for those notes.
- `agents_secret.md` must stay ignored by git and must not be committed.
- `aichat/skills/local_*.md` is gitignored on purpose: AI Chat skill files that describe a machine-local generation server (the `generate_music` / `generate_sfx` / `generate_speech` prompting rules and voice list for the audio gateway). Keep server-specific skill text there; the generic code, Settings fields, and `docs/audio_generation.md` are what the repo ships. `BuildWin64.bat` deletes `build\win\aichat\skills\local_*.md` before zipping (added 2026-09-13 after the 3.06 zip shipped them; `UpdateBuildDirConfigFiles.bat` copies the whole `aichat` folder).
- Server- and machine-specific facts are local-only too, not just credentials: hostnames, ports, GPU/cluster layout, SSH targets, server-side file paths, restart/launcher scripts, and which servers have which models installed. Keep them in `agents_secret.md`, never in tracked files (AGENTS.md, docs/, READMEs, code comments, workflow JSON). Tracked docs must stay generic ("the ComfyUI server", "the wedged instance") - other people don't have this hardware. Example hostnames in docs use placeholders like `gpu-box.lan`.
- Do not put secrets in commit messages, logs, issue text, pull request descriptions, generated docs, or other tracked files.
- Before committing, review staged changes for accidental secrets.

## Git

- Never add OpenAI/Codex/Claude etc as a co-author on git commits.
- Committing locally is allowed and expected (Seth, 2026-09-13): commit each self-contained change once it compiles, one logical change per commit with a descriptive message, instead of one big mixed commit that is hard to follow. Never commit secrets or the ignored local files (see Security).
- NEVER `git push` (and do not `git pull`) unless explicitly told to. "Commit" means commit
  locally only; committing is never permission to push.

## Keeping this file current

Update this file when a change touches any of:
- Build/run commands, build scripts, or what they copy/produce (the "Essential Commands" section).
- App version, Unity version, main scene, or other "Current local facts".
- Architecture: renamed/added/removed core scripts, renderers (`RTRendererType`), LLM providers (`LLMProvider`), job-script directives/placeholders, AI Chat flow, automation control endpoints, or experiment folders.
- Hard rules, directory layout, or CLI capabilities/limits.

When the user asks to commit, or when you finish a task, do a quick self-check: "did anything I changed make a statement in AGENTS.md wrong?" If yes, fix it here (and keep `CLAUDE.md` consistent). If you are unsure whether a fact is still true, verify against the code rather than copying the old claim forward. Keep edits concise and factual - this file is read at the start of every task, so brevity matters.

## Project Overview

Seth's AI Tools is a Unity 6 Windows application that provides a native front-end for ComfyUI workflows, image/video generation, LLM-assisted workflows, AI chat, and several interactive experiments.

Current local facts:
- Unity editor version: `6000.6.0f1` (`ProjectSettings/ProjectVersion.txt`)
- App version in code/version metadata: `3.06`
- Main scene: `Assets/Main.unity`
- Primary platform: Windows desktop; a limited Python CLI (Windows + Linux) also exists under `cli/`

### Bumping the app version

When the user says "bump the version", update ALL of these in the same task (do not stop after the code change):
1. `Assets/_Script/Config.cs` - `m_version` float (e.g. `3.02f`).
2. `latest_version_checker.json` (repo root) - `latest_version` number.
3. This file's "App version in code/version metadata" line above.
4. `README.md` - the `# Download` line (version number, date, and the zip size in MB) AND add a new `### V<x.yz> (<date>)` entry at the top of `## Recent changes` summarizing what changed since the last release (derive it from `git log <last-version-bump>..HEAD`).

Use the current date for README date strings. The download zip size changes per build; check the freshly built `SethsAIToolsWindows.zip` size and match it.

## Hard Rules

- Never push to or pull from git without explicit user directions. Local commits are allowed and expected (see "Git").
- Do not read or edit files starting with `test_`, `Test_`, or `TEST_` unless the user explicitly names them or asks to work with test files.
- Treat ignored config and debug files as local/private unless the user explicitly asks for them. This includes `config.txt`, `config_llm.txt`, `config_preferences.txt`, `log.txt`, generated `*_json_*` files, `comfyui_workflow_to_send_api.json`, and cached ComfyUI API files.
- Do not revert unrelated work. This repo may already contain user edits in many files.
- Writing style: no em-dashes or en-dashes used as em-dashes in any prose (this file, docs, skill/prompt text, commit messages, chat replies). Use a colon, a comma, parentheses, a spaced hyphen (the house style here), or two sentences. Seth's standing rule; the prose files were swept clean on 2026-08-31.
- Keep this file current: if a change you make invalidates anything documented here, update AGENTS.md in the same task (see "Keeping this file current").

## Essential Commands

```bat
BuildWin64.bat
```

Builds the Windows release. It calls `app_info_setup.bat`, deletes/recreates `build/win`, invokes Unity with `Win64Builder.BuildRelease` and `Assets/Settings/Build Profiles/ReleaseBuildProfile.asset`, copies runtime folders with `UpdateBuildDirConfigFiles.bat`, signs binaries, and creates `SethsAIToolsWindows.zip`.

Build timestamps are stamped automatically by `Assets/RT/Editor/BuildTimestampWriter.cs` (`IPreprocessBuildWithReport`), which writes `Assets/Resources/build_date.txt` (gitignored, folder auto-created) at the start of every player build regardless of build path (editor Build menu, Build Profiles window, batchmode). `RTBuildInfo.Timestamp` loads it at runtime and falls back to the current time in the editor. There is no `GenerateBuildDate.bat` anymore.

```bat
UpdateBuildDirConfigFiles.bat
```

`UpdateBuildDirConfigFiles.bat` copies `utils`, `web`, `Adventure`, `AIGuide`, `ComfyUI`, `Presets`, `aichat`, and local config files into `build/win`. `utils` includes runtime helper EXEs such as `RTClip` and the bundled FFmpeg/ffprobe helpers under `utils/ffmpeg/bin/`; those third-party FFmpeg binaries are copied as data and are not signed by the build scripts.

AI-harness note for running `BuildWin64.bat` from a tool shell: run it from the repo root, clear `NoDefaultCurrentDirectoryInExePath` first (Claude Code sets it to 1, which makes cmd's bare `call app_info_setup.bat` lines fail with "not recognized" even in the right directory), set `NO_PAUSE=1` so the final `pause` doesn't hang a background run, and disable sandboxing (the script needs the parent-folder `..\base_setup.bat`, registry queries to find Unity, and the `%RT_UTIL%` / `%RT_PROJECTS%` tool folders; local specifics live in `agents_secret.md`). Working invocation from PowerShell in the repo root: `Remove-Item Env:\NoDefaultCurrentDirectoryInExePath; $env:NO_PAUSE='1'; cmd /c .\BuildWin64.bat`. On failure the script pops notepad with `log.txt` and pauses; check `build\win\aitools_client.exe` exists to confirm success. Do not run it while the editor automation bridge is active (a second Unity instance fights the project lock).

There is no current root `BuildWebGL.bat`. WebGL build support is present in `Assets/RT/Editor/WebGLBuilder.cs`, and upload scripts exist (`UploadWebGLRSync*.bat`), but do not document or call a missing WebGL build script unless it is reintroduced.

For editor development, open `Assets/Main.unity` in Unity and enter Play mode.

For the Python CLI subset:

```bash
python cli/aitools_cli.py "<prompt>" output.png -p "Prompt To Image (Z-Image)"
```

On Windows, use `cli\aitools_cli.bat` instead - the first run creates `cli\venv\` and installs dependencies automatically.

The CLI uses `cli/config.txt`, `../ComfyUI`, and `../Presets`. Dependencies are `requests`, `websocket-client`, and `Pillow` (`cli/requirements.txt`). See `cli/README.md` before changing CLI behavior.

AI agents (Claude, Codex, etc.) can and should use this CLI themselves - on Windows or Linux - to generate images and verify workflow/preset changes end-to-end against the user's ComfyUI servers, e.g.:

```bat
cli\aitools_cli.bat "a cat" out.png -p "Prompt To Image (Z-Image)" -v
cli\aitools_cli.bat "make the sky red" out.png -p "Image To Image Klein Edit 1 Input" -i input.png
cli\aitools_cli.bat "she waves and says hi" out.mp4 -p "Image To Video (MiniMax H3) 5s" -i start.png --duration 8
cli\aitools_cli.bat "<Picture 1> dances like <Video 1>" out.mp4 -p "Reference Video To Video (MiniMax H3) 5s" --video clip.mp4 -i face.png
cli\aitools_cli.bat "test" out.mp4 -p "Prompt To Video (MiniMax H3) 5s" --width 1152 --height 640 --dry-run
```

Text-to-image, single-step image presets, and all four MiniMax H3 movie modes work (t2v / i2v start frame / reference photos / reference video+photos). Repeatable `-i`/`--video`/`--audio` reference inputs, `--width`/`--height`/`--duration` (H3 grid-snapped, unclamped), start-frame aspect auto-fit, ffprobe silent-clip pruning, `@upload|...|optional|` slot pruning, per-run `AITOOLS_UNIQUE_ID` substitution, `--dry-run` (writes `<output>.api.json` with zero server contact: use it to validate preset/workflow changes offline) and the `--alpha-key`/`--alpha-from-luma`/`--sprite-sheet` VFX post-processing are all documented in `cli/README.md` ("Generating movies (MiniMax H3)", "Transparent-background movies", "Preset support"). Multi-step chains and LLM presets error out by design.

## Current Architecture

### Core Unity App

- `Assets/_Script/GameLogic.cs` is the central UI/state coordinator. It owns prompts, negative prompts, generation parameters, selected renderer, preset/job-list text, temp image slots, global variables, and normal vs experiment mode.
- `Assets/_Script/Config.cs` loads `config.txt` and `config_cam.txt`, manages `GPUInfo` server entries, renderer selection, server busy state, per-server overrides, and the generated top-right server status rows.
- `Assets/_Script/ImageGenerator.cs` owns the global generation loop, GPU event queues, server selection, continuous generation, and throttling when job scripts start with LLM work before GPU work.
- `Assets/_Script/GUI/AppSettingsPanel.cs` is the unified Settings window (General, ComfyUI Settings = `AppSettingsTab.Configuration`, Audio, Web; the "LLM Settings" tab is a launcher that opens the standalone `LLMSettingsPanel` dialog). Its tabs write the modern subset of `config.txt` through `Config.BuildModernConfigText`, which REGENERATES the whole file: every parsed `set_*` / `add_server` key must be re-emitted there or Settings Apply wipes it (the Brave, STT and audio-gateway keys included). Tabs, keys, server priority order, forced reconnect and the compact/manual Tools panel controller: `docs/settings.md`.
- `Assets/_Script/PresetManager.cs` reads and writes `Presets/*.txt` files using `COMMAND_START|...COMMAND_END` blocks and `COMMAND_SET|...` lines.
- `Assets/_Script/VariableManager.cs` implements `%variable%` substitution for job scripts. Variables are local to a `PicMain` unless prefixed with `global_`.

### Per-Image Pipeline

- `Assets/_Script/Pic/PicMain.cs` is the main per-image controller. It stores textures, masks, temp images, job queues, job history, undo state, LLM manager references, and the job-script interpreter.
- `Assets/_Script/Pic/PicTextToImage.cs` loads ComfyUI workflow JSON, calls the ComfyUI workflow-to-API converter when needed, writes cached API JSON, applies `@replace` and placeholder substitution, sends `/prompt`, tracks progress, and writes debug JSON on errors.
- Every post-submit failure path in `PicTextToImage` must end in `FinishUpEverything(false)` and go through `PicMain.ReportRenderFailure` (frees the GPU, clears `m_waitingForPicJob`, tells the AI Chat model); while `/history` is empty the poll probes `/queue` and gives up on a lost job; a multi-input job line pins its ComfyUI server (`m_uploadPinnedServerID`) from the first upload through `run_workflow`. Details and history: `docs/workflows.md`.
- `Assets/_Script/Pic/PicGenerator.cs` handles continuous img2img/inpaint generation from an existing pic.
- Other `Pic/*` components are focused tools: mask editing, inpaint, upscale, interrogation, movie display, text-to-image status, info panels, size/menu nav, and target rectangles.

### Renderers and Servers

`RTRendererType` currently includes:
- `ComfyUI`
- legacy/compatibility values: `Any_Local`, `A1111`, `AI_Tools`, `AI_Tools_or_A1111`

The renderer dropdown currently exposes only `ComfyUI`. The legacy API image renderer path was removed; OpenAI LLM support is separate and still lives under the LLM provider stack. A1111 and AI Tools server support are legacy compatibility paths, not active primary targets.

ComfyUI servers must be reachable over HTTP and should be started with `--listen` or equivalent network binding. Current workflows are normal ComfyUI workflows in `ComfyUI/`; API-format cached files are generated beside them as `*_cached_api_version.json`.

### Runtime Helpers

- Bundled helper EXEs, copied with `utils` by `UpdateBuildDirConfigFiles.bat` and not signed: `utils/RTClip.*` (clipboard image/file helper; source in `utils/RTClip.zip`, with `Assets/RT/RTClipboardFileList.cs` reading real clipboard file lists in-process), `utils/yt-dlp/yt-dlp.exe` (behind `web_video`, `docs/web_media.md`) and `utils/ffmpeg/bin/ffmpeg.exe` + `ffprobe.exe` (GPL v3 with libx264: keep `utils/ffmpeg/NOTICE.txt` and `licenses/` beside them). Clipboard paste (Ctrl+V in the workspace and in AI Chat) accepts files and videos, not just bitmaps. `FfmpegTool`, `PicMovie` and the Media Foundation seek/playback rules (verify with `POST /movie_state`, never screenshots), preview proxies, clip import and the clip chooser: `docs/media_import.md`.

### Workflows, Presets, and Job Scripts

- `ComfyUI/*.json` contains the workflow files used by the app. There is no current `ComfyUI/FullWorkflowVersions/` folder.
- `Presets/*.txt` contains job scripts and default prompt settings. AutoPic presets are named `AutoPic*.txt` and are used by Adventure/AI-assisted flows.
- Placeholders `<AITOOLS_PROMPT>`, `<AITOOLS_NEGATIVE_PROMPT>`, `<AITOOLS_AUDIO_PROMPT>`, `<AITOOLS_SEGMENTATION_PROMPT>`, `<AITOOLS_INPUT_1..14>` (`PicJob.MAX_INPUT_SLOTS`), `<AITOOLS_PROMPT_1..8>` and the per-render `AITOOLS_UNIQUE_ID` token (put it in save-node `filename_prefix`es so concurrent renders can't collide); directives `@replace`, `@upload` (sources `image`/`image1..image10`, `temp1..3`, `video`/`video1`, `video2`, `audio1..3`, trailing `|optional|` prunes the loader when the source is missing), `@resize`, `@resize_if_larger`, `@copy`, `@add`, `@set`, `@setimage`, `@clear`, `@fill_mask_if_blank`, `@invert_alpha`, `@no_undo`, `@stopjob`, `@lock_gpu`, `@llm_*`, `@llm_add_image`, `@parse_llm_prompts`, `@prune_input`; `@start`/`@end` blocks; built-in variables such as `%video_fps%` / `%rife_output_fps%`. Full reference: `docs/workflows.md` and `cli/README.md` ("Preset support", the most complete human-facing list; the root `README.md` no longer documents directives). Verify against `PicMain.cs` when behavior matters.
- Two pitfalls every preset author must know (mechanism and history in `docs/workflows.md`): job-script variables are per-PicMain and SHARED by chained presets, so any preset that can be chained after another needs uniquely prefixed variables (`%vid_width%`, never `%width%`, which `Prompt To Image (Z-Image).txt` sets to 1024); and host dimension/frame-count overrides target the preset `@replace`'s REPLACEMENT half (raw `%var%="N"` or the compiled `command @set|%var%|N|` form), so PREFER a preset default equal to the workflow literal. Range-clamping model-supplied numbers is a forbidden gate (see AI Chat).
- Model-specific workflow notes (Bernini-R image/video edit, `Video Remove Background (BiRefNet)`, WAN/RIFE interpolation and the `rife_video` utility preset): `docs/workflows.md`. MiniMax H3 (AI Chat's default video route, FL2VA vs Ref2VA reference presets, turbo/cache variants, 9 photo + 2 clip + 3 audio reference slots, the official prompt format minus the `<d>` dialog markup, costs): `docs/minimax_h3.md`. Non-obvious constraints: the executor keys H3 reference behavior on preset-name substrings ("Reference Video To Video", "Reference To Video", "Reference To Image"), H3 has no negative-prompt path, `duration="N"` is UNCLAMPED on every H3 preset (nearest 17k+5 grid), and H3 models may be installed on only some servers (`agents_secret.md`).

### LLM Systems

- `Assets/_Script/LLM/LLMSettingsData.cs`, `LLMSettingsManager.cs`, and `LLMInstanceManager.cs` manage `config_llm.txt`, active providers, multiple LLM instances, replicas, model lists, context limits, and sampling/reasoning settings.
- Per-instance job routing is two orthogonal axes: `jobMode` (text-job size only: `Any`/`BigJobsOnly`/`SmallJobsOnly`) plus `supportsVision` (can accept image jobs) and `visionOnly` (reserve for vision - no text). Vision jobs route to `visionOnly` instances first, then any `supportsVision` instance. New OpenAI/Anthropic/Gemini instances default `supportsVision` on; new llama.cpp/Ollama/OpenAI-compatible instances default it off because local text servers often reject image input unless a real vision model/mmproj is loaded. `config_llm.txt` carries a `schemaVersion`; pre-decoupling configs (where vision was encoded in `jobMode` via the legacy `VisionJobsOnly`/`NonVisionOnly` values) are migrated once on load by `LLMInstanceManager.MigrateJobModes()`. The AI Chat caption sidecar warns in-chat when an image needs vision but no active instance has `supportsVision`, and surfaces backend failure details when a marked-vision instance rejects the request.
- LLM requests do not impose an optional output-token budget: OpenAI/OpenAI-compatible, llama.cpp, Ollama, and Gemini requests omit their output-limit field, including AI Chat sidecars, PicMain `call_llm`, Adventure, and AI Guide. Legacy llama.cpp/Ollama `max_tokens`, `max_new_tokens`, `max_output_tokens`, `max_completion_tokens`, `n_predict`, and `num_predict` provider parameters are ignored. Anthropic's Messages API requires `max_tokens`, so it receives the known per-model maximum. Model context/output ceilings and server-side generation configuration still apply.
- AI Chat main turns send `temperature` ONLY when the instance's sampling override is ticked (LLM Settings sampling section); otherwise the field is omitted so the server's model default applies, on every provider (Anthropic/Gemini builders take `includeTemperature`, the OpenAI/OpenAI-compatible builder already had it, llama.cpp/Ollama only ever read the `temperature` parm). Until 2026-09-05 AI Chat borrowed `AdventureLogic.GetExtractor().Temperature`, which is 0 (greedy) until an Adventure config is parsed in the session - so every AI Chat turn ran at temperature 0 unless Adventure had been opened. DeepSeek keeps its model-card values when not overridden; sidecars/one-shots keep their fixed 0.4. AI Guide, Adventure, and PicMain `call_llm` keep their own explicit temperature sources (their config files / job script).
- `LLMProvider` currently supports `OpenAI`, `Anthropic`, `LlamaCpp`, `Ollama`, `Gemini`, and `OpenAICompatible`.
- Request URL building (`LLMModelNotFound.BuildChatCompletionsUrl` / `JoinApiPath`: never append `/v1/chat/completions` to a base that already ends in a version segment), the stale-model auto-switch (`LLMModelAutoSwitch`, OpenAI Compatible only), the streaming download handlers (stateful UTF-8 decoder, newline-terminated SSE parsing) and the per-family reasoning/thinking wiring (DeepSeek-V4, GLM-5.3, Qwen Flash-Next, the Z.ai / DeepSeek hosted shapes): `docs/llm_providers.md`. Rules that hold everywhere: resolve thinking through `LLMRequestProfile.ResolveCompatReasoning`, never hand-roll per-site ternaries; adding a family = extend `IsXxxModel` + `HasConfigurableReasoningEffort` + the wire mapping in `LLMRequestProfile`, the branch in `OpenAITextCompletionManager.BuildChatCompleteJSON` (and `TexGenWebUITextCompletionManager` for llama.cpp), and `LLMProviderUI.RebuildReasoningEffortOptions`; `LLMReasoningEffort` is Off/Low/Medium/High/Max ("xhigh" parses to High) and a NEW OpenAI Compatible / llama.cpp instance starts at Off (GLM-5.3 clamps that to Low).
- `model_data.json` supplies shipped cloud model lists and default endpoints.
- `Assets/RT/AI/` contains provider/runtime managers: OpenAI text, Anthropic text, Gemini text, generic OpenAI-compatible/TextGen WebUI, ComfyUI file upload, speech-to-text, TTS, streaming download handlers, and main-thread dispatch.

### AI Chat

- `Assets/_Script/LLM/AIChatPanel.cs` builds the programmatic chat UI and routes chat requests through the same LLM instance/provider stack.
- Deep-dive: `docs/aichat.md` (attachments and drops, clip import, Main LLM override, Compact, skill autoload delivery, parser tolerance and deferral, thinking window, extract_still / rife_video / stitch_video, prompt-cache preservation, inspect_image, context menus, the `gpu=` soft hint, debugging log). Related: `docs/web_media.md` (the five web_* skills), `docs/audio_generation.md` (generate_* and set_video_audio), `docs/minimax_h3.md` (video routing and prompting), `docs/media_import.md` (clip chooser, playback).
- **Debugging AI Chat:** read `llm_aichat_log.json` (working dir; one chronological JSON array of `request` / `response` / `action` / `note` events, truncated per play session) FIRST when a chat output is wrong: it shows exactly which Pic each action targeted and what the model emitted. Provider-agnostic last request/response/error files are split by job size (`llm_last_request_sent_big.json` for AI Chat / AI Guide / Adventure, `..._small.json` for sidecars and `call_llm`). Details: `docs/aichat.md` "Debugging".
- Prompt-cache invariant: the stable prefix (pre/main/post prompt + skill summaries) must not change mid-conversation and NOTHING may `AddInteraction("system")` mid-chat; `BuildPromptChat` folds system-role lines into the FRONT message, which invalidates the server-side prompt cache for the whole history (a ~40 s re-prefill on long llama.cpp chats). Volatile state goes in the per-turn CURRENT STATE tail; runtime notes, autoloaded skill bodies and full captions travel in the info-recap tail of the next user message (`IChatHost.AddSystemInjectionAndBubble`); the only mid-conversation system line is the tagged compact summary. The exact text last sent per history line is cached on `GTPChatLine` and only a real user edit invalidates it.
- Error visibility: real backend/LLM failures on the main turn render as an always-visible red `Error` bubble (`AddErrorBubble` in `AIChatPanel.cs`) and cloud dispatch never silently substitutes a default model; sidecar failures and local notices are debug-gated `Info` bubbles ("Show debug stuff"). Every web fetch renders an always-visible "Web" trace bubble by design.
- Stop: `ShouldStopBeInteractable()` in `AIChatPanel.cs` is the single Stop-enable rule (streaming, forced-main wait, inspections and a pending inspect resume, pending auto-resumes, web work, audio generation, a running Compact, parked `stitch_video` / `set_video_audio` source waits); `SetBusyUI` must never compute its own, and every state listed has a cancel path in `OnStopClicked`. Continue turns requested by waits that outlive the reply (stitch, set_video_audio, audio generation) are dropped when a newer turn is current. Known gap: the video-import gate (`_videoImportCount`: clip imports, `set_video_audio`) blocks Send but has no Stop path.
- Web rules: model `url=` values must pass `WebMediaDownloader.IsAllowedPublicHttpUrl` (public http/https only; redirects followed by hand and re-gated; no raw quote/backslash/whitespace because the URL reaches yt-dlp's command line, which gets it after a `--` terminator); the Brave key lives in `config.txt` and MUST stay emitted by `Config.BuildModernConfigText`; the header "Web" checkbox gates all five web skills. Real people and named characters go through `web_image` + anchors, never rendered from memory; a voice sample in chat is used via the H3 `audio=` reference and `ref_voice` cloning is never offered as an alternative.
- Audio: an Audio bubble IS a Movie bubble (`IsChatImageMovie` is true for it; code that must tell video from sound checks `IChatHost.IsChatImageAudio`); the three `generate_*` skills are hidden from the prompt until a gateway URL is configured.
- Anchors and refs: a `chat_image*` / `source_chat_image` anchor name that doesn't resolve aborts the action immediately; chained actions carrying `anchor=` re-point the registry to the chained Pic; correction notes must name `chat_image` numbers, never attachment indexes; Movie-targeted `image_to_image` / `image_to_movie` requires explicit `movie_frame="true"`.
- `Assets/_Script/LLM/AIChat/Skills/SkillManager.cs` loads `aichat/skills/*.md` plus prompt files in `aichat/`.
- Do NOT add new deterministic executor gates that block actions over PROMPT-QUALITY judgments (missing quoted dialog, style rules, etc.) - Seth's explicit rule (2026-08-30): "they just cause pain later". Put such rules in skill text/frontmatter instead. The existing structural gates (reference tag mismatch, movie_frame, web preflight) stay; the bar for new ones is a structural/wiring error, not a prompt-writing one. Range-CLAMPING model-supplied numeric parameters counts as a forbidden gate too (2026-08-31: the H3 duration 124..362 clamp silently blocked 1s clips that render fine); pass values through with only structural math like grid snapping, and beware substring preset-name gates going stale when preset variants are added (the exact-"(MiniMax H3)" match missed the Quality/Turbo Cache names).
- Skill autoload keyword triggers miss real phrasings easily, so routing-critical rules must ALSO appear in each skill's `summary:`/`template:` frontmatter. `video_to_video` additionally has media-aware autoload: when the newest live chat medium is a Movie, deictic scene-edit and speech/audio-edit phrasing loads its full body even without the words video/clip/movie. Movie-targeted `image_to_image` / `image_to_movie` requires explicit `movie_frame="true"`; otherwise the executor blocks and auto-continues. Bernini v2v is silent, so generated/replaced dialogue/audio/sound is likewise blocked and redirected to H3 Ref2VA. When AI Chat's preset prefix is active (for example `test_`), unprefixed requested preset names first try the prefixed file and fall back to the bare production file only if no prefixed preset exists. Preset names then resolve case-insensitively, then by unique punctuation/spacing-only canonicalization, before the conservative fuzzy fallback emits a correction bubble.
- `ChatPicMirror.cs` mirrors generated pics into chat image bubbles so later actions can reference `chat_image="N"`.

### Automation Control Server (editor testing harness)

A loopback HTTP control server lets external tools (AI agents, scripts) drive the editor and AI Chat for automated end-to-end testing. It survives C# domain reloads and play stop/start, so an external loop can edit code, recompile, and re-test without anyone clicking Play.

**Prefer this automation bridge for Unity/editor validation whenever it is enabled.** Before launching a separate Unity batch/editor process, check `GET /status`; if the bridge is ready, use it to compile (`/rebuild`), drive the app (`/play`, `/stop`, `/settings`, `/llm_settings`, `/open_chat`, `/chat`, `/chat_import_video`, `/click`, `/scroll`, `/pic_cancel`, ...) and capture evidence (`/screenshot`, `/save`, `/chat_images`, `/movie_state`). For UI/AI Chat work, verify with screenshots whenever possible rather than relying only on code inspection. Do not start a second Unity editor/batchmode instance against this project while the editor lockfile/server is active unless the bridge is unavailable or the task specifically requires batchmode.

If a change cannot be validated because the bridge lacks a needed command, add a narrow loopback-only automation endpoint or driver capability in the same task when practical, then document the new endpoint in `docs/automation_bridge.md` (and keep the one-line pointer here current). Keep new automation commands deterministic, local-only, and scoped to testing/inspection; avoid broad privileged operations.

- Files: `Assets/_Script/Automation/AutomationBridge.cs` (runtime seam shared by editor and play-mode driver), `AutomationDriver.cs` (runtime MonoBehaviour; self-spawns in standalone builds with `-enable_automation`), `Editor/AutomationController.cs` (`[InitializeOnLoad]`, raw `TcpListener` on `http://127.0.0.1:8772/`, rebuild state machine persisted in `SessionState`). OFF by default: enable with **Tools > RT Automation > Enable Control Server** (EditorPrefs-persisted). Requests carrying an `Origin` header, or a `Host` that is not loopback, get 403 (browser / cross-site protection). The full endpoint catalogue with body keys, the `idle` semantics (it ANDs every AI Chat busy gate plus any chat-generated `PicMain` still rendering) and the async file-write notes: `docs/automation_bridge.md`. Recommended editor pref: **Script Changes While Playing = Stop Playing And Recompile**.

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

Do not assume every Unity job-script feature works in the CLI. Multi-step chains, temp slots, most LLM commands, and many Unity-only image operations deliberately error or are unsupported there. Movie generation (all four MiniMax H3 modes), repeatable `-i`/`--video`/`--audio` reference inputs, `--width`/`--height`/`--duration` overrides, start-frame aspect auto-fit, ffprobe silent-clip auto-pruning, per-run `AITOOLS_UNIQUE_ID` substitution, `--dry-run` offline validation, and a WS-drop `/history`-polling fallback for long renders are supported; `cli/README.md` "Generating movies (MiniMax H3)" is the reference.

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
- `docs/` - feature deep-dives referenced from this file (`aichat.md`, `automation_bridge.md`, `llm_providers.md`, `media_import.md`, `settings.md`, `workflows.md`, `minimax_h3.md`, `web_media.md`, `audio_generation.md`)
- `aichat/` - editable AI Chat prompts and skill markdown files
- `cli/` - Python CLI subset (Windows + Linux)
- `web/`, `utils/`, `Packaging/` - runtime/package support files, including bundled helper EXEs under `utils` (`RTClip`, `ffmpeg/bin`, `yt-dlp`)
- `Media/` - README screenshots and media

Generated or local-only folders include `Library/`, `Temp/`, `Logs/`, `build/`, `output/`, `UserSettings/`, `Images/`, `OldVersions/`, and `TestFiles/`.

## Development Notes

- Prefer existing Unity/C# patterns in the repo: MonoBehaviours, TextMeshPro UI, `UnityWebRequest`, `SimpleJSON`, and the existing RT utility classes.
- Unity 6000.6 / uGUI 2.6 treats `TMP_Text.enableWordWrapping` as a compile error (CS0619). Use `textWrappingMode = TextWrappingModes.Normal` or `TextWrappingModes.NoWrap`; do not retain legacy setters alongside the modern property.
- Programmatic UI is common in the LLM panels and AI Chat. Match the local style when extending those panels.
- Runtime-built `TMP_InputField`s (e.g. via `TMP_DefaultControls.CreateInputField`) run `OnEnable` before `textComponent` is wired, so TMP never creates the caret/selection renderer: typing works but the caret, mouse highlight, and wheel-scroll forwarding are dead. Call `TMPInputFieldCaretFix.Apply(input)` (`Assets/RT/TMPInputFieldCaretFix.cs`) after the field is fully wired AND parented; AI Chat's own `AIChatCaretFixer` already handles its fields.
- When adding or moving Unity assets, keep `.meta` files with them.
- Normal ComfyUI workflow files are the source of truth; cached API JSON files are generated artifacts and should generally not be hand-edited.
- Build scripts and packaging scripts are Windows-oriented and may delete/recreate build output directories.
- No reliable automated test command was found. If validation is needed, prefer focused C# compile/build checks or targeted Unity editor validation requested by the user.
