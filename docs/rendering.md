# Rendering and URP validation

## Configuration

The active app uses the editor-bundled Universal Render Pipeline 17.6.0 with Unity 6000.6.0f1. Shader Graph and render-pipeline core remain at 17.6.0. App version remains 3.06.

`Assets/Settings/Rendering/ForwardRenderer.asset` uses the Universal Renderer's Forward path with Render Graph. There are no renderer features or post-processing effects. Gamma color space, camera framing, transparent sorting and render scale 1 are retained. URP 17.6 runs Render Graph without the old compatibility-mode switch.

Graphics settings point to `Ultra.asset`. Each quality tier has an explicit pipeline asset, so neither build profile can fall back to Built-In by changing quality. The existing Windows default is Ultra. MSAA is off for Very Low/Low/Medium/High, 2x for Very High and 8x for Ultra. Existing texture, VSync, LOD and shadow quality choices remain in QualitySettings; corresponding URP shadow and light settings are copied into each pipeline asset. Both DevBuildProfile and ReleaseBuildProfile retain Gamma and inherit the project graphics/quality configuration.

## Active assets and text capture

- `DefaultPicPreFab` uses `SpriteUnlit.mat` for image, mask and outline sprites. It retains vertex tint, alpha blending and sprite ordering. `LineUnlit.mat` is a transparent, double-sided URP particles/unlit material for line renderers and the dynamically created selection frame. `PicMain.m_selectionFrameMaterial` keeps a serialized build dependency, avoiding `Shader.Find` and a new material allocation per selection.
- Movie and tiled-preview surfaces use URP/Unlit. Runtime `Material.mainTexture` assignment continues to address the shader's main texture. UI and TextMeshPro materials remain on their compatible existing shaders, including fallback fonts, caret/selection geometry and clipping.
- `RTUtil.RenderTextToTexture2D` keeps its overloads and synchronous `Texture2D` return. A disabled, isolated layer-31 camera submits `UniversalRenderPipeline.SingleCameraRequest` into an explicit RGBA render texture, with HDR, MSAA, post-processing and shadows disabled. Cleanup restores the caller's active render target, immediately disables temporary objects, and releases the target even on exceptions. The Built-In fallback remains available while the package is installed; other pipelines fail explicitly.
- Pipeline/global settings, renderer resources and serialized materials retain the shader dependencies required by Windows builds. Do not replace these references with runtime shader-name lookups without checking a player build.
- `DBufferClear.shader` replaces URP 17.6's always-loaded decal clear resource. Unity strips all of the packaged shader's variants when decals are disabled, leaving an unsupported shader in the player. The replacement retains a non-decal default variant with the same clear values; regular variant stripping stays enabled. No decal renderer feature is added. Recheck this workaround when updating URP.

Coverage includes workspace editing, AI Chat, image composition, video/audio previews, Adventure and AI Guide. Pizza and Shooting Gallery under inactive `OldStuff`, disabled CrazyCam, Breakout without an active startup path, and the unused outline package remain outside this migration. Their Built-In effects are not a supported URP path.

`POST /configure_urp` (stopped editor, clean scene) repeats the targeted setup implemented by `URPProjectSetup`. It writes rendering assets and updates the active picture prefab; it does not convert third-party samples or disabled experiments. Normal checkout/open/build uses the committed assets and does not need this command.

## Repeatable validation

Use the existing authorized loopback bridge. No desktop input or desktop captures are involved. The harness creates Unity Input System virtual input for painting and captures only the game view. Use a fresh, disposable Play session: it refuses to start if pictures already exist, but opens/closes panels and changes transient UI state. End Play after checking the report. Editor screenshots may contain local configuration displayed by the app, so keep artifacts private and ignored.

Generate the deterministic eight-second color/motion and tone fixture from the repository root:

```powershell
New-Item -ItemType Directory -Force build/render-validation
& ./utils/ffmpeg/bin/ffmpeg.exe -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=512x256:rate=24:duration=8' -f lavfi -i 'sine=frequency=440:sample_rate=48000:duration=8' -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest build/render-validation/fixture.mp4
Invoke-RestMethod -Method Post http://127.0.0.1:8772/play
Invoke-RestMethod -Method Post http://127.0.0.1:8772/render_validation -Body 'label=urp'
Invoke-RestMethod -Method Post http://127.0.0.1:8772/render_validation
```

