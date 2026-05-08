---
id: image_to_image
summary: Edit an image with a prompt. Default = ONE input. If the user wants to COMBINE/blend two images (subject from one + scene from the other, "put X from image 1 into image 2", style transfer with a reference), use the 2-Input preset and add a second source via chat_image2 / attachment2. Result spawns as a new image; originals unchanged.
inputs: attachment
template: <aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="describe the change" chat_image="N"/>  # 1-INPUT (default): replace N with the bubble number from CHAT IMAGES. Use attachment="N" for a fresh paste, or chain="true" alone for a same-reply generate->edit. 2-INPUT (combine two images): preset="{{Image To Image Klein Edit 2 Input.txt}}" with primary source PLUS chat_image2="M" or attachment2="M" for the second image.
---
# Image-to-image

Use this skill when the user wants you to TRANSFORM an existing image
(edit, restyle, alter, "make this person a robot", "add a hat", etc.)
into a new still image. The original is never modified - the result is
a brand-new image bubble below.

You must specify EXACTLY ONE source via either:

- `attachment="N"` - the Nth image the user pasted/dragged INTO THE CURRENT
  message (1-based). Use this when the user just attached something fresh.
- `chat_image="N"` - the Nth chat-image bubble already in this conversation
  (matches the "Image #N" / "Movie #N" labels visible above each bubble).
  Use this when the user says "edit the image you just made" or "tweak
  picture #2". The system prompt's CHAT IMAGES line tells you the highest
  N currently reachable.

## Available presets

- `{{Image To Image Klein Edit.txt}}` - prompt-driven semantic edit of ONE
  image. THIS IS THE DEFAULT - reach for it whenever you only have one
  reference image to feed in (one pasted/dragged image, one chat_image
  bubble, or one chained prior step). A scene that mentions extra invented
  people/objects in text alone is still ONE-input.
- `{{Image To Image Klein Edit 2 Input.txt}}` - takes TWO input images.
  ONLY use when you genuinely have TWO different reference bubbles to
  combine/blend (subject from one + scene from the other, style reference,
  "put this person in that room", etc.). Requires a second source via
  `attachment2="N"` or `chat_image2="N"`. Never duplicate the same image
  into both slots, and never reach for this preset just because the prompt
  describes two characters - what matters is whether you have two
  REFERENCE IMAGES to feed in.

## Invocation

DEFAULT - stack onto an image generated EARLIER IN THE SAME REPLY (chain="true"):
```
<aitools_action skill="generate_image" preset="Prompt To Image (Z-Image).txt" prompt="<full Z-Image scene description>"/>
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="Keep everything identical except change the time of day to dusk." chain="true"/>
```
This stacks the edit onto the SAME Pic, so the chat shows ONE bubble that
updates from base image -> edited version. Do NOT also pass attachment /
chat_image when you set chain="true" - the prior step's output is inherited
automatically. chain="true" only works as a follow-up to a generate action
emitted earlier in the same reply. This is the right form for any
"generate then edit" combo in one reply.

Edit a freshly-pasted image (user dropped/pasted an image THIS turn):
```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="add a sunhat to the woman" attachment="1"/>
```

Edit an image already in the chat from earlier (numbered bubble):
```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="add a sunhat to the woman" chat_image="1"/>
```

Two-input edit (combine/reference a 2nd image). Pick the primary source as usual (`attachment` / `chat_image` / `chain="true"`), then add ONE secondary source: `attachment2="N"` for a freshly-pasted image, or `chat_image2="N"` for an existing chat bubble. `chat_image2` wins over `attachment2` if both are set.
```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 2 Input.txt}}" prompt="Place the subject from image 1 into the scene from image 2, matching the lighting." chat_image="1" chat_image2="2"/>
```

## Writing the edit prompt

Natural language. A few well-aimed sentences beats a long essay. Cover:

1. **Keep X identical** - state what must NOT change (face, pose, background, lighting...). Skipping this is the #1 cause of drift.
2. **The change** - be specific (item, material, colour, position, size).
3. **Positional hints** when relevant ("top-right", "low on her ears").

You're describing the DELTA, not the whole image - the model already sees it. Don't pile on many edits at once; one focused change per call works best.

Example - user says "give the woman a hat":

> Keep her face, hair, expression, clothing, pose, and background identical. Add a wide-brimmed black straw sunhat with a faded pink ribbon, tilted slightly over her right brow, casting a soft shadow across her upper face.

## Aspect handling (auto)

The host automatically matches the edited image's aspect ratio to the source
image's aspect, preserving the preset's pixel budget. You don't need to do
anything special - a square source produces a square edit, a portrait source
produces a portrait edit. Explicit `width="N" height="N"` attributes (both
required, both > 0) override this if you really need a different aspect, but
in practice you almost never want that for an edit (the source dictates).

## Rules

- Pick exactly ONE source: `attachment`, `chat_image`, OR `chain="true"`.
  If both `attachment` and `chat_image` are set, `chat_image` wins. `chain="true"`
  must NOT be combined with the others - the chained step inherits the prior
  step's output automatically.
- Write a concise, specific edit prompt. ALWAYS include a "keep X
  identical" preservation clause for the parts that must not drift -
  this is the single biggest determinant of edit quality.
- The prompt describes the CHANGE, not the whole image (the model
  already sees the image).
- `chat_image="N"` only works while the world Pic for that bubble still
  exists. If the user deleted it, the CHAT IMAGES line in the system
  prompt will show fewer reachable images - reference accordingly, or
  if none are reachable just say so in chat.
- If the user asks for variations, emit multiple action tags in the
  same reply (each spawns its own bubble in stream order, with
  deliberately different choices for the change).
