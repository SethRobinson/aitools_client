---
id: image_to_image
summary: Two still-image modes. (1) NEW image FEATURING existing chat people/anchors/references ("them together", group shots, variations, re-poses) - DEFAULT preset {{Reference To Image (MiniMax H3).txt}}, up to 9 refs via chat_image + chat_image2..9; the prompt is the six-section H3 reference document (subject_definitions defining <Subject N> from every staged <Picture N> / summary / retention_analysis / detailed_description ~120-250 words, no dialog / overall_soundscape: N/A / non_diegetic_music: N/A) and MUST address every staged photo by its <Picture N> tag (use {{Reference To Image (MiniMax H3 Quality).txt}} for explicit high/maximum quality; prompt LAST in the tag). (2) In-place EDIT of one image - delta changes that preserve the source's exact composition - Klein/Flux 2 by INPUT COUNT (1-5), 40-70 words of narrative prose, slot-number references, concise identity locks; Bernini-R only when explicitly named. A Movie #N is NOT a still source by default: scene/motion/dialogue/audio edits use video_to_video. Only when the user explicitly requests one still/current frame may image_to_image target a Movie, and the action must include movie_frame="true". Result spawns as a new still; originals remain unchanged.
inputs: attachment
autoload: true
triggers: edit the image, edit this image, modify the image, alter the image, change the image, tweak the image, adjust the image, retouch, refine the image, transform the image, restyle, restyle as, redraw, repaint, change the pose, change her pose, change his pose, new pose, different pose, dress her, dress him, undress, replace the, swap the, swap out, remove from the image, in the style of, them together, all together, side by side, group photo of them, group shot of, all three of them, all four of them, all five of them, both of them in, the two of them in, in one image, all in one, use them as anchors, use these as anchors, combine them, combine these, put them together, put them all, put all of them, scene with them, scene with all, posing together, line them up, hanging out together
exclude_triggers: generate a brand new, brand new image, fresh image of a, fresh image from scratch, picture from scratch
template: <aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 1 Input.txt}}" prompt="<narrative prose, 40-70 words. For multi-input: name each subject by slot, give each a placement, end with scene + lighting.>" chat_image="N"/>  # STILL sources only. attachment= works only in the very message the user pasted the image in; on later turns use chat_image="N" (the paste's bubble number). A Movie source is allowed only for an explicit single-frame/current-frame request and requires movie_frame="true". Klein is the EDIT path; for a NEW scene FEATURING existing people/anchors use preset="{{Reference To Image (MiniMax H3).txt}}" with chat_image + chat_image2..chat_image9 and every staged photo addressed as <Picture N> in the prompt.
---
# Image-to-image (Klein / Flux 2 edit family)

## ANCHOR DISCIPLINE - reference recurring characters BY NAME

The single most common drift failure: a multi-character scene works once,
then on every follow-up turn the model points `chat_image` at the
most-recent composite instead of the original per-character anchor.
Every composite has already drifted slightly; chaining off it compounds
the drift every turn until the characters stop looking like themselves.

**Named anchors remove the bookkeeping.** When a character first appears
as a portrait, tag that action with `anchor="Name"`:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full portrait of the clockmaker>" anchor="Elias"/>
```

From then on, refer to that character by name in any `chat_image` slot -
`chat_image="Elias"` - and the host resolves the name to that
character's CURRENT anchor image automatically. You do NOT track slot
numbers, and the name always points at the canonical portrait, never a
drifted composite. The live name->slot map is printed every turn in the
`ANCHORS:` line of CURRENT STATE - read it to see who exists.

WRONG (drift trap - points at the composite, and guesses a number):
> User: "now show them at the beach"
> `<aitools_action skill="image_to_image" preset="{{Reference To Image (MiniMax H3).txt}}"
>   prompt="Move them to a sunny beach scene..." chat_image="5"/>`

RIGHT (reference each character by anchor name; the prompt is the
six-section H3 document - see "H3 REFERENCE RENDER" below):
> User: "now show them at the beach"
> `<aitools_action skill="image_to_image" preset="{{Reference To Image (MiniMax H3).txt}}"
>   chat_image="Elias" chat_image2="Mei" chat_image3="Jonah" chat_image4="Layla"
>   width="1152" height="640"
>   prompt="subject_definitions: <Subject 1> is the man in <Picture 1>, ...
>   <Subject 4> is the woman in <Picture 4>, ... / summary + retention_analysis
>   / detailed_description: the four on a sunny tropical beach, <Subject 1> on
>   the left holding a coconut, ... golden hour light - the full document as
>   specified below"/>`