Poll the final command until `state` is `passed` or `failed`. Editor PNGs and the report land in `build/render-validation/urp/`. The harness covers exact RGBA PNG saves, thumbnails, synchronous text with color/wrapping/alignment/auto-sizing, image labels, render-target restoration and camera cleanup, painting, checkerboard alpha, selection, zoom, overlap, menus, video playback/pause/seek/resume telemetry, clip previews, imported audio waveforms, chat selection and clipped fallback text, settings scrolling, Adventure and AI Guide. It also checks active material shader support and rendering errors.

Build the same checks in a standalone Windows player through the open editor:

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:8772/windows_render_validation -Body 'build'
Invoke-RestMethod -Method Post http://127.0.0.1:8772/windows_render_validation
```

The command stops Play, builds ReleaseBuildProfile into `build/urp-validation/`, copies runtime helper/data folders and the fixture, and writes an empty local server config. It does not sign, publish, touch `build/win`, or replace the release archive. It excludes local skill files and test-prefixed files from its runtime copy. The build report is `build/render-validation/windows-build.json` and reaches `built` when ready.

After permission to show the temporary validation-player window, launch it through the bridge:

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:8772/windows_render_validation -Body 'run-visible'
```

This launches only the fixed validation executable with `-render-validation`; no desktop clicks or keystrokes are used. A hidden/minimized window skips normal backbuffer rendering on Windows and gives black captures. Batch mode also stalls the end-of-frame capture coroutine, so neither is a valid substitute. Each capture now checks that it contains image content. The player closes itself when finished. `player-running` means inspect the player result next, not that acceptance is complete; `player-exited` includes its exit code. Player results are `build/urp-validation/build/render-validation/player/report.json` with game-view PNGs beside it and `player-validation.log` at the player root. Exit codes are 0 for pass, 1 for a failed check and 2 if fixture startup was refused. A standalone player does not expose an HTTP listener. Inspect startup shader errors in the player log as well as the fixture report, because those errors can precede the harness's log subscription.

Compare `saved-opaque.png`, `saved-alpha.png`, `text-left.png`, `text-auto.png`, `image-label.png` and `thumbnail.png` against the Built-In baseline using decoded RGBA pixels, including dimensions. Screenshots need visual/RGB comparison: URP fills the game-view background alpha differently from Built-In, while image-save alpha is preserved. Animated movie screenshots can capture different frames; verify progression and seeking from report telemetry as well.

## Migration evidence and rollback

Migration branch: `migrate-active-features-urp`. Starting commit: `bfb439fb8efea9c6fc79f8f1bc8ebfe1167c01c0`. Commit `3fe5069` added the initial fixture harness while still using Built-In. Local baseline artifacts are under `build/render-validation/builtin/`; they are not shipped or committed.

Editor validation passed all 54 checks on Direct3D 11/Gamma at 1587x738. All six saved-output comparisons above were exactly equal to the Built-In baseline, including text rasterization and alpha. The unsigned Windows build succeeded with zero errors. Full player visual acceptance still requires the visible-window run; the earlier hidden-window run was invalid because its game-view captures were black. Its offscreen saved-image/text comparisons did match the baseline, but this does not establish player acceptance.

The latest editor sample averaged 3.79 ms/frame versus the Built-In baseline's 2.72 ms/frame over 90 frames of the same three-picture workspace. Allocated editor memory was 1.59 GB versus 1.10 GB, after several imports/builds in the URP session. These are coarse observations, not a controlled benchmark: editor/package caches and background activity affect both, and the memory difference needs a fresh-process comparison before attributing it to runtime rendering. Player timing from the invalid hidden-window run must not be used as rendering-performance evidence.

Rollback must revert the complete migration commit set, newest first, including package manifest/lock, graphics/quality settings, pipeline assets, materials, prefab references, text helper and validation tooling. Review `git log --oneline bfb439fb..migrate-active-features-urp` to identify those commits before later work is added. Stop Play, preserve unrelated changes, revert only those migration commits, let Unity reimport, and rerun the relevant checks. Clearing only the Graphics pipeline reference is insufficient because quality overrides and URP materials remain active. Do not copy or commit generated Library data.
