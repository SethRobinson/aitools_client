---
id: image_to_image
summary: Edit / compose an image with a prompt (Klein, Flux 2 family). Pick the preset by INPUT COUNT (1/2/3/4/5 Input) - one input per distinct REFERENCE image. Klein wants NARRATIVE PROSE (40-70 words total), NOT keyword soup or repeated "Keep X identical" boilerplate. Reference each subject by SLOT NUMBER (image 1, image 2, ...) - NEVER by chat name (Klein has no chat history). Multi-person scenes need (a) "maintaining exact likeness of image N's face, hair, build" as a concise identity clause per slot, (b) PER-SUBJECT PLACEMENT ("image 1's man on the left holding a mug, image 2's woman on the right beside the tree"), and (c) explicit left-to-right ordering. Generic "all four together smiling" produces vague placement - place each subject individually. Result spawns as a new image; originals unchanged.
inputs: attachment
autoload: true
triggers: edit, edit the image, modify, modify the image, alter, alter the image, change, change the image, change her, change him, change them, change the, tweak, tweak the image, adjust, adjust the image, retouch, refine the image, transform, transform the image, restyle, restyle the image, redo, redo the image, redraw, repaint, repose, reposition, new pose, different pose, change pose, change the pose, change his pose, change her pose, pose her, pose him, make her, make him, give her, give him, dress her, dress him, undress, put her in, put him in, put a, put on, add a, add to the image, remove the, remove from the image, take off, replace the, swap the, swap out, restyle as, in the style of, but make it, but with, now show, but now, picture but, image but, photo but, them together, all together, together being, side by side, group photo of them, group shot of, all five of them, all four of them, all three of them, both of them in, the two of them in, all in one, in one image, as anchors, use them as anchors, use these as anchors, anchor images, anchor each, combine these, combine them, combine the, put them together, put them all, put all of them, image of them, image where they, picture of them, scene with them, scene with all, hanging out together, friends together, meet up, posing together, line them up
exclude_triggers: generate a brand new, brand new image, fresh image of a, fresh image from scratch, picture from scratch
template: <aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="<narrative prose, 40-70 words. For multi-input: name each subject by slot, give each a placement, end with scene + lighting. See examples below.>" chat_image="N"/>  # 1-INPUT (default): one source via chat_image / attachment / chain. For multi-input use preset="{{Image To Image Klein Edit N Input.txt}}" with chat_image2..chat_image5 (or attachment2..attachment5). Pick N = EXACT count of references you're feeding (4 people -> 4 Input). 5 Input is the absolute maximum.
---
# Image-to-image (Klein / Flux 2 edit family)

## ANCHOR DISCIPLINE - ALWAYS use the ORIGINAL anchor bubbles

The single most common drift failure: a multi-character scene works once,
then on every follow-up turn the LLM points at the most-recent composite
instead of going back to the original per-character anchor portraits.
Every composite has already drifted slightly from the sources; chaining
off the composite compounds the drift every turn until the characters
stop looking like themselves.

If you generated anchor portraits up-front (typically Image #1..#K, one
per character), those bubbles are the CANONICAL references for those
characters for the rest of the session. EVERY subsequent image that
includes those characters MUST feed THOSE SAME original numbers as
`chat_image` / `chat_image2` / ... never feed a downstream composite.

