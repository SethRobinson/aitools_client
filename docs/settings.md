# Settings window and the Tools panel modes

Deep-dive for `Assets/_Script/GUI/AppSettingsPanel.cs` (the unified Settings window and the `config.txt` keys it writes) and `MainToolPanelModeController.cs`. The round-trip rule (every parsed key must be re-emitted by `Config.BuildModernConfigText`) is in `AGENTS.md`.

Moved verbatim from AGENTS.md on 2026-09-13 (bullet style kept); update this file when the feature changes and keep the AGENTS.md index entry to one or two lines.

## AppSettingsPanel

- **AI Chat** contains the entire former AI Chat Settings dialog: main prompt editor, scrollable loaded skills, media/thinking/debug/context options, session post-message text, Compact controls, attachment size, preset prefix and media-on-Clear count. The chat's Settings button opens `AppSettingsPanel.Show(AppSettingsTab.AIChat)`. `AIChatSettingsPanel` now builds embedded content and uses the main window's footer for Reload skills, Open aichat folder and Save & Close; it never creates another canvas/window. The tab also works before AI Chat has been opened. Main Settings sorts above AI Chat and below advanced LLM Settings.
- AI Chat edits save on Save & Close, the window's X/Escape, or switching to another content tab. Clicking the selected AI Chat tab preserves the draft. Closing destroys the embedded page so reopening reloads current files/preferences. Reload skills saves pending edits first. Prompt edits still write only when changed and to the exact path loaded, even if the preset prefix was edited at the same time. A prompt write failure keeps the page open and reports the error. Saving this tab does not rewrite config.txt or reconnect ComfyUI. Saved settings notify an existing chat so skill/config changes are available to its next turn.
- **Allow controls in think blocks** remains off by default, saved in PlayerPrefs, and independent of Strip think tags; it affects the next main reply after saving. Execution and history details are in `docs/aichat.md`.
- Validation: `pwsh -File scripts/VerifyAIChatSettings.ps1` uses the editor bridge's `POST /aichat_settings_validation` in Play mode. It creates the real embedded content under a disabled fixture canvas, checks input bounds, scrollbars, all controls and the host action bar, and verifies no-op saves, prefix changes and Reload preserving edits using synthetic prompt/skill files. Every preference is restored, no real prompt/config files are touched, and no desktop input or screenshots are used. Fixtures remain under ignored `build/aichat-settings-validation-*`.

- Brave API SafeSearch defaults to **off** (Seth, 2026-10-20). Missing/blank config values and settings/API fallbacks use `Config.DefaultWebSearchSafeSearch`; an explicit saved `strict` choice or action override remains respected.
- Web includes **Sign in / change account** and **Use remembered YouTube login**.
  The dedicated Firefox session is kept in `%LOCALAPPDATA%\SethsAITools\YouTubeFirefox`;
  `set_youtube_firefox_login` persists the reuse choice and must survive modern
  config regeneration. When enabled it overrides the cookie file/browser fields
  for YouTube only. Unchecking leaves the profile on disk; sign out in that
  Firefox window to remove the login. Authentication-error offers and retry
  lifecycle: `docs/web_media.md`.

- `Assets/_Script/GUI/AppSettingsPanel.cs` is the unified Settings window. It owns the visible General, ComfyUI Settings, Audio, Web, and AI Chat content tabs plus an "LLM Settings" launcher in the same tab strip (the Web tab holds the Brave Search API key, SafeSearch toggle, yt-dlp cookies-from-browser and persistent Cookie file fields, a speech-to-text endpoint/key/model for `web_video speech="true"` checks, and a yt-dlp/ffmpeg/JS-runtime/STT status line; it writes `set_brave_search_api_key` / `set_web_search_safesearch` / `set_ytdlp_cookies_browser` / `set_ytdlp_cookies_file` / `set_stt_endpoint` / `set_stt_api_key` / `set_stt_model` to `config.txt`); the old generate gear and Configuration button route into it. The "LLM Settings" tab is a launcher, not a content page: clicking it opens the standalone advanced `LLMSettingsPanel` dialog directly (there is no in-tab LLM summary). The standalone LLM Settings button (`GameLogic.OnLLMSettingsButtonClicked`) opens that dialog directly too, and `AppSettingsPanel.Show(AppSettingsTab.LLM)` / automation `/settings tab=llm` open the window on General and pop the advanced dialog. Its ComfyUI Settings and Audio tabs write the modern supported subset of `config.txt` (ComfyUI servers, tokens/names, VRAM annotations, image editor path, audio defaults, Text To Speech settings, and the AI Chat audio generation gateway URL/key: `set_audio_gen_endpoint` / `set_audio_gen_api_key`, see `docs/audio_generation.md`) and reconnect through `Config.ProcessConfigString()` where needed. ComfyUI server rows have up/down priority controls; Apply/reconnect writes that order to `add_server` lines, and `Config.GetFreeGPU()` tries matching idle servers from top to bottom. If reconnect is blocked by active/queued GPU work, the panel shows a confirmation dialog that can force-cancel generation work, clear runtime GPU busy state, and reconnect.

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