Note the `prompt=` binds people to photos ONLY through slot-order tags -
`<Picture 1>` is whichever name you put in `chat_image`, `<Picture 2>` is
`chat_image2`, and so on (Klein edits use "image N" the same way). Names are
ONLY for the `chat_image*` attributes; the prose never says "Elias".

If a character has no anchor name yet (older session, or a user-supplied
reference), fall back to the numeric slot from the `ANCHORS:` / `CHAT
IMAGES:` lines - same rule, just feed the canonical portrait's number,
never a composite.

**Updating a character's look** (new outfit, haircut, scar): generate a
fresh image of them FROM their current anchor and re-tag the SAME name -
`anchor="Elias"` - which re-points the name to the new image. Every later
`chat_image="Elias"` then uses the updated look:

```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 1 Input.txt}}" prompt="Keep his face, white beard, and ~60s age exactly as is. Change his outfit to a charcoal three-piece suit." chat_image="Elias" anchor="Elias"/>
```

Single-character variation series follow the same rule: feed
`chat_image="Elias"` (the anchor) for every "show him doing X" follow-up,
NOT the previous variant's bubble.

## H3 REFERENCE RENDER - the DEFAULT for new scenes FEATURING existing people

When the request is a NEW image STARRING people/subjects who already exist in
chat - "make an image of them together", "group photo", "show her at the
beach", "use these as anchors/references" - do NOT edit an existing image.
Render fresh from references with H3 Ref2VA (the same reference engine as the
H3 video presets, single-still output):

- `{{Reference To Image (MiniMax H3).txt}}` - the DEFAULT (8-step turbo).
- `{{Reference To Image (MiniMax H3 Quality).txt}}` - only for explicit
  "high quality" / "maximum quality" requests (20 steps, ~2x slower).

Slots: `chat_image` (or `attachment`) is `<Picture 1>`, `chat_image2..9` /
`attachment2..9` are `<Picture 2>..<Picture 9>` - up to NINE references
(Klein tops out at 5). Anchors by name work in every slot.

The prompt is the official six-section H3 reference document (DIFFERENT from
Klein prose; same structure as the H3 video reference presets, minus dialog):

- **subject_definitions**: one line per referenced person/thing, DEFINED from
  its `<Picture N>` tag - `<Subject 1> is the man in <Picture 1>, with short
  gray hair and a charcoal suit.` Address EVERY staged photo by its tag here,
  in slot order; the host BLOCKS the render and bounces the action back if
  any staged photo goes untagged or a tag has no photo behind it. Several
  photos of the SAME person strengthen the lock - define them as ONE subject
  (`<Subject 1> is the man in <Picture 1> and <Picture 2>`).
- **summary**: one sentence, opening `[reference generation]`.
- **retention_analysis**: one line per subject, normally `fully_preserved`
  (a deliberate wardrobe/hair change is `partially_preserved - <what
  changes>`).
- **detailed_description**: the NEW scene as observable prose, ~120-250
  words, described COMPLETELY from scratch - placement (left to right for
  groups), pose, action, environment, lighting, style. H3 regenerates fresh
  every time and carries nothing over except what the tags pin, so never
  write a delta ("same as the last image but...") - that is Klein EDIT
  phrasing. The tags ARE the identity lock: describe each person ONLY
  as they appear in their reference (brief traits at most); invented details
  ("auburn hair") OVERRIDE the photo and drift identity. No dialog, no
  `(Sx)` IDs - the output is a still.
- **overall_soundscape: N/A** and **non_diegetic_music: N/A** - always, for
  stills.
- Chat names never appear in `prompt=` (H3 has no chat history either).
- Canvas: default 864x480. For identity-critical faces, group shots, or
  "high quality" requests raise it - `width="1152" height="640"` landscape,
  640x1152 portrait, 896x896 square (trained cap 1344x768). Omitting dims
  inherits <Picture 1>'s aspect at the default pixel budget.
- Put `prompt` LAST in the action tag.

Example - two anchored characters in one new scene:

