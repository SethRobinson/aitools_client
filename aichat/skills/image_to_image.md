---
id: image_to_image
summary: Edit / compose an image with a prompt (Klein). Pick the preset by INPUT COUNT (1/2/3/4/5 Input) - one input per distinct REFERENCE you need the model to see (canvas + one anchor per recurring person/object). Multi-person scenes with previously-shown characters should use 3/4/5-Input presets and pass a chat_image of each person as separate anchors so faces / hair / age / ethnicity stay consistent across renders. THE PROMPT MUST OPEN WITH IDENTITY-ANCHOR CLAUSES that quote age + ethnicity verbatim from the CHAT IMAGES caption for every person you're anchoring ("Keep <NAME>'s face 100% identical - same features, expression, skin tone, ~<age>, <ethnicity>" / "Keep <NAME>'s hair 100% identical - color, length, style" / "Keep body proportions identical"). Skipping the anchors makes Klein silently mutate the face / hair / age / ethnicity even on edits unrelated to the person. Result spawns as a new image; originals unchanged.
inputs: attachment
autoload: true
triggers: edit, edit the image, modify, modify the image, alter, alter the image, change, change the image, change her, change him, change them, change the, tweak, tweak the image, adjust, adjust the image, retouch, refine the image, transform, transform the image, restyle, restyle the image, redo, redo the image, redraw, repaint, repose, reposition, new pose, different pose, change pose, change the pose, change his pose, change her pose, pose her, pose him, make her, make him, give her, give him, dress her, dress him, undress, put her in, put him in, put a, put on, add a, add to the image, remove the, remove from the image, take off, replace the, swap the, swap out, restyle as, in the style of, but make it, but with, now show, but now, picture but, image but, photo but, them together, all together, together being, side by side, group photo of them, group shot of, all five of them, all four of them, all three of them, both of them in, the two of them in, all in one, in one image, as anchors, use them as anchors, use these as anchors, anchor images, anchor each, combine these, combine them, combine the, put them together, put them all, put all of them, image of them, image where they, picture of them, scene with them, scene with all, hanging out together, friends together, meet up, posing together, line them up
exclude_triggers: generate a brand new, brand new image, fresh image of a, fresh image from scratch, picture from scratch
template: <aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="Keep face 100% identical - same facial structure, features, expression, skin tone, ~<AGE from caption>, <ETHNICITY from caption>. Keep hair 100% identical - color, length, style, parting, texture. Keep body proportions identical - same build and height. <YOUR SPECIFIC EDIT HERE - clothing/pose/background/etc.>" chat_image="N"/>  # 1-INPUT (default): replace N with the bubble number from CHAT IMAGES; replace <AGE> and <ETHNICITY> with the values from that bubble's caption ("About 28, female, East Asian..." -> ~28, East Asian). Use attachment="N" for a fresh paste, or chain="true" alone for a same-reply generate->edit. 2/3/4/5-INPUT (combine multiple anchors): preset="{{Image To Image Klein Edit N Input.txt}}" with the primary source PLUS chat_image2..chat_image5 (or attachment2..attachment5) for each additional anchor. Pick N = total reference images you need the model to see (e.g. scene canvas + 2 person anchors = 3-Input). Reuse previously-generated chat images of the same people as their anchors so identity stays consistent.
---
# Image-to-image

