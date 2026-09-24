---
id: image_to_image
summary: Still-image edits AND new stills featuring existing chat people/anchors/references. DEFAULT preset {{Image To Image (Qwen Image 2.1).txt}} for both, up to 10 inputs via chat_image + chat_image2..10. (1) In-place EDIT of one image (delta that keeps the source's composition): chat_image = the image to edit, omit width/height, prompt = an instruction that leads with the operation and names what stays fixed (single image: say "the image", no tags). (2) NEW scene FEATURING existing people/anchors ("them together", group shots, variations, re-poses): every person's anchor in its own slot, prompt calls them <image1>, <image2>... and points at the images for identity (never re-describe faces), and pass width/height for the new frame (1248x832 group/landscape, 832x1248 portrait). Output canvas = chat_image's aspect unless width/height are given, so the image being edited (the scene/canvas) goes in chat_image. Klein, Bernini and H3 Reference To Image only when the user names them. A Movie #N is NOT a still source by default: scene/motion/dialogue/audio edits use video_to_video. Only when the user explicitly requests one still/current frame may image_to_image target a Movie, and the action must include movie_frame="true". Result spawns as a new still; originals remain unchanged.
inputs: attachment
autoload: true
triggers: edit the image, edit this image, modify the image, alter the image, change the image, tweak the image, adjust the image, retouch, refine the image, transform the image, restyle, restyle as, redraw, repaint, change the pose, change her pose, change his pose, new pose, different pose, dress her, dress him, undress, replace the, swap the, swap out, remove from the image, in the style of, them together, all together, side by side, group photo of them, group shot of, all three of them, all four of them, all five of them, both of them in, the two of them in, in one image, all in one, use them as anchors, use these as anchors, combine them, combine these, put them together, put them all, put all of them, scene with them, scene with all, posing together, line them up, hanging out together
exclude_triggers: generate a brand new, brand new image, fresh image of a, fresh image from scratch, picture from scratch
template: <aitools_action skill="image_to_image" preset="{{Image To Image (Qwen Image 2.1).txt}}" chat_image="N" prompt="<instruction: lead with the operation, name what stays fixed. 1 input: 'the image'. 2+ inputs: <image1>, <image2>... with each image's role.>"/>  # STILL sources only. attachment= works only in the very message the user pasted the image in; on later turns use chat_image="N" (the paste's bubble number). A Movie source is allowed only for an explicit single-frame/current-frame request and requires movie_frame="true". For a NEW scene featuring existing people add chat_image2..chat_image10 (one per person, anchors by name) plus width/height for the new frame.
---
# Image-to-image (Qwen Image 2.1 edit / multi-reference)

`{{Image To Image (Qwen Image 2.1).txt}}` is the default for every still-image
job that starts from existing images: in-place edits, and new scenes that star
people or things already in chat. It takes up to TEN inputs: `chat_image` (or
`attachment`) is `<image1>`, `chat_image2..chat_image10` (or
`attachment2..10`) are `<image2>..<image10>`.

## ANCHOR DISCIPLINE - reference recurring characters BY NAME

The single most common drift failure: a multi-character scene works once,
then on every follow-up turn the model points `chat_image` at the
most-recent composite instead of the original per-character anchor.
Every composite has already drifted slightly; chaining off it compounds
the drift every turn until the characters stop looking like themselves.

**Named anchors remove the bookkeeping.** When a character first appears
as a portrait, tag that action with `anchor="Name"`:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Qwen Image 2.1).txt}}" width="832" height="1248" prompt="<full portrait of the clockmaker>" anchor="Elias"/>
```

From then on, refer to that character by name in any `chat_image` slot -
`chat_image="Elias"` - and the host resolves the name to that
character's CURRENT anchor image automatically. You do NOT track slot
numbers, and the name always points at the canonical portrait, never a
drifted composite. The live name->slot map is printed every turn in the
`ANCHORS:` line of CURRENT STATE - read it to see who exists.

WRONG (drift trap - points at the composite, and guesses a number):
> User: "now show them at the beach"
> `<aitools_action skill="image_to_image" preset="{{Image To Image (Qwen Image 2.1).txt}}"
>   prompt="Move them to a sunny beach..." chat_image="5"/>`

RIGHT (each character by anchor name, new frame size given):
> User: "now show them at the beach"
> `<aitools_action skill="image_to_image" preset="{{Image To Image (Qwen Image 2.1).txt}}"
>   chat_image="Elias" chat_image2="Mei" chat_image3="Jonah" chat_image4="Layla"
>   width="1248" height="832"
>   prompt="Create a wide realistic photograph of the four people from <image1>,
>   <image2>, <image3> and <image4> together on a sunny tropical beach ...
>   - the full new-scene pattern below"/>`

