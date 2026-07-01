---
id: generate_movie
summary: Make a new video from a text description. DEFAULT recipe is two actions: generate a Z-Image still, then animate it with image_to_movie chain="true". For "WAN video", use `Image To Video (WAN) 5s.txt` as the second preset; otherwise use LTX. Use direct text-to-video (`skill="generate_movie"`) only when the user explicitly asks for direct/text-to-video or no still-image base.
inputs: none
autoload: true
triggers: generate a video, generate video, make a video, create a video, create video, generate a movie, make a movie, create a movie, generate a clip, make a clip, wan video, wan movie, ltx video, ltx movie, prompt to video, text to video, text-to-video, direct video
exclude_triggers: edit this video, restyle this video, video to video, video-to-video, animate this image, animate this, animate it, image to video, image-to-video
template: <aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="full self-contained still-image scene prompt"/><aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="motion + one camera move + one short quoted dialog line if plausible, then ambient sound" chain="true"/>
---
# Generate a movie

Use this skill when the user asks for a NEW short video / animation /
clip / movie from a text description and has not supplied a source image.

## Default Workflow

For normal "make a video of X" requests, DO NOT use direct text-to-video.
Build the clip in two actions:

1. Generate a strong still frame with `{{Prompt To Image (Z-Image).txt}}`.
2. Animate that exact still with `image_to_movie chain="true"`.

This gives the video model a concrete first frame, which is more reliable than
raw text-to-video for both LTX and WAN.

Generic or LTX video:
```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full Z-Image still prompt: subject, wardrobe, pose, setting, lighting, camera, style>"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="<4-8 sentence LTX motion prompt with one camera move, one short quoted dialog line if plausible, and ambient sound>" chain="true"/>
```

WAN / Wan 2.2 video:
```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full Z-Image still prompt for the opening frame>"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (WAN) 5s.txt}}" prompt="<silent WAN motion prompt: subject, scene, motion over time, camera, lighting, style>" chain="true"/>
```

The chained movie action carries ONLY `chain="true"` plus its preset/prompt.
Do not also pass `attachment` or `chat_image`; the prior Z-Image result is
inherited automatically.

## Model Choice

- If the user says **WAN**, **Wan 2.2**, or asks for higher-quality silent
  video, use `{{Image To Video (WAN) 5s.txt}}` for the second action.
- If the user says **LTX** or does not name a video model, use
  `{{Image To Video (LTX) 5s.txt}}` for the second action.
- Always use `{{Prompt To Image (Z-Image).txt}}` for the still base unless the
  user explicitly names a different still-image model.

## Direct Text-To-Video Escape Hatch

Only use `skill="generate_movie"` directly when the user explicitly asks for:

- "text-to-video", "direct text-to-video", or "prompt to video";
- "do not generate an image first" / "no still-image base";
- a specific `Prompt To Video ...` preset.

Direct text-to-video presets are still available, but they are NOT the default:

- `{{Prompt To Video (LTX) 5s.txt}}` - fast 5s text-to-video with audio.
- `{{Prompt To Video (Wan22).txt}}` - slow silent Wan 2.2 text-to-video.

Direct LTX example, only for explicit direct T2V:
```
<aitools_action skill="generate_movie" preset="{{Prompt To Video (LTX) 5s.txt}}" prompt="4-8 sentence direct text-to-video prompt"/>
```

## Prompt Writing

### Z-Image Still Base

Write a full still-image prompt, not the user's short wording. Include visible
subject identity, clothing, pose/body language, exact setting, lighting, camera,
and style. The opening frame should already look like the video the user asked
for.

For "a Japanese woman playing basketball", the still prompt should choose the
court, time of day, wardrobe, pose, camera, and style explicitly instead of
passing that phrase unchanged.

### LTX Image-To-Video

Use 4-8 sentences in one paragraph:

1. Restate the visible subject briefly.
2. Describe concrete motion over the 5 seconds.
3. Include ONE short quoted line of in-scene dialog if there is a plausible
   speaker, with language + accent. Skip only when the user asks for silent
   video or the scene has no plausible speaker.
4. Add one camera move.
5. Add mood/lighting continuity and ambient sound.

Avoid jump cuts ("suddenly", "cuts to") and vague motion words ("dynamic",
"epic"). The source image already exists; the motion prompt should tell LTX how
that image moves.

### WAN Image-To-Video

WAN is silent. Do not include dialog or sound tags. Use a longer motion prompt:
Subject -> Scene -> Motion -> Aesthetic Control -> Stylization. Describe body
motion, environmental motion, camera motion, and lighting changes over time.

## Small Size

If the user asks for a **"small size"** (or "small" / "low res") video, render
it at **832x480** (or **480x832** for a clearly portrait scene) - WAN's native
fast resolution, and fine for LTX too. Because the chained movie only inherits
the still's ASPECT and keeps the video preset's own pixel budget, put the same
`width`/`height` on BOTH actions - the `generate_image` still AND the
`image_to_movie` step - so the first frame is small too. See `image_to_movie` ->
"Small size" for the full example.

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<still prompt>" width="832" height="480"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (WAN) 5s.txt}}" prompt="<motion prompt>" chain="true" width="832" height="480"/>
```

## Rules

- User asked for a new video -> spawn it, no confirmation.
- Default is Z-Image still -> `image_to_movie chain="true"`.
- "Small size" video -> 832x480 (or 480x832 portrait), applied to BOTH the
  still and the movie action.
- "WAN video of X" means Z-Image still -> `Image To Video (WAN) 5s.txt`, NOT
  direct `Prompt To Video (Wan22).txt`.
- "LTX video of X" and generic "make a video of X" mean Z-Image still ->
  `Image To Video (LTX) 5s.txt`, unless the user explicitly asks for direct
  text-to-video.
- Default to 5s unless the user asks longer.