> **Required prompt format (Klein):** every prompt MUST open with three
> anchor clauses, in this order, before the actual edit instruction:
>
> 1. `Keep face 100% identical - same facial structure, features,
>    expression, skin tone, ~<AGE from CHAT IMAGES caption>,
>    <ETHNICITY from CHAT IMAGES caption>.`
> 2. `Keep hair 100% identical - color, length, style, parting, texture.`
> 3. `Keep body proportions identical - same build and height.`
>
> Look up `<AGE>` and `<ETHNICITY>` from the CHAT IMAGES block of the
> system prompt - the caption for each Image #N opens with exactly
> "<age>, <gender>, <ethnicity>, <skin tone>". Quote those values into
> the anchor verbatim (e.g. caption "About 32, female, Latina, medium
> warm skin" -> "~32, Latina"). Do NOT use vague phrases like "the
> woman" / "the subject" - Klein needs concrete values or it silently
> mutates the face, hair, age, and ethnicity even on edits that have
> nothing to do with the person. Drop an anchor clause ONLY when the
> user explicitly asked to change that exact attribute, and tighten
> the others further.

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
  Use when you have TWO distinct reference bubbles to combine (subject +
  scene, style reference, "put this person in that room", or two recurring
  characters together for the first time). Requires a second source via
  `attachment2="N"` or `chat_image2="N"`. Never duplicate the same image
  into both slots.
- `{{Image To Image Klein Edit 3 Input.txt}}`,
  `{{Image To Image Klein Edit 4 Input.txt}}`,
  `{{Image To Image Klein Edit 5 Input.txt}}` - same workflow, 3 / 4 / 5
  reference slots. Use these when the scene involves multiple recurring
  people / objects that already exist as separate chat images and need to
  stay visually consistent. Pass each anchor via `chat_image2`..`chat_image5`
  (or `attachment2`..`attachment5`). Pick N = (canvas-or-primary-subject) +
  (one extra anchor per other recurring character or reference object).

### Anchor heuristic for multi-person scenes (IMPORTANT)

When the user asks for a scene that contains two-or-more previously-shown
people ("Alice and Bob having coffee", "the whole crew at the table",
"put her with him"), DO NOT try to describe everyone in text alone -
Klein will quietly drift their faces, hair, age, and ethnicity. Instead:

1. Count the recurring people who already exist as chat-image bubbles
   (look at CHAT IMAGES). Each one needs its own anchor slot.
2. Pick the smallest N-Input preset that fits: 1 primary + (people - 1)
   extra slots, capped at 5 inputs total. If you also have a separate
   scene/background reference bubble, count that as another input.
3. Use the first person's chat-image as the primary (`chat_image="N"`)
   and feed the rest as `chat_image2="M"`, `chat_image3="...", etc.
4. In the prompt, restate identity anchors for EVERY anchored person,
   each one quoting the age + ethnicity from that bubble's caption.

Never duplicate the same chat_image into two slots, and never reach for
a multi-input preset just because the prompt mentions multiple people in
text - what matters is whether each person has their own REFERENCE IMAGE
bubble already. People who are being newly invented stay in the prompt
text; only feed a slot per person you want to lock to a specific past
appearance.

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

Multi-anchor edit (3/4/5 input). Use when you have 3+ recurring references that all need to stay visually consistent - typically two-or-more previously-shown people together in one new scene. The primary source goes in `chat_image` / `attachment` / `chain="true"`; each additional anchor goes in `chat_image2`..`chat_image5` (or `attachment2`..`attachment5`). chat_image{N} wins over attachment{N} at the same slot. Pick the smallest preset that fits the input count.
```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 3 Input.txt}}" prompt="Keep Alice's face 100% identical - same features, expression, skin tone, ~32, Latina. Keep Alice's hair 100% identical - same color, length, style. Keep Bob's face 100% identical - same features, expression, skin tone, ~35, East Asian. Keep Bob's hair 100% identical - same color, length, style. Place Alice (from image 2) and Bob (from image 3) together at a cafe table from the scene in image 1, warm afternoon lighting, candid conversation." chat_image="1" chat_image2="2" chat_image3="3"/>
```
Above, image 1 is the cafe scene reference, image 2 is Alice's anchor, image 3 is Bob's anchor. For a four-person scene with no separate scene reference, use the 4-Input preset and pass the four people as `chat_image` + `chat_image2` + `chat_image3` + `chat_image4`.

## Writing the edit prompt

Natural language. A few well-aimed sentences beats a long essay. Cover:

1. **Keep X identical** - state what must NOT change (face, pose, background, lighting...). Skipping this is the #1 cause of drift.
2. **The change** - be specific (item, material, colour, position, size).
3. **Positional hints** when relevant ("top-right", "low on her ears").

You're describing the DELTA, not the whole image - the model already sees it. Don't pile on many edits at once; one focused change per call works best.

### CRITICAL: faces and hair drift the worst

Klein quietly mutates faces and hair on almost every edit if you don't
explicitly anchor them, even when the requested change has nothing to do
with the person. A "give her a hat" prompt will subtly reshape the jawline,
shift the eye spacing, or restyle the hair colour and length. Once that
happens the image is "broken" - the subject no longer looks like the same
person, and the user notices immediately.

**Default rule:** unless the user explicitly asked to change the face,
hair, age, or ethnicity, ALWAYS include all three of these clauses in the
prompt, AND restate the subject's apparent age + ethnicity / skin tone as
concrete values pulled from the CHAT IMAGES caption (not vague terms like
"the woman"):

- "Keep her/his/their face 100% identical - same facial structure, same
  features, same expression, same apparent age (~<NN>), same ethnicity
  (<East Asian / Black / White / Latina / South Asian / Middle Eastern /
  mixed / etc.>), same skin tone."
- "Keep the hair 100% identical - same colour, same length, same style,
  same parting, same texture."
- "Keep body proportions identical - same build, same height, same
  apparent body type."

Pull the age and ethnicity values from the LONG caption in the CHAT
IMAGES block (the caption opens with "<age>, <gender>, <ethnicity>,
<skin tone>"). Quote them verbatim into the anchor clause. Without
explicit numeric age + named ethnicity, Klein routinely shifts a
30-year-old East Asian subject toward a younger or whiter-presenting face
even on edits that never mentioned the person.

These clauses should be near the top of the edit prompt, not buried at
the end. Even for edits that seem unrelated (clothing, background, props,
lighting) the anchors are still required - they cost nothing when the
change really is unrelated, and they prevent the silent drift when it
isn't.

**Only drop the face anchor when the user explicitly asked to change the
face** ("make her older", "give him a beard", "change her ethnicity"). Same
for hair ("make her blonde", "shave his head", "give her a bob"). When
asked to change ONE attribute, still anchor the OTHERS explicitly so only
the requested attribute moves - especially age and ethnicity, which Klein
will silently drift even when only the hair was supposed to change.

Exception for roleplay / story / identity anchors: when the source image is
being reused as a named character or recurring person, the prompt must still
include a full visual identity restatement. Do not write only the character
name. Use the source as the anchor AND spell out the subject, opening with
explicit age + ethnicity exactly as captioned:

> Use the person from reference image as a woman in her late 20s, East Asian,
> light olive skin, compact athletic build, short black undercut hair,
> angular cheekbones, dark focused eyes, small split scar at the right
> eyebrow; keep her facial identity, apparent age (~28), ethnicity (East
> Asian), complexion, and hair recognizably consistent. Put her in a
> rain-slick train tunnel wearing a wet charcoal tactical jacket, vaulting a
> turnstile, tense expression, red emergency backlight, handheld 35mm
> action shot.

Example - user says "give the woman a hat" (and the CHAT IMAGES caption
opens "About 32, female, Latina, medium-warm skin..."):

> Keep her face 100% identical - same facial structure, features, expression, skin tone, apparent age (~32), ethnicity (Latina). Keep the hair 100% identical - same colour, length, style, parting, texture. Keep body proportions, clothing, pose, and background identical. Add a wide-brimmed black straw sunhat with a faded pink ribbon, tilted slightly over her right brow, casting a soft shadow across her upper face.

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
  already sees the image), except for scenario / identity-anchor use
  where you must restate the person visually as documented above.
- `chat_image="N"` only works while the world Pic for that bubble still
  exists. If the user deleted it, the CHAT IMAGES line in the system
  prompt will show fewer reachable images - reference accordingly, or
  if none are reachable just say so in chat.
- If the user asks for variations, emit multiple action tags in the
  same reply (each spawns its own bubble in stream order, with
  deliberately different choices for the change).
