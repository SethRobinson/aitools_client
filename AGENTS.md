# AGENTS.md

Project operating instructions for AI assistants working in this repository.

## Shared Project Memory

- Read this file at the start of every new task in this repo, before any other action. Follow-up replies in the same task do not need a reread unless this file changed or is no longer in context.
- This file is the shared memory for every AI tool (Claude, Codex, Cursor, ...). Do not keep project facts in vendor-specific or hidden memory systems.
- Also read `agents_local.md` if it exists: per-checkout/developer notes (machines, ssh targets). It is never checked in.
- Record durable repo knowledge here or in the linked doc: build/test commands, conventions, decisions, pitfalls, Seth's preferences. Fix or delete stale information when you find it, and label temporary or uncertain notes as such.

### Keeping docs current

- Scope: this file holds cross-cutting rules, workflows, gotchas and a feature index. Feature deep-dives live in `docs/<topic>.md` (index below).
- Before working on a feature, read its doc. When finishing, update that doc in the same change and keep the index entry here to one or two lines (where it lives + the non-obvious constraint).
- Size limit: no doc (this file included) over 25 KB; keep this file around 20 KB. When a doc grows past that, trim history first, then split by topic.
- Write current behavior and rules, not changelogs. Drop "until 2026-xx we did X" stories once they stop guiding work; git history keeps them.
- Update this file when a change touches build/run commands or what they produce, app/Unity version or main scene, core scripts / renderers (`RTRendererType`) / LLM providers (`LLMProvider`) / job-script directives / AI Chat flow / automation endpoints / experiment folders, hard rules, directory layout, or CLI capabilities.

## Hard Rules

- Never `git push` or `git pull` unless explicitly told to. "Commit" means commit locally only.
- Committing locally is allowed and expected (Seth, 2026-09-13): commit each self-contained change once it compiles, one logical change per commit with a descriptive message.
- Never add OpenAI/Codex/Claude or any AI as a co-author, and no AI-attribution lines in commits or PRs.
- Do not read or edit files starting with `test_`, `Test_` or `TEST_` unless the user names them or asks to work with test files.
- Treat ignored config and debug files as private unless asked: `config.txt`, `config_llm.txt`, `config_preferences.txt`, `log.txt`, generated `*_json_*` files, `comfyui_workflow_to_send_api.json`, cached ComfyUI API files.
- Do not revert unrelated work; the tree may contain user edits in many files.
- Writing style: no em-dashes or en-dashes used as dashes in any prose (docs, skill/prompt text, commits, chat replies). Use a colon, comma, parentheses, a spaced hyphen (house style) or two sentences.

## Testing

- Design automated tests for new features and bug fixes when possible, and run the relevant ones after changes.
- If tests cannot be run or do not exist, say so in the handoff and describe the manual verification done.
- C# compile check without the editor: `dotnet build Assembly-CSharp.csproj --nologo -v:q` (needs Unity-generated root `.csproj` files; does not validate assets, scenes or runtime).
- When the editor bridge is enabled, prefer it for compile and runtime validation (see "Automation Control Server").
- There is no app-wide test command. Existing suites: Input System EditMode tests (`docs/input.md`), offline scripts in `scripts/Verify*.ps1` (delete safety, release package, release smoke, screen capture, AI Chat actions/settings, web media), and the opt-in rendering fixture (`docs/rendering.md`).

## Security

- Never commit secrets: credentials, tokens, passwords, private keys, cookies, customer or personal data, machine-specific auth material. Not in commits, logs, issues, PRs or generated docs either.
- Local secrets and machine-specific notes for agents go in `agents_secret.md`, which must stay gitignored.
- Review staged changes for accidental secrets before committing.

## Safety

**Destructive deletes: fail closed.** Applies to scripts, app code, remote commands and agent commands (`rm -rf`, `Remove-Item -Recurse`, `rd /s`, `del` wildcards, `Directory.Delete(..., true)`, delete loops over wildcard matches).

