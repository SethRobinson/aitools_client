---
id: scenario_storytelling
summary: Scenario and roleplay workflow for recurring characters, identity anchors, story prose plus illustrations/movies, and GPU-aware multi-shot planning.
inputs: none
autoload: true
triggers: roleplay, role-play, rp, scenario, scenarios, story, stories, storytelling, tell a story, continue the story, adventure, campaign, quest, character, characters, protagonist, hero, heroine, villain, companion, party, reference character, reference characters, main character, use this person, use this image as, these are the characters, identity anchor, anchor image, illustrate the story, illustrate this
exclude_triggers: storyboard, storyboards, comic, comics, comic panel, comic strip, comic book, manga panel, manga page, magazine cover, poster, posters, motivational poster, meme, memes, info card, quote card, infographic, filmstrip, diptych, before/after, picture book, childrens book, children's book, storybook, story book, story-book, illustrated story, illustrated stories, fairy tale, fairytale, side by side, side-by-side, photo grid, image grid, collage
template: <aitools_action skill="read_skill" id="scenario_storytelling"/>
---
# Scenario storytelling

Use this for roleplay, stories, recurring characters, and user-supplied
character reference images.

## Always Do This

Every story / roleplay turn has this shape:

1. Write visible story prose first: 1-3 concise paragraphs of narration,
   dialogue, or in-character response. Names are fine here.
2. Then emit image/movie action tags for important visual beats, unless the
   user explicitly asked for text-only / no images / no video.
3. In action prompts, do not rely on names. Describe each visible person by
   apparent age, ethnicity/complexion, build, hair, face, wardrobe,
   expression, pose/action, setting, lighting, camera, and style.


## Anchor Tracking

If the user says an image is a character ("use this person as Bob",
"these are the two main characters", "this is the heroine"), remember:

- character name -> Image #N
- character name -> visual identity sheet
- for pairs/groups, remember which character maps to which Image #N

For a user-supplied anchor, never use raw `generate_image` or
`generate_movie` for that character. Use `image_to_image` to compose the
anchored character into the scene first.

## Setup turn (when YOU invent the characters)

If the user kicks off a roleplay without supplying any reference images
("create a random situation and characters" / "make up some characters and
let's roleplay" / "start a story with 3 characters"), you are inventing
the cast. In the SETUP reply you MUST:

1. Write the visible chat prose introducing the setting and the cast.
2. Emit ONE `generate_image` per named character you intend to anchor
   later - in the SAME setup reply, before the user replies again. Each
   one spawns its own numbered chat bubble.
3. Record in chat prose which character maps to which Image #N (e.g.
   "Jax (Image #1)", "Elara (Image #2)") so the user and you both know.

If you introduce 3 characters in prose, you MUST emit 3 generate_image
tags (one per character). Skipping anchor portraits and then trying to
reference `chat_image2="N"` on the next turn will fail - that bubble
does not exist. Establishing anchors up front is the whole point of
this workflow.

The setup turn is the ONLY exception to "use image_to_image for anchored
characters" - on the setup turn there's nothing to anchor TO yet, so
generate_image is correct for minting each portrait. From the NEXT reply
onward, treat every named character as an anchor and use Patterns C-F.

### Precondition for Pattern E / F (multi-anchor) on follow-up turns

Before emitting an `image_to_image` with `chat_image2`/`chat_image3`/...,
look at the CHAT IMAGES block: every chat_image{N} attribute you set MUST
point to a bubble that ALREADY exists in CHAT IMAGES. If a character you
want to anchor has no portrait yet, you have two options:

- (preferred) Mint the missing anchor portrait FIRST in this same reply
  via `generate_image`, then reference its predicted new Image #N in the
  follow-up `image_to_image`. Remember each `generate_image` appends one
  bubble at the next available number - if CHAT IMAGES currently shows
  K images and you emit a generate_image for the missing anchor, that
  anchor becomes Image #K+1.