The `prompt=` binds people to photos ONLY through slot-order tags -
`<image1>` is whichever name you put in `chat_image`, `<image2>` is
`chat_image2`, and so on. Names are ONLY for the `chat_image*` attributes;
the prose never says "Elias".

If a character has no anchor name yet (older session, or a user-supplied
reference), fall back to the numeric slot from the `ANCHORS:` / `CHAT
IMAGES:` lines - same rule, just feed the canonical portrait's number,
never a composite.

**Updating a character's look** (new outfit, haircut, scar): edit their
current anchor and re-tag the SAME name - `anchor="Elias"` - which re-points
the name to the new image. Every later `chat_image="Elias"` then uses the
updated look:

```
<aitools_action skill="image_to_image" preset="{{Image To Image (Qwen Image 2.1).txt}}" chat_image="Elias" anchor="Elias" prompt="Change the man's outfit to a charcoal three-piece suit with a white shirt and a dark green tie. Keep his face, white beard, hair, pose, the background and the lighting exactly as in the image."/>
```

Single-character variation series follow the same rule: feed
`chat_image="Elias"` (the anchor) for every "show him doing X" follow-up,
NOT the previous variant's bubble.

## The two modes

Decide first which one the user wants - it changes the prompt, the canvas and
how much you build.

**EDIT - "change this picture"**: a local object/attribute/background change,
text change, style change, relighting, removal ("change the sky", "add a hat",
"remove the car", "make it night"). The output keeps the source's composition.
`chat_image` = the image being edited; extra slots only for material to bring
in (a garment, a logo, a second person). Omit width/height: the canvas follows
`chat_image`.

**NEW SCENE - "a new picture of these subjects"**: people/things already in
chat placed in a new setting, a group shot, a variation, a re-pose, a photo
shoot. Every subject gets its own slot; there is no canvas image, so pass
width/height for the new frame: `1248x832` group / landscape scene,
`832x1248` portrait / single figure, `1344x768` wide cinematic, `1024x1024`
square. You design the scene, lighting and composition to a professional
standard here.

If a scene image IS the canvas ("put her into this cafe photo" with the cafe
as a chat image), that is an EDIT of the cafe: the cafe goes in `chat_image`,
the person in `chat_image2`, no width/height.

## Prompt rules (from Qwen's official edit prompt enhancer)