- Never assume the working directory or pass an unchecked path variable. Anchor to the script location or the app's absolute root. Reject unset/empty/whitespace variables BEFORE building paths, and reject relative paths, roots, variable-supplied traversal and unexpected destinations.
- Keep a literal component at the delete call (`build\win`, `tempCache`, `${HOME:?}/www/${web_sub_dir:?}/Build`) and validate that the final absolute target is inside the intended directory. Quote paths and use literal-path APIs.
- Check ancestors and contents for symlinks/junctions before bulk deletes; refuse instead of following links. Wildcard cleanup uses a fixed validated directory and a narrow pattern, deleting checked individual files.
- A failed validation or directory change stops the operation: never continue to cleanup, packaging or upload. Agent-issued recursive deletes spell out a checked full literal target.
- Existing guarded helpers: `scripts/CleanBuildOutput.ps1` (`Reset` / `Package`), `scripts/CleanWebGL.sh` after `scripts/ValidateWebGLUpload.ps1`, and `Assets/RT/RTSafeFileSystem.cs` at runtime (fixed `tempCache` tree, `ytdlp_<8 hex>.*` files). Checks: `pwsh -File scripts/VerifyDeleteSafety.ps1` and `sh scripts/VerifyWebGLCleanup.sh` (Linux/WSL).

## Computer Control

