---
id: rife_video
summary: Add 2x RIFE frame interpolation to an existing Movie #N without changing content. Use when the user asks to smooth/interpolate/raise FPS of a video or says "run RIFE on this clip". This is not a restyle/edit model; it preserves the source video and creates a smoother Movie bubble. For content edits such as hats, outfits, backgrounds, or style changes use video_to_video instead.
inputs: none
autoload: true
triggers: rife, run rife, interpolate video, interpolate the video, smooth video, smooth the video, increase fps, raise fps, higher fps, frame interpolation, make it smoother, smoother clip, smoother video
exclude_triggers: add a hat, change the video, restyle the video, edit the video, remove something, replace something
template: <aitools_action skill="rife_video" chat_image="N"/>  # chat_image must be an existing Movie #N bubble. Use chain="true" when interpolating a movie created/clipped earlier in THIS reply. Optional: fps="60" only when the user explicitly wants a fixed output FPS.
---
# RIFE video interpolation

Use this skill when the user wants to make an existing video smoother using
RIFE frame interpolation. It does not change the subject, style, prompt, camera,
lighting, clothing, or scene; it only inserts interpolated frames.

## Invocation

Existing Movie bubble:
```
<aitools_action skill="rife_video" chat_image="1"/>
```

After clipping or generating a movie earlier in the same reply:
```
<aitools_action skill="clip_video" chat_image="1" start="30" duration="5"/>
<aitools_action skill="rife_video" chain="true"/>
```

## Rules

- Source must be a Movie bubble, not a still image.
- Use this only for smoothing/interpolation/FPS requests.
- Do not use this for visual edits. For "add a hat", "change the outfit",
  "make it anime", etc., use `video_to_video`.
- Default is 2x RIFE. The output FPS is automatically source FPS x 2, so timing
  stays the same.
- Add `fps="N"` only when the user explicitly requests a fixed output frame
  rate. A mismatched FPS can change playback timing, so normally omit it.
