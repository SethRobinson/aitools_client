# Input System

The app uses Input System 1.20.0 exclusively. `Packages/manifest.json` pins it directly, and `activeInputHandler: 1` is set in the project settings and the embedded player settings in BOTH Development and Release build profiles. Changing the active backend requires restarting the Unity editor. Updating only the project setting leaves builds using the old profile override.

Gotcha (2026-10-05): the editor's "This project uses Input Manager, which is marked for deprecation" warning came back because `ReleaseBuildProfile.asset` had drifted to `activeInputHandler: 2` (Both) in a later unrelated commit, most likely re-saved by the Build Profiles window while that profile was active. The warning fires whenever the ACTIVE profile's embedded player settings say 0 or 2, regardless of `ProjectSettings.asset`. After any Build Profiles / Player Settings edit, re-check all three with `grep -rn activeInputHandler ProjectSettings/ProjectSettings.asset "Assets/Settings/Build Profiles/"` and expect `1` everywhere; then restart the editor for it to take effect.

## Runtime code

- `Assets/RT/Input/RTInput.cs` and `RTInputManager.cs` live in the small, automatically referenced `RT.Input` assembly. The manager's existing MonoScript GUID is preserved across its move into this folder. This assembly also lets the editor tests reference the implementation without depending on Assembly-CSharp.
- `RTInput` implements held/pressed/released keyboard and mouse reads, mouse position/delta and wheel polling using Input System devices. Missing devices and unsupported bindings return neutral values. `KeyCode` is retained only to preserve existing Inspector bindings; it is explicitly mapped to Input System controls, never numerically cast to `Key`. Mouse0..4 map to left/right/middle/back/forward buttons. Binding names represent physical keys, not text characters.
- Input System 1.20 defaults to `UniformAcrossAllPlatforms` scrolling. Read its normalized wheel values directly, preserving fractional ticks. Do not divide by 120 or replace deltas with their sign. `GetMouseAxis` preserves the three existing SceneLikeCamera axis names and their old 0.1 sensitivity; it does not consult InputManager settings.
- `RTInputManager` retains four logical players, keyboard alternatives, the public zero-based gamepad index API and button events. Internal assignment uses device identity so removing another controller does not move a surviving controller to a different player. A returning device reclaims its former slot if free; otherwise it uses a free slot. Extra pads wait until a slot opens. Disconnected or disabled devices stop supplying held state, and keyboard/pad button states are combined before emitting edges. Movement retains negative Y for up.
- The main scene already uses `InputSystemUIInputModule` and keeps its bindings and scroll multiplier. Both bundled color-picker scenes use the new module with its default actions and a wheel multiplier of 1, preserving their old UI wheel units. Their obsolete `TouchInputModule` components are removed. The color-picker assembly references Input System directly.
- `SimpleCameraMoverWithPinch` retains the existing generated touch action map and allows active touches through its window check even when no Mouse device exists. UI focus/hover checks and right-button camera overrides remain in their existing call sites.
- AI Chat still handles Enter, keypad Enter and Shift+Enter in `LateUpdate`, after TMP processes text. Prompt history, paste routing, undo/redo and Ctrl+wheel suppression retain their original focus and timing rules. Adventure reads the new backend once instead of falling back to the disabled legacy backend. TMP continues handling text entry and IME; `RTInput` only supplies shortcut state.

## Automated checks

`Assets/RT/Input/Tests/Editor/RTInputMigrationTests.cs` uses Unity's `InputTestFixture`, which replaces the input runtime with isolated virtual devices and restores it afterward. It does not send desktop input. The editor-only test assembly references `RT.Input`, `Unity.InputSystem`, and `Unity.InputSystem.TestFramework`.

Coverage includes serialized key mappings, both modifier sides, Enter/keypad Enter, same-frame readers, held/pressed/released transitions, mouse buttons, missing devices, fractional scroll and camera scaling, combined keyboard/gamepad buttons, movement direction, disconnect/reconnect identity, disabled devices and the four-player limit.

Run `RTInputMigrationTests` in Unity's EditMode Test Runner. For a batch run, first close the editor for this project (never open a second Unity instance against the locked project), set `$unityEditor` to the installed editor executable, and run this from the repository root:

```powershell
& $unityEditor -batchmode -nographics -projectPath (Get-Location).Path -runTests -testPlatform EditMode -testFilter RTInputMigrationTests -testResults Temp/input-migration-results.xml -logFile Temp/input-migration-tests.log
```

Do not add `-quit`: the test runner exits when the run finishes. An isolated temporary Unity project containing `Assets/RT/Input` and the same Input System/test-framework versions can also run these tests without touching the app editor.

Also scan runtime source for legacy input polling and scene/prefab YAML for legacy UI modules, and check that all three active-input settings remain 1. Compile the runtime, editor, color-picker and test assemblies. A C# compilation check does not replace an actual Unity player build or the live checks below.

## Live verification after restarting Unity

These checks are performed manually unless computer control is explicitly authorized or an authorized MCP connection is available:

1. Open the main scene and enter Play mode. Confirm the legacy Input Manager warning is gone and no input exceptions appear.
2. Exercise workspace selection, Ctrl-selection, dragging/resizing, Delete, mask painting with Ctrl/Alt, camera pan/zoom and touch/pinch where available. Verify UI panels still consume scrolling and the right-button override still works.
3. Exercise paste and undo/redo with and without a focused text field; verify typing, caret movement, selection, IME, Escape, console shortcuts and movie controls.
4. In AI Chat and Adventure, check Enter, keypad Enter, Shift+Enter and held keys without duplicate submits/newlines. Check prompt history and Ctrl+wheel font sizing without conversation scrolling or camera zoom.
5. Open both color-picker demos and verify pointer interaction, keyboard navigation and scrolling. Check the Development and Release profiles remain on the new backend.

Migration validation on 2026-10-03: C# compilation passed for the input runtime, tests, color-picker, app runtime with editor/player defines, and app editor code, with the legacy-input define removed. Existing unrelated compiler warnings remain. Automatic approval review blocked launching the isolated Unity test process, so virtual-device execution, Unity player build and post-restart live checks were not completed in that session.
