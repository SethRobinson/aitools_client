# Windows release packaging

Run `BuildWin64.bat` from the checkout with `NO_PAUSE=1` for unattended builds. Close the Unity editor first, or use `BuildWin64.bat --editor` with the current automation bridge enabled, ReleaseBuildProfile active, and the editor stopped with saved scenes. The latter builds through the open editor, without a second Unity instance or desktop input. The build is local: it never uploads or pushes.

`scripts/ReleasePackage.ps1` anchors all paths to the checkout, refuses linked inputs/output and uses `git ls-files` to choose runtime inputs. It copies current working-tree bytes, so version/changelog edits are included before a commit. New runtime files must be added to the Git index before building. Filesystem copies of the project without Git metadata are not supported by this release script.

The root `UpdateBuildDirConfigFiles.bat` is a gitignored local helper for syncing a developer test build (it also copies private configs and test files on purpose). `BuildWin64.bat` calls `ReleasePackage.ps1 -Phase Stage` directly and must never depend on that bat. Never commit it and do not "fix" it to exclude private files (Seth, 2026-10-08).

## Build flow

`BuildWin64.bat` runs from its own directory: `app_info_setup.bat`, `scripts/CleanBuildOutput.ps1 -Phase Reset` (validates and recreates `build/win`), Unity with `Win64Builder.BuildRelease` and `Assets/Settings/Build Profiles/ReleaseBuildProfile.asset`, `scripts/BuildScreenCapture.ps1 -Check`, `scripts/ReleasePackage.ps1 -Phase Stage`, `-Phase Package` (prunes development output), signing, `SethsAIToolsWindows.zip`, then `scripts/SmokeReleaseZip.ps1`. Any copy, build-result, cleanup, signing, content or archive failure stops packaging.

Running from an AI tool shell: set `NO_PAUSE=1` so a background run cannot hang at `pause`. The build needs the parent-folder `..\base_setup.bat`, registry queries to find Unity, and the `%RT_UTIL%` / `%RT_PROJECTS%` tool folders (local specifics in `agents_secret.md`). From PowerShell in the repo root: `$env:NO_PAUSE='1'; cmd /c .\BuildWin64.bat` (append `--editor` when the editor bridge is active). Check the exit code and `build\win\aitools_client.exe`. The ordinary batch mode must not run against an open editor project.

The build rewrites the tracked, unsigned `utils/ScreenCapture/AIToolsCapture.exe` with header-only nondeterministic bytes even when its source is unchanged: `git checkout --` it rather than committing noise (the zip holds a separately signed copy).

Build timestamps: `Assets/RT/Editor/BuildTimestampWriter.cs` (`IPreprocessBuildWithReport`) writes gitignored `Assets/Resources/build_date.txt` at the start of every player build on any build path; `RTBuildInfo.Timestamp` reads it (editor falls back to the current time).

WebGL: `Assets/RT/Editor/WebGLBuilder.cs` and `UploadWebGLRSync*.bat` exist, but there is no root `BuildWebGL.bat`. Do not document or call one unless it is reintroduced. Remote WebGL cleanup is `scripts/CleanWebGL.sh`, preceded by `scripts/ValidateWebGLUpload.ps1`.

## Contents

The package includes Unity's release player plus tracked files in `utils`, `web`, `Adventure`, `AIGuide`, `ComfyUI`, `Presets` and `aichat`; the Python CLI's top-level Python/BAT files, README, requirements and example config; and root `config_cam.txt`, `model_data.json`, `README.md` and `LICENSE.md`. README screenshots stay online and the `Media` folder is excluded from the download. The public camera preset config is intentional. The application creates personal configuration on first launch.

Excluded before staging: ignored/untracked files, private application and CLI configs, Python environments/caches, generated ComfyUI API caches, test/local-prefixed files, Unity `.meta` files, `Unused` directories, RTClip's source ZIP and the unrelated `cli/codex_imagegen.sh` helper. FFmpeg/ffprobe and yt-dlp ship with their notices/licenses and are copied unchanged. The application, RTClip EXE/DLL and AIToolsCapture.exe are signed.

After Unity builds, `BuildWin64.bat` runs `scripts/BuildScreenCapture.ps1 -Check`.
This requires the .NET SDK, restores locked ScreenRecorderLib 7.0.1 dependencies,
builds the net48/x64 helper from `tools/ScreenCapture`, and refreshes tracked runtime
files under `utils/ScreenCapture` (identical files are skipped so loaded native DLLs
do not block staging). Its dependency-only check never captures screens
or sound. Third-party CRT DLLs, the recorder DLL, licenses and hashes are included;
the build checks dependency hashes and never harvests machine-installed CRT DLLs.
ScreenRecorderLib is the pinned 7.0.1 build with our HDR patch, not the stock NuGet
binary. The helper csproj preserves the checked-in patched runtime and the build
rejects mismatched patch/header hashes. To rebuild it, use
`pwsh -File scripts/BuildScreenCaptureHdr.ps1 -Stage` with the pinned native toolchain
listed in `screen_recording.md`; regular release builds need only the .NET SDK.
Source, build intermediates and vendor PDB/XML files are not packaged. Add new
runtime files to the index before staging. See `screen_recording.md`.

Four source presets are intentionally omitted because their workflows were previously retired:

- `(Photo Edit) Everyone is at Disneyland`
- `Image To Image Inpaint (SDXL)`
- `Image To Image Inpaint Mask Subject (SDXL)`
- `Image to Video (VLLM, LLM, Wan) 20s 4p`

Do not restore obsolete workflow files just to satisfy these references. When replacing a retired preset with a working one, remove its exclusion and validate it.

## Validation and failure behavior