```
<aitools_action skill="image_to_image" preset="{{Reference To Image (MiniMax H3).txt}}" chat_image="Elias" chat_image2="Mei" width="1152" height="640" prompt="subject_definitions:
<Subject 1> is the man in <Picture 1>, with short gray hair and a trimmed white beard.
<Subject 2> is the woman in <Picture 2>, with a dark bob and round glasses.

summary:
[reference generation] The target image shows <Subject 1> and <Subject 2> laughing over coffee at an outdoor cafe at dusk.

retention_analysis:
<Subject 1> (main subject): fully_preserved - his face, gray hair, and beard are retained.
<Subject 2> (main subject): fully_preserved - her face, bob, and glasses are retained.

detailed_description:
A live-action photographic style at dusk with warm streetlight from the left. <Subject 1> sits on the left side of a small round marble cafe table, one hand around an espresso cup, leaning back mid-laugh; <Subject 2> sits on the right, elbows on the table, grinning at him over her raised cappuccino. Between them a shared plate of biscotti, a folded newspaper, and a small tealight. Behind the table, a cobbled street falls out of focus into warm city-light bokeh, with a bicycle leaning against a lamppost and awning stripes catching the last violet of the sky. Shallow depth of field at 50mm, natural skin tones, gentle film grain.

overall_soundscape: N/A

non_diegetic_music: N/A"/>
```

Use KLEIN instead (see below) when the task is an in-place EDIT: the output
must keep the source image's exact composition/pixels with a delta applied
("change the sky", "add a hat", "remove the car"), the logo paste/integration
flows, building an exact start frame for a video, or when the user explicitly
names Klein/Flux/Bernini. H3 REGENERATES a fresh scene from references; it
does not preserve the source's composition.

## "DO N MORE VERSIONS" - keep it image_to_image, emit them all at once

When the user asks for several variations of someone already in chat -
"now as an elephant, a bee, and a dragon", "give me three more versions",
"same boys but at the beach / in space / as superheroes" - EVERY variation
is another `image_to_image` action with
`{{Reference To Image (MiniMax H3).txt}}`, NOT a `generate_image`. Rules:

- Feed the SAME ORIGINAL source on every variation (`chat_image="1"` or the
  anchor name) as `<Picture 1>` - the canonical face, never the previous
  variation's output (chaining off the last variant compounds drift).
- NEVER use `generate_image` / Z-Image for a variation of an existing person.
  Re-describing them from text produces a stranger no matter how detailed -
  that is the exact failure this skill exists to prevent.
- The variations are INDEPENDENT of each other, so emit ALL of them in ONE
  reply (one `image_to_image` tag per variation). You do NOT need `continue`
  for independent variations - only use `continue` when a later step needs an
  earlier step's OUTPUT image. Do not stop after one or two and trail off.
