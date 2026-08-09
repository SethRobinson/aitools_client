---
id: image_to_movie
summary: Animate an image (user-pasted OR a previously-generated chat image) into a short video clip. Default to MiniMax H3 (`Image To Video (MiniMax H3) 5s.txt`, native audio + dialog). Explicit LONG ~15s request -> `Image To Video (MiniMax H3) 15s.txt` (~3x render time); other lengths keep the 5s H3 preset and add duration="N" (seconds, ~5-15, H3 only - LTX/WAN ignore it). A video STARRING existing people/photos (identity from references, no exact start frame) -> `Reference To Video (MiniMax H3) 5s.txt` with the photos in chat_image/chat_image2..chat_image9 (up to 9 refs; prompt calls them <Picture 1>..<Picture 9>) - do NOT composite a Klein still first. Use LTX 2.3 via `Image To Video (LTX) 5s.txt` when the user asks for LTX or the fastest option; use WAN / Wan 2.2 via `Image To Video (WAN) 5s.txt` when the user asks for WAN or silent video.
inputs: attachment
autoload: true
triggers: animate, animation, image to video, image-to-video, image to movie, image-to-movie, animate this, animate it, make this move, make it move, make a video, make a movie, make a clip, create a video, create a movie, generate a video, generate a movie, video starring, movie starring, video of, movie of, second video, second movie, reference to video, using wan, use wan, with wan, wan 2.2, wan2.2, wan22, using ltx, use ltx, with ltx, ltx 2.3, minimax, mini max, minmax, minimax h3, minmax h3, h3, hailuo
template: <aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3) 5s.txt}}" prompt="describe motion + camera, with ONE short line of in-scene dialog in double-quotes (language+accent), then ambient sound" chat_image="N"/>  # default MiniMax H3 (native audio). When the user asks for LTX use preset="{{Image To Video (LTX) 5s.txt}}"; for WAN/Wan 2.2 use preset="{{Image To Video (WAN) 5s.txt}}" with a silent motion prompt.
---
# Image-to-movie

Use this skill when the user wants you to ANIMATE an image into a short
video clip. The source can be either freshly pasted or already in chat.

If the user asks for a NEW text-described video with no source image
("make a video of X", "generate a MiniMax/WAN/LTX video of X"),
first emit `generate_image` with `{{Prompt To Image (Z-Image).txt}}`, then emit
this skill with `chain="true"`. Do NOT use direct `generate_movie` /
text-to-video unless the user explicitly asks for direct text-to-video, "no
still first", or a named `Prompt To Video ...` preset.

Specify EXACTLY ONE source via:

- `attachment="N"` - the Nth image pasted INTO THE CURRENT message (1-based).
- `chat_image="N"` - the Nth chat-image bubble already in this conversation
  ("Image #N" / "Movie #N" labels). Use when the user says "animate the
  image you just made"; the CHAT IMAGES line in the system prompt shows
  the highest N reachable.

## Available presets

- `{{Image To Video (MiniMax H3) 5s.txt}}` - DEFAULT. ~5s, native stereo audio +
  spoken dialog (11 languages), strong motion/identity. MiniMax H3. For a
  specific duration between 5 and 15 seconds, keep THIS preset and add
  `duration="10"` (seconds) - the host snaps it to H3's frame grid (~10.1s).
  H3 presets only; LTX/WAN are fixed-length.
- `{{Image To Video (MiniMax H3) 15s.txt}}` - same model, ~15s single
  generation. Only when the user explicitly asks for a long clip (~3x render
  time). `duration` is ignored here - use the 5s preset for in-between lengths.
- `{{Reference To Video (MiniMax H3) 5s.txt}}` - the SUBJECT of the source
  image doing something new; output does NOT start on the source frame. See
  "Reference-to-video" below.
- `{{Image To Video (LTX) 5s.txt}}` - fast 5s clip with audio (LTX 2.3). Use
  when the user asks for LTX or the fastest/quickest video.
- `{{Image To Video (WAN) 5s.txt}}` - high-quality 5s, slow, silent (Wan 2.2 / WAN).
- `{{Image To Video (Wan22).txt}}` - legacy alias for the same production Wan 2.2 path.

## Invocation

DEFAULT - stack onto the image you JUST generated in this same reply (chain="true").
Size BOTH actions to the video canvas (see "Sizing" below):
```
<aitools_action skill="generate_image" preset="Prompt To Image (Z-Image).txt" prompt="<full Z-Image scene description>" width="864" height="480"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3) 5s.txt}}" prompt="<full H3 motion + dialog beat>" chain="true" width="864" height="480"/>
```
This stacks the video onto the SAME Pic as the image you just made, so the
chat shows ONE bubble that updates from still -> playing video. Do NOT also pass
attachment / chat_image when you set chain="true" - the prior step's output is
inherited automatically. chain="true" only works as a follow-up to a generate
action emitted earlier in the same reply. This is the right form for any
"<image-model> + <video-model>" combo (e.g. Z-Image + MiniMax H3).

Animate a freshly-pasted image (user dropped/pasted an image THIS turn):
```
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3) 5s.txt}}" prompt="slow camera push-in, leaves rustling" attachment="1"/>
```

