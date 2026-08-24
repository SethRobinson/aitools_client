---
id: set_video_audio
summary: Put a SOUND onto an existing Movie #N (local FFmpeg, no GPU, seconds) - MIX a generated song / sound effect / spoken line under or over the clip's own soundtrack, or REPLACE the soundtrack entirely. The picture is untouched; the result is a new Movie bubble of the same length. chat_image = the Movie, audio = an Audio #N bubble (output of generate_music / generate_sfx / generate_speech, or a dropped-in audio file) or another Movie (its soundtrack is taken). Use THIS - never video_to_video - whenever the user wants existing or generated AUDIO attached to a video: video_to_video re-renders the picture and invents its own audio. The host waits for a still-rendering Movie, so "make a video and a song, then put them together" is ONE reply.
inputs: none
autoload: true
triggers: add it to the video, add it to the movie, add it to the clip, add that to the video, add the song to, add music to, add the music to, add a song to, add a soundtrack, add sound to, add the sound to, add audio to, add the audio to, add narration to, add a voiceover to, add voice over, put the song on, put music on, put it on the video, put it over the video, replace the audio, replace the video's audio, replace the videos audio, replace the sound, replace the music, replace the soundtrack, swap the audio, swap the soundtrack, new soundtrack, background music for, music under the video, music over the video, over the video, onto the video, dub the video, dub the clip, soundtrack for, score the video, set the audio, change the audio to, use the audio from, use the sound from, use the music from, mute the video and, with the song, with that song, with the music, with the sound effect
template: <aitools_action skill="set_video_audio" chat_image="N" audio="song" mode="mix" original_volume="0.3"/>  # chat_image = the Movie #N (or its anchor). audio = an Audio #N number, or the anchor you gave a same-reply generate_music / generate_sfx / generate_speech (never a guessed future number). mode="mix" (default) keeps the clip's own track - duck it with original_volume (0.2-0.5 under music); mode="replace" drops it. Optional: volume="1.0" (new track gain), start="1.5" (seconds into the video where the audio begins), loop="true" (repeat a short sound to fill the clip), fade_out="1.5", fade_in="0.5", resume="true" to get a turn once it is done. Size a same-reply generate_music duration to the Movie's listed length, minimum 10 (e.g. a 5.2s clip -> duration="12"); the audio is cut to the video anyway.
---
# Set video audio (mix / replace a clip's soundtrack)

Attach a sound file to an existing `Movie #N` bubble. This is a LOCAL FFmpeg
operation: no GPU, no ComfyUI, a couple of seconds. The video stream is copied
untouched; only the audio track changes. The result is a NEW Movie bubble of
exactly the source's length (the audio is padded with silence or cut, optionally
looped and faded), and the source Movie stays in the chat.

## When to use it

- The user wants a **generated song / jingle / sound effect / spoken line ON a
  video**: "generate a song about bananas and add it to the video", "put a door
  slam at the start of clip 2", "narrate movie 3 with this text".
- The user wants to **replace** a clip's audio: "replace the video's audio with
  a funny song about computers", "mute it and put music under it".
- The user wants **another clip's soundtrack** on this clip: `audio="4"` where
  #4 is a Movie.

Do NOT use `video_to_video` for any of these. H3 Reference Video To Video makes
a brand-new picture with its own invented audio; Bernini is silent. Only when the
user wants the CONTENT of the video changed (new dialogue spoken by the
character, different scene) is `video_to_video` right.

## Invocation

```
<aitools_action skill="set_video_audio" chat_image="2" audio="5" mode="mix" original_volume="0.3"/>
```

- `chat_image` - the VIDEO: a `Movie #N` number or anchor. Defaults to the
  newest Movie bubble, but be explicit.
- `audio` - the SOUND: an `Audio #N` number, the anchor of a same-reply audio
  action, or a Movie number (its soundtrack is used). Defaults to the newest
  Audio bubble. Aliases: `sound`, `music`, `song`, `track`.
- `mode` - `mix` (default): keep the clip's own soundtrack and layer the new
  audio over it. `replace`: the new audio is the only track. A silent clip
  behaves the same either way.
- `original_volume` - gain of the clip's own track in mix mode (1.0 = as is).
  Use 0.2-0.5 to duck dialogue/ambience under music, 0.6-0.8 for a subtle sound
  effect that must not bury the speech.
- `volume` - gain of the new audio (1.0 default; 0.5 quieter, 1.5 louder).
- `start` - seconds into the video where the new audio begins (default 0).
  Use it to place a sound effect ("the crash at 2.5 seconds").
- `loop="true"` - repeat audio that is shorter than the video (ambience loops).
  Default off: a short sound plays once and the rest stays as it was.
- `fade_out` / `fade_in` - seconds. Fade-out defaults to 1 s automatically when
  the audio is cut off by the end of the video (a song longer than the clip),
  else 0. Set `fade_out="0"` to force a hard cut.
- `resume="true"` - get a continue turn once the new Movie exists (only when
  you still have something to do with it).

## Same-reply recipes

**Song + existing video** (the common case). Read the Movie's length from CHAT
IMAGES (`864x480 @24fps, 5.2s`) and size the music a little longer than that
(never below the music model's 10 s minimum); `mode="music"` is implied by
`generate_music`:

```
<aitools_action skill="generate_music" prompt="<structured caption for a silly upbeat ukulele song about bananas>" duration="12" vocals="true" lyrics="[verse]\nBananas in the morning, bananas in my shoe\n[chorus]\nYellow yellow banana, I'm bananas over you" anchor="banana_song"/>
<aitools_action skill="set_video_audio" chat_image="3" audio="banana_song" mode="mix" original_volume="0.25"/>
```

**Replace the audio**: same, with `mode="replace"`.

**Video AND song from scratch in one reply**: the host parks the mix until the
render lands, so never wait a turn to "check on" the clip:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="..." width="864" height="480"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="..." chain="true" anchor="banana_clip"/>
<aitools_action skill="generate_music" prompt="..." duration="12" anchor="banana_song"/>
<aitools_action skill="set_video_audio" chat_image="banana_clip" audio="banana_song" mode="replace"/>
```

**Sound effect at a moment**:

```
<aitools_action skill="generate_sfx" prompt="heavy wooden door slamming shut, indoor, short tail" duration="1.5" anchor="slam"/>
<aitools_action skill="set_video_audio" chat_image="2" audio="slam" mode="mix" start="2.4" original_volume="0.8"/>
```

**Narration / voice-over**:

```
<aitools_action skill="generate_speech" text="And that is how the banana saved the day." voice="belinda" scene="warm documentary narrator" anchor="vo"/>
<aitools_action skill="set_video_audio" chat_image="2" audio="vo" mode="mix" original_volume="0.4" start="0.5"/>
```

Use anchors for same-reply references; `chain="true"` is wrong here (it would
make the AUDIO the chain target).

## Notes

- Audio bubbles (`Audio #N`) are sound files shown as a waveform; the user can
  play them from the bubble. They are not pictures: never send them to image or
  video skills.
- The new Movie keeps the source clip's description plus a "Soundtrack: ..."
  note, so you can describe it without re-inspecting.
- Later edits: the result is a normal Movie bubble (clip, stitch, extract_still,
  another set_video_audio, `<Video 1>` reference all work).
