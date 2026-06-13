---
id: scenario_storytelling
summary: Roleplay / story workflow. Mint one NAMED character anchor per character in the setup turn (generate_image with anchor="Name"), then on later turns compose them with image_to_image referencing each by anchor name, and chain image_to_movie for video. Keeps a recurring cast visually consistent across turns and into video.
inputs: none
autoload: true
triggers: roleplay, role-play, rp, scenario, scenarios, storytelling, tell a story, tell me a story, continue the story, continue our story, start a story, these are the characters, this is the heroine, this is the hero, use this person, use this image as, use these as the characters, reference character, reference characters, main character, identity anchor, anchor image, illustrate the story, let's roleplay, lets roleplay
exclude_triggers: storyboard, storyboards, comic, comics, comic panel, comic strip, comic book, manga panel, manga page, magazine cover, poster, posters, motivational poster, meme, memes, info card, quote card, infographic, filmstrip, diptych, before/after, picture book, childrens book, children's book, storybook, story book, story-book, illustrated story, illustrated stories, fairy tale, fairytale, side by side, side-by-side, photo grid, image grid, collage
template: <aitools_action skill="read_skill" id="scenario_storytelling"/>
---
# Scenario storytelling

For roleplay, stories, and recurring characters that must stay visually
consistent across many turns and into video.

## How to behave EVERY turn - the user WILL notice violations

DO:
- Write the story prose FIRST (1-3 short paragraphs; names fine in prose),
  THEN emit the action tags for the beat.
- DRIVE the story yourself. Narrate the next beat, let the other characters
  act and speak, introduce complications, stop on a story line.
- SHIP THE VISUAL every beat. If the user has EVER asked for movies
  ("provide movies", "illustrate with movies", "two movies each turn"),
  treat it as a STANDING default for the whole session and render each beat
  as a MOVIE (the two-step below), not a still and not prose alone. (The
  SETUP turn is the exception: its visuals are the anchor portraits;
  scene movies start the next turn.)

DO NOT (these are the exact things users keep having to correct - never do
them):
- Do NOT narrate your tool use. NEVER write "I'll render this as a movie",
  "Here's the next beat", "Let me generate", "Now I'll make", or any
  sentence describing what you are about to create. Write the story, then
  emit the action tags silently - the host shows the result.
- Do NOT end with a menu or a request for the user's next move. NEVER write
  "Your move", "Your turn", "Write what Jeff does next", or a bulleted
  "Do you: A / B / C?". End on the last line of story prose. The user writes
  their own next action without being asked.
- Do NOT compliment the user or react to their input ("Nice", "Perfect",
  "Good call", "Got it", "You're taking the lead", "I like that"). Just
  continue the fiction straight from what they wrote.
- Keep NPC dialogue SHORT: ONE brief sentence per character per beat. No
  speeches. Trim hard.
- In EVERY `prompt=` (stills AND movies), describe people by appearance, not
  by name - the image/video model has no names. "the Latina woman in the
  field jacket", never "Lena whispers...".

## The two-turn anchor flow (this is the whole workflow)

Recurring characters stay consistent via ANCHORS: one canonical portrait
per character, tagged with a name. Mint them once; reuse them by name
forever. The live name->slot map shows up every turn in the `ANCHORS:`
line of CURRENT STATE.

