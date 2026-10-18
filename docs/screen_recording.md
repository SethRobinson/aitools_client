# Screen recording

The main Tools panel has **Paste** and **Record screen** beside one another in both
compact and advanced modes. Record screen or the global **Ctrl+Alt+F9** shortcut
opens a movable, always-on-top Windows toolbar. General Settings can change or
disable the shortcut; conflicts are reported without disabling the GUI button.

Select an area on any single monitor, then Record. Toolbar **Settings** remembers
the countdown in seconds (default 3, including 0 for immediate capture and fractions
such as 0.3). Escape cancels selection/countdown; Stop, the same shortcut, or closing the
recording toolbar finishes the recording. The rectangle cannot cross the starting
monitor. Coordinates are physical pixels with per-monitor DPI awareness, including
monitors left of or above the primary. H.264 dimensions round down to even pixels.
The outline is blue when ready and red when recording; recorder controls and
outlines use Windows capture exclusion.

While selecting, only the area outside the rectangle is dimmed. The helper renders
an alpha bitmap before showing each selection window, without an initial opaque
paint or a desktop screenshot. The dragged interior is fully transparent; a fixed
box awaiting a click uses 1/255 black alpha to retain Windows mouse hit testing.
Capture exclusion is set when window handles are created, before they are shown.
The surface uses Windows [per-pixel layered windows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow).

The toolbar's **Size** dropdown selects **Custom** or **Fixed**. Custom starts with a
selection: drag its sides or its coordinate label to move it, and its corner
handles to resize it. In Custom mode, **Aspect ratio** remembers **Any** (unlocked),
1:1, 16:9, 9:16, 4:3, 3:4, 3:2, 2:3, 4:5, 5:4, 16:10, 10:16, 21:9, 9:21, 32:9,
1.85:1, 2.35:1 or 2.39:1. The lock applies while drawing and resizing, including at
monitor edges, and changing it adjusts the existing selection immediately. Corner
resizing anchors the opposite corner. H.264 rounding can change the ideal shorter
dimension by up to one physical pixel. Any restores free sizing; Fixed ignores the
remembered Custom ratio. Fixed shows remembered width/height inputs and places a box
of exactly that size; all handles move it. Oversize fixed boxes cannot be placed on
a smaller monitor. Both modes stay on the selected monitor. Dimensions are even
physical pixels. The label shows width, height and desktop X/Y beside the bottom
right, moving inside only when the monitor edge leaves no room outside. It remains
visible during capture and is excluded from the video. Position and size lock at
countdown start; the outline passes clicks through during capture.

FPS is editable: type any positive whole-number target, or choose the 16, 24, 30,
60 and 120 presets (initially 30). Custom values are remembered. Invalid text
disables Record and explains the required format; it never silently falls back to
a preset. ScreenRecorderLib's frame-rate API is integer-only. System audio and cursor capture
default on. Audio uses the default playback endpoint chosen at recording start,
including all apps playing through it. There is no microphone input. That endpoint
stays fixed for the recording; disconnection stops it. No duration cap is imposed.
High FPS is a target, not a promise of distinct frames beyond the source, display
refresh rate or hardware throughput.

On completion AI Tools restores its window and opens `ChatVideoClipChooser` for every
recording, even below five seconds, with the whole clip selected. Captures and their
trimmed exports keep source resolution and frame rate, including custom rates above
120 FPS. Other callers retain the five-second default
selection and existing 832x480 export bound. Existing export-to-file/chat controls,
audio normalization preference, audio-only/still exports and multiple exports remain
available. Resizing is deferred to a separate feature.

## Implementation and lifetime

- `tools/ScreenCapture` contains the x64 .NET Framework 4.8 WinForms helper. It uses
  pinned ScreenRecorderLib 7.0.1 with the AI Tools HDR patch, Desktop Duplication, H.264 quality-based encoding
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
- Recorder creation, default audio-device lookup, device polling, Stop and native
  disposal run on one serial MTA worker, never the toolbar thread. Starting shows
  live elapsed preparation time and allows Stop, including cancellation before
  capture begins. Completion waits for native cleanup, with stale/duplicate events
  ignored. Driver/encoder initialization can still take time; `diagnostic` events
  log device lookup, recorder creation and first-frame timings in Unity's log.
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
  requires the bundled patched DLL described below.

## HDR conversion

