---
id: generate_image
summary: Generate a brand-new still image from a text prompt. Use when the user asks for a picture of a NEW subject. Do NOT use this when the user wants a scene featuring people / things that already exist as numbered chat-image bubbles - that's image_to_image with the Image To Image (Qwen Image 2.1) preset, feeding each existing bubble as a chat_image reference and calling each <image1>, <image2>... in the prompt. generate_image cannot reproduce a specific past face from text alone; it will produce strangers no matter how detailed the description.
inputs: none
template: <aitools_action skill="generate_image" preset="{{Prompt To Image (MOSS-Image).txt}}" width="1248" height="832" prompt="one to three English paragraphs describing the finished image as an observer, usually ~120-250 words, short for a minimal request"/>
---
# Generate an image

Use this skill when the user asks you to create or show a still image
of a brand-new subject with no input reference (no chat-image character
to preserve, no pasted image to transform). If the user wants to put
previously-shown chat-image characters together in a new scene - even
if their wording is "create / make / generate an image of them" - that
is **image_to_image** with the `Image To Image (Qwen Image 2.1)` preset
(each person's bubble/anchor in a chat_image slot, called `<image1>`,
`<image2>`... in the prompt), not this skill.
generate_image cannot reproduce a specific past face from text.

Same trap for "do N more versions / variations" of a person already in
chat ("now as an elephant", "same boys at the beach", "three more costume
versions"). Each variation is another **image_to_image** edit off the SAME
original source/anchor - NOT a generate_image. Reaching for generate_image
here is the most common way to accidentally replace an anchored person with
a stranger. If there is an existing face to keep, you are editing, not
generating.

If the user asks for a brand-new subject WITH an attached logo, mark,
watermark, decal, or sticker, `generate_image` is only stage 1: create the
clean base subject first. For a literal flat sticker/decal/watermark, follow
with `paste_image` using the actual uploaded art as the final step. For
"fit it onto the chest/back/body/object", tattoo, engraving, embroidery,
painted scales, branded hide, inlaid metal, or any request that says it should
look physically part of the subject, follow with a placement guide paste and a
2-input Qwen Image 2.1 integration pass as described in `image_to_image` (Qwen
stays the edit model; MOSS-Image is text-to-image only). Do not try to
draw a specific attached logo from text alone.

## Available presets

Pick the preset whose strengths best match the user's request. If unsure,
default to `{{Prompt To Image (MOSS-Image).txt}}`.

- `{{Prompt To Image (MOSS-Image).txt}}` - DEFAULT. MOSS-Image: high quality
  general purpose, strong prompt adherence, and it renders exact quoted text
  (signs, labels, headlines, poster copy, small print) very well.
- `{{Prompt To Image (MOSS-Image Turbo).txt}}` - the same model distilled to
  8 steps, about 4-5x faster with slightly simpler detail. Use it when the
  user asks for "turbo", "fast", "quick", "draft" or "rough" images. Same
  prompt rules as the default.
- `{{Prompt To Image (Qwen Image 2.1).txt}}` - Qwen-Image 2.1, the previous
  default. Use ONLY when the user names Qwen.
- `{{Prompt To Transparent Image (Qwen Image 2.1).txt}}` - Qwen-Image 2.1 with a
  real alpha channel (MOSS-Image has none): use it for a transparent-background asset (sprite, icon,
  sticker, cut-out subject, logo art). Wrap the prompt: `This is an RGBA format
  image with transparency. <description>. The image has an alpha channel and a
  transparent background.` Describe the subject only, no scenery.
- `{{Prompt To Image (Z-Image).txt}}` - Z-Image Turbo. Use ONLY when the user
  says "Z-Image" / "zimage" (plain "fast" requests go to MOSS-Image Turbo).
- `{{Prompt To Image (Krea 2 Turbo).txt}}` - fast aesthetic-focused image
  generation and strong visual/art-direction variety. Use ONLY when the user
  says "krea", "krea2", or "krea 2" - the `krea` skill auto-loads with
  prompt and invocation rules; follow it.
- `{{Prompt To Image (Ideogram 4).txt}}` - excels at rendered text, posters,
  comic pages/strips, and precise layout, BUT its prompt must be a structured
  JSON caption, never prose. Use when the user says "ideo"/"ideogram"/
  "ideograph" OR when an auto-loaded recipe such as `comics` tells you to use
  Ideogram for a whole new comic page/strip. Follow the loaded `ideo` or
  `comics` JSON rules.


## Invocation

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (MOSS-Image).txt}}" width="1248" height="832" prompt="A wide realistic photograph of ..."/>
```

## Canvas (width / height)

Pick the frame from the subject and always pass it (the preset's own default
is a 1024x1024 square):

- horizontal scene, landscape, group, interior: `width="1248" height="832"` (3:2)
- vertical portrait, full-body figure, poster: `width="832" height="1248"` (2:3)
- wide cinematic frame, wallpaper: `width="1344" height="768"` (16:9)
- phone screen, tall banner: `width="768" height="1344"` (9:16)
- square icon, badge, album cover, single centred emblem: `1024x1024`

The user's own size wins ("1080p" -> 1920x1088; MOSS-Image sides must be
multiples of 16). MOSS-Image is best at 1-2 MP: for explicit "high quality" /
"2K" / print / poster-size requests go up to about 2 MP (e.g. 1728x1152 for
3:2, 1152x1728 for 2:3), which is ~2x slower. Keep the Turbo preset near 1 MP.
When the still feeds a chained video, use the VIDEO's canvas instead (see
Stacking below).

## Writing good image prompts

Sources: OpenMOSS's official MOSS-Image prompt-enhancer skill and user guide,
and the prompt enhancer in Comfy-Org's MOSS-Image template. The model learned
from detailed captions of finished images with every visible string quoted
exactly. You ARE that enhancer here: write its output directly, never the
user's short request.

### Shape

Cohesive English prose (one to three paragraphs) that describes the FINISHED
image as if you were looking at it: present tense, third person, declarative.
Resolve every open creative choice yourself; keep everything the user
specified (subjects, counts, relationships, medium, style, exclusions).

Length follows the brief, not a word budget:
- An ordinary scene, portrait or illustration usually lands around 120-250
  words, because you decide identity, setting, framing, lighting, materials and
  palette that the user left open.
- An explicitly minimal request (a flat icon, a simple mark, one object on a
  plain background, "minimalist") stays short and minimal. Do not pad it with
  invented scenery or props.
- A designed image with a lot of copy runs as long as the copy needs.

(For an explicitly requested Qwen Image 2.1 render, write ~300-500 words in
one paragraph.)

Cover, roughly in this order:

1. **Medium, style and main subject** first: `A wide realistic photograph of
   ..., set against ...`. Name the style once (realistic, cinematic,
   watercolour, flat-vector, isometric, 3D-rendered, editorial...).
2. **Setting and composition**: walk the frame with positional words
   (upper-left corner, across the lower third, foreground, far right), never
   coordinates. For one subject filling the frame: pose and placement, face,
   each garment, what they hold, the background and how it falls off.
3. **Text**, only if something is meant to be read (rules below).
4. **Lighting**: source, direction, quality, and the shadows and highlights it
   leaves.
5. **Palette, materials and mood**. A short closing line that steps back to
   the whole image is fine, but no fixed formula.

### Text in the image

- Quote every string exactly, in its original language and script, with its
  capitalisation and punctuation. Never translate or paraphrase it. Inside the
  action tag write the quotes as `&quot;` (see the action protocol rules):
  `a bold black headline across the top reads &quot;OPEN LATE&quot;`.
- Write the real copy. If the user asks for a designed image (poster, cover,
  menu, label, sign, infographic) without giving its words, write short,
  specific copy that fits, never placeholders such as "headline here".
- Say where each string sits, its reading order, alignment, relative size,
  weight, font style and colour.
- Line breaks: MOSS wraps text on its own, but to control exactly where a line
  breaks, put a real line break inside the quoted string (the `prompt=` value
  may span lines):

  ```
  ... a chalkboard by the door reads &quot;TODAY
  Tomato soup 4.50
  Apple pie 3.00&quot; in white chalk capitals ...
  ```
- For a designed image, establish reading order and hierarchy that fit the
  content instead of a generic title / hero image / footer template.
- Distant or unimportant text is "blurred" or "too small to read": never
  invent letters for it.

### Rules

- Observe, don't instruct: no "you", "create", "make sure", no quality
  boosters ("masterpiece, 8k, highly detailed, award-winning").
- Name colours with a modifier (deep navy, muted olive, warm terracotta) and
  give materials (brushed metal, coarse linen, weathered wood).
- Enumerate, never summarise: say what each item is; write small counts as
  words ("three candles").
- People: build, posture, gaze, expression, hair, skin tone, each garment with
  colour and material. Age as a life stage or decade ("in her thirties"),
  never a number of years.
- Objects by class, not brand, unless the user named the brand.
- Keep it physically coherent: shadows fall away from the light, scale is
  consistent, reflections match.
- Never write the ratio or pixel size into the prompt - that is width/height.
- Plain prose only: no lists, headings, JSON, Markdown or keyword tags.
- Leave negative_prompt out: each preset supplies its own default.

### Examples

User asked: "a woman smoking on a rooftop". Ship something like (with
`width="832" height="1248"`):

> A vertical realistic photograph of a woman in her early thirties smoking at
> the edge of a Brooklyn rooftop at golden hour, framed against a warm amber
> skyline. She stands in the centre-right of the frame at a low concrete
> parapet, weight on her right leg, her left forearm resting on the rough grey
> ledge. She has a slim athletic build, sun-warmed olive skin with faint
> freckles across her cheekbones, and dark brown almond eyes gazing just above
> the camera with a faint smirk. Her chest-length espresso brown bob is swept
> across her forehead by the wind. She wears an oversized faded black band
> t-shirt tucked loosely into high-waisted light-wash jeans, a worn indigo
> denim jacket draped over her shoulders, and scuffed black leather boots. Her
> right hand holds a thin cigarette near her mouth, and a pale coil of smoke
> drifts toward the upper left of the frame. Behind her, across the upper half
> of the frame, the Manhattan skyline is soft out-of-focus towers in muted gold
> and blue-grey, and a squat wooden water tower sits on a neighbouring roof on
> the far left. Along the bottom edge, the tar-paper roof shows scuffs and a
> few scattered bottle caps. The lighting is a low warm sun behind her acting
> as a rim light along her hair and shoulders, with warm-grey fill bounced from
> the concrete and deep cool shadows under her jaw. The overall composition is
> an intimate off-centre editorial portrait with a warm honey palette and
> shallow depth of field.

User asked: "a simple flat icon of a leaf". Keep it minimal (with
`width="1024" height="1024"`):

> A flat vector icon of a single green leaf centred on a plain white
> background. The leaf is drawn with smooth rounded shapes in two shades of
> fresh green, split by a thin pale midrib, with no outline, shadow or texture.

## Stacking with a follow-up step (chain="true")

If the user asks for something like "make a picture and animate it with MiniMax H3" or
"image-to-image change the weather, then animate it" - emit `generate_image`
first, then a follow-up action with `chain="true"` (image_to_movie /
image_to_image) IN THE SAME REPLY. Both steps run on the SAME Pic, so the
chat shows ONE bubble that updates from still -> edited / animated as each
stage finishes. See `image_to_movie` / `image_to_image` for the chained
syntax. The chained step inherits this image's output automatically - do
not pass attachment / chat_image alongside chain="true". When the chained
step is a video, put the SAME width/height (the video's canvas, e.g.
864x480) on both actions.

## Scenario / recurring characters

Detailed roleplay, scenario, character-sheet, and identity-anchor
workflows live in `scenario_storytelling`. If that skill is auto-loaded,
follow it for story prose, visual pacing, reference characters, and
GPU-aware multi-shot planning.

Still keep every generate_image prompt self-contained: visible identity,
setting, pose/action, lighting, mood, camera, and style.

## Rules

- If the user asked for an image - or one would obviously help - just spawn
  it. Don't ask for confirmation.
- Write the prompt as one English observer paragraph in the shape above.
  Decide every detail the user left out. Don't pass the user's 1-liner to
  the model verbatim.
- `gpu="N"` is optional - omit to let the scheduler pick the best free GPU.
