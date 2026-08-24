---
id: generate_movie
summary: Make a new video from a text description. DEFAULT recipe is two actions: generate a Z-Image still, then animate it with image_to_movie chain="true" using `Image To Video (MiniMax H3 Turbo Cache) 5s.txt`. Use direct text-to-video (`skill="generate_movie"`) only when the user explicitly asks for direct/text-to-video or no still-image base.
inputs: none
autoload: true
triggers: generate a video, generate video, make a video, create a video, create video, generate a movie, make a movie, create a movie, generate a clip, make a clip, minimax video, minimax movie, minmax video, h3 video, prompt to video, text to video, text-to-video, direct video
exclude_triggers: edit this video, restyle this video, video to video, video-to-video, animate this image, animate this, animate it, image to video, image-to-video
template: <aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="full self-contained still-image scene prompt"/><aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="motion + one camera move + one short quoted dialog line if plausible, then ambient sound" chain="true"/>
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
raw text-to-video.

Note the SAME `width`/`height` on both actions - that makes the still at
exactly the video's canvas instead of a needlessly large 1024x1024 frame (see
"Sizing" below):
```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full Z-Image still prompt: subject, wardrobe, pose, setting, lighting, camera, style>" width="864" height="480"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="<H3 motion prompt with one camera move, one short quoted dialog line if plausible, and ambient sound>" chain="true" width="864" height="480"/>
```

The chained movie action carries ONLY `chain="true"` plus its preset/prompt.
Do not also pass `attachment` or `chat_image`; the prior Z-Image result is
inherited automatically.

## Model Choice

- If the user does not name a video model, or says **MiniMax / MinMax / H3 /
  Hailuo**, use `{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}` for the
  second action (native stereo audio + dialog, multi-shot capable; 8-step
  turbo LoRA + Spectrum cache, the fastest H3 path). Cache runs show
  "Step N/16" (8 real steps + 8 cheap replay ticks) - normal, not slower.
- If the user asks for a specific duration between 5 and 15 seconds (e.g.
  "a 10 second video"), keep `{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}`
  and add `duration="10"` (seconds) to the movie action - the host snaps it
  to H3's frame grid (so ~10.1s).
- If the user explicitly asks for a LONG (~15 second) clip, use
  `{{Image To Video (MiniMax H3) 15s.txt}}` (plain turbo - there is no 15s
  cache variant - roughly 4x the default render time). `duration` is ignored
  on the 15s preset.
- When the user asks for high/maximum quality, use the full 20-step
  `{{Image To Video (MiniMax H3 Quality) 5s.txt}}` / `... 15s.txt` /
  `{{Prompt To Video (MiniMax H3 Quality) 5s.txt}}` variants (~3x render time),
  AND raise the canvas to the 1280x720 budget (1280x720 landscape / 720x1280
  portrait / 960x960 square) on both actions - "high quality" means more steps
  and more pixels.
- If the user explicitly asks to SKIP the cache ("no cache", "plain turbo",
  "without spectrum") or blames it for artifacts/audio stutter, use
  `{{Image To Video (MiniMax H3) 5s.txt}}` for the movie action, or
  `{{Prompt To Video (MiniMax H3) 5s.txt}}` for direct text-to-video - the
  same turbo pipeline without the cache ("Step N/8", ~1.4x slower).
  Reference To Video presets have NO cache/turbo variant (reference
  generations always run the full 20-step model) - never invent such a
  preset name; if references are needed, use the plain Reference preset and
  tell the user the cache does not apply.
- Always use `{{Prompt To Image (Z-Image).txt}}` for the still base unless the
  user explicitly names a different still-image model.

## Direct Text-To-Video Escape Hatch

Only use `skill="generate_movie"` directly when the user explicitly asks for:

- "text-to-video", "direct text-to-video", or "prompt to video";
- "do not generate an image first" / "no still-image base";
- a specific `Prompt To Video ...` preset.

Direct text-to-video presets are still available, but they are NOT the default:

- `{{Prompt To Video (MiniMax H3 Turbo Cache) 5s.txt}}` - the direct-t2v
  DEFAULT: 5s text-to-video with native audio, turbo + Spectrum cache.
- `{{Prompt To Video (MiniMax H3) 5s.txt}}` - the same without the cache; only
  on explicit no-cache requests or cache-blamed artifacts.
- `{{Prompt To Video (MiniMax H3 Quality) 5s.txt}}` - full 20-step render, ~3x
  time; for high/maximum-quality requests.

Direct example, only for explicit direct T2V:
```
<aitools_action skill="generate_movie" preset="{{Prompt To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="full direct text-to-video prompt"/>
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

### MiniMax H3 Image-To-Video (default)

Single-scene: 4-8 sentences, one paragraph, subject restatement, concrete
motion, ONE short quoted dialog line (language + accent) if there is a
plausible speaker, one camera move, mood/lighting, ambient sound. Avoid jump
cuts ("suddenly", "cuts to") and vague motion words ("dynamic", "epic"). H3
also supports explicit multi-shot structure (`SHOT 1: ... SHOT 2: cut to ...`)
- 1-2 shots at 5s. No negative prompts for H3. See `image_to_movie` ->
"MiniMax H3" for full guidance.

## Sizing

Always put the SAME `width`/`height` on BOTH actions of the pair, so the still
is made at exactly the video's canvas rather than the 1024x1024 default.

**Whatever size the user asks for wins**: "720p" -> 1280x720, "1080p" ->
1920x1080, "vertical" -> a tall canvas, an explicit "1024x576" -> exactly that.
Only when they say nothing about size, use the defaults:

- landscape (default): `width="864" height="480"`
- portrait / vertical: `width="480" height="864"`
- square: `width="640" height="640"`

"small / low-res" -> 640x352 (352x640 portrait, 512x512 square); "big /
high-res" -> 1152x640. Render time scales with pixel count, so don't upsize
unless asked. See `image_to_movie` -> "Sizing" for details.

## Rules

- User asked for a new video -> spawn it, no confirmation.
- Default is Z-Image still -> `image_to_movie chain="true"` with
  `{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}`.
- ALWAYS put the same `width`/`height` on both actions. Use the size the user
  asked for (720p -> 1280x720, 1080p -> 1920x1080); if they said nothing, use
  864x480 landscape, 480x864 portrait, or 640x640 square. "Small" -> 640x352.
- "MiniMax / H3 video of X" and generic "make a video of X" mean Z-Image
  still -> `Image To Video (MiniMax H3 Turbo Cache) 5s.txt`, unless the user
  explicitly asks for direct text-to-video.
- Default to 5s unless the user asks longer. A specific 6-14s duration ->
  the default 5s cache preset + `duration="N"` on the movie action; ~15s ->
  the H3 15s preset.
- SEVERAL clips joined into one film ("make 10 videos telling a story, then
  stitch them together"): emit every pair in ONE reply, give each MOVIE
  action `anchor="sceneN"`, and end the reply with one
  `stitch_video chat_images="scene1,...,sceneN"`. The host waits for all the
  renders and posts the joined Movie; never wait a turn or guess Movie
  numbers. Recipe: the `stitch_video` skill.