- Each variation's prompt uses the `<Picture 1>` tag and describes the new
  scene/costume; a tight in-place delta on the ORIGINAL image ("same picture,
  just add a hat") is a Klein edit instead.

## IDENTITY LOCK (KLEIN EDITS) - anchoring MEANS "keep their identity" BY DEFAULT

(On the H3 reference path above, identity rides the `<Picture N>` tags and
prose traits stay MINIMAL - the clause below is for KLEIN edit prompts.)

Using an anchor, or editing an existing person via `chat_image`, IS the
instruction to keep their face / height / build - that is the entire point of
anchoring. The user does NOT have to say "don't change their faces"; assume
it. Include the lock clause on EVERY anchored / `chat_image` edit by default,
unless the user EXPLICITLY asks to change their face, age, or body. Don't wait
to be asked, and don't wait for a second complaint to make it strong.

The #1 quality complaint is faces and HEIGHTS drifting on an edit. Lock
identity hard on the FIRST attempt:

> "preserving exact faces, exact hairstyles, exact heights, exact body
>  proportions, exact poses, and exact relative positioning - do NOT change
>  their faces, heights, or stances at all"

"face, hair, and build" alone is too weak for full-body or multi-person
shots - HEIGHT, proportions, stance, and left-to-right spacing are exactly
what slip. Name them explicitly the first time, not only after a complaint.

RELOCATION edits (costume swap + new setting, e.g. "put them at the North
Pole") are the HIGHEST-drift case: the more of a fresh scene you describe,
the more the model re-renders the people and loses their likeness. Keep the
people clause as the hard lock above, and describe the change as a tight
DELTA - "only swap the duck onesie for a penguin costume; background becomes
Arctic ice with real penguins" - NEVER a full from-scratch scene description
("They stand on ice floes, aurora overhead, photorealistic daylight..."). A
full scene re-description is what made identity drift on the first pass.

## NEVER use chat character names in the prompt - HARDEST RULE

Neither model has chat history. H3 sees the photos only through `<Picture N>`
tags; Klein sees only the numbered input
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

## Klein multi-input scene composition - canonical pattern

(Default for 2+ recurring people in a NEW scene is the H3 REFERENCE RENDER
above; use this Klein pattern when the composite must preserve existing
pixels/composition or Klein was explicitly requested.)

For 2+ recurring people in one composed scene, use this 4-part structure:

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
  "Image #N" label). May also be a character ANCHOR NAME
  (`chat_image="Elias"`) - the host rewrites it to that character's
  current slot number (see Anchor Discipline above).
- `chain="true"` - output of a generate-class action emitted earlier in
  THIS SAME reply. Do not also pass attachment / chat_image with it.

A `Movie #N` bubble is not an ordinary image source. Use `video_to_video` for
any scene, motion, dialogue, voice, audio, or sound change. Only when the user
explicitly asks for a single still/current frame may `image_to_image` point at
the Movie; add `movie_frame="true"` to make that opt-in explicit. The executor
rejects an unmarked Movie-to-still action instead of silently grabbing a frame.

Extra slots (for N-Input presets) go in `chat_image2`..`chat_image5` or
`attachment2`..`attachment5` (each may be a number or an anchor name).
`chat_image{N}` wins over `attachment{N}`.

## New subject + logo/reference on its surface

When the user asks for a NEW subject with an attached logo, emblem, mark,
watermark, decal, sticker, or other graphic reference on it, first decide
whether they want a flat graphic placed on top or a mark physically integrated
into the subject.

For literal "sticker", "decal", "watermark", "paste this logo", UI marks, or
requests where exact source pixels/colors/alpha matter most, use the
exact-fidelity paste flow:

1. `generate_image` creates the clean subject and tags it with an anchor.
2. `paste_image` places the actual uploaded/pasted mark onto the generated
   subject using alpha compositing. Pick a conservative rect on the visible
   surface, preserve aspect with `mode="fit"`, and use `opacity="1"` unless
   the user asked for translucency.

For "fit the logo onto the chest/back/body/object", "make it part of the
surface", tattoo, engraving, embroidery, scales, hide, armor, fabric, metal,
or any wording that rejects a pasted look, use the integration flow:

1. `generate_image` creates the clean subject and tags it with the final
   subject anchor name.
2. `paste_image` places the real logo in the intended area as a visible
   placement guide only. Use `chain="true"` if it immediately follows the base
   render; otherwise use `chat_image="BaseAnchor"` as the canvas.
3. `image_to_image` with `{{Image To Image Klein Edit 2 Input.txt}}` uses the
   guide composite as input 1 (`chain="true"` when adjacent) and the original
   logo as input 2 (`attachment2="N"` / `chat_image2="N"`). The prompt must
   say image 1 is only the placement guide and image 2 is the logo source.
   Ask for the mark to be painted/inlaid/embossed/tattooed/formed into the
   material, following curvature, lighting, shadows, and texture; preserve the
   logo's geometry and colors as much as possible; do not make it glowing,
   white, or a generic letter unless the source is. Anchor this final Klein
   result with the same subject anchor name, then use that final anchor for
   later edits, dangerous variants, and videos.

Never use the logo/reference as the primary `chat_image`; that edits the logo
itself into the requested scene instead of applying it to the subject.

Integrated same-reply example:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full visual prompt for a realistic baby dragon, no logo yet>" anchor="Dragon"/>
<aitools_action skill="paste_image" chain="true" source_attachment="1" x="43%" y="45%" width="14%" height="14%" mode="fit" opacity="1"/>
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 2 Input.txt}}" prompt="Image 1 is a realistic baby dragon with a temporary logo placement guide on its chest. Integrate image 2's logo geometry and colors into the dragon's chest scales as a natural inlaid scale pattern following the body curvature and forest lighting, not a flat pasted overlay. Preserve the dragon pose and make the logo readable; do not make it glowing or white unless the source logo is glowing or white." chain="true" attachment2="1" anchor="Dragon"/>
```

## Presets

H3 reference render (NEW scene featuring existing people - the composite
default, up to 9 refs):

- `{{Reference To Image (MiniMax H3).txt}}` - DEFAULT (8-step turbo).
- `{{Reference To Image (MiniMax H3 Quality).txt}}` - explicit high/maximum
  quality only (20 steps, ~2x slower).

Klein edit family - pick by INPUT COUNT:

- `{{Image To Image Klein Edit 1 Input.txt}}` - 1 input. EDIT DEFAULT.
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

## Bernini - EXPLICIT OPT-IN ONLY

- `{{Image To Image (Bernini).txt}}` - 1 input. ByteDance Bernini-R
  instruction edit.

Use this preset ONLY when the user EXPLICITLY names "Bernini" (e.g. "edit
this with Bernini", "use Bernini"). For every other image edit, default to
the Klein presets above - do NOT pick Bernini on your own. Bernini is a
single-image edit path here (one input via `chat_image` / `attachment` /
`chain`); it does not take multiple reference slots, so use it only for
single-source edits. Same narrative-prose, identity-lock, and
describe-the-delta rules apply.

## Multi-person heuristic

For a scene with 2+ previously-shown people:

1. Count the recurring people who exist as chat-image bubbles.
2. Default: `{{Reference To Image (MiniMax H3).txt}}` - feed each person's
   bubble/anchor as `chat_image` / `chat_image2` / ... / `chat_image9` and
   tag each as `<Picture N>` in the prompt (H3 REFERENCE RENDER above).
3. Klein N-Input instead only when the composite must preserve existing
   pixels/composition (then N = exact reference count, max 5, canonical
   4-part Klein pattern above).

Never duplicate the same chat_image into two slots. Newly-invented
people stay in the prompt text - only feed a slot per person you want
to lock to a specific past appearance.

## Single-input edit pattern (1-Input preset)

Single-subject edits don't need the multi-input structure. Just open
with a brief identity clause ("Keep her face and hair exactly as is,
~32, Latina") then state the delta:

```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 1 Input.txt}}" prompt="Keep her face and hair exactly as is, ~32, Latina. Add a wide-brimmed black straw sunhat with a faded pink ribbon, tilted slightly over her right brow." chat_image="1"/>
```

Drop the identity clause ONLY if the user explicitly asked to change
the face/hair/age/ethnicity. When changing one of those, anchor the
OTHERS explicitly so only the requested attribute moves.

The brief "face and hair" clause is enough only for tight head-and-shoulders
edits. For full-body or multi-person subjects, or any edit that also moves
them to a new setting, use the stronger lock from IDENTITY LOCK above
(exact heights, body proportions, poses, relative positioning) on the FIRST
attempt and keep the setting change a tight delta.

## Invocation examples

Same-reply generate then edit (chain):
```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full Z-Image scene>"/>
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 1 Input.txt}}" prompt="Keep everything as is except change the time of day to dusk, warm orange light from the west." chain="true"/>
```

Subject + scene combine (2-Input):
```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 2 Input.txt}}" prompt="Image 1's subject (Latina woman, ~32) seated at the cafe table in the scene from image 2, maintaining exact likeness, soft afternoon window light from the right." chat_image="1" chat_image2="2"/>
```

Group photo, 4 people (H3 reference render - the default):
```
<aitools_action skill="image_to_image" preset="{{Reference To Image (MiniMax H3).txt}}" chat_image="1" chat_image2="2" chat_image3="3" chat_image4="4" width="1152" height="640" prompt="subject_definitions:
<Subject 1> is the man in <Picture 1>, <a few caption traits>. <Subject 2> is the woman in <Picture 2>, ... <Subject 3> is the man in <Picture 3>, ... <Subject 4> is the woman in <Picture 4>, ...

