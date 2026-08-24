---
id: image_to_movie
summary: Animate a STILL image into a short video. Default to Image To Video (MiniMax H3 Turbo Cache) 5s (native audio/dialog, 8-step turbo + Spectrum cache; progress shows 16 steps - 8 real + 8 cheap replay ticks, that is normal); use the 15s preset only for explicit long clips and duration="N" for ~5-15s. "High quality"/max-quality requests use the 20-step (MiniMax H3 Quality) presets; plain Image To Video (MiniMax H3) 5s is the cache-free variant, used only when the user explicitly asks to skip the cache or blames it for artifacts/audio stutter. Reference To Video presets REQUIRE a photo source and have NO Turbo/Cache/Quality variants - never invent preset names; a video with no source at all is generate_movie (direct t2v) territory. For a video starring reference photos without an exact start frame, use H3 Reference To Video with up to 9 photos. A Movie #N is not an image source by default: edit/reference an existing Movie with video_to_video. Only when the user explicitly wants to animate one current frame may image_to_movie target a Movie, and it must include movie_frame="true".
inputs: attachment
autoload: true
triggers: animate, animation, image to video, image-to-video, image to movie, image-to-movie, animate this, animate it, make this move, make it move, make a video, make a movie, make a clip, create a video, create a movie, generate a video, generate a movie, video starring, movie starring, video of, movie of, second video, second movie, reference to video, minimax, mini max, minmax, minimax h3, minmax h3, h3, hailuo, spectrum, turbo cache, spectrum cache
template: <aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="motion + camera + one short quoted dialog line + ambient sound" chat_image="N"/>  # STILL sources only. attachment= works only in the very message the user pasted the image in; on later turns use chat_image="N" (the paste's bubble number). To use an explicit current frame from a Movie add movie_frame="true"; otherwise existing Movies require video_to_video.
---
# Image-to-movie

Use this skill when the user wants you to ANIMATE an image into a short
video clip. The source can be either freshly pasted or already in chat.

If the user asks for a NEW text-described video with no source image
("make a video of X", "generate a MiniMax video of X"),
first emit `generate_image` with `{{Prompt To Image (Z-Image).txt}}`, then emit
this skill with `chain="true"`. Do NOT use direct `generate_movie` /
text-to-video unless the user explicitly asks for direct text-to-video, "no
still first", or a named `Prompt To Video ...` preset.

Specify EXACTLY ONE source via:

- `attachment="N"` - the Nth image pasted INTO THE CURRENT message (1-based,
  per-message; NOT the bubble number, and invalid on any later turn - use
  chat_image then).
- `chat_image="N"` - the Nth STILL chat-image bubble already in this
  conversation. Use when the user says "animate the image you just made";
  the CHAT IMAGES line in the system prompt shows the highest N reachable.

An existing `Movie #N` must use `video_to_video` for scene, motion, dialogue,
voice, audio, or sound changes. The only exception is an explicit request to
animate one single still/current frame from that Movie; add
`movie_frame="true"`. Unmarked Movie-to-image actions are rejected.

## Available presets

