---
id: image_to_movie
summary: Animate an image (user-pasted OR a previously-generated chat image) into a short video clip. Default to LTX 2.3; use WAN / Wan 2.2 via preset `Image To Video (WAN) 5s.txt` when the user asks for WAN or higher-quality silent video.
inputs: attachment
autoload: true
triggers: animate, animation, image to video, image-to-video, image to movie, image-to-movie, animate this, animate it, make this move, make it move, using wan, use wan, with wan, wan 2.2, wan2.2, wan22
template: <aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="describe motion + camera, with ONE short line of in-scene dialog in double-quotes (language+accent), then ambient sound" chat_image="N"/>  # default LTX; when the user asks for WAN/Wan 2.2, use preset="{{Image To Video (WAN) 5s.txt}}" and write a silent WAN motion prompt.
---
# Image-to-movie

Use this skill when the user wants you to ANIMATE an image into a short
video clip. The source can be either freshly pasted or already in chat.

If the user asks for a NEW text-described video with no source image
("generate a WAN video of X", "make an LTX movie of X", "create a video of X"),
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

- `{{Image To Video (WAN) 5s.txt}}` - high-quality 5s, slow, silent (Wan 2.2 / WAN). In `test_` mode this resolves to the test WAN image-to-video preset.
- `{{Image To Video (Wan22).txt}}` - legacy alias for the same production Wan 2.2 path.
- `{{Image To Video (LTX) 5s.txt}}` - fast 5s clip (LTX 2.3)

## Invocation

DEFAULT - stack onto the image you JUST generated in this same reply (chain="true"):
```
<aitools_action skill="generate_image" preset="Prompt To Image (Z-Image).txt" prompt="<full Z-Image scene description>"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="<full LTX motion + dialog beat>" chain="true"/>
```
This stacks the LTX video onto the SAME Pic as the image you just made, so the
chat shows ONE bubble that updates from still -> playing video. Do NOT also pass
attachment / chat_image when you set chain="true" - the prior step's output is
inherited automatically. chain="true" only works as a follow-up to a generate
action emitted earlier in the same reply. This is the right form for any
"<image-model> + <video-model>" combo (e.g. Z-Image + LTX).

Animate a freshly-pasted image (user dropped/pasted an image THIS turn):
```
<aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="slow camera push-in, leaves rustling" attachment="1"/>
```

Animate an image already in the chat from earlier (numbered bubble):
```
<aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="the wind picks up, hair flutters" chat_image="2"/>
```

## Writing good image-to-video prompts

The model sees the source, but the prompt still needs enough visual subject
description to anchor the motion. For ordinary one-off images, keep the still
scene brief and focus on what changes over time. For roleplay, recurring
characters, or identity anchors, the first sentence MUST restate the visible
person fully: apparent age, ethnicity/complexion, build, hair, face, wardrobe,
and expression. Never animate "Mara", "Bob", "the heroine", or "the same
person" by name only.

### LTX 2.3 (`{{Image To Video (LTX) 5s.txt}}`)

Source: [docs.ltx.video](https://docs.ltx.video/api-documentation/prompting-guide).

- **4-8 sentences, single flowing paragraph.** Tuned for this length.
- Order: visual subject restatement → **motion + ONE short line of dialog** →
  one camera move → mood/lighting → short ambient-sound tag.
- **Dialog is DEFAULT ON.** LTX 2.3 generates real audio for quoted
  lines - animating a person without giving them one wastes the
  feature. Add a short line (~3-12 words) in double quotes with
  language + accent, tucked into the motion sentence - e.g.
  `she murmurs "I told him I was done" in English with a soft New York
  accent`. Write EXACT words, never "she says something". Skip ONLY
  when the source has no plausible speaker (alone and silent, face
  hidden / turned away, empty landscape / abstract, user said "silent").
- Don'ts: no abstract energy words ("dynamic", "epic"); no jump-cut
  words ("suddenly", "flashes", "cuts to"); don't pile aesthetics
  without described motion.

LTX 2.3 example for a previously-generated rooftop-smoking image:

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
- **Wan 2.2 uses negative prompts** (unlike LTX / Z-Image). Common:
  `blurry, low quality, distorted faces, jittery motion, watermark`.
- Same hard-cut rule: avoid "suddenly", "flashes", "cuts to".

## Aspect handling (auto)

The host automatically matches the output video's aspect ratio to the source
image's aspect, while keeping the preset's overall pixel budget. So a 1024x1024
source animated with the LTX 5s preset (default 960x544 landscape) actually
runs at ~720x720 - no top/bottom crop. You don't need to do anything for this.

If you specifically want a different aspect (e.g. portrait video from a
landscape source), pass explicit `width="N" height="N"` attributes (both
required, both must be > 0). They'll be snapped to multiples of 32 and clamped
to a sensible range. Example for a 9:16 portrait:

```
<aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="..." chat_image="2" width="448" height="768"/>
```

Skip `width`/`height` unless the user is explicitly asking for a non-source
aspect ratio - the auto-match is correct in 99% of cases.

## Rules

- Pick exactly ONE source: `attachment`, `chat_image`, OR `chain="true"`.
  If both `attachment` and `chat_image` are set, `chat_image` wins. `chain="true"`
  must NOT be combined with the others - the chained step inherits the prior
  step's output automatically.
- Describe MOTION/CAMERA over time. For roleplay / identity anchors, also
  restate the visible character identity in the first sentence; name-only
  prompts are not valid.
- Pick ONE camera move with magnitude. Two competing moves fight.
- LTX 2.3: 4-8 sentence paragraph, motion-first, one camera move, and
  **ONE short quoted line of in-scene dialog** (language + accent) in
  the motion beat unless no plausible speaker.
- Wan 2.2: longer multi-beat motion (200-400 words) is fine.
- User asked to animate → just do it.