Animate an image already in the chat from earlier (numbered bubble):
```
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3) 5s.txt}}" prompt="the wind picks up, hair flutters" chat_image="2"/>
```

## Writing good image-to-video prompts

The model sees the source, but the prompt still needs enough visual subject
description to anchor the motion. For ordinary one-off images, keep the still
scene brief and focus on what changes over time. For roleplay, recurring
characters, or identity anchors, the first sentence MUST restate the visible
person fully: apparent age, ethnicity/complexion, build, hair, face, wardrobe,
and expression. Never animate "Mara", "Bob", "the heroine", or "the same
person" by name only.

### MiniMax H3 (`{{Image To Video (MiniMax H3) 5s.txt}}`) - DEFAULT

H3 generates video AND native stereo audio (speech, ambience, music cues) in
one pass, and understands the source frame as `<Picture 1>`.

- For a single continuous scene, use the same shape as LTX: 4-8 sentences, one
  paragraph - subject restatement -> motion + ONE short quoted dialog line ->
  one camera move -> mood/lighting -> ambient-sound tag.
- **Dialog is DEFAULT ON.** Give any plausible speaker a short line (~3-12
  words) in double quotes with language + accent, e.g. `she murmurs "I told
  him I was done" in English with a soft New York accent`. Write EXACT words,
  never "she says something". Skip ONLY when there is no plausible speaker
  (empty landscape, face hidden, user said "silent").
- **Multi-shot IS allowed** (unlike LTX/WAN): H3 is trained for explicit cuts.
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
- If a reference is a MOVIE bubble rather than a still, use
  `skill="video_to_video"` with `{{Reference Video To Video (MiniMax H3) 5s.txt}}`
  and `<Video 1>` in the prompt instead - that preset also takes photo
  references and a second clip; see the video_to_video skill.

### Exact START FRAME plus a specific person - two-stage recipe

H3 cannot pin a literal start frame AND take references in one pass (start
frames are the i2v model; references are a different checkpoint). When the
user wants the video to open on an exact frame but with a specific person in
it, build the frame first, then animate it:

1. `image_to_image` with `{{Image To Image Klein Edit 2 Input.txt}}` - insert
   the person (photo in slot 2) into the desired start frame.
2. `image_to_movie` with `{{Image To Video (MiniMax H3) 5s.txt}}` and
   `chain="true"` in the same reply - animates that exact composed frame.

If the opening frame does NOT need to be exact, skip the two-stage recipe and
use reference-to-video: the photo guides identity without pinning frame 1.

### LTX 2.3 (`{{Image To Video (LTX) 5s.txt}}`)

Source: [docs.ltx.video](https://docs.ltx.video/api-documentation/prompting-guide).
Use when the user asks for LTX or the fastest option.

- **4-8 sentences, single flowing paragraph.** Tuned for this length.
- Order: visual subject restatement → **motion + ONE short line of dialog** →
  one camera move → mood/lighting → short ambient-sound tag.
- Dialog default ON, same quoting rules as H3.
- Don'ts: no abstract energy words ("dynamic", "epic"); no jump-cut
  words ("suddenly", "flashes", "cuts to"); don't pile aesthetics
  without described motion. Single continuous shot ONLY.

Example (H3 or LTX) for a previously-generated rooftop-smoking image:

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

### WAN / Wan 2.2 (`{{Image To Video (WAN) 5s.txt}}`)

Source: [wan2-2.app/prompt](https://wan2-2.app/prompt).

- Formula: **Subject → Scene → Motion → Aesthetic Control →
  Stylization**. No strict word count - "more complete = higher quality".
- Handles longer multi-beat motion than LTX. 200-400 words of timed
  motion + environmental motion + lighting evolution work well.
- **WAN is silent** - no dialog or sound tags.
- **Wan 2.2 uses negative prompts** (unlike H3 / LTX / Z-Image). Common:
  `blurry, low quality, distorted faces, jittery motion, watermark`.
- Hard-cut rule applies: avoid "suddenly", "flashes", "cuts to".

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
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3) 5s.txt}}" prompt="<motion prompt>" chain="true" width="864" height="480"/>
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

When the source is an `attachment` or `chat_image` (no still made this turn),
OMIT `width`/`height`. The host automatically matches the video to the source's
aspect while keeping the preset's pixel budget, so a square source renders a
square video with no crop. Only pass explicit `width`/`height` here if the user
asks for a different shape than the source (e.g. a portrait video from a
landscape photo). Both are required together; they snap to multiples of 32.

### Size keywords

The default canvas is already the fast size. Map vague size words like this,
and put the result on both actions:

- "small" / "low res" -> 640x352 (352x640 portrait, 512x512 square). Don't go
  lower; H3 quality falls apart under ~384p.
- "big" / "high res" / "detailed" -> 1152x640, near H3's trained maximum.
- "720p" -> 1280x720. "1080p" -> 1920x1080. These are above what H3 was
  trained on (~1MP), so they are slower and can look softer, but if the user
  asked for them, use them.

## Rules

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
- LTX 2.3: 4-8 sentence paragraph, single continuous shot, one camera move,
  ONE short quoted dialog line unless no plausible speaker.
- Wan 2.2: longer multi-beat motion (200-400 words) is fine; silent.
- User asked to animate → just do it.
