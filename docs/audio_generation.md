# AI Chat audio: generated music / sound effects / speech, Audio bubbles, set_video_audio

AI Chat can generate music, sound effects, and spoken lines through a small HTTP
"audio generation gateway" the user runs (Settings > Audio > Audio generation), show
them as playable `Audio #N` bubbles, accept dropped-in sound files the same way, and
mix or replace a Movie bubble's soundtrack with any of them via the bundled FFmpeg
(`set_video_audio`). Typical requests: "generate a song about bananas and add it to
the video", "replace the video's audio with a funny song about computers", "generate
a door shutting sound effect", "narrate movie 2 in a Scottish voice".

## Pieces

| piece | where | role |
|---|---|---|
| Gateway client | `Assets/_Script/LLM/AIChat/Audio/AudioGenClient.cs` | `POST {base}/audio` (music, sfx) / `POST {base}/tts` (speech) as multipart, optional `Authorization: Bearer`, saves the returned file under `tempCache/aichat_audio/`, surfaces JSON `detail` errors, `Handle.Cancel()` for Stop |
| FFmpeg audio helpers | `Assets/_Script/LLM/AIChat/Video/FfmpegToolAudio.cs` (`partial class FfmpegTool`) | `ProbeAudio`, `CreateAudioWaveformPreview` (showwaves -> 640x160 H.264 + AAC), `ExtractAudioSection` (voice-clone sample, mono 24k), `ExtractAudioWavSection` (full-quality native-rate WAV for the clip chooser's `Export audio clip`), `MuxAudioIntoVideo` + `BuildMuxAudioArgs` (mix / replace graph), `IsSupportedAudioExtension` |
| Executor | `SkillActionExecutor.cs` `ExecuteGenerateAudio` / `ExecuteSetVideoAudio` | attribute parsing + aliases, clamps (music 10-360 s: the music model rejects shorter with a 422, sfx 0.1-11 s), `ref_voice` resolution, defers the pump like clip_video |
| Host | `AIChatPanel.cs` "Audio generation" region | `GenerateAudioActionCoroutine`, `SetVideoAudioActionCoroutine`, `AppendAudioBubble`, `HandleDroppedAudioFile`, `CancelAllAudioGeneration`, `ChatImageRecord.isAudio/audioPath/durationSeconds` |
| Prompt | `ChatContextBuilder.cs` (`ChatImageState.IsAudio`, the "Audio #N" legend), `aichat/skills/set_video_audio.md` (tracked), `aichat/skills/local_generate_music.md` / `local_generate_sfx.md` / `local_generate_speech.md` (gitignored, machine-local) | routing + the model-facing parameter docs |
| Settings | `AppSettingsPanel.BuildAudioTab` "Audio generation" box, `Config.cs` `set_audio_gen_endpoint` / `set_audio_gen_api_key` | gateway URL + optional key in `config.txt` |
| Drops | `ChatImageAttachmentZone.OnAudioFileDropped`, `DragAndDropHandler` | `.wav .mp3 .flac .ogg .m4a .aac .opus .wma .aiff` dropped on the chat -> `Audio #N (you)`; dropped elsewhere -> toast |
| Clip audio | `ChatVideoClipChooser` `Export audio clip` button -> `AIChatPanel.AddLocalClipAudioToChat` | the video clip chooser (drag-drop import and `Process > Export movie or audio clip`) cuts the selected range's audio to a WAV `Audio #N` bubble (kind `user audio`) and/or a WAV in the output folder |

## Gateway contract (what the server must implement)

The client is deliberately dumb: every action attribute the executor accepts is passed
through as a multipart form field, and the response BODY is the finished audio file.

- `POST {base}/audio` - fields `prompt`, `duration` (seconds), and optionally `mode=music`
  (force the music model for a short jingle), `lyrics`, `vocals=true`, `seed`, `bpm`,
  `steps`, `format` (the app always asks for `wav`). Returns `audio/wav` (or flac / mp3;
  the extension is taken from Content-Type, then Content-Disposition, then magic bytes).
- `POST {base}/tts` - fields `text`, and optionally `voice`, `scene`, `language`,
  `engine`, `temperature`, `seed`; optional file `ref_voice` (mono 24 kHz WAV sample the
  host cuts from a chat bubble). Returns `audio/wav`.
- Errors: any non-2xx; a JSON body `{"detail": "..."}` is shown to the model verbatim
  (700 chars) so it can fix the offending parameter. A 2xx with a JSON/text body or fewer
  than 64 bytes is treated as "no audio".
- Timeouts: 15 min for music (long tracks render around 1x realtime), 5 min otherwise.

The base URL may also end in `/audio` or `/tts`; the client strips that and appends the
right path per kind. Anything implementing this shape works (the reference server is a
FastAPI front on local music / sfx / TTS workers; machine specifics live in
`agents_secret.md`, not here).

## Audio bubbles = Movie bubbles with a waveform picture

There is no separate audio player. Every sound (generated or dropped) is rendered by
ffmpeg into a small MP4 whose picture is its scrolling waveform (`showwaves`, colored
per kind: blue music, orange sfx, green speech, purple user file) and whose track is the
audio as AAC. That MP4 is loaded through the normal `AppendVideoClipBubble` path
(`ImageGenerator.AddImageByFileName` -> `PicMovie`), so playback, the app clip volume /
global mute, click-to-focus, save, `clip_video`, `stitch_video`, `extract_still`, and
`<Video 1>` references all work with zero new UI. The record additionally carries
`isAudio`, `audioPath` (the ORIGINAL lossless file, used by `set_video_audio` and
`ref_voice`) and `durationSeconds`; `IChatHost.IsChatImageAudio` /
`GetChatImageAudioFilePath` expose them. The bubble label is `Audio #N`, the CHAT IMAGES
kind is `generated music` / `generated sound effect` / `generated speech` / `user audio`,
and the caption is SYNTHESIZED (prompt, voice, duration; never a vision call, a waveform
tells the vision model nothing) with `alwaysIncludeCaption` so the model always sees it.
The preview MP4 auto-deletes with the Pic; the original stays in `tempCache/aichat_audio/`
(its path is in the recap line). The pic's S / save button copies the preview movie AND the
original sound file next to it (`PicMovie.SetCompanionAudioFile`, same stem, e.g. `.wav`).

In the text column the reply shows a `[skill: generate_music]` marker; clicking it expands
what was sent (caption, lyrics, voice, scene...) under the bubble, clicking again hides it.

Consequence to remember: `IsChatImageMovie` is TRUE for audio bubbles. Code that must
tell a real video from a sound checks `IsChatImageAudio` too (`ExecuteSetVideoAudio`
does; `FindLatestChatMedia(wantAudio:false)` skips audio when picking "the newest Movie").

## Model-facing actions

| skill | attributes | result |
|---|---|---|
| `generate_music` | `prompt` (structured caption), `duration` (10-360, ceiling; shorter is raised to 10 with a silent note), `lyrics` (`[verse]`/`[chorus]` tags, `\n` lines), `vocals`, `seed`, `bpm`, `steps`, `anchor`, `resume` | `Audio #N` kind `generated music`; always sends `mode=music` + `format=wav`; words in `lyrics` imply `vocals=true` |
| `generate_sfx` | `prompt` (foley brief), `duration` (0.1-11, clamped with a silent note), `seed`, `steps`, `anchor`, `resume` | `Audio #N` kind `generated sound effect` |
| `generate_speech` | `text`, `voice` OR `ref_voice` (Audio/Movie number or anchor; `ref_start`, `ref_duration` 3-30 s), `scene`, `language`, `engine`, `temperature`, `seed`, `anchor`, `resume` | `Audio #N` kind `generated speech`; a `ref_voice` that is not a bubble is reused as `voice=` with a silent note |
| `set_video_audio` | `chat_image` (Movie), `audio` (Audio #N / anchor / Movie; aliases `sound`, `music`, `song`, `track`; falls back to `chat_image2`, then the newest Audio), `mode=mix\|replace` (default mix; a silent source behaves as replace), `volume`, `original_volume`, `start`, `loop`, `fade_in`, `fade_out` (default: 1 s only when the audio is cut by the video end), `resume` | NEW `Movie #N` with the source's caption + a "Soundtrack: ..." note; `-c:v copy`, AAC 192k, exactly the video's length |

Aliases (`NormalizeSkillId`): `music`, `song`, `compose`, `jingle`, `soundtrack`... ->
`generate_music`; `sfx`, `sound`, `sound_effect`, `foley`... -> `generate_sfx`; `tts`,
`speech`, `speak`, `say`, `voice`, `narrate`, `voiceover`... -> `generate_speech`;
`add_audio`, `add_music`, `replace_audio`, `swap_audio`, `mux`, `dub`, `score_video`... ->
`set_video_audio`.

## Scheduling and gating

- Generation runs under the video-import gate (`BeginVideoImport("Generating music")`):
  Send is blocked and the footer counts up, because the reply's later actions almost
  always depend on the sound (`set_video_audio audio="song"`). Stop aborts the HTTP
  request (`CancelAllAudioGeneration`); Clear does too. The executor defers the pump, so
  a same-reply `set_video_audio` runs after the bubble (and its anchor) exists.
- `set_video_audio` first WAITS for its sources like `stitch_video` (the same
  `CollectStitchSourceState` poll, 2 h cap, 30 s "finished but no file" grace, footer
  "set_video_audio: waiting for 1 clip to render"; the chat stays usable, only Stop/Clear
  cancel), then probes + muxes under the import gate. So "generate_image ->
  image_to_movie chain anchor=clip, generate_music anchor=song, set_video_audio
  chat_image=clip audio=song" is one reply with no continue turns.
- Failures go to the model via `AddSystemInjectionAndBubble` + `RequestContinueTurn()`
  (a bad parameter is fixable next turn; the gateway's `detail` names the field).
- The audio skills are hidden from the SKILLS block and keyword autoload
  (`HiddenSkillIdsForPrompt`) while no gateway URL is configured; `set_video_audio` is
  always listed (it only needs ffmpeg). Movie dimensions in CHAT IMAGES now include the
  clip length (`864x480 @24fps, 5.2s`) so the model can size a song to a clip.

## FFmpeg graphs

Waveform preview: `[0:a]aformat=channel_layouts=stereo,showwaves=s=640x160:mode=cline:rate=25:colors=C|C:scale=sqrt:draw=full,drawbox=(center line),format=yuv420p[v]`
then `-map [v] -map 0:a:0 -c:v libx264 -crf 23 -c:a aac -b:a 192k -shortest`. ~0.3 s per
12 s of audio.

Mix: `[0:a]aformat=44100/stereo,volume=ORIG[a0];[1:a]aformat=44100/stereo[,volume=V][,adelay=START ms:all=1],apad[,afade in][,afade out][a1];[a0][a1]amix=inputs=2:duration=longest:dropout_transition=0:normalize=0[a]`
with `-map 0:v:0 -map [a] -c:v copy -c:a aac -t <video duration>` (`-stream_loop -1` on the
audio input when `loop`). Replace / silent source: the `[1:a]...[a]` chain alone. `apad` +
`-t` is what makes the output exactly the video's length; `normalize=0` keeps the levels
the user asked for (amix would otherwise halve both inputs).

## Local-only skill files

`aichat/skills/local_*.md` is gitignored. The three `generate_*` skill files describe the
reference gateway's prompting rules (the structured music caption skeleton, lyric tags,
the 136-voice library and its language locks); they only make sense where that gateway
exists, so they stay out of the repo. Other machines get the generic code, the Settings
fields, `set_video_audio`, and this doc; they can write their own `local_*.md` for
whatever server they point the URL at (the skill ids must stay `generate_music`,
`generate_sfx`, `generate_speech` because the executor dispatches on them).

## Verification notes (2026-08-24)

Measured against the reference gateway: sfx 1.5 s in ~1 s, tts line in ~4 s (24 kHz mono
wav), 12 s music (`format=wav`) in ~14 s; the mix and replace+loop graphs were validated
with the bundled ffmpeg before the C# was written. Debug: `llm_aichat_log.json` gets
`generate_*` notes with the exact fields posted and `set_video_audio` notes with the ffmpeg
command line.
