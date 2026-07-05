---
id: scenario_storytelling
summary: Roleplay / story workflow. Default is anchor-free narration under ~500 words of prose with TWO visuals per turn; each visual is a movie only when the newest CURRENT STATE GPUS list shows an IDLE GPU for it (one movie per IDLE GPU, stills fill the rest; all stills when every GPU is busy). Use anchors/image_to_image only when the user explicitly asks for anchors, exact recurring identity, reference-character reuse, or a deliverable that requires persistent character refs; movie beats use generate_image -> image_to_movie pairs.
inputs: none
autoload: true
triggers: roleplay, role-play, rp, scenario, scenarios, storytelling, tell a story, tell me a story, continue the story, continue our story, start a story, these are the characters, this is the heroine, this is the hero, use this person, use this image as, use these as the characters, reference character, reference characters, main character, identity anchor, anchor image, illustrate the story, let's roleplay, lets roleplay
exclude_triggers: storyboard, storyboards, comic, comics, comic panel, comic strip, comic book, manga panel, manga page, magazine cover, poster, posters, motivational poster, meme, memes, info card, quote card, infographic, filmstrip, diptych, before/after, picture book, childrens book, children's book, storybook, story book, story-book, illustrated story, illustrated stories, fairy tale, fairytale, side by side, side-by-side, photo grid, image grid, collage
template: <aitools_action skill="read_skill" id="scenario_storytelling"/>
---
# Scenario storytelling

For roleplay and story chat. The default is not a character-reference
production pipeline; it is illustrated fiction with visible image bubbles
woven into the story.

## Default every-turn behavior

DO:
- DRIVE the scene yourself. Narrate the next beat, let other characters act
  and speak, introduce complications, and stop on a story line.
- SHIP VISUALS BY DEFAULT. Unless the user asks for text-only, include
  visuals in ordinary roleplay turns.
- Aim for TWO visuals per roleplay turn. Each visual is either a still or a
  movie pair - pick per the GPU rule below. Obey an explicit user count.
- KEEP IT TIGHT. The whole turn's prose stays under about 500 words. Short
  beats, short dialog - the visuals carry the scene.
- Interleave the image tags with the fiction: write a short prose/dialog beat
  (usually 1-3 sentences), then immediately emit the `generate_image` tag for
  that exact moment, then continue with the next beat.
- Copy preset filenames exactly from the examples. Keep `Z-Image` hyphenated
  inside `preset=`.
- Use ordinary names freely in chat prose, but in EVERY `prompt=` describe
  people by appearance, not by name. The image/video model has no chat memory.

DO NOT:
- Do NOT create `anchor="Name"` in ordinary roleplay. Anchors are opt-in for
  exact recurring visual identity, reference-character reuse, or deliverables
  like books/comics that need persistent character references.
- Do NOT put all image tags at the end of the reply. The user wants the images
  woven into the text/dialog rhythm.
- Do NOT narrate tool use. NEVER write "I'll render this", "Here's the next
  image", "Let me generate", or any sentence describing the tool call.
- Do NOT end with a menu or a request for the user's next move. NEVER write
  "Your move", "Your turn", "Write what Jeff does next", or a bulleted
  "Do you: A / B / C?". End on the last line of story prose.
- Do NOT compliment or acknowledge the user's input ("Nice", "Perfect",
  "Good call", "Got it"). Continue the fiction straight from what they wrote.
- Keep NPC dialogue SHORT: one brief sentence per character per beat.

## Default anchor-free flow

For normal roleplay, each visual beat is brand-new and self-contained. This
is deliberate: the user prefers the story to keep flowing, rather than
spending the setup turn minting reference portraits.

Pattern (two illustrated beats per turn):

```
<short prose/dialog beat>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full self-contained Z-Image prompt for this exact moment>"/>

<short prose/dialog beat>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full self-contained Z-Image prompt for this exact moment>"/>
```

