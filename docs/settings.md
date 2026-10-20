# Settings window and the Tools panel modes

Deep-dive for `Assets/_Script/GUI/AppSettingsPanel.cs` (the unified Settings window and the `config.txt` keys it writes) and `MainToolPanelModeController.cs`. The round-trip rule (every parsed key must be re-emitted by `Config.BuildModernConfigText`) is in `AGENTS.md`.

Moved verbatim from AGENTS.md on 2026-09-13 (bullet style kept); update this file when the feature changes and keep the AGENTS.md index entry to one or two lines.

## AppSettingsPanel

- Brave API SafeSearch defaults to **off** (Seth, 2026-10-20). Missing/blank config values and settings/API fallbacks use `Config.DefaultWebSearchSafeSearch`; an explicit saved `strict` choice or action override remains respected.

- `Assets/_Script/GUI/AppSettingsPanel.cs` is the unified Settings window. It owns the visible General, ComfyUI Settings, Audio, and Web content tabs plus an "LLM Settings" launcher in the same tab strip (the Web tab holds the Brave Search API key, SafeSearch toggle, yt-dlp cookies-from-browser and persistent Cookie file fields, a speech-to-text endpoint/key/model for `web_video speech="true"` checks, and a yt-dlp/ffmpeg/JS-runtime/STT status line; it writes `set_brave_search_api_key` / `set_web_search_safesearch` / `set_ytdlp_cookies_browser` / `set_ytdlp_cookies_file` / `set_stt_endpoint` / `set_stt_api_key` / `set_stt_model` to `config.txt`); the old generate gear and Configuration button route into it. The "LLM Settings" tab is a launcher, not a content page: clicking it opens the standalone advanced `LLMSettingsPanel` dialog directly (there is no in-tab LLM summary). The standalone LLM Settings button (`GameLogic.OnLLMSettingsButtonClicked`) opens that dialog directly too, and `AppSettingsPanel.Show(AppSettingsTab.LLM)` / automation `/settings tab=llm` open the window on General and pop the advanced dialog. Its ComfyUI Settings and Audio tabs write the modern supported subset of `config.txt` (ComfyUI servers, tokens/names, VRAM annotations, image editor path, audio defaults, Text To Speech settings, and the AI Chat audio generation gateway URL/key: `set_audio_gen_endpoint` / `set_audio_gen_api_key`, see `docs/audio_generation.md`) and reconnect through `Config.ProcessConfigString()` where needed. ComfyUI server rows have up/down priority controls; Apply/reconnect writes that order to `add_server` lines, and `Config.GetFreeGPU()` tries matching idle servers from top to bottom. If reconnect is blocked by active/queued GPU work, the panel shows a confirmation dialog that can force-cancel generation work, clear runtime GPU busy state, and reconnect.

## Tools panel compact / manual modes

- The compact group's clipboard row contains **Paste** and **Record screen** side
  by side and stays visible in advanced mode. General Settings has a Screen Recording
  section for enabling/editing the global shortcut (default Ctrl+Alt+F9), opening the
  toolbar and seeing status/errors. Preferences use PlayerPrefs, not config.txt.
  The floating toolbar remembers FPS/system-audio/cursor settings, Custom/Fixed size
  and fixed pixel dimensions, plus Custom's aspect-ratio lock (Any by default).
  Its Settings button edits the countdown, including
  zero and fractional seconds. Details:
  `screen_recording.md`.

- General Settings' Screen Recording section also has **Show clock**. It opens one
  movable, scalable Windows clock above other apps, with Clock/Stopwatch modes and
  optional recorder synchronization. The native recording toolbar has the same
  **Clock** launcher. Clock preferences use the separate PlayerPrefs key
  `desktop_clock_settings_v1`; details are in `screen_recording.md`.

- `Assets/_Script/GUI/MainToolPanelModeController.cs` toggles the scene-authored `CompactToolControls` and `ManualToolControls` groups on the main Tools panel without changing the underlying `GameLogic` state. It captures the scene-authored manual panel rect at startup, fits the panel to compact controls in compact mode, and restores the captured rect in manual mode. The compact row's top-left pair is two half-width text buttons (2026-10-13; the old gear icon was too easy to miss): `GenerateSettingsButton` labeled "Settings" (opens the Settings window; its gear `Image` child is inactive) and `ManualModeButton` labeled "Advanced", which keeps one label and shows the expanded state with the AI Chat button's cyan accent (`ManualActiveColor`).
