---
id: video_to_video
summary: Operate on an EXISTING "Movie #N" clip. Two modes - (1) VISUAL-ONLY RESTYLE/EDIT it with Bernini-R, keeping its motion but producing silent output; (2) REFERENCE-generate a brand-NEW MiniMax H3 clip that carries the source's subject/motion/style into a new scene, or creates/replaces dialogue, voice, music, audio, or sound effects. H3 preset: `Reference Video To Video (MiniMax H3) 5s.txt`; its prompt refers to the source as <Video 1>. For explicit LONG ~15s use the 15s preset; for other lengths use duration="N" (~5-15s). H3 also accepts up to 9 reference photos and/or a second clip. Never use image_to_image on a Movie unless the user explicitly requests one still/current frame and the action has movie_frame="true". For smoothing/FPS use rife_video; for animating a STILL use image_to_movie.
inputs: attachment
autoload: true
triggers: video to video, restyle the video, restyle this clip, edit the video, edit the clip, change the video, change the clip, redo the video, redo the clip, make the video, make the clip, the video but, the clip but, same video but, restyle the clip, turn the video, turn the clip, video into, clip into, re-render the video, regenerate the video, based on this video, based on that video, based on the clip, based on this clip, like this video, like the video, like this clip, same character as the video, from this video, from the clip, restyle the movie, edit the movie, change the movie, redo the movie, make the movie, the movie but, same movie but, turn the movie, movie into, re-render the movie, regenerate the movie, based on this movie, based on that movie, based on the movie, like this movie, like the movie, same character as the movie, from this movie, from the movie, use movie, use the movie, use this movie, use that movie, as reference, as a reference, reference video, reference clip, reference movie
exclude_triggers: animate the image, animate this, make a movie of, make a video of a, turn this image into a video, turn the photo into
template: <aitools_action skill="video_to_video" preset="{{Video To Video (Bernini).txt}}" prompt="<visual-only changes + motion to keep, 4-8 sentences>" chat_image="N"/>  # Bernini is SILENT. For new/replaced dialogue, voice, audio, music, or sound effects use preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" and refer to <Video 1> in the prompt. chat_image="N" = source Movie. Slot-2+ adds references: Bernini takes one still; H3 takes up to 9 photos and/or a second Movie. Never use an image skill on a Movie unless the user explicitly asks for its still/current frame and supplies movie_frame="true".
---
# Video-to-video (Bernini-R / MiniMax H3)

Use this skill when the user wants to work FROM an EXISTING video clip. It has
two distinct modes - pick by what the user wants:

1. **VISUAL-ONLY RESTYLE / EDIT (Bernini)** - change the clip's style,
   setting, lighting, or visible content while KEEPING its motion and timeline,
   optionally guided by one reference image. Bernini's output is SILENT: it
   cannot create or replace dialogue, voices, music, audio, or sound effects.
   Preset is always `{{Video To Video (Bernini).txt}}`.
