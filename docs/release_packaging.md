# Windows release packaging

Run `BuildWin64.bat` from the checkout with `NO_PAUSE=1` for unattended builds. Close the Unity editor first. The build is local: it never uploads or pushes.

`scripts/ReleasePackage.ps1` anchors all paths to the checkout, refuses linked inputs/output and uses `git ls-files` to choose runtime inputs. It copies current working-tree bytes, so version/changelog edits are included before a commit. New runtime files must be added to the Git index before building. Filesystem copies of the project without Git metadata are not supported by this release script.

## Contents

The package includes Unity's release player plus tracked files in `utils`, `web`, `Adventure`, `AIGuide`, `ComfyUI`, `Presets` and `aichat`; the Python CLI's top-level Python/BAT files, README, requirements and example config; and root `config_cam.txt`, `model_data.json`, `README.md` and `LICENSE.md`. README screenshots stay online and the `Media` folder is excluded from the download. The public camera preset config is intentional. The application creates personal configuration on first launch.

Excluded before staging: ignored/untracked files, private application and CLI configs, Python environments/caches, generated ComfyUI API caches, test/local-prefixed files, Unity `.meta` files, `Unused` directories, RTClip's source ZIP and the unrelated `cli/codex_imagegen.sh` helper. FFmpeg/ffprobe and yt-dlp ship with their notices/licenses and are copied unchanged. Only the application and RTClip EXE/DLL are signed.

Four source presets are intentionally omitted because their workflows were previously retired:

- `(Photo Edit) Everyone is at Disneyland`
- `Image To Image Inpaint (SDXL)`
- `Image To Image Inpaint Mask Subject (SDXL)`
- `Image to Video (VLLM, LLM, Wan) 20s 4p`

Do not restore obsolete workflow files just to satisfy these references. When replacing a retired preset with a working one, remove its exclusion and validate it.

## Validation and failure behavior

Staging parses shipped JSON and checks workflow dependencies in Presets, Adventure and AIGuide before copying. Verification rejects unexpected files in runtime directories, private/development artifacts and missing Unity components; checks staged bytes against source (except the two signed RTClip files); and requires valid Authenticode signatures on all three signed binaries. Archive verification checks unique `aitools_client/` paths, file counts and SHA-256 equality for every archived file against staging.

The build stops on a failed Unity BuildReport, copy, cleanup, signing, signature/content check, archive creation or archive comparison. `CleanBuildOutput.ps1` removes Burst diagnostics and Unity backup folders with the same path/link guards as other build cleanup.

The ZIP command targets `build/./win/` without 7-Zip's `-r` search flag. A directory argument already includes its contents. `-r` with the bare name `win` also finds unrelated nested `build/win` fixture directories below `build`, which can leak test/private files into the archive. Archive verification rejects those extra entries.

Offline regression checks:

```powershell
pwsh -File scripts/VerifyDeleteSafety.ps1
pwsh -File scripts/VerifyReleasePackage.ps1
wsl -d Ubuntu-24.04 -- sh /mnt/f/Unity/aitools_client/scripts/VerifyWebGLCleanup.sh
powershell -NoProfile -File scripts/ReleasePackage.ps1 -Phase VerifyArchive
```

On Linux, use `sh scripts/VerifyWebGLCleanup.sh` for the third command. The first three retain disposable fixtures and do not contact servers. The last command validates an already built release.

Run all Unity EditMode tests without a test filter; this repository currently has 40 input tests. PlayMode discovery currently finds no tests. The standalone `-render-validation` fixture provides the app rendering/media checks and needs an explicitly authorized visible player window; see `docs/rendering.md`. Run it on a disposable copy of the final player so its generated configs, logs and screenshots cannot contaminate the release. CLI syntax/help and offline preset checks complement these suites; actual ComfyUI/LLM generation requires separately configured servers.

After building, update the README download size from the actual archive, recopy README and update its archive entry, then rerun `VerifyArchive`. Keep the date/version and the packaged README synchronized. Do not upload or push without an explicit request.

## V3.07 validation (2026-10-08)

The signed Windows build completed successfully. Removing the optional README screenshots reduced the final download from 181.5 MB to 158.9 MB; the player binaries are unchanged. The final archive contains 350 files, including 228 tracked runtime inputs, with all three signatures valid and every archived file matching staging. The content audit found no private-key, common credential-token or personal Windows-path matches in 229 text files.

Passed: 40 EditMode tests, 54 standalone rendering/media checks, 98 Windows cleanup checks, 29 Linux cleanup checks, 29 release staging checks, CLI syntax/help and a BiRefNet offline dry run, and parsing/default expansion for 20 Qwen/Flux/H3 presets. Six saved-image outputs match the Built-In baseline exactly. PlayMode discovery found zero tests. No live ComfyUI/LLM generation was performed. Rendering used a disposable copy whose app/input assemblies and UnityPlayer match the final build; only game-view images were captured. The fixture logs contain Media Foundation color-primaries fallback warnings, with no rendering failures or exceptions.

Ignored evidence is under `build/release-validation/`: `editmode.xml`, `playmode.xml`, `build-final.log`, `pixel-comparison.json`, `preset-checks.json`, `content-audit.json` and `player-307/build/render-validation/player/report.json`. The release build's Unity log is root `log.txt`.