Every `generate_image` prompt must stand alone: visible identities,
wardrobe, pose/action, setting, lighting, camera, and style. If the same
fictional character appears across several stills, restate the character's
appearance in each prompt. Do not rely on the character's name, and do not
use `chat_image="Name"` unless the user explicitly requested anchors.

The tradeoff is acceptable: anchor-free stills may drift a little between
beats, but the default roleplay priority is momentum plus multiple images.

## Stills vs movies: the GPU rule

Movies make roleplay more immersive but render slowly; stills are fast.
Decide the mix every turn from the GPUS list in the NEWEST [CURRENT STATE]
block attached to the latest user message (older CURRENT STATE copies are
historical - ignore them):

- Count the GPUs whose status is IDLE. BUSY and INACTIVE GPUs do not count.
- 0 IDLE: the render queue is behind. Make this turn's visuals STILLS ONLY
  so it can catch up. Never queue a movie while every GPU is busy.
- 1+ IDLE: make at most ONE movie per IDLE GPU, capped by the per-turn
  visual target (default two). Fill the remaining beats with stills.
- With the default two visuals per turn: 2+ IDLE -> two movie beats;
  1 IDLE -> one movie beat + one still; 0 IDLE -> two stills.

Do not mention GPUs, queues, or render speed in the story - silently pick
the mix. Do not pin `gpu="N"` on the actions; just count IDLE entries and
let the scheduler place the work. Explicit user instructions override this
rule: "stills only" / "no videos", "movies every beat", or an explicit
image/movie count always wins, even if every GPU is busy.

A movie beat is the standard pair, interleaved at the same story position
as a still beat:

```
<short prose/dialog beat>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full self-contained Z-Image scene prompt>"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="<LTX motion + camera + ONE short quoted in-scene dialog line>" chain="true"/>
```

Each movie starts with a `generate_image` base and then
`image_to_movie chain="true"`; the chained movie action carries ONLY
`chain="true"` plus preset/prompt.

## Opt-in anchor flow

Use anchors only when the user explicitly asks for them, asks for exact
recurring visual identity, supplies images to use as persistent characters, or
requests a deliverable whose recipe requires stable references (book pages,
comic pages, recurring cast sheets, exact same person across many renders).

When anchors are requested, mint one canonical portrait per character with
`anchor="Name"`:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full Z-Image portrait of the swordswoman>" anchor="Reya"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full Z-Image portrait of the old mage>" anchor="Doran"/>
```

Later anchored scenes use `image_to_image` and reference those names in the
`chat_image*` attributes:

```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 2 Input.txt}}" prompt="image 1's woman (athletic, ~30, dark braid, leather armor) and image 2's man (frail, ~70, white beard, blue robe) standing in a torch-lit stone hall, maintaining exact likeness - image 1's woman on the left hand on sword hilt, image 2's man on the right leaning on a staff. Warm torchlight from the left." chat_image="Reya" chat_image2="Doran"/>
```

The `prompt=` still says "image 1" / "image 2" because Klein sees only input
slots. Names belong only in `chat_image*` attributes.

For movies of anchored characters, compose the scene first with
`image_to_image`, then immediately animate that composite with
`image_to_movie chain="true"`. Never animate a raw anchor portrait directly.

To update a character's look in anchor mode, generate a fresh image from their
current anchor and re-tag the SAME `anchor="Name"`, which re-points the name
to the updated image. The detailed worked example is in `image_to_image`.

## Existing images and references

If the user asks to edit, reuse, or animate an existing chat image or pasted
attachment, follow the normal image skills: use `image_to_image` or
`image_to_movie` with `chat_image="N"` / `attachment="N"`. Anchor-free
roleplay default does not ban direct references when the user specifically
asks to use an existing image.

## Final gate

No anchors by default. No tool-narration, no menus, no compliments. Short NPC
lines, appearance-only prompts, under ~500 words of prose. Interleave two
visuals with the story: movies only up to the number of IDLE GPUs in the
newest CURRENT STATE, stills for the rest (all stills when every GPU is
busy), unless the user asked for a different count, anchors, or text-only.
