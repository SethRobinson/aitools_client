# AGENTS.md

This file provides guidance to LLMs when working in this repository. Read it in full at the START of every task, before reading, searching, editing, or answering — it is the authoritative source of truth for this repo.

## Keeping this file current (self-maintenance)

**This file must stay accurate. Whenever a change you make invalidates something documented here, update AGENTS.md in the SAME task — do not leave it stale.** Treat it as part of the deliverable, not a follow-up.

Update this file when a change touches any of:
- Build/run commands, build scripts, or what they copy/produce (the "Essential Commands" section).
- App version, Unity version, main scene, or other "Current local facts".
- Architecture: renamed/added/removed core scripts, renderers (`RTRendererType`), LLM providers (`LLMProvider`), job-script directives/placeholders, AI Chat flow, or experiment folders.
- Hard rules, directory layout, or CLI capabilities/limits.

When the user asks to commit, or when you finish a task, do a quick self-check: "did anything I changed make a statement in AGENTS.md wrong?" If yes, fix it here (and keep `CLAUDE.md` consistent). If you are unsure whether a fact is still true, verify against the code rather than copying the old claim forward. Keep edits concise and factual — this file is read at the start of every task, so brevity matters.

## Project Overview

Seth's AI Tools is a Unity 6 Windows application that provides a native front-end for ComfyUI workflows, image/video generation, LLM-assisted workflows, AI chat, and several interactive experiments.

Current local facts:
- Unity editor version: `6000.4.6f1` (`ProjectSettings/ProjectVersion.txt`)
- App version in code/version metadata: `2.52`
- Main scene: `Assets/Main.unity`
- Primary platform: Windows desktop; a limited Python CLI (Windows + Linux) also exists under `cli/`

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