- (fallback) Drop to a smaller-N preset (e.g. use the 1-Input preset
  with only the anchor that DOES exist, and describe the un-anchored
  characters visually in the prompt - they won't stay consistent across
  renders, but the action won't fail).

Never reference a chat_image{N} that doesn't exist yet just because you
"plan" to make it later. Either generate it earlier in the same reply,
or pick a different pattern.

## Pick Exactly One Pattern

After the visible story prose, choose the simplest matching pattern below.

### Pattern A: No Anchor, Still Image

Use for invented/new characters or scenes with no user-supplied identity
anchor.

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full visual prompt: people, setting, action, lighting, camera, style>"/>
```

### Pattern B: No Anchor, Movie

Generate the still first, then animate that same Pic.

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full visual prompt: people, setting, action, lighting, camera, style>"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="<full LTX prompt: describe subjects visually again, concrete motion, one quoted in-scene dialog line unless silent, one camera move, lighting/mood, ambient sound>" chain="true"/>
```

### Pattern C: One Anchor, Still Image

Use the 1-input image-to-image preset. The prompt must both anchor identity
and describe the new scene.

```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="Use the person from reference image as <full visual identity: apparent age, ethnicity/complexion, build, hair, face, notable features>; keep facial identity, apparent age, complexion, and hair recognizably consistent. <new scene, wardrobe-of-the-day, pose/action, expression, setting, lighting, camera, style>" chat_image="<anchor N>"/>
```

### Pattern D: One Anchor, Movie

This is the most important anchor-video rule. Always do both steps in one
reply:

1. `image_to_image` composes the anchored person into the new scene.
2. `image_to_movie chain="true"` animates that composed scene.

```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="Use the person from reference image as <full visual identity: apparent age, ethnicity/complexion, build, hair, face, notable features>; keep facial identity, apparent age, complexion, and hair recognizably consistent. <new scene, wardrobe-of-the-day, pose/action, expression, setting, lighting, camera, style>" chat_image="<anchor N>"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="<full LTX prompt: the same visually-described person, concrete motion, one quoted in-scene dialog line unless silent, one camera move, lighting/mood, ambient sound>" chain="true"/>
```

Never skip the first line. Never animate the raw anchor portrait directly
unless the user specifically asks for a portrait animation.

### Pattern E: Two Anchors, Still Image

Use the 2-input preset. Map image 1 and image 2 explicitly in the prompt.

```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 2 Input.txt}}" prompt="Use the person from image 1 as <full visual identity for character A>; use the person from image 2 as <full visual identity for character B>; keep both facial identities, apparent ages, complexions, and hair recognizably consistent. <new scene, explicit position/action for each person, wardrobe-of-the-day, expressions, setting, lighting, camera, style>" chat_image="<anchor A>" chat_image2="<anchor B>"/>
```

### Pattern F: Two Anchors, Movie

Compose both anchored people first, then animate that composite.

```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 2 Input.txt}}" prompt="Use the person from image 1 as <full visual identity for character A>; use the person from image 2 as <full visual identity for character B>; keep both facial identities, apparent ages, complexions, and hair recognizably consistent. <new scene, explicit position/action for each person, wardrobe-of-the-day, expressions, setting, lighting, camera, style>" chat_image="<anchor A>" chat_image2="<anchor B>"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (LTX) 5s.txt}}" prompt="<full LTX prompt: both people described visually again, concrete motion for each, one quoted in-scene dialog line unless silent, one camera move, lighting/mood, ambient sound>" chain="true"/>
```

If a scene uses two known characters but only one anchor is visible in the
shot, use Pattern C or D with that one anchor. If there are three or more
anchored characters, feed the two most important anchors and describe the
others visually in the prompt.

## Multiple Beats

For multiple shots, repeat one complete pattern per shot. Do not mix parts
of different shots together. Example: two anchored movie shots means:

1. story prose
2. `image_to_image` for shot 1
3. `image_to_movie chain="true"` for shot 1
4. `image_to_image` for shot 2
5. `image_to_movie chain="true"` for shot 2

You may add `gpu="N"` to independent first actions when the GPUS block shows
idle GPUs, but GPU planning must not change the required pattern.

## Prompt Rules

- Chat prose can use names. Action prompts must use visual descriptions.
- Every action prompt must be self-contained.
- For anchored people, always include both:
  - "Use the person from reference image / image 1 / image 2..."
  - "keep facial identity, apparent age, complexion, and hair recognizably
    consistent"
- For LTX prompts, include one short quoted in-scene dialog line unless the
  scene has no plausible speaker or the user asked for silence.
- If you catch yourself writing "Mara", "Reyes", "Bob", "the heroine", or
  "the same person" in an action prompt without a full visual restatement,
  rewrite the prompt before emitting it.

## Failure Checklist

Before finalizing a roleplay/story reply, check:

- Did I write visible narration/dialogue before action tags?
- SETUP TURN: if I introduced N named characters and I intend to anchor
  them in future turns, did I emit N `generate_image` tags in THIS reply
  (one per character) and tell the user which Image # maps to which name?
- For every `chat_image{N}` / `chat_image2{N}` / `chat_image3{N}` ... in
  my action tags this turn: does that Image #N already exist in CHAT
  IMAGES, OR will it exist because I'm generating it earlier in this same
  reply? If neither is true, the action will fail - rewrite it.
- If there is a user-supplied anchor, did I use `image_to_image` first?
- If making an anchored movie, did I include `image_to_movie chain="true"`
  immediately after that `image_to_image`?
- If there are two anchored people, did I use the 2-input preset with
  `chat_image` and `chat_image2`?
- Did every action prompt restate the character visually instead of relying
  on a name?