- `{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}` - DEFAULT. ~5s, native
  stereo audio + spoken dialog (11 languages), strong motion/identity. The
  fast 8-step turbo LoRA plus the Spectrum step-forecast cache. NOTE: its
  progress shows "Step N/16" (8 real sampler steps + 8 cheap transformer-free
  replay ticks from the cache's two-pass mode) - that is normal, not a longer
  render. For a specific duration between 5 and 15 seconds, keep THIS preset
  and add `duration="10"` (seconds) - the host snaps it to H3's frame grid
  (~10.1s). Direct text-to-video with the cache is
  `{{Prompt To Video (MiniMax H3 Turbo Cache) 5s.txt}}` via generate_movie;
  there is no 15s or Quality cache variant.
- `{{Image To Video (MiniMax H3) 5s.txt}}` - the same turbo pipeline WITHOUT
  the Spectrum cache ("Step N/8", ~1.4x slower). Use ONLY when the user
  explicitly asks to skip/disable the cache ("no cache", "plain turbo",
  "without spectrum") or blames the cache for artifacts or audio stutter.
  Supports `duration="N"` the same way.
- `{{Image To Video (MiniMax H3) 15s.txt}}` - same model, ~15s single
  generation, plain turbo (there is no 15s cache variant). Only when the user
  explicitly asks for a long clip (~4x the default render time). `duration`
  is ignored here - use the default 5s preset for in-between lengths.
- `{{Image To Video (MiniMax H3 Quality) 5s.txt}}` / `{{Image To Video (MiniMax H3 Quality) 15s.txt}}` -
  the full 20-step render (~3x the default's render time, slightly finer
  detail). Use when the user asks for high/maximum/highest quality or
  complains about turbo/cache output quality.
- `{{Reference To Video (MiniMax H3) 5s.txt}}` - the SUBJECT of the source
  image doing something new; output does NOT start on the source frame. See
  "Reference-to-video" below. REQUIRES at least one photo source
  (`attachment`/`chat_image`/`chain`) - never emit it sourceless. There is NO
  Turbo, Cache, or Quality variant of ANY Reference preset (the reference
  model cannot run the turbo LoRA; reference generations are always the full
  20-step render) - never invent preset names by combining suffixes; use only
  names listed in this skill or generate_movie.

## Invocation

DEFAULT - stack onto the image you JUST generated in this same reply (chain="true").
Size BOTH actions to the video canvas (see "Sizing" below):
```
<aitools_action skill="generate_image" preset="Prompt To Image (Z-Image).txt" prompt="<full Z-Image scene description>" width="864" height="480"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="<full H3 motion + dialog beat>" chain="true" width="864" height="480"/>
```
This stacks the video onto the SAME Pic as the image you just made, so the
chat shows ONE bubble that updates from still -> playing video. Do NOT also pass
attachment / chat_image when you set chain="true" - the prior step's output is
inherited automatically. chain="true" only works as a follow-up to a generate
action emitted earlier in the same reply. This is the right form for any
"<image-model> + <video-model>" combo (e.g. Z-Image + MiniMax H3).

Animate a freshly-pasted image (user dropped/pasted an image THIS turn):
```
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="slow camera push-in, leaves rustling" attachment="1"/>
```

Animate an image already in the chat from earlier (numbered bubble):
```
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="the wind picks up, hair flutters" chat_image="2"/>
```

## Writing good image-to-video prompts

The model sees the source, but the prompt still needs enough visual subject
description to anchor the motion. For ordinary one-off images, keep the still
scene brief and focus on what changes over time. For roleplay, recurring
characters, or identity anchors, the first sentence MUST restate the visible
person fully: apparent age, ethnicity/complexion, build, hair, face, wardrobe,
and expression. Never animate "Mara", "Bob", "the heroine", or "the same
person" by name only.

### MiniMax H3 (`{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}`) - DEFAULT

H3 generates video AND native stereo audio (speech, ambience, music cues) in
one pass, and understands the source frame as `<Picture 1>`.

- For a single continuous scene, use 4-8 sentences, one paragraph - subject
  restatement -> motion + ONE short quoted dialog line -> one camera move ->
  mood/lighting -> ambient-sound tag.
- **Dialog is DEFAULT ON.** Give any plausible speaker a short line (~3-12
  words) in double quotes with language + accent, e.g. `she murmurs "I told
  him I was done" in English with a soft New York accent`. Write EXACT words,
  never "she says something". Skip ONLY when there is no plausible speaker
  (empty landscape, face hidden, user said "silent").
- **Multi-shot IS allowed**: H3 is trained for explicit cuts.
  Structure them as numbered shots, each with concrete motion:
  `SHOT 1: the scene opens exactly on image 1, ... SHOT 2: cut to an extreme
  macro profile of ..., ... SHOT 3: cut to a low-angle beauty shot ...`.
  Keep the environment/palette description constant across shots. 1-2 shots
  fit the 5s preset; save 3-4 shots for the 15s preset.
- A `Timeline: [0s-1s] ... [1s-3s] ...` block is also understood for precisely
  timed beats.
- Do NOT use negative prompts with H3; there is no negative path.
- Avoid vague energy words ("dynamic", "epic") - describe literal motion.

### Reference-to-video (`{{Reference To Video (MiniMax H3) 5s.txt}}`)

Use INSTEAD of the normal i2v preset when the user wants the PERSON/OBJECT
from an image doing something new - not that exact frame animated. The output
does not start on the source image; the reference locks identity/style. This
is ideal for anchors: "make a video of Mara surfing" from an anchored portrait.

- The prompt MUST refer to the reference as `<Picture 1>` (e.g. `The woman
  from <Picture 1>, now in a wetsuit, carves across a wave...`), and should
  still restate her key visible traits once.
- Same source attributes as normal (`attachment` / `chat_image` / `chain`).
- UP TO 9 PHOTOS: add `chat_image2`..`chat_image9` (or `attachment2`..
  `attachment9`) for more references - `<Picture 2>`..`<Picture 9>` in slot
  order (e.g. `<Picture 1>` = the person, `<Picture 2>` = a second character,
  `<Picture 3>` = the setting). Unused slots are pruned automatically, same
  preset name.
- **This is THE way to make a video STARRING several existing people**: one
  action, each person's photo(s) in their own slots. Do NOT build a Klein
  composite still first and animate it - that is only for pinning an exact
  start frame (see the two-stage recipe below).
- Reference each supplied photo by its `<Picture N>` tag in the prompt and
  give it ONE job. Multiple photos of the SAME person are encouraged (better
  identity lock) but must be described as ONE character: `the man from
  <Picture 1> and <Picture 2>` - never as two people standing together.
- Dialog/audio rules are the same as normal H3.
- Describe each referenced person ONLY from what is visible in their photo(s)
  or caption - NEVER from outside knowledge of a film, show, or actor, even
  when you recognize them. Unfaithful prose (wrong hair color, missing
  wardrobe detail) overrides the photo reference and changes the person; when
  unsure of a trait, leave it out and let the `<Picture N>` tag carry it.
- If a reference is a MOVIE bubble rather than a still, use
  `skill="video_to_video"` with `{{Reference Video To Video (MiniMax H3) 5s.txt}}`
  and `<Video 1>` in the prompt instead - that preset also takes photo
  references and a second clip; see the video_to_video skill. To pull face
  stills OUT of a movie for use as photo refs, use `extract_still` (anchored)
  first.

### Exact START FRAME plus a specific person - two-stage recipe

H3 cannot pin a literal start frame AND take references in one pass (start
frames are the i2v model; references are a different checkpoint). When the
user wants the video to open on an exact frame but with a specific person in
it, build the frame first, then animate it:

1. `image_to_image` with `{{Image To Image Klein Edit 2 Input.txt}}` - insert
   the person (photo in slot 2) into the desired start frame.
2. `image_to_movie` with `{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}` and
   `chain="true"` in the same reply - animates that exact composed frame.

If the opening frame does NOT need to be exact, skip the two-stage recipe and
use reference-to-video: the photo guides identity without pinning frame 1.

Example (H3) for a previously-generated rooftop-smoking image:

> She slowly raises the cigarette and takes a long drag, eyes
> half-closing, then lowers her hand, tilts her head slightly back, and
> exhales a thin plume of smoke that drifts up and camera-right, and
> murmurs "I told him I was done" in English with a soft New York
> accent. Her dark espresso bob is gently lifted by a soft rooftop
> breeze, with a few strands fluttering across her forehead, and the
> loose denim jacket on her shoulders shifts slightly in the wind. The
> camera holds a very slow dolly-in of just a few centimetres over the
> clip, at her chest height. The warm low golden-hour sun continues to
> rim-light her hair in honey amber, the smoke catches the light as it
> drifts, and the Manhattan skyline glows soft behind her. Cinematic
> style of a mid-2010s editorial portrait, Portra 400 film grain,
> natural skin tones; ambient sound of distant city traffic.

Roleplay / identity-anchor example style:

> The woman from the reference image, in her late 20s with olive skin, compact
> athletic build, short black undercut hair, angular cheekbones, dark focused
> eyes, and a small split scar at the right eyebrow, stands waist-deep in a
> flooded archive wearing a soaked charcoal tactical jacket and gripping a red
> flare. She raises the flare, turns toward the glass tank, shoulders tight,
> and whispers "It's still alive" in English with a tense low voice as red
> sparks drift into the blue emergency light. The camera makes one slow push-in
> from a chest-height 35mm medium shot. Server lights ripple awake behind her,
> reflected across black water; ambient sound of humming machines, dripping
> water, and flare crackle.

## Several clips into one film (stitch_video)

When the user wants a SEQUENCE of clips joined into one video ("make 10
clips that tell a story, then stitch them together", "a 1 minute episode"),
emit every clip in ONE reply, give each MOVIE-producing action its own
`anchor="sceneN"` and a `duration="N"` (total = clips x duration; "about a
minute" = 4 x `duration="15"`), and end the reply with one
`stitch_video chat_images="scene1,...,sceneN"`. The host waits for all the
renders and then posts the joined Movie; do not wait a turn or guess Movie
numbers. Full recipe: the `stitch_video` skill.

If the cast are REAL / NAMED / anchored people, each clip is ONE
reference action instead of a still -> movie pair: `image_to_movie` with
`{{Reference To Video (MiniMax H3) 5s.txt}}` and the photo anchors in
`chat_image`, `chat_image2`.. (never a Z-Image lookalike still):

```
<aitools_action skill="image_to_movie" preset="{{Reference To Video (MiniMax H3) 5s.txt}}" prompt="The man from <Picture 1> and the woman from <Picture 2> ... 'exact line' ..." chat_image="alex" chat_image2="sam" duration="15" width="864" height="480" anchor="scene1"/>
<aitools_action skill="image_to_movie" preset="{{Reference To Video (MiniMax H3) 5s.txt}}" prompt="<Picture 1> and <Picture 2> again, ..." chat_image="alex" chat_image2="sam" duration="15" width="864" height="480" anchor="scene2"/>
<aitools_action skill="stitch_video" chat_images="scene1,scene2" anchor="film"/>
```

Invented characters use the still -> movie pair per clip:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<scene 1 still>" width="864" height="480"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="<scene 1 motion>" chain="true" width="864" height="480" duration="10" anchor="scene1"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<scene 2 still>" width="864" height="480"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="<scene 2 motion>" chain="true" width="864" height="480" duration="10" anchor="scene2"/>
<aitools_action skill="stitch_video" chat_images="scene1,scene2" anchor="film"/>
```

## Sizing

The video presets render at **864x480** (~0.4MP). Video cost scales with pixel
count, so an oversized canvas is the single easiest way to make a clip take
twice as long for no visible benefit.

### Chaining a still into a movie: size BOTH actions

When you generate a still and chain a movie onto it in the same reply (the
default text-to-video flow), put the SAME `width`/`height` on BOTH actions, so
the still is made at exactly the video's canvas:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full still prompt>" width="864" height="480"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="<motion prompt>" chain="true" width="864" height="480"/>
```

**The user's request always wins.** If they name a size or format, use it on
both actions: `720p` -> 1280x720, `1080p` -> 1920x1080, `4k`/`2k` -> the
closest sensible size, "vertical/portrait/phone" -> a tall canvas, an explicit
"1024x576" -> exactly that. Only fall back to the defaults below when the user
said nothing about size.

Default canvas when the user did not ask for one:

- landscape (default): `width="864" height="480"`
- portrait / vertical / phone: `width="480" height="864"`
- square: `width="640" height="640"`

Without this the still defaults to 1024x1024 - slower to render than the video
canvas needs, and a needlessly large first frame that just gets resampled.
Bigger canvases cost proportionally more time (1080p is ~5x the pixels of the
default), so don't upsize unless asked.

### Animating an image that already exists

When the source is an `attachment` or `chat_image` (no still made this turn)
and the user named no size, OMIT `width`/`height`. The host automatically
matches the video to the source's aspect while keeping the preset's pixel
budget, so a square source renders a square video with no crop.

When the user DOES name a size (720p, 1080p, "big", an exact WxH), pass it as
`width`/`height` even for existing sources: on start-frame presets the host
treats a size whose aspect differs from the source as a PIXEL BUDGET and
refits it to the source's aspect (720p on a portrait photo renders a ~0.92MP
portrait), so the start frame is never distorted - you don't need to compute
the aspect yourself. To truly CHANGE the shape of the output, crop_resize the
still to the new shape first (start-frame presets stretch, never crop), or
use reference-to-video where no frame is pinned. Both attributes are required
together; they snap to multiples of 32.

### Size keywords

The default canvas is already the fast size. Map vague size words like this,
and put the result on both actions:

- "small" / "low res" -> 640x352 (352x640 portrait, 512x512 square). Don't go
  lower; H3 quality falls apart under ~384p.
- "big" / "high res" / "detailed" -> 1152x640, near H3's trained maximum.
- "720p" -> 1280x720. "1080p" -> 1920x1080. These are above what H3 was
  trained on (~1MP), so they are slower and can look softer, but if the user
  asked for them, use them.
- "high quality" / "highest quality" -> BOTH the `(MiniMax H3 Quality)` preset
  AND a 1280x720-budget canvas (1280x720 landscape / 720x1280 portrait /
  960x960 square). Quality means more steps AND more pixels, not just one.

## Rules

- Use ONLY exact preset names listed in this skill or generate_movie. Never
  construct new names by mixing suffixes (e.g. there is no "Reference To
  Video (MiniMax H3 Turbo Cache)"). If a requested speed/quality variant does
  not exist for a mode, use the closest listed preset and tell the user which
  variant applied (e.g. reference generations ignore the spectrum cache).
- Reference To Video / Reference Video To Video always need at least one
  photo/clip source. A brand-new video with NO source: default recipe is
  Z-Image still + chained image_to_movie onto the DEFAULT
  `{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}`; explicit direct
  text-to-video -> generate_movie with a `Prompt To Video ...` preset.
- Chained still -> movie: put the SAME `width`/`height` on BOTH actions
  (864x480 landscape, 480x864 portrait, 640x640 square). Animating an existing
  attachment/chat_image: omit them and let the host match the source aspect.
- Pick exactly ONE source: `attachment`, `chat_image`, OR `chain="true"`.
  If both `attachment` and `chat_image` are set, `chat_image` wins. `chain="true"`
  must NOT be combined with the others - the chained step inherits the prior
  step's output automatically.
- Describe MOTION/CAMERA over time. For roleplay / identity anchors, also
  restate the visible character identity in the first sentence; name-only
  prompts are not valid.
- Pick ONE camera move with magnitude per shot. Two competing moves fight.
- MiniMax H3 (default): paragraph or numbered SHOT structure, dialog default
  ON, cuts allowed between shots, no negative prompts. 15s preset only on
  explicit request.
- User asked to animate → just do it.