`tools/ScreenCapture/Hdr/ScreenRecorderLib.patch` applies to upstream **v7.0.1**,
commit `646383234187efd3854e6b1069612b80c3ab9981`. HDR monitors use
`IDXGIOutput5::DuplicateOutput1` with floating-point scRGB support instead of the
old mandatory 8-bit BGRA conversion. `HdrToneMapper.h` uses a GPU shader to divide
by that monitor's Windows SDR-white multiplier, preserve midtones, smoothly compress
highlights, and encode sRGB into the existing BGRA/H.264 pipeline. HDR float values
are converted before cropping/encoding, while SDR capture follows the original path.
It does not toggle Windows HDR, change monitor settings, or apply a brightness
filter to already-clipped video. Previously recorded clips cannot recover lost detail.

The shader uses the capture device and restores the existing renderer's state.
Textures are reused between frames. HDR conversion/setup errors propagate through
the existing recording-failure path. SDR white is resolved at capture initialization;
restart recording after changing Windows' HDR/SDR brightness setting.

API references: [DuplicateOutput1](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_5/nf-dxgi1_5-idxgioutput5-duplicateoutput1),
[SDR white level](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-displayconfig_sdr_white_level).

## Build and validation

`pwsh -File scripts/BuildScreenCapture.ps1 -Check` restores locked NuGet dependencies,
builds the helper, stages allowlisted runtime files under `utils/ScreenCapture`,
checks pinned third-party hashes, and runs a dependency-only `--check`. This mode
opens no window or capture device and registers no global shortcut. Vendored
Microsoft DLLs come from the official signed 14.44.35211 x64 redistributable, with
installer/file hashes in `dependencies.json`; do not harvest System32 DLLs.
Helper source and built runtime files are tracked, following the RTClip model.
Staging skips identical files so loaded, unchanged DLLs do not prevent a helper
update. A changed executable can still be locked by a running helper; normally
close AI Tools or exit Unity Play mode before rebuilding it.
The NuGet package supplies the pinned API reference and original license; the
helper's build output uses the patched DLL from `utils/ScreenCapture`. Patch/header
hash checks reject stale native builds. `.gitattributes` fixes their line endings
so source hashes remain stable between checkouts.

To rebuild the native patch, run `pwsh -File scripts/BuildScreenCaptureHdr.ps1 -Stage`,
then `pwsh -File scripts/BuildScreenCapture.ps1 -Check`. The native script verifies
and extracts the pinned upstream archive into `build/capture-native`, applies the
patch, builds with Visual Studio's **v143 14.44.35207** C++/CLI tools and Windows SDK
**10.0.26100.0**, runs synthetic WARP GPU checks, and stages the DLL with updated
source/binary hashes in `dependencies.json`. Without `-Stage` it only builds/checks.
It performs no desktop capture, display enumeration, audio recording or UI input.
Normal release builds use the checked-in patched DLL and only require the .NET SDK.

`pwsh -File scripts/VerifyScreenCapture.ps1` runs geometry, shortcut, settings,
session lifecycle, worker ordering/cancellation, stale/duplicate completion and path checks plus synthetic-only
FFmpeg audio/video fixtures at all five preset FPS values and custom 25/50/144 FPS. It links production FfmpegTool
source with minimal Unity math/log stubs, checks native-resolution trimming, leading
silence and existing export defaults, and retains evidence below `build/capture-checks-*`.
It never captures the desktop or audio devices. `dotnet build Assembly-CSharp.csproj
--nologo -v:q` checks Unity C# compilation when Unity-generated projects include the
new assets.
The separate .NET Framework `Checks/Overlay` program renders only synthetic memory
bitmaps to check transparent interiors, outside dimming, moving holes and negative
monitor origins. It opens no windows and reads no desktop pixels.

Release builds call BuildScreenCapture after Unity builds and before staging,
sign AIToolsCapture.exe, and verify four first-party signatures. Vendor DLLs retain
their licenses; the VC runtime retains its original bytes. See `release_packaging.md`.

Validation (2026-10-18): helper/native and Unity C# compilation and
2404 offline recorder checks and 18 synthetic overlay checks passed, including all aspect-ratio choices, all four
drag/resize directions, monitor-edge clamping, stable pixel rounding, preference
round trips, custom FPS validation/export, fixed-size bounds, movement and zero/fractional countdowns.
523 synthetic GPU checks cover HDR highlights,
SDR white normalization, RGB channel order, native sizes, SDR passthrough and renderer
state preservation. Seth confirmed the original live recording works; the new HDR
path, flash prevention, startup responsiveness and interactive rectangle controls
still need live acceptance. Mixed-DPI UI,
hardware encoder fallback, device loss and a clean Windows machine remain manual
acceptance checks. Automated desktop capture or input requires Seth's explicit
authorization; compile and synthetic checks do not exercise those paths.