`GenerateBuildDate.bat` updates build-date data. `UpdateBuildDirConfigFiles.bat` copies `utils`, `web`, `Adventure`, `AIGuide`, `ComfyUI`, `Presets`, `aichat`, and local config files into `build/win`.

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
cli\aitools_cli.bat "make the sky red" out.png -p "Image To Image Klein Edit" -i input.png
```

Text-to-image and single-step image-to-image presets (`-i` / `-i2` inputs) both work; multi-step chains and LLM presets error out by design.

## Current Architecture

### Core Unity App

- `Assets/_Script/GameLogic.cs` is the central UI/state coordinator. It owns prompts, negative prompts, generation parameters, selected renderer, preset/job-list text, temp image slots, global variables, and normal vs experiment mode.
- `Assets/_Script/Config.cs` loads `config.txt` and `config_cam.txt`, manages `GPUInfo` server entries, renderer selection, server busy state, and per-server overrides.
- `Assets/_Script/ImageGenerator.cs` owns the global generation loop, GPU event queues, server selection, continuous generation, and throttling when job scripts start with LLM work before GPU work.
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

### Workflows, Presets, and Job Scripts

- `ComfyUI/*.json` contains the workflow files used by the app. There is no current `ComfyUI/FullWorkflowVersions/` folder.
- `Presets/*.txt` contains job scripts and default prompt settings. AutoPic presets are named `AutoPic*.txt` and are used by Adventure/AI-assisted flows.
- Workflow placeholders replaced by `PicTextToImage` include `<AITOOLS_PROMPT>`, `<AITOOLS_NEGATIVE_PROMPT>`, `<AITOOLS_AUDIO_PROMPT>`, `<AITOOLS_AUDIO_NEGATIVE_PROMPT>`, `<AITOOLS_SEGMENTATION_PROMPT>`, `<AITOOLS_INPUT_1>` through `<AITOOLS_INPUT_5>`, and `<AITOOLS_PROMPT_1>` through `<AITOOLS_PROMPT_8>`.
- Job scripts support workflow lines with `@` directives, `command` lines without a workflow, `call_llm`, `%var%="value"` assignments, and multi-line `@start`/`@end` or command blocks.
- Common directives include `@replace`, `@upload`, `@resize`, `@resize_if_larger`, `@copy`, `@add`, `@set`, `@setimage`, `@clear`, `@fill_mask_if_blank`, `@invert_alpha`, `@no_undo`, `@stopjob`, `@lock_gpu`, `@llm_*`, `@llm_add_image`, and `@parse_llm_prompts`.
- `README.md` has the most complete human-facing job-script reference. Verify against `PicMain.cs` when behavior matters.

### LLM Systems

- `Assets/_Script/LLM/LLMSettingsData.cs`, `LLMSettingsManager.cs`, and `LLMInstanceManager.cs` manage `config_llm.txt`, active providers, multiple LLM instances, replicas, model lists, context limits, and sampling/reasoning settings.
- Per-instance job routing is two orthogonal axes: `jobMode` (text-job size only: `Any`/`BigJobsOnly`/`SmallJobsOnly`) plus `supportsVision` (can accept image jobs) and `visionOnly` (reserve for vision — no text). Vision jobs route to `visionOnly` instances first, then any `supportsVision` instance. `config_llm.txt` carries a `schemaVersion`; pre-decoupling configs (where vision was encoded in `jobMode` via the legacy `VisionJobsOnly`/`NonVisionOnly` values) are migrated once on load by `LLMInstanceManager.MigrateJobModes()`. The AI Chat caption sidecar warns in-chat when an image needs vision but no active instance has `supportsVision`.
- `LLMProvider` currently supports `OpenAI`, `Anthropic`, `LlamaCpp`, `Ollama`, `Gemini`, and `OpenAICompatible`.
- `model_data.json` supplies shipped cloud model lists and default endpoints.
- `Assets/RT/AI/` contains provider/runtime managers: OpenAI text, Anthropic text, Gemini text, generic OpenAI-compatible/TextGen WebUI, ComfyUI file upload, speech-to-text, TTS, streaming download handlers, and main-thread dispatch.

### AI Chat

- `Assets/_Script/LLM/AIChatPanel.cs` builds the programmatic chat UI and routes chat requests through the same LLM instance/provider stack.
- `Assets/_Script/LLM/AIChat/Context/ChatContextBuilder.cs` builds the stable system prompt from `aichat/main_prompt.txt`, skill summaries, and action protocol instructions; the volatile per-turn state block adds GPU state plus compact chat-image provenance/captions.
- `Assets/_Script/LLM/AIChat/Skills/SkillManager.cs` loads `aichat/skills/*.md` plus prompt files in `aichat/`.
- `SkillActionParser` and `SkillActionExecutor` parse and execute `<aitools_action .../>` tags. Image/movie skills reuse `PicMain.RunPresetByName()` and the existing preset/job pipeline.
- `read_skill` loads a skill markdown body into the next LLM prompt and now requests one scoped synthetic `(continue)` turn automatically, so a mid-reply self-skill load can continue without the user clicking Send. Stop/Clear/new Send cancels the pending skill-load resume, and the injected reference text tells the model not to call `read_skill` again for that skill.
- `ChatPicMirror.cs` mirrors generated pics into chat image bubbles so later actions can reference `chat_image="N"`.
- The AI Chat action executor serializes skill actions and defers actions that need generated chat-image pixels, including `paste_image source_chat_image` and `inspect_image`'s implicit latest-image fallback, until the referenced Pic is readable and no longer rendering. This avoids composing or inspecting the black placeholder texture from a same-reply source image.
- AI Chat annotation-style local composition ops (`draw_text`, `draw_shape`, `add_border`) capture a clean pre-overlay PNG the first time they mutate a generated/composed Pic. The volatile CHAT IMAGES block marks `clean_base=available`, and later composition actions can pass `clean_base="true"` with `chat_image="N"` to rebuild revised text/bubbles from that pre-overlay base instead of drawing over baked-in old overlays. Layout-building ops like `paste_image` and `crop_resize` are treated as base assembly, so multi-panel comics keep pasted panels in the clean base.
- AI Chat local composition records same-turn `paste_image` and probable top-band title `draw_text` rectangles, then injects a layout warning if a full-width title overlaps or sits too close to pasted panels. This is a narrow deterministic guard for comic/page title bugs, not a general layout validator.
- AI Chat prompt-slimming settings default to lean mode: old executed `<aitools_action .../>` XML is kept in raw history/logs but stripped from future outgoing LLM requests, and generated images use compact provenance instead of automatic caption sidecar calls unless "Auto-caption generated images" is enabled. User-pasted attachments are still captioned once for context.
- User-pasted attachment captions are queued behind available vision-capable LLM capacity. Attachment thumbnails show `Queued` / `Captioning` / `Ready` / `No caption`, the footer status line shows live caption progress, and Send remains blocked until every attachment is either captioned or marked unavailable.
- AI Chat has an executable `inspect_image` skill for on-demand real vision inspection/captioning of an existing `chat_image`, current attachment, or the latest generated chat image when no explicit source is provided. Use it when the user asks the assistant to check its work or verify actual generated pixels; QA/layout inspections are prompted to return defect-first PASS/FAIL results. It shows the readable sidecar prompt in chat with image bytes elided, waits for generated-image pixels before dispatch, queues behind available vision-capable LLM capacity, shows footer progress, blocks Send/Continue until done, can be cancelled with Stop, and returns the result to chat/context. If the action includes `resume="true"`, AI Chat waits for all pending inspections and then sends one synthetic `(continue)` turn so the main assistant can answer from the result without the user clicking Send again. Its watchdog timeout is intentionally longer than attachment captions for slower local vision models.
- When `/applystyle` rewrites a render prompt, chat-image provenance keeps the rendered `prompt` and also records `pre_applystyle_prompt` so future chat turns can see the assistant's original requested image.
- The optional `gpu="N"` action attribute is a SOFT hint: it sets `PicMain.m_requestedServerID` with `m_requestedServerIsPreference=true`, so if that GPU is busy when the pic is ready, the scheduler falls back to any free GPU (PicMain.UpdateJobs) instead of waiting. Adventure's per-server render uses the same field with the flag left false (hard pin, waits for that exact server). The model is told in `main_prompt.txt` to normally omit `gpu`/`llm` and let the scheduler load-balance — hand-assigning GPUs for bulk requests caused movie batches to collide on a couple GPUs while others sat idle.

**Debugging AI Chat:** the editor appends the whole exchange to `llm_aichat_log.json` in the working dir (project root in-editor). It is one chronological JSON array of `request` / `response` / `action` / `note` events: the outgoing LLM body, the raw reply with its inline `<aitools_action .../>` tags, each parsed tool call about to run (so `generate_image` prompts and `draw_text` rect/font/`chat_image` are visible), and per-action notes (e.g. `draw_text` auto-fit results: chosen font size, canvas size, overflow). It is truncated at the start of each play session and deleted on exit. Read this FIRST when a poster/book page comes out wrong — it shows exactly which Pic each action targeted and what the model emitted. Provider-agnostic last request/response files are split by job size: `llm_last_request_sent_big.json` / `llm_last_response_sent_big.json` for AI Chat, AI Guide, and Adventure-style big jobs, and `llm_last_request_sent_small.json` / `llm_last_response_sent_small.json` for PicMain `call_llm` jobs and AI Chat sidecars. Errors are similarly split as `llm_last_error_big.json` / `llm_last_error_small.json`. Source: `Assets/RT/AI/AIChatLog.cs` and `Assets/RT/AI/LLMDebugLog.cs`.

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
- `servers.py`, `comfy_api.py`, `progress.py`, `images.py`, `util.py` - server probing, ComfyUI HTTP/WebSocket calls, image upload/resizing, output save

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
- `web/`, `utils/`, `Packaging/` - runtime/package support files
- `Media/` - README screenshots and media

Generated or local-only folders include `Library/`, `Temp/`, `Logs/`, `build/`, `output/`, `UserSettings/`, `Images/`, `OldVersions/`, and `TestFiles/`.

## Development Notes

- Prefer existing Unity/C# patterns in the repo: MonoBehaviours, TextMeshPro UI, `UnityWebRequest`, `SimpleJSON`, and the existing RT utility classes.
- Programmatic UI is common in the LLM panels and AI Chat. Match the local style when extending those panels.
- When adding or moving Unity assets, keep `.meta` files with them.
- Normal ComfyUI workflow files are the source of truth; cached API JSON files are generated artifacts and should generally not be hand-edited.
- Build scripts and packaging scripts are Windows-oriented and may delete/recreate build output directories.
- No reliable automated test command was found. If validation is needed, prefer focused C# compile/build checks or targeted Unity editor validation requested by the user.
