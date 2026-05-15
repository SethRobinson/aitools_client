# AGENTS.md

This file provides guidance to Codex when working in this repository.

## Project Overview

Seth's AI Tools is a Unity 6 Windows application that provides a native front-end for ComfyUI workflows, image/video generation, LLM-assisted workflows, AI chat, and several interactive experiments.

Current local facts:
- Unity editor version: `6000.4.6f1` (`ProjectSettings/ProjectVersion.txt`)
- App version in code/version metadata: `2.52`
- Main scene: `Assets/Main.unity`
- Primary platform: Windows desktop; a limited Linux Python CLI also exists under `linux/`

## Hard Rules

- Never automatically commit, push, or pull from git without explicit user directions.
- Do not read or edit files starting with `test_`, `Test_`, or `TEST_` unless the user explicitly names them or asks to work with test files.
- Treat ignored config and debug files as local/private unless the user explicitly asks for them. This includes `config.txt`, `config_llm.txt`, `config_preferences.txt`, `log.txt`, generated `*_json_*` files, `comfyui_workflow_to_send_api.json`, and cached ComfyUI API files.
- Do not revert unrelated work. This repo may already contain user edits in many files.

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

For the Linux CLI subset:

```bash
python linux/aitools_cli.py "<prompt>" output.png -p "Prompt To Image (Z-Image)"
```

The CLI uses `linux/config.txt`, `../ComfyUI`, and `../Presets`. Dependencies are `requests`, `websocket-client`, and `Pillow`. See `linux/README.md` before changing CLI behavior.

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
- `LLMProvider` currently supports `OpenAI`, `Anthropic`, `LlamaCpp`, `Ollama`, `Gemini`, and `OpenAICompatible`.
- `model_data.json` supplies shipped cloud model lists and default endpoints.
- `Assets/RT/AI/` contains provider/runtime managers: OpenAI text, Anthropic text, Gemini text, generic OpenAI-compatible/TextGen WebUI, ComfyUI file upload, speech-to-text, TTS, streaming download handlers, and main-thread dispatch.

### AI Chat

- `Assets/_Script/LLM/AIChatPanel.cs` builds the programmatic chat UI and routes chat requests through the same LLM instance/provider stack.
- `Assets/_Script/LLM/AIChat/Context/ChatContextBuilder.cs` builds the system prompt from `aichat/main_prompt.txt`, GPU state, chat image captions, skill summaries, and action protocol instructions.
- `Assets/_Script/LLM/AIChat/Skills/SkillManager.cs` loads `aichat/skills/*.md` plus prompt files in `aichat/`.
- `SkillActionParser` and `SkillActionExecutor` parse and execute `<aitools_action .../>` tags. Image/movie skills reuse `PicMain.RunPresetByName()` and the existing preset/job pipeline.
- `ChatPicMirror.cs` mirrors generated pics into chat image bubbles so later actions can reference `chat_image="N"`.

### Experiments

Experiment code lives in `Assets/Experiments/`:
- `Adventure/` - interactive story, quiz, Twine/HTML export, AutoPic flows
- `ShootingGallery/` - paintball/gallery target experiment
- `Pizza/` - pizza generator experiment
- `Breakout/` - Breakout experiment
- `CrazyCam/` - older camera/photobooth code; README notes CrazyCam is currently disabled/replaced in practice by SpookyCam-style presets

Prompt/template text for higher-level modes also exists in root `Adventure/` and `AIGuide/`.

### Linux CLI

The `linux/` folder is a separate Python command-line front-end for ComfyUI generation. It intentionally implements only a subset of the Unity app:
- `aitools_cli.py` - argparse entry point
- `config.py` - `linux/config.txt` parser
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
- `linux/` - Linux Python CLI subset
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
