---
id: video_to_video
summary: Restyle / edit an EXISTING short video clip into a new video, optionally guided by a reference image (e.g. "restyle this clip", "make this video look like winter", "turn the video anime", "redo the clip with her as the character in image 2"). Source is a video already in chat as a "Movie #N" bubble. Freshly dropped home videos are imported/clipped into Movie bubbles first; use clip_video for explicit start/duration trims. Uses ByteDance Bernini-R for content edits/restyles. For pure smoothing/FPS interpolation use rife_video instead. For animating a STILL image into a video use image_to_movie instead.
inputs: attachment
autoload: true
triggers: video to video, restyle the video, restyle this clip, edit the video, edit the clip, change the video, change the clip, redo the video, redo the clip, make the video, make the clip, the video but, the clip but, same video but, restyle the clip, turn the video, turn the clip, video into, clip into, re-render the video, regenerate the video
exclude_triggers: animate the image, animate this, make a movie of, make a video of a, turn this image into a video, turn the photo into
template: <aitools_action skill="video_to_video" preset="{{Video To Video (Bernini).txt}}" prompt="<what changes + motion to keep, 4-8 sentences>" chat_image="N"/>  # chat_image="N" = source "Movie #N" bubble (or chain="true" for a movie made/clipped earlier THIS reply). SUPPORTS a reference still for face/character/look swaps: ALSO add chat_image2="M" (existing still bubble or anchor) or attachment2="M" (a still pasted this turn) - the host auto-switches to the reference-guided workflow. Do NOT use image_to_image+image_to_movie for this; v2v takes the reference directly.
---
# Video-to-video (Bernini-R)

Use this skill when the user wants to RESTYLE or EDIT an EXISTING video clip
into a new clip - change its style, setting, lighting, or content while keeping
its motion - optionally guided by one reference image. This is the content-edit
video-to-video path in the app, so it always uses the Bernini preset; never pick
a different preset for restyle/edit v2v.

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

## Reference still (optional) - inject a face / character / look from an image

To steer the edit with a REFERENCE STILL - put a specific person's face into the
clip, give the subject the outfit/look of a reference image, apply a style frame:

- The source MOVIE stays in `chat_image="N"` (or `chain="true"`).
- The reference STILL goes in the SLOT-2 attribute, NOT the primary one:
  - `chat_image2="M"` - an existing still bubble (number or anchor name).
  - `attachment2="M"` - the Mth still the user pasted THIS turn (use `attachment2="1"`
    for a single pasted face). Do NOT use `attachment="2"` / `chat_image="2"` for a
    reference - those are PRIMARY-source attributes and will be ignored here.

A still the user pastes this turn is auto-detected as the reference even if the slot
syntax is off, but prefer the explicit `attachment2` / `chat_image2` form. Keep
`preset="{{Video To Video (Bernini).txt}}"` - the host switches to the reference-guided
workflow automatically when a reference still is present.

In the prompt, say what to take from the reference, e.g. "give the runner the face and
hair of the person in image 2, keeping the original running motion and camera exactly."
Only ONE reference still is used.

## Writing good v2v prompts

Bernini keeps the source video's motion and timing; the prompt describes what
should CHANGE and what should stay. Lead with the transformation (style,
setting, wardrobe, lighting), then state what motion / framing to preserve.

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

## Rules

- v2v source must be a VIDEO (a "Movie #N" bubble or a chained movie), not a still
  image. If the user only has a still, use image_to_movie.
- Always use `{{Video To Video (Bernini).txt}}` - the host swaps to the reference-guided
  workflow automatically when you add a `chat_image2` still.
- To inject a face/character/style from a still, add `chat_image2="M"` (only one
  reference still is used); the source MOVIE stays in `chat_image="N"`.
- Pick exactly ONE source MOVIE; `chain="true"` must not be combined with `chat_image`.
- `chat_image="N"` must reference a Movie bubble. If you point it at a still image
  the action will report that it needs a video source.
- Describe the CHANGE plus the motion to preserve; the model already sees the
  source video.