summary:
[reference generation] The target image shows all four together in a cozy Christmas living room.

retention_analysis:
<Subject 1> / <Subject 2> / <Subject 3> / <Subject 4>: fully_preserved - faces, hair, and wardrobe retained.

detailed_description:
A warm photographic evening style lit by fireplace glow from the left. Left to right: <Subject 1> holding a steaming mug, <Subject 2> next to him laughing, <Subject 3> leaning on the mantle with an arm around <Subject 4>. A decorated Christmas tree glows behind them... <complete the scene to ~120-250 words>

overall_soundscape: N/A

non_diegetic_music: N/A"/>
```

Same scene as a Klein 4-Input composite (only when preserving existing
pixels/composition, or Klein was requested): swap the preset, use "image N"
phrasing plus visual tags and the likeness clause instead of <Picture N>.
For 2-3 subjects target 50-65 words.

## Rules summary

- NEW scene FEATURING existing people/anchors -> `{{Reference To Image
  (MiniMax H3).txt}}` (Quality variant only on explicit high-quality asks),
  `<Picture N>` tag per staged photo, up to 9 refs. In-place EDITS ->
  Klein by input count.
- Pick exactly ONE primary source.
- Movie sources require an explicit still/current-frame request plus
  `movie_frame="true"`; all other Movie edits use video_to_video.
- Recurring characters: feed them by anchor NAME in `chat_image*`
  (`chat_image="Elias"`); the prose says `<Picture N>` (H3) / "image N"
  (Klein), never the chat name.
- Never feed a downstream composite as the anchor; names already prevent
  this. Update a look by re-tagging `anchor="Name"` on a fresh edit.
- Klein prompts: open with a concise per-slot identity clause, include
  per-subject placement + left-to-right ordering on multi-person scenes,
  and describe the CHANGE, not the whole image. H3 prompts: the six-section
  document - tags carry identity in subject_definitions,
  detailed_description (~120-250 words) describes the new scene, both audio
  sections N/A.