**Setup turn** (you invent the cast, or the user supplies references).
Introduce the cast in prose, then emit ONE `generate_image` per character,
each tagged `anchor="Name"` - that mints and names the anchor in one step:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full Z-Image portrait of the swordswoman>" anchor="Reya"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full Z-Image portrait of the old mage>" anchor="Doran"/>
```

Say in prose which name is which ("Reya the swordswoman, Doran the mage").
Do NOT also compose a group scene on the setup turn - the anchors aren't
finished rendering yet. Let them settle; compose from the next turn on.

**Every later turn.** Compose the shot from the EXISTING anchors,
referencing each character by NAME. The host turns each name into its
current slot, so you never track numbers and never drift onto a composite.
The `image_to_image` SKILLS summary (always visible above) has the prose
rules; the example below is usually enough - call
`read_skill id="image_to_image"` only if you need the full reference.

```
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 2 Input.txt}}" prompt="image 1's woman (athletic, ~30, dark braid, leather armor) and image 2's man (frail, ~70, white beard, blue robe) standing in a torch-lit stone hall, maintaining exact likeness - image 1's woman on the left hand on sword hilt, image 2's man on the right leaning on a staff. Warm torchlight from the left." chat_image="Reya" chat_image2="Doran"/>
```

The `prompt=` still says "image 1" / "image 2" (Klein sees the feed in
slot order: slot 1 = whatever is in `chat_image`, slot 2 = `chat_image2`).
Names go ONLY in the `chat_image*` attributes.

## Which actions - one table, two questions

After the prose, pick by (a) how many anchored characters appear IN THIS
shot and (b) still image or movie:

| Anchored chars in shot | Still image | Movie |
|---|---|---|
| 0 (brand-new / invented) | `generate_image` | `generate_image`, then `image_to_movie chain="true"` |
| 1 | `image_to_image` (1-Input Klein), `chat_image="Name"` | ...then `image_to_movie chain="true"` |
| 2-5 | `image_to_image` (N-Input Klein), one `chat_image*` per name | ...then `image_to_movie chain="true"` |

Rules for the table:
- A movie of EXISTING characters is ALWAYS two steps in one reply: compose
  the scene with `image_to_image` (feeding each character by anchor name)
  FIRST, then `image_to_movie chain="true"` to animate that composite.
  `generate_movie` and `image_to_movie chain` on a bare text prompt CANNOT
  show an existing anchored character (Reya, Doran, ...) - text alone
  produces strangers. Only use `generate_movie` for a brand-new character
  who has no anchor. Never animate a raw anchor portrait directly either -
  compose the scene first.
- Multiple movies/beats in one turn: repeat the WHOLE pair per beat -
  `image_to_image` then `image_to_movie chain="true"`, then the next
  `image_to_image` then its `image_to_movie chain="true"`. "Three movies a
  turn" = SIX action tags (three pairs), one striking beat each. Each
  `image_to_movie chain="true"` auto-pairs with the `image_to_image` right
  before it.
- The chained `image_to_movie` carries ONLY `chain="true"` (plus preset and
  prompt). NEVER add `chat_image=`, `attachment=`, or a guessed slot number
  to it - chain already feeds it the previous step's image, and a number
  there is wrong (you cannot know the new bubble's number yet). WRONG:
  `image_to_movie ... chat_image="3" chain="true"`. RIGHT:
  `image_to_movie ... chain="true"`.
- N-Input preset count = number of anchors in the shot (2 chars -> 2
  Input). If a shot has more than 5 characters, feed the 5 most important
  by name and describe the rest visually in the prose.
- The LTX video prompt still needs its one quoted in-scene dialog line -
  see `generate_movie` / `image_to_movie` for the exact rule.

## Updating a character's look

New outfit, injury, aged up: generate a fresh image of them FROM their
current anchor and re-tag the SAME `anchor="Name"`, which re-points the
name to the new image. Worked example is in the `image_to_image` skill.

## "Don't use anchors" mode

If the user says they don't want anchors ("don't use anchors", "stop using
anchors", "just freestyle the images"), switch for the rest of the session
to anchor-free beats: build each movie as `generate_image` (Z-Image) ->
`image_to_movie chain="true"`, and do NOT use `image_to_image` / Klein or
any `chat_image="Name"` reference. Describe everyone visually in each
`generate_image` prompt. The cast won't stay perfectly consistent shot to
shot - that is the tradeoff the user chose. (This is the "0 anchors" row of
the table; it's also the right path for a brand-new character who has no
anchor yet.)

## Final gate (re-read before sending)

No tool-narration, no menu, no compliments. Short NPC lines, appearance-only
prompts. Ship the beat's visual (a movie if movies were asked for).