Staging parses shipped JSON and checks workflow dependencies in Presets, Adventure and AIGuide before copying. Verification rejects unexpected files in runtime directories, private/development artifacts and missing Unity components; checks staged bytes against source (except the signed RTClip EXE/DLL and AIToolsCapture.exe); and requires valid Authenticode signatures on all four signed binaries. Archive verification checks unique `aitools_client/` paths, file counts and SHA-256 equality for every archived file against staging.

The build stops on a failed Unity BuildReport, copy, cleanup, signing, signature/content check, archive creation, archive comparison or fresh-ZIP smoke check. `CleanBuildOutput.ps1` removes Burst diagnostics and Unity backup folders with the same path/link guards as other build cleanup.

## Fresh-ZIP command-line smoke check

Every release build finishes with `scripts/SmokeReleaseZip.ps1`. To repeat it without
rebuilding, run `pwsh -File scripts/SmokeReleaseZip.ps1`; an optional `-ArchivePath`
accepts an absolute ZIP filename. It validates entry paths and rejects traversal,
links, duplicate entries and unsafe Windows filenames, then extracts into a new
`build/package-smoke-<guid>/aitools_client` directory. Existing folders are never
reused or deleted. Evidence, the synthetic clip and logs remain beside that copy.

The extracted executable runs with `-batchmode -nographics -package-smoke-check` and
a minimal Windows PATH. `PackageSmokeCheck` keeps scene roots inactive before
Awake/Start, then uses Unity's normal quit path without starting app services that
write preferences, register hotkeys or contact servers. It loads Unity/managed assemblies and a built resource,
parses shipped model/workflow JSON, checks key runtime files, constructs/disposes
the recorder's native manager with `AIToolsCapture.exe --check`, runs `yt-dlp
--version`, and uses the bundled FFmpeg/ffprobe to encode, probe and decode a tiny
synthetic H.264/AAC clip. It records each result in `package-smoke/report.json` and
returns 0 only on success. The script also enforces a timeout and retains a summary
with the ZIP hash and extracted file count. A missing report or nonzero exit fails.
Missing scene-script references also fail the check.

For an additional failure-path test, run `pwsh -File scripts/SmokeReleaseZip.ps1
-CheckMissingRecorderDll`. After a healthy run it temporarily parks ScreenRecorderLib.dll
inside that disposable extraction, reruns the player, requires a failed recorder
dependency check/nonzero exit, and restores the DLL and healthy report. Both reports
and player logs are retained. It never changes the source runtime or release ZIP.

This is a dependency/startup smoke check on the current Windows installation,
not a clean-OS test or interactive rendering/capture test. It does not access the
clipboard, capture the desktop/audio devices, or simulate input. RTClip's files
are checked, but its .NET 6 desktop runtime and clipboard operation are not exercised.
The recorder still requires Windows .NET Framework 4.8 and Media Foundation.

`BuildInOpenEditor.ps1` powers `--editor`: it cleans only the guarded `build/win`
tree, sends a unique request to `POST /windows_release_build`, and accepts only its
matching successful `build/release-build.json`. The bridge refuses Play mode, dirty
scenes, concurrent builds and compile failures. The script checks the bridge's project
path and stops if that editor process exits before returning a result. Both build
paths pass RT_RELEASE through `BuildPlayerOptions.extraScriptingDefines`, avoiding
profile writes and recompilation merely to add/remove a temporary define. Signing/staging/ZIP steps are shared
with the ordinary batch build.

The ZIP command targets `build/./win/` without 7-Zip's `-r` search flag. A directory argument already includes its contents. `-r` with the bare name `win` also finds unrelated nested `build/win` fixture directories below `build`, which can leak test/private files into the archive. Archive verification rejects those extra entries.

Offline regression checks:

```powershell
pwsh -File scripts/VerifyDeleteSafety.ps1
pwsh -File scripts/VerifyReleasePackage.ps1
pwsh -File scripts/VerifyReleaseSmoke.ps1
wsl -d Ubuntu-24.04 -- sh /mnt/f/Unity/aitools_client/scripts/VerifyWebGLCleanup.sh
powershell -NoProfile -File scripts/ReleasePackage.ps1 -Phase VerifyArchive
```

On Linux, use `sh scripts/VerifyWebGLCleanup.sh` for the WSL command. The verification scripts retain disposable fixtures and do not contact servers. `VerifyReleaseSmoke` checks rejection of unsafe ZIP paths, aliases, duplicates and links without starting a player. `VerifyArchive` validates an already built release.

Run all Unity EditMode tests without a test filter; this repository currently has 40 input tests. PlayMode discovery currently finds no tests. The standalone `-render-validation` fixture provides the app rendering/media checks and needs an explicitly authorized visible player window; see `docs/rendering.md`. Run it on a disposable copy of the final player so its generated configs, logs and screenshots cannot contaminate the release. CLI syntax/help and offline preset checks complement these suites; actual ComfyUI/LLM generation requires separately configured servers.

Release shells can inherit a module search path that hides PowerShell's `Get-FileHash`: hash release/helper files with .NET SHA256 (as the packaging and recorder scripts do) and explicitly import Windows signature modules where needed.

Smoke-mode gotchas: use Unity's normal quit with inactive scene roots (`Environment.Exit` hung during Mono teardown), and `BeforeSceneLoad` searches must include inactive objects because the active scene's root list is not yet available there. Release defines are passed per build, not written to profiles (a profile save hit a file lock).

For a release intended for publication, update the README download size from the actual archive, recopy README and update its archive entry, then rerun `VerifyArchive`. Keep the date/version and the packaged README synchronized. Trial builds retain the published download metadata and record their local ZIP size/hash in validation evidence instead. Do not upload or push without an explicit request.