- **An instruction, not a caption.** Lead with the operation ("Replace the
  daytime sky with ...", "Place the woman from <image2> ...", "Create a wide
  realistic photograph of ..."), written as someone holding only the input
  images. One English paragraph, no line breaks.
- **Tags.** With ONE input, never use tags - say "the image", "the man in the
  image". With TWO OR MORE inputs, every reference is `<image1>`, `<image2>`,
  ... - never "image 1", "the first image", "picture A". State each image's
  role (the canvas whose composition survives vs. the material or identity
  taken from it) and describe every referenced image individually.
- **Name what changes concretely and push it to an unmistakable degree.** A
  faint edit that could be mistaken for the input is a failure.
- **Hold everything else with ONE blanket preservation clause** that names
  kept content by type and role, not appearance: "Keep the people, their
  faces, poses and clothing, the background and the lighting exactly as in
  the image." Re-describing something you meant to keep makes the model
  regenerate it, and it drifts.
- **Identity comes from the image, not from words.** Point at the reference
  ("the woman from <image2>", "keep his face exactly as in <image1>") instead
  of describing facial features; verbal descriptions make the model redraw the
  face and lose the likeness.
- **Only what was asked.** Don't add operations or clean up unmentioned
  clutter. When something is removed or moved, say what now fills the exposed
  area.
- **Text is literal.** Any readable text in the output gets its exact
  characters, written as `&quot;...&quot;` inside the action tag. Match the
  typography the image already uses unless asked otherwise.
- **Length.** EDIT: ~30-80 words. NEW SCENE: ~120-250 words - design the
  placement (left to right for groups), pose, action, setting, lighting and
  style, but still no face descriptions.
- Never write sizes or ratios in the prompt; that is width/height.

## IDENTITY LOCK - anchoring MEANS "keep their identity" BY DEFAULT

Using an anchor, or editing an existing person via `chat_image`, IS the
instruction to keep their face / height / build - that is the entire point of
anchoring. The user does NOT have to say "don't change their faces"; assume
it. Include the lock on EVERY anchored / `chat_image` edit by default, unless
the user EXPLICITLY asks to change their face, age, or body. Lock hard on the
FIRST attempt, not after a complaint: faces AND heights, proportions, stance
and left-to-right spacing are what slip on full-body and group images.

- EDIT: "Keep every person's face, hairstyle, height, body proportions, pose
  and position exactly as in the image."
- NEW SCENE: "Keep each person's face, hairstyle, build and clothing exactly as
  in their reference image" (plus "<image1>'s man is noticeably taller than
  <image2>'s woman" style notes when relative height matters).

RELOCATION edits (costume swap + new setting on an existing composite, e.g.
"put them at the North Pole") are the highest-drift EDIT case: keep the people
clause strong and describe the change as a tight delta ("replace the duck
onesies with penguin costumes and the background with Arctic ice") rather than
a full from-scratch scene. If the user really wants a NEW scene, use the NEW
SCENE mode from their anchors instead.

## "DO N MORE VERSIONS" - keep it image_to_image, emit them all at once

When the user asks for several variations of someone already in chat -
"now as an elephant, a bee, and a dragon", "give me three more versions",
"same boys but at the beach / in space / as superheroes" - EVERY variation
is another `image_to_image` action, NOT a `generate_image`. Rules:

- Feed the SAME ORIGINAL source on every variation (`chat_image="1"` or the
  anchor name) - the canonical face, never the previous variation's output
  (chaining off the last variant compounds drift).
- NEVER use `generate_image` for a variation of an existing person.
  Re-describing them from text produces a stranger no matter how detailed -
  that is the exact failure this skill exists to prevent.
- The variations are INDEPENDENT of each other, so emit ALL of them in ONE
  reply (one `image_to_image` tag per variation). You do NOT need `continue`
  for independent variations - only use `continue` when a later step needs an
  earlier step's OUTPUT image. Do not stop after one or two and trail off.
- A new setting/costume/pose is a NEW SCENE (pass width/height); "same
  picture, just add a hat" is an EDIT (omit them).

## NEVER use chat character names in the prompt - HARDEST RULE

The model has no chat history. It sees only the numbered input images and
the literal `prompt=` text. A name like "Mei-Lin", "Elias Thorne", "the
heroine" is just an unresolvable token. Refer to each subject by its tag
(`<image1>`, or "the man in the image" for a single input), plus at most a
brief role word.

WRONG (bare name): `"Place Elias and Mara at the fireplace..."`
WRONG (tag + name hybrid, common failure): `"<image1>'s clockmaker Elias next
to <image2>'s scientist Mei"`
RIGHT: `"the man from <image1> on the left next to the woman from <image2>"`

Chat prose can still use names freely. This rule applies ONLY to the
`prompt=` attribute.

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

Extra slots go in `chat_image2`..`chat_image10` or `attachment2`..`attachment10`
(each may be a number or an anchor name). `chat_image{N}` wins over
`attachment{N}`. Never put the same image in two slots. Newly-invented people
stay in the prompt text - only feed a slot per person you want to lock to a
specific past appearance.

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
3. `image_to_image` with `{{Image To Image (Qwen Image 2.1).txt}}` uses the
   guide composite as `<image1>` (`chain="true"` when adjacent) and the
   original logo as `<image2>` (`attachment2="N"` / `chat_image2="N"`). The
   prompt says `<image1>` is the canvas with only a placement guide and
   `<image2>` is the logo source, and asks for the mark to be
   painted/inlaid/embossed/tattooed/formed into the material, following
   curvature, lighting, shadows, and texture; preserve the logo's geometry and
   colors; do not make it glowing, white, or a generic letter unless the
   source is. Anchor this final result with the same subject anchor name, then
   use that final anchor for later edits, dangerous variants, and videos.

Never use the logo/reference as the primary `chat_image`; that edits the logo
itself into the requested scene instead of applying it to the subject.

Integrated same-reply example:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Qwen Image 2.1).txt}}" width="1248" height="832" prompt="<full visual prompt for a realistic baby dragon, no logo yet>" anchor="Dragon"/>
<aitools_action skill="paste_image" chain="true" source_attachment="1" x="43%" y="45%" width="14%" height="14%" mode="fit" opacity="1"/>
<aitools_action skill="image_to_image" preset="{{Image To Image (Qwen Image 2.1).txt}}" chain="true" attachment2="1" anchor="Dragon" prompt="Integrate the logo from <image2> into the chest scales of the baby dragon in <image1>, replacing the flat placement guide on its chest with a natural inlaid scale pattern that follows the body curvature, scale texture and forest lighting. Keep the logo's exact geometry and colors from <image2> and keep it readable, not glowing or white. Keep the dragon's pose, body, the background and the lighting of <image1> exactly as they are."/>
```

## Presets

- `{{Image To Image (Qwen Image 2.1).txt}}` - DEFAULT for edits and new
  scenes, 1-10 inputs. Qwen-Image 2.1 (strong identity from references,
  legible text, native up to 2048x2048).

Only when the user explicitly names the model:

- Klein / Flux 2 ("Klein", "Flux"): `{{Image To Image Klein Edit 1 Input.txt}}`
  .. `{{Image To Image Klein Edit 5 Input.txt}}`, picked by EXACT input count
  (4 people -> 4 Input). Klein wants 40-70 words of narrative prose that
  refers to slots as "image 1", "image 2" instead of tags.
- MiniMax H3 ("H3", "MiniMax" still): `{{Reference To Image (MiniMax H3).txt}}`
  (`... (MiniMax H3 Quality).txt` for high quality), up to 9 refs. Its prompt is
  the six-section H3 reference document (subject_definitions / summary /
  retention_analysis / detailed_description / overall_soundscape: N/A /
  non_diegetic_music: N/A) and every staged photo MUST be addressed as
  `<Picture N>` - the host blocks the render otherwise. The same format as the
  H3 video reference presets (see image_to_movie).
- Bernini ("Bernini"): `{{Image To Image (Bernini).txt}}` - 1 input,
  ByteDance Bernini-R instruction edit; same instruction-style prompt.

## Invocation examples

Single-input edit (no tags, no size):
```
<aitools_action skill="image_to_image" preset="{{Image To Image (Qwen Image 2.1).txt}}" chat_image="1" prompt="Add a wide-brimmed black straw sunhat with a faded pink ribbon to the woman, tilted slightly over her right brow and casting a soft shadow across her forehead. Keep her face, hair, expression, clothing, pose, the background and the lighting exactly as in the image."/>
```

Same-reply generate then edit (chain):
```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Qwen Image 2.1).txt}}" width="1248" height="832" prompt="<full Qwen Image scene>"/>
<aitools_action skill="image_to_image" preset="{{Image To Image (Qwen Image 2.1).txt}}" chain="true" prompt="Change the time of day to dusk: a deep orange sky with long warm light from the left and lit windows. Keep every person, object and the composition exactly as in the image."/>
```

Person into an existing scene photo (the scene is the canvas):
```
<aitools_action skill="image_to_image" preset="{{Image To Image (Qwen Image 2.1).txt}}" chat_image="2" chat_image2="Mei" prompt="Place the woman from <image2> seated at the empty cafe table in <image1>, facing the camera with a coffee cup in her hands. Keep her face, hairstyle and clothing exactly as in <image2>, and match her lighting and colour to the soft afternoon window light of <image1>. Keep the cafe, the furniture and the composition of <image1> exactly as they are."/>
```

Group photo, 4 anchored people (new scene - width/height given):
```
<aitools_action skill="image_to_image" preset="{{Image To Image (Qwen Image 2.1).txt}}" chat_image="Elias" chat_image2="Mei" chat_image3="Jonah" chat_image4="Layla" width="1248" height="832" prompt="Create a wide realistic photograph of the man from <image1>, the woman from <image2>, the man from <image3> and the woman from <image4> together in a cozy wood-paneled living room on Christmas evening. Left to right: the man from <image1> holds a steaming mug, the woman from <image2> stands next to him laughing, the man from <image3> leans on the stone mantle with an arm around the woman from <image4>. Behind them a decorated fir tree glows with warm white lights, stockings hang from the mantle and snow is visible through a frosted window on the right. Keep each person's face, hairstyle, build, relative height and clothing exactly as in their reference image. The lighting is warm firelight from the left with soft amber fill and gentle shadows, shallow depth of field, natural skin tones."/>
```

## Rules summary

- DEFAULT `{{Image To Image (Qwen Image 2.1).txt}}`, 1-10 inputs; Klein /
  H3 / Bernini only when named.
- EDIT: the edited image in `chat_image`, no width/height, instruction +
  one blanket preservation clause, ~30-80 words.
- NEW SCENE featuring existing people: one slot per person (anchors by
  name), width/height for the new frame, `<image1>..` tags, identity pointed
  at the images, ~120-250 words.
- 1 input: no tags. 2+ inputs: `<imageN>` tags only, never chat names.
- Pick exactly ONE primary source; never the same image in two slots.
- Movie sources require an explicit still/current-frame request plus
  `movie_frame="true"`; all other Movie edits use video_to_video.
- Never feed a downstream composite as the anchor; names already prevent
  this. Update a look by re-tagging `anchor="Name"` on a fresh edit.
