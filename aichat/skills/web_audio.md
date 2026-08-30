---
id: web_audio
summary: Download ONE bare sound file (.wav/.mp3/.flac/.ogg/.m4a...) from the web into chat as a playable Audio #N bubble - from a direct file URL (url=) or an audio link listed by web_page (result="P1:a2"). There is NO audio search engine, so finding sounds is a two-step flow - web_search kind="web" for a page that hosts them (a soundboard, a quotes page), web_page to read it (its bare sound-file links are listed as P<n>:a<i>), then web_audio result=. No vision check (a waveform shows nothing); the file is ffprobe-gated and, with speech="true", audio-checked (ffmpeg + Whisper) so silent / music-only files are rejected as voice references. The transcript lands in the caption. It auto-continues by default. Uses - set_video_audio onto a Movie, or (the assumed path for a voice) an H3 standalone audio reference: audio="N" on a Reference To Video action.
inputs: none
autoload: true
triggers: wav file, wav files, .wav, mp3 file, mp3 files, .mp3, sound file, sound files, audio file from the web, download audio, download a sound, download the sound, download sounds, fetch a sound, fetch audio, get a sound of, find a sound, find sounds, find audio of, sound of, sound bite, soundbite, sound bites, sound clip from the web, audio clip from the web, soundboard, sound board, sound effect from the web, quotes in audio, audio quotes, voice clip, voice clips, their voice from the web
template: <aitools_action skill="web_audio" url="https://www.example.com/sounds/quote.wav" speech="true" anchor="george_voice"/>  # or result="P1:a2" (an audio link listed by web_page). speech="true" whenever it will be a VOICE reference (silent/music-only files are rejected and the transcript is captured). Optional start/duration seconds to trim, resume="false" (it auto-continues by default). No query= mode: to FIND sounds, first web_search kind="web" for the hosting page, then web_page it (audio links are listed as P<n>:a<i>).
---
# Web audio fetch

Download one bare sound file from the web into chat as a playable `Audio #N`
bubble, exactly like a dropped .wav or generated audio: it can be played, saved,
muxed onto a Movie with `set_video_audio`, or staged as an H3 standalone audio
reference (the assumed way to make a character SOUND right in a render).

## When to use it

- The user asks for a **sound file** from the web: a movie/TV quote .wav, a
  sound effect, a theme song file, a soundboard clip.
- A **voice reference** when a bare audio file exists (a .wav of the character
  speaking). When only a VIDEO exists, use `web_video speech="true"` instead.
- The user pasted a direct link to a `.wav` / `.mp3` / similar file.

## Invocation

```
<aitools_action skill="web_audio" url="https://www.example.com/sounds/serenity-now.wav" speech="true" anchor="george_voice"/>
<aitools_action skill="web_audio" result="P1:a3" anchor="scream"/>
<aitools_action skill="web_audio" url="https://.../theme.mp3" start="10" duration="30"/>
```

- Exactly ONE of `url` / `result`. `url` must point at the sound FILE itself
  (the path ends in .wav/.mp3/.flac/.ogg/.m4a...), not at a page about it.
- `result="P1:a2"` = audio link 2 of page session P1 (web_page lists every bare
  sound-file link on a page as `P<n>:a<i>`).
- There is **no query= mode** - no search engine indexes bare audio. To FIND
  sounds: `web_search kind="web"` for the hosting page ("Seinfeld George wav
  sounds"), `web_page` the best hit, then `web_audio result="P1:aN"` for the
  links it lists. That is one fetch per turn chain; each step auto-continues.
- `speech="true"` whenever the sound will be a VOICE reference: the host checks
  the audio with ffmpeg + Whisper, rejects silent or music-only files, and puts
  the transcript in the caption. Without it any sound is accepted (music and
  effects are fine) and a transcript is still captured when speech-to-text is
  configured.
- `start` / `duration` (seconds) trim the file; omitted = the whole file
  (sources over 300 s are trimmed to 300 s).
- `anchor="name"` so later actions can reference it without guessing the number.
- It auto-continues by default; `resume="false"` opts out.

## Using the result

- Voice for a render: the H3 standalone audio reference IS the way -
  `audio="<N or anchor>"` on a `Reference To Video` / `Reference Video To
  Video` action, referenced in the prompt by its `<Audio N>` tag. STYLE
  conditioning: the voice character / music / ambience is nudged toward the
  sample. ALWAYS assume this path when the user wants someone to sound right;
  do not ask about or offer other voice options.
- `set_video_audio chat_image="<movie>" audio="<N or anchor>"` puts the sound
  itself on a Movie (mix or replace).
- It is a sound, not a picture: never use image skills on it.

## Protected sound sites (expect failures, pick sources accordingly)

Most dedicated sound-clip sites deliberately block direct file downloads: they
serve their audio through JavaScript players, token-bearing URLs built by
scripts (invisible to the page reader), anti-bot 403s, or decoy pages returned
in place of the .mp3/.wav. When a fetch reports "the server returned a WEB PAGE
instead of the sound file", that HOST is protected: do not retry its other
direct links and do not web_page the decoy - move to a different site.
**archive.org is the most reliable host** (its item pages list plain .mp3/.ogg
files that download cleanly), so include "archive.org" in the web_search query
when hunting for a sound, e.g. `query="bugs bunny sound clips archive.org"`.
If no downloadable file exists anywhere, fall back to `web_video
speech="true"`: the fetched talking clip serves as the H3 voice reference
instead.

## Limits

- Only public http/https URLs; never invent URLs. Files over 50 MB abort.
- A URL that turns out to be a page / image / video is refused with a pointer to
  web_page / web_image / web_video. A video file's soundtrack: fetch the video
  with web_video, then `set_video_audio` or use the Movie directly.
- Speech checks need the speech-to-text endpoint (Settings > Web) or an OpenAI
  key; without one only silent files can be rejected and the trace says so.
- The file lives in the session temp cache; the user can save the Audio bubble
  (S copies the original sound file next to the waveform preview).