2. **REFERENCE-GENERATE (MiniMax H3)** - make a brand-NEW clip that carries
   the source's subject, style, or motion into a NEW scene or action ("make a
   new video based on this clip", "the same cat but chasing a butterfly",
   "another shot of this character"). This is ALSO the required mode whenever
   the user asks to create/replace dialogue, voice, music, audio, or sound
   effects. The output does NOT preserve the source's timeline; it is a fresh
   ~5s generation with native audio. Preset is
   `{{Reference Video To Video (MiniMax H3) 5s.txt}}`, and the prompt MUST
   refer to the source as `<Video 1>`. The source's SOUNDTRACK is a reference
   too: up to 15s of the clip's frames and audio condition the output, so the
   new clip can carry similar music, voices, and ambience - reference the
   soundtrack as `<Audio 1>` when the user wants that ("same music", "keep the
   voices/score", "sounds like the clip"). For an explicitly LONG result
   ("15 second video like this clip") use
   `{{Reference Video To Video (MiniMax H3) 15s.txt}}` (~3x render time).
   For a specific in-between duration ("10 second video like this clip"),
   keep the 5s preset and add `duration="10"` (seconds; snapped to H3's frame
   grid, ~5-15s range). `duration` is ignored on the 15s preset.
   A silent source clip is fine: the host detects it, drops the audio
   reference automatically, and H3 synthesizes a soundtrack from the prompt
   (there is then no `<Audio 1>` to reference).

   The SAME preset also accepts extra references alongside the clip:
   - **Photo references** (up to 9): stills in `chat_image2`..`chat_image10`
     (existing bubbles or anchors) or `attachment2`..`attachment10` (pasted
     this turn) become `<Picture 1>`..`<Picture 9>` in slot order. This is THE
     way to "put this person into a video like that clip": the clip drives
     motion/camera/audio, the photo locks the person's identity. Multiple
     photos of the SAME person improve the lock, but describe them as ONE
     character (`the man from <Picture 1> and <Picture 2>`), never as two
     people.
   - **A second reference clip**: point `chat_image2` at another MOVIE bubble
     and it becomes `<Video 2>` / `<Audio 2>` (e.g. `<Video 1>` = subject,
     `<Video 2>` = camera style or music source). A movie in `chat_image2`
     always means "second clip"; stills always mean photo references.
   Give each reference ONE job in the prompt and unused slots simply don't
   exist - no preset switching needed.

Rule of thumb: "this video, but different-looking, no sound requested" ->
Bernini restyle. "A NEW video of the same subject/motion" OR any new/replaced
speech/audio/sound -> H3 reference-generate.

If the user only wants smoother playback, interpolation, or higher FPS without
changing the pixels/content, use `rife_video` instead.

If the user instead wants to ANIMATE a still image into a video, that is
`image_to_movie`, not this skill.

## Source selection

The source MUST be a short imported/clipped video. Pick EXACTLY ONE of:

- `chat_image="N"` - the Nth chat bubble that is a VIDEO ("Movie #N" label).
  Use when the user says "restyle the clip you just made" or "edit movie 1" - the
  CHAT IMAGES line in the system prompt shows the reachable Movie numbers. Pointing
  it at a still IMAGE bubble will not work - that is `image_to_movie`, not this skill.
- `chain="true"` - a movie produced by a generate/animate/clip action emitted earlier
  in THIS SAME reply (do not also pass chat_image). Example: clip Movie 1 from 30s
  for 5s, then in the same reply restyle the resulting short clip.

Freshly dropped `.mov` / `.mp4` / `.avi` files are not image attachments. The host
imports them as Movie bubbles. If the user requests a specific segment, call
`clip_video` first, then run this skill with `chain="true"` in the same reply.

## Extra references - faces, characters, looks, second clips

The source MOVIE always stays in `chat_image="N"` (or `chain="true"`); references
ride the SLOT-2+ attributes, NOT the primary one:

- `chat_image2="M"` (then `chat_image3`..`chat_image10`) - existing bubbles
  (number or anchor name).
- `attachment2="M"` (then `attachment3`..`attachment10`) - stills the user pasted
  THIS turn (use `attachment2="1"` for a single pasted face). Do NOT use
  `attachment="2"` / `chat_image="2"` for a reference - those are PRIMARY-source
  attributes and will be ignored here.

What the slots mean depends on the mode:

- **Bernini restyle**: only slot 2 is used, and it must be a STILL. Keep
  `preset="{{Video To Video (Bernini).txt}}"` - the host switches to the
  reference-guided workflow automatically when a reference still is present.
  In the prompt, say what to take from the reference, e.g. "give the runner the
  face and hair of the person in image 2, keeping the original running motion
  and camera exactly."
- **H3 reference-generate**: stills in slots 2-4 are `<Picture 1>`..`<Picture 3>`
  photo references; a MOVIE bubble in `chat_image2` is the second reference clip
  `<Video 2>`. Address them by tag in the prompt and give each one job.

A still the user pastes this turn is auto-detected as a reference even if the slot
syntax is off, but prefer the explicit `attachment2` / `chat_image2` form.

## Writing good v2v prompts

Bernini keeps the source video's motion and timing; the prompt describes what
should CHANGE and what should stay. Lead with the transformation (style,
setting, wardrobe, lighting), then state what motion / framing to preserve.
Do not put spoken lines, music, audio, or sound effects in a Bernini prompt;
use the H3 Reference Video To Video preset for those requests.

- 4-8 sentences, single flowing paragraph.
- Restate the visible subject so the edit stays anchored (apparent age, build,
  hair, wardrobe), then state what changes and what motion to preserve.
- Keep it a tight DELTA when the user wants a small change ("only change the
  season to winter, keep everyone's motion and positions identical"); describe
  a full new scene only when they truly want a full restyle.
- Avoid hard-cut words ("suddenly", "cuts to"); v2v preserves the original cut.

## Invocation examples

Restyle the clip just made (chain):
```
<aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="<motion beat>" chat_image="2"/>
<aitools_action skill="video_to_video" preset="{{Video To Video (Bernini).txt}}" prompt="Re-render the same clip as a hand-painted watercolor animation, keeping every motion, pose, and camera move identical; soft paper texture, muted autumn palette." chain="true"/>
```

Restyle / edit an existing Movie bubble (most common - e.g. "add a hat to the dog in movie 1"):
```
<aitools_action skill="video_to_video" preset="{{Video To Video (Bernini).txt}}" prompt="Keep the woman's exact motion and the camera push-in, but change the setting from a sunny park to a snowy night street, add falling snow, cool blue moonlight, and a wool coat over her outfit. Preserve her face and build." chat_image="1"/>
```

Reference-guided - put the face/look from a still onto the person in the clip
("make the woman in movie 1 have image 2's face"):
```
<aitools_action skill="video_to_video" preset="{{Video To Video (Bernini).txt}}" prompt="Replace the woman's face and hairstyle with the person in image 2 - olive skin, ~30, long dark hair - while keeping her exact body, walking motion, the beach setting, waves, and golden-hour light unchanged." chat_image="1" chat_image2="2"/>
```

Reference-generate a NEW clip from a movie ("make a new video based on movie 1
where the cat plays in snow"):
```
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" prompt="The fluffy orange tabby cat from <Video 1>, now in a snowy garden at dusk, pounces at drifting snowflakes and shakes the snow off its fur. One low lateral tracking move at cat height. Cool blue twilight; ambient sound of soft wind and an excited chirping meow." chat_image="1"/>
```
The H3 reference prompt follows normal H3 style (motion, ONE camera move,
quoted dialog when there is a plausible speaker, ambient sound) and must
name the source as `<Video 1>`, restating its key visible traits once.

Put a specific person (from a photo) into a new clip guided by the source
("make a video like movie 1 but starring the person in image 3"):
```
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" prompt="The woman from <Picture 1> - preserve her exact face, shoulder-length copper hair, and green jacket - walks the same beachside path as <Video 1>, matching its steady tracking shot and relaxed pace. <Audio 1> supplies the ambient waves and distant gulls. She smiles and says 'what a morning'." chat_image="1" chat_image2="3"/>
```

Two reference clips - subject from one, camera/music from the other:
```
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" prompt="The husky from <Video 1> runs through a sunflower field. Use the slow orbiting drone move and the upbeat acoustic track from <Video 2> / <Audio 2>. Golden-hour light, petals drifting in the wind." chat_image="1" chat_image2="4"/>
```

## Rules

- v2v source must be a VIDEO (a "Movie #N" bubble or a chained movie), not a still
  image. If the user only has a still, use image_to_movie.
- Visual-only restyle/edit (keep the timeline, silent result) -> `{{Video To Video (Bernini).txt}}` - the host
  swaps to the reference-guided workflow automatically when you add a `chat_image2`
  still. NEW clip from the source's subject/motion, or any new dialogue/voice/audio/
  music/sound effect -> `{{Reference Video To Video (MiniMax H3) 5s.txt}}` with
  `<Video 1>` in the prompt. Pick by intent; never mix the two presets up.
- To inject a face/character/style from a still: Bernini uses ONE `chat_image2`
  still; the H3 reference presets take up to nine (`chat_image2`..`chat_image10`
  -> `<Picture 1>`..`<Picture 9>`) plus optionally a second MOVIE in `chat_image2`
  (-> `<Video 2>`). The source MOVIE always stays in `chat_image="N"`.
- Pick exactly ONE source MOVIE; `chain="true"` must not be combined with `chat_image`.
- `chat_image="N"` must reference a Movie bubble. If you point it at a still image
  the action will report that it needs a video source.
- Describe the CHANGE plus the motion to preserve; the model already sees the
  source video.