- MCP operations within the requested task need no extra permission question (inspecting, creating, editing, validating or exporting an app's content through its MCP).
- Desktop screenshots and desktop control (Computer Use, UI automation, SendInput, clicking, typing) need express permission in the current request, including when wrapped by an MCP. Permission to "finish the task" or "proceed autonomously" does not imply it.
- Driving an app through its own scripting interface (for example Photoshop COM `DoJavaScript`) is always allowed.

## Project Overview

Seth's AI Tools is a Unity 6 Windows app: a native front-end for ComfyUI workflows, image/video generation, LLM-assisted workflows, AI chat, and several interactive experiments.

Current facts:
- Unity editor: `6000.6.8f1` (`ProjectSettings/ProjectVersion.txt`)
- App version: `3.08` (`Config.cs` `m_version`)
- Main scene: `Assets/Main.unity` (open it and enter Play mode for editor development)
- Rendering: URP 17.6.0, Forward/Render Graph, Gamma; Windows Ultra keeps 8x MSAA (`docs/rendering.md`)
- Platform: Windows desktop; a limited Python CLI (Windows + Linux) lives in `cli/`

### Bumping the app version

When asked to "bump the version", update ALL of these in the same task:
1. `Assets/_Script/Config.cs` `m_version` (e.g. `3.02f`).
2. `latest_version_checker.json` `latest_version`.
3. The "App version" line above.
4. `README.md`: the `# Download` line (version, current date, zip size in decimal MB with one decimal, from the freshly built `SethsAIToolsWindows.zip`) AND a new `### V<x.yz> (<date>)` entry at the top of `## Recent changes`, summarized from `git log <last-version-bump>..HEAD`.

### Releasing (upload + push)

Only when Seth asks. `UpdateChecker.cs` reads `latest_version_checker.json` from GitHub `main` and its Download button opens the fixed `https://www.rtsoft.com/files/SethsAIToolsWindows.zip`, so upload BEFORE pushing a version bump.
1. Build with `BuildWin64.bat` and confirm the fresh-ZIP smoke PASS. Revert the rebuilt `utils/ScreenCapture/AIToolsCapture.exe` with `git checkout --` (see `docs/release_packaging.md`).
2. Upload: `scp -o BatchMode=yes SethsAIToolsWindows.zip rtsoft@rtsoft.com:www/files/SethsAIToolsWindows.zip` (passwordless from Seth's PC). Avoid `UpdateFilesRemotely.bat` from a tool shell: it needs `base_setup.bat` env and ends in `pause`.
3. Verify `curl -sI https://www.rtsoft.com/files/SethsAIToolsWindows.zip` Content-Length equals the local zip size.
4. `git push origin main`, then confirm raw GitHub `latest_version_checker.json` shows the new number.

## Essential Commands

- Windows release: `BuildWin64.bat` (from a tool shell: `$env:NO_PAUSE='1'; cmd /c .\BuildWin64.bat`). With the editor already open and the bridge enabled (ReleaseBuildProfile active, Play stopped, scenes saved) use `BuildWin64.bat --editor`, which builds through `POST /windows_release_build` instead of a second editor. Every build ends with the fresh-ZIP smoke check (`scripts/SmokeReleaseZip.ps1`, rerunnable alone). Flow, staging rules, retired presets, smoke details and gotchas: `docs/release_packaging.md`.
- Release staging alone: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\ReleasePackage.ps1 -Phase Stage`. It copies only git-tracked release inputs (new runtime files must be `git add`ed first); private configs, caches, test/local files and `Media/` are excluded.
- `UpdateBuildDirConfigFiles.bat` is a gitignored local dev helper that intentionally copies private configs into `build\win`. Never commit it, never make the build depend on it, do not "fix" it.
- There is no root `BuildWebGL.bat`; do not call one.
- Release checks (offline, disposable fixtures, never upload): `pwsh -File scripts/VerifyDeleteSafety.ps1`, `pwsh -File scripts/VerifyReleasePackage.ps1`, `pwsh -File scripts/VerifyReleaseSmoke.ps1`, `scripts/ReleasePackage.ps1 -Phase VerifyArchive`, and `wsl -d Ubuntu-24.04 -- sh /mnt/f/Unity/aitools_client/scripts/VerifyWebGLCleanup.sh`.

### Python CLI

AI agents can and should use the CLI (Windows or Linux) to generate images and verify workflow/preset changes end-to-end against the ComfyUI servers. On Windows use `cli\aitools_cli.bat` (first run creates `cli\venv\`); elsewhere `python cli/aitools_cli.py`.

```bat
cli\aitools_cli.bat "a cat" out.png -p "Prompt To Image (Qwen Image 2.1)" -v
cli\aitools_cli.bat "put the woman from <image2> into <image1>" out.png -p "Image To Image (Qwen Image 2.1)" -i scene.png -i person.png
cli\aitools_cli.bat "she waves and says hi" out.mp4 -p "Image To Video (MiniMax H3) 5s" -i start.png --duration 8
cli\aitools_cli.bat "test" out.mp4 -p "Prompt To Video (MiniMax H3) 5s" --width 1152 --height 640 --dry-run
```

- `--dry-run` writes `<output>.api.json` with zero server contact: use it to validate preset/workflow changes offline.
- Supported: text-to-image, single-step image presets, all four MiniMax H3 movie modes, repeatable `-i` / `--video` / `--audio`, `--width` / `--height` / `--duration`, VFX alpha post-processing. Multi-step chains, temp slots and most LLM commands error out by design.
- Uses `cli/config.txt`, `../ComfyUI`, `../Presets`. Read `cli/README.md` before changing CLI behavior ("Preset support", "Generating movies (MiniMax H3)", "Transparent-background movies").

## Current Architecture

### Core app

- `Assets/_Script/GameLogic.cs`: central UI/state coordinator (prompts, generation parameters, renderer, preset/job-list text, temp image slots, global variables, normal vs experiment mode).
- `Assets/_Script/Config.cs`: loads `config.txt` / `config_cam.txt`, `GPUInfo` server entries, server busy state and per-server overrides, top-right server status rows.
- `Assets/_Script/ImageGenerator.cs`: global generation loop, GPU event queues, server selection, continuous generation, LLM-before-GPU throttling.
- `Assets/_Script/GUI/AppSettingsPanel.cs`: unified Settings window. **`Config.BuildModernConfigText` REGENERATES the whole `config.txt`**: every parsed `set_*` / `add_server` key (Brave, STT, audio gateway, yt-dlp cookies included) must be re-emitted there or Settings Apply wipes it. Tabs, keys, server order, Tools panel modes: `docs/settings.md`.
- `Assets/_Script/PresetManager.cs`: `Presets/*.txt` with `COMMAND_START|...COMMAND_END` blocks and `COMMAND_SET|...` lines. `VariableManager.cs`: `%variable%` substitution (per-PicMain unless prefixed `global_`).
- Rendering: assets in `Assets/Settings/Rendering/`, all quality levels select URP; keep image/movie surfaces unlit and post-processing off. `RTUtil.RenderTextToTexture2D` must stay synchronous (`SingleCameraRequest`). `docs/rendering.md`.
- Input: Input System 1.20.1 only (`activeInputHandler: 1` in project settings AND both build profiles). Use `RTInput` / `RTInputManager` (`Assets/RT/Input/`, `RT.Input` assembly); no legacy `UnityEngine.Input` reads; do not divide wheel values by 120. `docs/input.md`.

### Per-image pipeline

- `Assets/_Script/Pic/PicMain.cs`: per-image controller (textures, masks, temp images, job queue/history, undo, LLM refs, job-script interpreter).
- `PicTextToImage.cs`: loads workflow JSON, converts to API format when needed (cached as `*_cached_api_version.json`), applies `@replace`/placeholders, submits `/prompt`, tracks progress. Every post-submit failure must end in `FinishUpEverything(false)` via `PicMain.ReportRenderFailure`; multi-input jobs pin their server from first upload through `run_workflow`. `docs/workflows.md`.
- `PicGenerator.cs`: continuous img2img/inpaint. Other `Pic/*` components are focused tools (mask, inpaint, upscale, interrogation, movie display, info panels).

### Renderers and servers

- `RTRendererType`: `ComfyUI` is the only one exposed; `Any_Local`, `A1111`, `AI_Tools`, `AI_Tools_or_A1111` are legacy compatibility values.
- ComfyUI servers must be reachable over HTTP (start with `--listen`). Server and machine specifics: `agents_secret.md`.

### Workflows, presets and job scripts

- `ComfyUI/*.json` are normal workflows (source of truth); cached API JSON is generated, do not hand-edit. `Presets/*.txt` are job scripts; `AutoPic*.txt` serve Adventure/AI flows.
- Placeholders (`<AITOOLS_PROMPT>`, `<AITOOLS_INPUT_1..14>`, `AITOOLS_UNIQUE_ID` for save-node prefixes, ...) and directives (`@replace`, `@upload` with `|optional|`, `@llm_*`, `@start`/`@end`, ...): `docs/workflows.md` and `cli/README.md` "Preset support". Verify against `PicMain.cs` when behavior matters.
- Preset pitfalls: job-script variables are per-PicMain and SHARED by chained presets, so chainable presets need uniquely prefixed variables (`%vid_width%`, never `%width%`); host dimension/frame overrides target the `@replace` REPLACEMENT half, so prefer preset defaults equal to the workflow literal.
- Default models: MOSS-Image is AI Chat's text-to-image default (`Prompt To Image (MOSS-Image)`, Turbo for fast/draft; no image input). Qwen-Image 2.1 is the edit default (`Image To Image (Qwen Image 2.1)`, 1-10 inputs, `<imageN>` tags) and the main GUI text-to-image fallback. Others run only when named. `docs/workflows.md`.
- MiniMax H3 is AI Chat's default video route: `docs/minimax_h3.md`. H3 reference behavior is keyed on preset-name substrings, it has no negative prompt, `duration` is unclamped, and its models may exist on only some servers.

### LLM systems

- Settings/instances: `Assets/_Script/LLM/` (`LLMSettingsData.cs`, `LLMSettingsManager.cs`, `LLMInstanceManager.cs`), providers in `Assets/RT/AI/`. `LLMProvider`: `OpenAI`, `Anthropic`, `LlamaCpp`, `Ollama`, `Gemini`, `OpenAICompatible`.
- Rules: no optional output-token budget is ever sent (Anthropic gets the per-model max); AI Chat sends `temperature` only when the instance override is ticked; resolve thinking through `LLMRequestProfile.ResolveCompatReasoning`; mirror `model_data.json` changes in `LLMModelData.CreateDefault`. Routing axes, URL building, model auto-switch, per-family reasoning wiring, adding a model family: `docs/llm_providers.md`.

### AI Chat

`Assets/_Script/LLM/AIChatPanel.cs` (UI + turn flow), `AIChat/Skills/SkillManager.cs` (loads `aichat/skills/*.md` and prompt files in `aichat/`), `ChatPicMirror.cs` (mirrors pics into `chat_image="N"` bubbles). Deep-dive: `docs/aichat.md` and the docs it indexes. Rules every AI Chat change must respect:

- **Debug from the log first:** `llm_aichat_log.json` (working dir, chronological `request` / `response` / `action` / `note` events per play session) shows which Pic each action targeted and what the model emitted.
- **Prompt-cache invariant:** the stable prefix never changes mid-conversation and nothing may `AddInteraction("system")` mid-chat. Volatile state goes in the per-turn CURRENT STATE tail; runtime notes travel in the next user message (`IChatHost.AddSystemInjectionAndBubble`).
- **No new prompt-quality gates** (Seth, 2026-08-30): do not add executor checks that block actions over prompt-writing judgments, and do not range-clamp model-supplied numbers (structural math like grid snapping is fine). Put such guidance in skill text/frontmatter. Structural/wiring gates are allowed.
- **Skill routing:** autoload keywords miss phrasings, so routing-critical rules must also be in each skill's `summary:` / `template:` frontmatter.
- **Stop:** `ShouldStopBeInteractable()` is the single Stop-enable rule; every busy state it lists needs a cancel path in `OnStopClicked`.
- **Errors:** real main-turn failures show an always-visible red Error bubble, and cloud dispatch never silently substitutes a model.
- **Web:** model URLs must pass `WebMediaDownloader.IsAllowedPublicHttpUrl`; the header "Web" checkbox gates all web skills; real people and named characters go through `web_image` + anchors. `docs/web_media.md`.
- **Video:** MiniMax H3 is the default video route; a supplied reference video stays the original clip plus audio via Reference Video To Video (stills only supplement it). `docs/aichat_media.md`, `docs/minimax_h3.md`.

### Automation Control Server (editor testing harness)

Loopback HTTP server on `http://127.0.0.1:8772/` that drives the editor and AI Chat for end-to-end testing; it survives domain reloads and play stop/start. OFF by default: **Tools > RT Automation > Enable Control Server**. Standalone builds enable it with `-enable_automation`.

- **Prefer it for Unity validation whenever enabled.** Check `GET /status` first; then compile (`/rebuild`), drive (`/play`, `/stop`, `/settings`, `/open_chat`, `/chat`, `/click`, ...) and capture evidence (`/screenshot`, `/chat_images`, `/movie_state`, ...). Verify UI/AI Chat work with screenshots when possible. Do not start a second Unity instance against the project while the bridge is up unless the task requires batchmode.
- If validation needs a missing command, add a narrow loopback-only, deterministic endpoint in the same task and document it in `docs/automation_bridge.md` (full endpoint catalogue, `idle` semantics, security rules).
- Recommended editor pref: Script Changes While Playing = Stop Playing And Recompile.

### Runtime helpers

- Bundled in `utils/`: `RTClip.*` (clipboard helper, signed), `ffmpeg/bin/ffmpeg.exe` + `ffprobe.exe` (GPL v3: keep `NOTICE.txt` and `licenses/` beside them), `yt-dlp/yt-dlp.exe`, `ScreenCapture/` (signed AIToolsCapture). Vendor binaries keep their original bytes.
- Screen recording and the floating clock: `ScreenCaptureController` + the helper in `tools/ScreenCapture` (`docs/screen_recording.md`). Live desktop capture needs explicit authorization; offline check: `pwsh -File scripts/VerifyScreenCapture.ps1`.
- Clipboard paste, FFmpeg/`PicMovie` playback (verify with `POST /movie_state`, never screenshots), clip import: `docs/media_import.md`.

### Experiments

`Assets/Experiments/`: `Adventure/` (story, quiz, Twine/HTML export, AutoPic), `ShootingGallery/`, `Pizza/`, `Breakout/`, `CrazyCam/` (older, effectively replaced by SpookyCam-style presets). Prompt/template text for these modes also lives in root `Adventure/` and `AIGuide/`.

## Doc Index (`docs/`)

| Doc | Covers |
|---|---|
| `aichat.md` | AI Chat core rules, attachments, parser, bubbles, Compact, skills, prompt cache, debugging |
| `aichat_media.md` | AI Chat model/video routing, execution order, `gpu` hint, extract_still / rife_video / stitch_video, inspect_image |
| `web_media.md` | web_* skills index, hard rules (URL gate), image ranking, trace bubble format |
| `web_video.md` | yt-dlp, range downloads, video verification, speech check, transcripts, cookies / YouTube Firefox login |
| `web_page.md` | web_page reader and web_audio |
| `minimax_h3.md` | H3 models, presets, reference slots, executor wiring, troubleshooting |
| `minimax_h3_prompting.md` | H3 prompt format, dialog rules, voice references |
| `audio_generation.md` | generate_* audio skills, Audio bubbles, set_video_audio |
| `workflows.md` | placeholders, directives, preset pitfalls, per-model workflow notes |
| `llm_providers.md` | LLM routing, request params, URL building, reasoning families, streaming |
| `settings.md` | Settings window tabs/keys, Tools panel modes |
| `media_import.md` | clipboard, FFmpeg, movie playback, clip chooser |
| `automation_bridge.md` | control server endpoints and checks |
| `screen_recording.md` | recorder helper, HDR, floating clock |
| `release_packaging.md` | build flow, staging, smoke check, release gotchas |
| `rendering.md` | URP config, text capture, render validation |
| `input.md` | Input System architecture and tests |

## Directory Map

- `Assets/_Script/` main app (`Pic/` per-image pipeline, `GUI/` panels, `LLM/` LLM + AI Chat, `Automation/` control server)
- `Assets/RT/` reusable toolkit, editor builders, `AI/` provider managers
- `Assets/Experiments/` experiment modes; `Assets/Settings/Build Profiles/` build profiles
- `ComfyUI/` workflows (+ generated API caches); `Presets/` job scripts; `aichat/` AI Chat prompts and skill markdown (runtime content, shipped)
- `cli/` Python CLI; `tools/ScreenCapture/` recorder source; `utils/`, `web/`, `Packaging/` runtime/package support; `scripts/` build and verify scripts; `docs/` deep-dives; `Media/` README images
- Generated/local only: `Library/`, `Temp/`, `Logs/`, `build/`, `output/`, `UserSettings/`, `Images/`, `OldVersions/`, `TestFiles/`

## Development Notes

- Follow existing patterns: MonoBehaviours, TextMeshPro UI, `UnityWebRequest`, `SimpleJSON`, RT utility classes. Programmatic UI is common in LLM panels and AI Chat; match local style.
- Unity 6000.6 / uGUI 2.6: `TMP_Text.enableWordWrapping` is a compile error (CS0619). Use `textWrappingMode = TextWrappingModes.Normal` / `NoWrap`.
- Runtime-built `TMP_InputField`s get no caret/selection/wheel forwarding because `OnEnable` runs before `textComponent` is wired: call `TMPInputFieldCaretFix.Apply(input)` after the field is wired and parented (AI Chat's `AIChatCaretFixer` already does this).
- Keep `.meta` files with added or moved assets.
- Build and packaging scripts are Windows-oriented and recreate build output directories.