WRONG (drift trap - composite is Image #5, anchors were #1..#4):
> User: "now show them at the beach"
> `<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}"
>   prompt="Move them to a sunny beach scene..." chat_image="5"/>`

RIGHT (always re-anchor to the originals):
> User: "now show them at the beach"
> `<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 4 Input.txt}}"
>   prompt="The four people from images 1, 2, 3, and 4 together on a sunny
>   tropical beach, maintaining exact likeness, arranged left to right -
>   image 1's man (Caucasian, ~60s) on the left holding a coconut, image 2's
>   woman (East Asian, ~28) next to him in the surf, image 3's man
>   (Caucasian, ~35) building a sandcastle with image 4's woman
>   (Middle Eastern, ~40) on the right. Golden hour light from the west,
>   waves rolling in." chat_image="1" chat_image2="2" chat_image3="3" chat_image4="4"/>`

The right form is more work to write but it's the only way the
characters keep looking like the originals across a series. This rule
applies even when many chat images exist downstream - the original
anchor portraits are the canonical references, no matter how old.

Single-character variation series follow the same rule: if Image #1 is
the canonical portrait, every "show her doing X" follow-up feeds
`chat_image="1"`, NOT the previous variant's bubble.

## NEVER use chat character names in the prompt - HARDEST RULE

Klein has NO access to chat history. It sees only the numbered input
images (image 1, image 2, ...) and the literal `prompt=` text. A name
like "Mei-Lin", "Elias Thorne", "the heroine" is just an unresolvable
token. Refer to each subject by SLOT NUMBER ("image 1's subject", "the
woman from image 2") plus a brief visual tag (ethnicity + age).

WRONG (bare name): `"Place Elias and Mara at the fireplace..."`
WRONG (slot + name hybrid, common failure): `"Image 1's clockmaker Elias
(white beard) is on the left next to image 2's scientist Mei (lab coat)"`
RIGHT: `"Image 1's clockmaker (Caucasian man, ~60s, white beard) on the
left next to image 2's scientist (East Asian woman, ~28, lab coat)"`

The slot-plus-name hybrid is the trap to watch for. Once you've used
"image 1's", the slot already tells Klein who you mean - adding the
chat name after it is pure noise. Write the description directly inside
the parenthetical, not the name.

Chat prose can still use names freely. This rule applies ONLY to the
`prompt=` attribute.

## Prompt style - NARRATIVE PROSE, ~40-70 WORDS

Klein wants flowing prose like a novelist describing a scene, NOT
keyword soup and NOT long lists of "Keep X 100% identical" boilerplate.

- Total length 40-70 words for most edits. Even multi-person scenes
  rarely need more.
- Front-load the subjects: open the sentence with "image 1's <subject>"
  rather than burying it after a scene description.
- One concise identity clause per slot, not three separate ones. The
  phrase "maintaining exact likeness of image N's face, hair, and build"
  does the same job as the verbose triple-clause pattern.
- Skip "8k", "high-resolution", "ultra-detailed", "masterpiece" - those
  are Flux.1 / SDXL habits and add no value on Klein.
- Lighting matters: one short clause about light direction / warmth /
  source helps a lot.

## Multi-input scene composition - canonical pattern

For 2+ recurring people in one new scene (the most common multi-input
case), use this 4-part structure:

1. **Anchor list** (one sentence): "The N people from images 1, 2,
   ..., N, maintaining exact likeness of each face, hair, and build."
2. **Left-to-right ordering**: "arranged left to right in that order"
   (or whatever ordering you choose - just be explicit).
3. **Per-subject placement** (one short phrase per slot): "image 1's
   man on the left holding a mug, image 2's woman next to him laughing,
   image 3's man on the right with an arm around image 4's woman".
4. **Scene + lighting** (one short clause): "in a warm wood-paneled
   living room, Christmas tree behind them, fireplace glow from the
   left, soft evening atmosphere".

The PER-SUBJECT PLACEMENT clause is the part most often missed and is
what makes Klein actually distinguish each subject. "All four standing
together smiling" produces a generic clump where the model loses track
of who is who; "image 1's man on the left ... image 4's woman on the
right" forces it to place each one distinctly.

## Source selection

Specify EXACTLY ONE primary source:

- `attachment="N"` - Nth image the user pasted/dragged into the CURRENT
  message (1-based).
- `chat_image="N"` - Nth existing chat-image bubble (matches the
  "Image #N" label).
- `chain="true"` - output of a generate-class action emitted earlier in
  THIS SAME reply. Do not also pass attachment / chat_image with it.

Extra slots (for N-Input presets) go in `chat_image2`..`chat_image5` or
`attachment2`..`attachment5`. `chat_image{N}` wins over `attachment{N}`.

## Presets - pick by INPUT COUNT

- `{{Image To Image Klein Edit.txt}}` - 1 input. DEFAULT.
- `{{Image To Image Klein Edit 2 Input.txt}}` - 2 inputs.
- `{{Image To Image Klein Edit 3 Input.txt}}` - 3 inputs.
- `{{Image To Image Klein Edit 4 Input.txt}}` - 4 inputs.
- `{{Image To Image Klein Edit 5 Input.txt}}` - 5 inputs. ABSOLUTE MAX.
  (Klein officially tops out at 4 reference images; the 5-Input preset
  is available for forward-compat with future edit models but quality
  may degrade at 5 on current Klein - prefer 4 when possible.)

Pick N = EXACTLY the count of references you're feeding (primary +
extras). 4 people -> 4 Input, NOT 5 Input. Picking a larger preset than
you have inputs for fails the workflow.

## Multi-person heuristic

For a scene with 2+ previously-shown people:

1. Count the recurring people who exist as chat-image bubbles.
2. Pick the smallest N-Input preset that fits (N = people, or
   people + 1 if you also have a separate scene/background reference).
3. Feed each person's bubble as `chat_image` / `chat_image2` / ... /
   `chat_image5`.
4. Use the canonical 4-part prompt pattern above.

Never duplicate the same chat_image into two slots. Newly-invented
people stay in the prompt text - only feed a slot per person you want
to lock to a specific past appearance.

## Single-input edit pattern (1-Input preset)

Single-subject edits don't need the multi-input structure. Just open
with a brief identity clause ("Keep her face and hair exactly as is,
~32, Latina") then state the delta:

```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="Keep her face and hair exactly as is, ~32, Latina. Add a wide-brimmed black straw sunhat with a faded pink ribbon, tilted slightly over her right brow." chat_image="1"/>
```

Drop the identity clause ONLY if the user explicitly asked to change
the face/hair/age/ethnicity. When changing one of those, anchor the
OTHERS explicitly so only the requested attribute moves.

## Invocation examples

Same-reply generate then edit (chain):
```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full Z-Image scene>"/>
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="Keep everything as is except change the time of day to dusk, warm orange light from the west." chain="true"/>
```

Subject + scene combine (2-Input):
```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 2 Input.txt}}" prompt="Image 1's subject (Latina woman, ~32) seated at the cafe table in the scene from image 2, maintaining exact likeness, soft afternoon window light from the right." chat_image="1" chat_image2="2"/>
```

Group photo, 4 people (canonical pattern):
```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 4 Input.txt}}" prompt="The four people from images 1, 2, 3, and 4 together in a cozy Christmas living room, maintaining exact likeness of each face, hair, and build, arranged left to right in that order - image 1's man (Caucasian, ~62) on the left holding a steaming mug, image 2's woman (East Asian, ~28) next to him laughing, image 3's man (Caucasian, ~35) leaning on the mantle with an arm around image 4's woman (Middle Eastern, ~40) on the right. Christmas tree behind them, fireplace glow from the left, warm evening atmosphere." chat_image="1" chat_image2="2" chat_image3="3" chat_image4="4"/>
```

That last example is ~80 words - on the high side but acceptable for
4 subjects. For 2-3 subjects target 50-65 words.

## Rules summary

- Pick exactly ONE primary source.
- Refer to each subject by slot number, never by chat name.
- Open with a concise per-slot identity clause; don't repeat boilerplate.
- For multi-person scenes, always include per-subject placement +
  left-to-right ordering.
- Describe the CHANGE, not the whole image (model sees the input).
- For variations, emit multiple action tags with deliberately different
  scene/placement choices.
