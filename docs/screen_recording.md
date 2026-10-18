# Screen recording

The main Tools panel has **Paste** and **Record screen** beside one another in both
compact and advanced modes. Record screen or the global **Ctrl+Alt+F9** shortcut
opens a movable, always-on-top Windows toolbar. General Settings can change or
disable the shortcut; conflicts are reported without disabling the GUI button.

Select an area on any single monitor, then Record. A three-second countdown precedes
capture. Escape cancels selection/countdown; Stop, the same shortcut, or closing the
recording toolbar finishes the recording. The rectangle cannot cross the starting
monitor. Coordinates are physical pixels with per-monitor DPI awareness, including
monitors left of or above the primary. H.264 dimensions round down to even pixels.
The outline is blue when ready and red when recording; recorder controls and
outlines use Windows capture exclusion.

FPS choices are 16, 24, 30, 60, and 120, initially 30. System audio and cursor capture
default on. Audio uses the default playback endpoint chosen at recording start,
including all apps playing through it. There is no microphone input. That endpoint
stays fixed for the recording; disconnection stops it. No duration cap is imposed.
High FPS is a target, not a promise of distinct frames beyond the source, display
refresh rate or hardware throughput.

On completion AI Tools restores its window and opens `ChatVideoClipChooser` for every
recording, even below five seconds, with the whole clip selected. Captures and their
trimmed exports keep source resolution. Other callers retain the five-second default
selection and existing 832x480 export bound. Existing export-to-file/chat controls,
audio normalization preference, audio-only/still exports and multiple exports remain
available. Resizing is deferred to a separate feature.

## Implementation and lifetime

- `tools/ScreenCapture` contains the x64 .NET Framework 4.8 WinForms helper. It uses
  pinned ScreenRecorderLib 7.0.1, Desktop Duplication, H.264 quality-based encoding
  (quality 80), and stereo AAC system audio. Hardware encoding is preferred, with one
  software retry for initialization failure. The requested FPS is preserved. This
  library version includes its silent-WASAPI padding fix.
- `ScreenCaptureController` starts the helper from GameLogic and enables background
  Unity updates. `ScreenCaptureContract` is shared, Unity-free DTO/geometry/state
  code. Helper projects are outside Assets; narrow `.gitignore` exceptions retain
  their csproj files while bin/obj remain ignored.
- The helper has no console or visible UI until requested. Redirected UTF-8 JSON
  lines carry `configure`, `show`, `stop`, and `shutdown` commands. Events are `ready`,
  `settings`, `state`, `notice`, `error`, and `completed` (session ID plus absolute
  path). The ready handshake applies preferences before opening the toolbar. Unity
  marshals events through Update, rejects old-process events, validates completion
  paths, deduplicates session IDs, and queues recordings if its capture trim dialog
  is already open. Preferences use PlayerPrefs `screen_capture_settings_v1`.
- Startup arguments identify the parent PID/start time and fixed
  `tempCache/screen_recordings` directory. Recordings use `capture_<guid>.mp4` names.
  Paths reject junctions/symlinks. Sources remain for the session so closing a dialog
  during export cannot remove its input. Existing guarded tempCache cleanup owns
  their eventual deletion. The helper does not perform bulk deletes.
- The helper watches its parent and stdin. GameLogic shuts it down before cache
  cleanup; Unity play-mode teardown also disposes it. Stop finalizes before delivering
  a clip. Failures surface in the toolbar/app; nonempty partial outputs are probed
  before opening and otherwise retained in the cache for diagnosis.
- Requires Windows 10 version 2004 or newer, .NET Framework 4.8 and Media Foundation.
  Vendor CRT DLLs are app-local, with no installer. Windows N editions need their
  Media Feature Pack. Output is ordinary SDR MP4; color accuracy on HDR desktops
  has not yet been validated.

## Build and validation

`pwsh -File scripts/BuildScreenCapture.ps1 -Check` restores locked NuGet dependencies,
builds the helper, stages allowlisted runtime files under `utils/ScreenCapture`,
checks pinned third-party hashes, and runs a dependency-only `--check`. This mode
opens no window or capture device and registers no global shortcut. Vendored
Microsoft DLLs come from the official signed 14.44.35211 x64 redistributable, with
installer/file hashes in `dependencies.json`; do not harvest System32 DLLs.
Helper source and built runtime files are tracked, following the RTClip model.

`pwsh -File scripts/VerifyScreenCapture.ps1` runs geometry, shortcut, settings,
session lifecycle, stale/duplicate completion and path checks plus synthetic-only
FFmpeg audio/video fixtures at all five FPS values. It links production FfmpegTool
source with minimal Unity math/log stubs, checks native-resolution trimming, leading
silence and existing export defaults, and retains evidence below `build/capture-checks-*`.
It never captures the desktop or audio devices. `dotnet build Assembly-CSharp.csproj
--nologo -v:q` checks Unity C# compilation when Unity-generated projects include the
new assets.

Release builds call BuildScreenCapture after Unity builds and before staging,
sign AIToolsCapture.exe, and verify four first-party signatures. Vendor DLLs retain
their original bytes and licenses. See `release_packaging.md`.

Initial implementation validation (2026-10-18): helper and Unity C# compilation and
67 offline recorder checks passed. Live browser/system-audio capture, mixed-DPI UI,
hardware encoder fallback, device loss and a clean Windows machine remain manual
acceptance checks. Automated desktop capture or input requires Seth's explicit
authorization; compile and synthetic checks do not exercise those paths.
