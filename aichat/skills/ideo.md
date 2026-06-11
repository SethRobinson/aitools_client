---
id: ideo
summary: Ideogram 4 structured-caption image generation. When the user says "ideo" or "ideogram" (e.g. "ideo: a ramen shop at night"), you write a structured JSON caption and send THAT JSON as the prompt of a generate_image action using the Ideogram 4 preset. This is a recipe, NOT an executable action - NEVER emit skill="ideo"; the recipe auto-loads when triggered, then follow its Invocation section. Excellent at rendered text, posters, signage, and precisely composed scenes.
inputs: none
autoload: true
triggers: ideo, ideogram, ideogram4, ideogram 4
template: <aitools_action skill="read_skill" id="ideo"/>
---
# Ideogram 4 ("ideo") - structured JSON caption images

When the user asks for an image "with ideo" / "using ideogram" (or similar),
do NOT write a normal prose prompt. Instead, convert the user's idea into a
structured JSON caption and emit it as the `prompt` of a normal
`generate_image` action using the Ideogram 4 preset. Ideogram 4's text
encoder consumes this JSON directly - it gives precise control over layout,
typography, and per-element detail.

## Invocation

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Ideogram 4).txt}}" width="1024" height="1024" prompt="...single-line JSON caption, every inner double quote escaped as \"..."/>
```

Procedure:
1. Pick dimensions FIRST. Did the user's message explicitly name a ratio,
   orientation, or format ("wide", "16:9", "portrait", "banner", "book
   cover", "phone wallpaper")? If YES, use the matching table row below.
   If NO, you MUST use width="1024" height="1024" and aspect_ratio "1:1" -
   no exceptions. Put the matching `W:H` string in the JSON's
   `aspect_ratio` field; it must agree with the width/height attributes.
   The ratio drives every bbox decision.
2. Build the JSON caption following the CAPTION FORMAT below.
3. Minify it to ONE line (no markdown fences, no commentary), place it in the
   `prompt` attribute. Keep `prompt` as the LAST attribute in the tag.

Attribute escaping (important):
- Escape every double quote inside the JSON as `\"`.
- A line break INSIDE a text element's `text` field must be written as `\\n`
  (double backslash) so it survives attribute decoding as the JSON escape `\n`.
- Apostrophes / single quotes need no escaping.

### Aspect ratio -> width/height table

Dimensions must be multiples of 32, max 1280 per side (~1MP budget).

| aspect_ratio | width x height | typical use |
|---|---|---|
| 1:1 | 1024 x 1024 | default, ambiguous subjects |
| 16:9 | 1280 x 736 | panoramic, cinematic, landscape |
| 9:16 | 736 x 1280 | phone wallpaper, story format |
| 3:2 | 1216 x 800 | classic photo landscape |
| 2:3 | 800 x 1216 | book cover, portrait photo |
| 4:3 | 1152 x 864 | landscape scenes |
| 3:4 | 864 x 1152 | poster, portrait |
| 4:5 | 896 x 1120 | social portrait |
| 2:1 | 1280 x 640 | wide banner |
| 3:1 | 1280 x 416 | extreme banner / header |

**1:1 (1024 x 1024) is MANDATORY unless the user's own words name a ratio,
orientation, or format.** The model is tuned at 1024x1024. The SUBJECT is
never a reason to deviate: a street scene, landscape, cityscape, or group
shot does NOT "demand" a wide frame, and a standing person does NOT
"demand" a tall one - compose the square. Only the user's explicit request
("wide", "16:9", "portrait", "banner", "book cover", "wallpaper") selects
a non-square row: then panoramic -> wide, portrait subjects -> tall,
designed artifacts -> format conventions (2:3 book cover, 3:4 poster), or
the nearest table row to a concrete ratio they gave.

### Complete example

User says: "ideo: a cat sheltering in a ramen shop doorway at night"

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Ideogram 4).txt}}" width="1024" height="1024" prompt="{\"aspect_ratio\":\"1:1\",\"high_level_description\":\"A photograph of a calico cat sheltering in the doorway of a small ramen shop at night in the rain, neon signs glowing above the entrance.\",\"compositional_deconstruction\":{\"background\":\"Nighttime street outside a small ramen shop; wet asphalt sidewalk with puddles reflecting the neon signage; gentle rain falling; warm light spilling through the glass door; distant blurred streetlights; cool-neutral white balance.\",\"elements\":[{\"type\":\"obj\",\"desc\":\"Compact ramen shop front: wood-and-glass facade, warm yellow interior light, visible menu board through the window, small awning above the entrance, rain dripping from the awning edge.\"},{\"type\":\"text\",\"text\":\"RAMEN\",\"desc\":\"Neon sign above the entrance, red and white lettering, slightly weathered, mounted on a metal frame.\"},{\"type\":\"text\",\"text\":\"OPEN\",\"desc\":\"Small neon sign inside the window, green letters on a dark background.\"},{\"type\":\"obj\",\"bbox\":[710,620,840,820],\"desc\":\"Small calico cat crouched just inside the doorway, white-orange-black patched coat, ears perked, tail wrapped around its paws, all four paws resting on the wooden door sill, partially sheltered from the rain.\"}]}}"/>
```

The chained / multi-step mechanics (`chain="true"`, follow-up image_to_movie,
etc.) work exactly as with any other generate_image. The negative prompt is
unused by this preset.

---

# CAPTION FORMAT

Exactly three top-level keys, in this order:

```json
{"aspect_ratio":"W:H","high_level_description":"...","compositional_deconstruction":{"background":"...","elements":[ ... ]}}
```

- Single-line minified JSON, no other top-level keys.
- Preserve non-ASCII characters as-is (CJK, Cyrillic, accented Latin). Never
  escape as `\uNNNN`, transliterate, or replace `café` with `cafe`.
- Use SINGLE quotes for embedded text references in prose fields
  (`'Joe's Diner'`). The `text` field of text elements is the exception - it
  holds the user's verbatim characters.

### `high_level_description` - observational summary (50-word hard cap)

- ONE long sentence preferred, never more than two.
- Reads like a short natural-language prompt, not an analysis. Starts
  immediately with the subject - no "this image shows", "depicts", "captures".
- Identifies subject(s), medium, and overall composition. Names recognized
  pop-culture entities by full name (`Nike Air Jordan 1`, `Eiffel Tower`).
- Don't enumerate granular features (every color, every typography choice) -
  that detail belongs in element descs or `background`. `various`/`multiple`
  ARE appropriate here; the specificity rule applies to descs, not this field.
- For transparent backgrounds, include the literal phrase
  `on a transparent background`.

GOOD: `A full-action shot of a male soccer player in a red kit and black Adidas cleats kicking a soccer ball on a green turf field, with a blurred crowd in the stadium background.`
BAD (over-specifies): `A male soccer player captured mid-kick on a bright green grass pitch, right leg fully extended through the follow-through at the precise moment...`

## ELEMENTS

Each element is one of:
```
{"type":"obj","bbox":[y1,x1,y2,x2],"desc":"..."}
{"type":"text","bbox":[y1,x1,y2,x2],"text":"LINE ONE\nLINE TWO","desc":"..."}
```
`bbox` is optional per-element (see BBOX STRATEGY).

### SINGLE SUBJECT = SINGLE ELEMENT

A coherent subject - one animal, person, vehicle, building, plant, machine -
is exactly ONE `obj` element. Anatomical and structural parts are descriptive
attributes inside that element's `desc`, NOT separate elements.

FORBIDDEN: a bee split into 8 elements (thorax/wings/eyes/legs); a car split
into 6 (body/wheels/windshield); a person split into 7 (head/torso/limbs); a
building split into 5 (walls/windows/roof/door).

When MULTIPLE distinct subjects appear (a person AND a dog; two bees), use
MULTIPLE elements - one per subject.

**Test:** part-of-one-thing -> goes in that thing's desc. Separate thing ->
its own element.

**Transparent enclosure + featured contents = ONE element** (snow globes,
aquariums, display cases, specimen jars): name enclosure + contents as a
single unified desc.

**Configured parts + revealed interior = ONE element.** A car with an open
door, a machine with raised hood: the open state and revealed interior are
attributes of the single subject's desc.

### Element desc - what to write (30-60 words, 60-word HARD CAP)

Identity first, then major attributes briefly, then one distinguishing detail.
Each desc is a standalone catalog entry - open with the subject's identity,
not a referring phrase like "the X".

GOOD: `Woman walking on the platform, medium size. Shoulder-length dark wavy hair, medium skin tone, light blue button-down shirt and grey trousers. Small bag slung over the right shoulder.`

**Major attributes - always name:**
- People: skin tone, hair (color + style), each visible garment with color,
  expression/gaze, pose, distinguishing feature (mole, glasses, held prop).
- Objects: shape, material, color, distinctive parts (handle, label, logo).
- Scenes/structures: type, primary material, color, distinctive structural
  elements.

**Skip (eats word budget):** surface-finish micro-prose (pick ONE of
matte/glossy/metallic/textured or omit); per-limb pose mechanics (ONE summary
action phrase); camera/shadow/lighting micro-detail (belongs in `background`);
fabric weave, micro-anatomy.

### Element desc - what NOT to include

**No shadows.** Cast/drop/ground/contact shadows - describe in `background`
only when scene-wide, otherwise omit (the renderer infers them).

**No camera or render language** (depth of field, bokeh, motion blur, grain)
inside obj descs - that belongs in `high_level_description` or `background`
as natural prose ONLY when the user explicitly named it.
EXCEPTION: viewpoint/angle (`from a low-angle perspective`, `bird's-eye view`)
IS allowed - place once, usually in the focal subject's desc or background.

**No impressions instead of physical reality.** Avoid `luminous`, `radiant`,
`vibrant`, `lush`, `dynamic`, `gorgeous`, `stunning`, `breathtaking`. Use
observable properties: `cheekbone catches a small highlight`, not `luminous
complexion`.

**No scene-context repetition per-element.** Lighting direction, weather,
ambient surface - describe ONCE in `background`; each desc covers what's
UNIQUE to that element.

**Anchor placements to named references.**
CORRECT: `applied to the forehead near the hairline above the left eyebrow`;
`resting on the lower-right corner of the table directly in front of the laptop`.
INCORRECT: `pressed against the skin`; `sitting on the surface`.

## BACKGROUND (CRITICAL)

`background` describes the scene SHELL: walls and finishes, floor/ground and
surface state, ceiling and architectural fixtures, windows as architecture,
atmosphere (sky, clouds, fog), scene-wide ambient lighting, distant
out-of-focus context (horizon, blurred crowds, distant scenery).

### No double-counting

Anything described in `background` CANNOT also appear as an obj element. Each
scene component lives in EXACTLY ONE field. Before emitting an obj element,
scan `background` - if the component is named there, omit the element.

### ALWAYS-BACKGROUND - never obj elements:

sky, clouds, horizon, distant mountains/hills/tree lines, fog/haze/mist/smoke,
distant cityscape or stadium architecture, distant blurred crowds, the
floor/ground/turf/paving the scene sits on, ambient walls or studio backdrop.

You cannot split these by region (`sky upper-left`, `sky behind the fortress`
are the SAME component - describe once). Technique-level detail on atmospheric
components (watercolor wet-on-wet sky blooms) goes in `background`; the
`background` field is allowed to be long.

### Ground/floor/pavement is ALWAYS background - zero tolerance

The surface the scene sits on (floor, grass, sand, asphalt, road, deck, water
surface, snow, tile, hardwood) lives in `background` only - even if the user's
idea lists it as a foreground item, RE-CLASSIFY it. Surface state (wet,
rain-slicked, cracked, polished), puddles, reflections, neon pools, footprints,
tire tracks, frost - all part of the ground surface, never separate objs, even
when a puddle reflects the hero.

(Why: a floor emitted as an obj at the bottom of the frame becomes a 2D band
that clips the hero's legs - figure rendered half-buried.)

**Discrete objects ON the floor are still elements:** broken glass shards,
scattered debris, leaves, rocks, dropped tools, foreground litter. The rule
covers the SURFACE and its state, never solid objects resting on it.

### Background is the shell only

Furniture, vehicles, equipment, people, animals, decor (artwork, potted
plants, stacks of books), free-standing lamps -> obj elements, never
`background`.

### Shell-affixed prominent objects - DUAL MENTION

Objects that are simultaneously part of the shell AND focal (a chalkboard
covering a classroom's back wall, a fireplace, a large mounted TV, a stage
proscenium, a built-in bookshelf, a fixed sign): all three steps MANDATORY:
1. MENTION in `background` as part of the shell (anchors it to the wall).
2. EMIT as an obj element starting its desc with `the primary background
   element` (carries the detail: material, content, frame, mounting).
3. PLACE FIRST in the elements list (painter's algorithm draws it behind
   foreground items).
Applies ONLY to objects defining the room's architectural identity;
free-standing items get the normal treatment (element only).

### Recession/arrangement is not architecture

Don't smuggle furniture or people into `background` as a receding arrangement.
Forbidden background phrasings: `rows of desks recede toward the back`,
`students seated at the desks`, `cars parked along the street`, `customers
seated at the tables`. The arrangement IS foreground content - emit elements.

### No medium/post-processing effects in background

Forbidden in `background` (route to high_level_description instead, and only
when the user asked): film grain, ISO noise, lens flare, chromatic aberration,
vignetting, bokeh quality, color cast / film-stock shift, paper/canvas
texture, brushstroke texture, halftone dots.

**Test:** read `background` aloud. If you can picture the EMPTY room - no
furniture, people, equipment, wall decor - you're in the shell. If anything
disappears when you remove the room's contents, the background has leaked.

## BBOX STRATEGY

Exactly THREE kinds of elements carry a bbox:
1. **Text whose exact placement IS the point** - hero typography on a
   poster/cover, a title block, a sign the user explicitly positioned
   ("logo top-left"). Ordinary in-scene signage (shop signs, menus, neon,
   labels) stays a text element WITHOUT a bbox - it still renders, but
   follows the scene's perspective. Bboxes are SCREEN-SPACE: stacking many
   small sign boxes down the canvas forces a row of miniature storefronts
   to host them, wrecking the scene's scale against the focal subject.
   At most ~4 text bboxes, and only on designed artifacts.
2. **The focal subject** (the ONE hero person/animal/product) - a single
   tight bbox. For a standing figure, the bbox bottom edge must sit on the
   ground line (y2 roughly 880-980 in a full-scene shot) and the desc must
   state explicit ground contact (`both feet planted on the wet asphalt`,
   `loafers on the pavement`). A focal subject with no bbox - or a bbox
   that ends mid-frame - renders floating in mid-air or at the wrong scale
   while bbox'd signage claims the layout.
3. **Layout-critical design objects** (a logo in a specific corner, a badge
   on a poster) - graphic-design artifacts only.

Everything else - secondary people, vehicles, furniture, stalls, props -
gets NO bbox; the renderer places them naturally. Many overlapping object
boxes produce seamed collages and duplicated subjects.

**Order elements back-to-front (painter's algorithm).** Shell-affixed
dual-mention objects first, then midground objects, then the focal subject
LAST so it is composited in front. A focal subject listed first gets
painted over and fights the later elements for depth.

**No grab-bag elements.** Never bundle unrelated props into one element
(`trash bins, boxes, potted plants, wires, puddles...`). One element = one
coherent subject. Worthy props get their own element (no bbox); the rest
stay unmentioned - and NEVER restate things `background` already owns
(wires, puddles, pavement) as an element.

When you DO include one: coordinates are normalized to the target image -
`x` runs left->right (0 = left edge, 1000 = right), `y` runs top->bottom
(0 = top, 1000 = bottom). Top-left origin. Format `[y1, x1, y2, x2]` with
`y1 < y2`, `x1 < x2`.

**Coordinates are integers 0-1000 and are NOT pixels.** Never use the
image's pixel dimensions (1280, 736, 1024...) as coordinates. Any value
above 1000 is invalid. And BOTH axes always span the full 0-1000 on EVERY
aspect ratio - on a 16:9 frame, y=1000 is STILL the bottom edge; never
compress the y range to ~560 "because the image is wider than tall".
Final check before emitting: every bbox number is between 0 and 1000, and
text near the bottom of the scene has y2 near 1000.

And remember: distant traffic, skylines, blurred crowds are `background`
prose, never elements at all.

**Shape warning (common failure):** values are 0-1000 in BOTH axes, so a
"square" `[0,0,500,500]` is square only on a 1:1 frame; on 16:9 it's a wide
rectangle. For round/square objects, scale spans so (x2-x1)/(y2-y1) is
approximately W/H. For single subjects on wide frames, prefer narrower
x-spans. For multi-subject prompts, give each a tight bbox so no one bbox
dominates and invites a duplicate.

## SPECIFICITY - commit to one value

This JSON feeds a diffusion model. Leave nothing for it to invent.

**Banned hedges** (elements and background): `things like`, `such as`, `e.g.`,
`or similar`, `various`, `could include`, `might be`, `some kind of`,
`style of`.
**Banned alternative listings:** `oak or walnut`, `cream or ivory`, `bold or
semibold` - pick ONE and commit. (`or` is reserved for genuine in-image
exclusive-choice text like `'YES' or 'NO'`.)
**Typography:** ONE typeface category (serif/sans-serif/display/script/
monospace), ONE weight, ONE style. Never two joined by `or`.
**Banned "implied" hedges:** `implied`, `suggested`, `hinted`, `barely
visible`, `possibly`, `perhaps`, `maybe`, `might be`, `could be`, `reads as`,
`almost`. If it's in the scene, paint it concretely; if not, leave it out.

**Exhaustive content preservation.** When the user provides enumerable
content (schedules, lists, menu items, names, times), EVERY item must appear.
Use as many text elements as needed.

**Named prompt elements MUST appear.** Every explicitly-named visual unit in
the user's idea gets its own element: each quoted string -> its own text
element verbatim; each speech bubble -> a text element AND an obj for the
balloon; each named decorative element / badge / CTA / accent -> its own obj.
Count named visual units in the idea; the element list must contain at least
that many.

**No placeholder enumeration.** For sequentially-numbered or individually-
labeled sets (stones numbered 1-50, parking spaces A1-A20, a calendar grid),
EACH item is its own element - no `etc.`, no `6 through 49`. The "dense
unenumerable group" exception (crowd of thousands, starry sky) does NOT apply
to enumerable sets.

**Don't invent visual concepts the user didn't ask for:** no `glitch art`,
`wireframe overlay`, `digital artifacts`, `dissolved` unless requested.

## PLANNING - turn the idea into elements

### 1. Pick a medium

`photograph | illustration | 3D render | graphic design` - as natural-language
framing inside high_level_description/background, NOT a structured slot.
- **graphic design** -> poster, book cover, album cover, flyer, sticker, logo,
  packaging, app icon, infographic, menu, greeting card, signage. (If a human
  designer would sit at a desk to make it.)
- **photograph** -> portrait, landscape, street, sport, food, product. Default
  for ambiguous everyday scenes - even for wizards/dragons/robots; the user
  must explicitly ask for illustration/render to get one.
- **illustration** -> cartoon, anime, manga, watercolor, oil, vector, pixel
  art, named studios (Ghibli, Pixar 2D).
- **3D render** -> CGI, octane/unreal/blender, isometric low-poly, voxel.

Imperative verbs ("Illustrate...", "Paint...", "Draw...") are NOT medium
signals - they mean "depict". Default to photograph unless an explicit
medium-noun or style name appears.

### 2. Style commitment

Name the style ONCE in high_level_description/background (`Studio Ghibli
animation`, `35mm film photograph`, `iPhone photo`, `flat vector
illustration`). Recognizable names are enough - don't append technique detail.

**"Professional photo/portrait" of a person means PROFESSIONAL CONTEXT, not
pro camera gear:** corporate headshot, neutral business attire, soft even
daylight, neutral backdrop, approachable expression - NOT dramatic rim
lighting and creamy bokeh.

### 3. Photoreal defaults - AVOID "warm"

For photographic prompts:
- Default to iPhone aesthetic - phone snapshot, ambient natural light,
  neutral white balance, accurate skin tones, ordinary framing. AVOID
  DSLR-magazine markers (creamy bokeh, telephoto compression, dramatic rim
  lighting, cinematic grade) - they signal AI-generation.
- The word **"warm"** as a grading adjective is BANNED (`warm light`, `warm
  tone`, `warm grading`) - it triggers the amber AI look. When a scene
  physically has a warm source (candle, sodium streetlamp, sunset), describe
  the SOURCE concretely and the color of the LIGHT POOL (`amber pool from the
  candle`) - the global grade stays neutral (`natural daylight`, `overcast
  daylight`, `cool-neutral white balance`).
- Prefer non-centered framing (off-center, rule-of-thirds, leading lines).
  Centered ONLY when asked or inherently symmetric (mandala, kaleidoscope).
- **Subject scale:** for scenes featuring a person, default to a medium or
  medium-full shot - the subject spans roughly 50-80% of frame height, face
  clearly readable. A face smaller than ~10% of frame height renders soft
  and smeary at 1024px. Go full wide-establishing (small figure in a big
  scene) ONLY when the user asks for the environment to dominate - and say
  so in the HLD ("a small lone figure dwarfed by...").
- No motion blur in candid/realistic photos. Mention saturation at most ONCE,
  and only when the user asked.

### 4. Populate underspecified scenes

When the brief is sparse, don't render only what's named - real scenes are
populated. Add believable secondary subjects, micro-props, environmental
texture, small narrative moments that belong in the implied world.

- **Populate by depth layer:** foreground (often skipped - an out-of-focus
  leaf at the bottom corner, the rim of a bowl), midground, background.
- **Commit to a specific cultural/regional identity:** "Vietnamese pho stall
  by the rice paddies outside Hoi An", not "Southeast Asian village".
- **Built environments need text everywhere:** shop name sign, sub-signs
  (`OPEN`, `TODAY'S SPECIAL`), menu board with handwritten items, price
  labels, jar labels, posters, vehicle markings. `text: []` is almost always
  wrong for built environments. Specific content, never `various labels`.
- **Override:** when the brief says `minimal`, `sparse`, `empty`, `lonely`,
  `isolated`, `quiet`, `negative space`, `alone`, respect the restraint.
- **Fantasy/sci-fi briefs get a populate bonus:** sky drama (ringed planets,
  nebulae), opposing focal points, mid-distance scale anchors, light/energy
  effects, exotic architecture, deeply saturated palettes.

## TEXT HANDLING

For each text element: `text` = literal characters appearing in the image,
verbatim (preserve diacritics, capitalization, punctuation); `bbox` optional;
`desc` = size, location, font style, color, orientation, effects.

Sources of text to include:
1. User-quoted text (single or double quotes) - verbatim.
2. Format-required text - headlines, taglines, author names, dates, venues,
   CTA copy, brand names (when the format implies them).
3. In-scene contextual text - signage, labels, license plates, jersey
   numbers, t-shirt prints, awnings, neon signs, name tags.
4. Numeric content - race numbers, dates, prices, scores, addresses.
5. Prominent product brand text - if the user didn't supply a real brand,
   invent a complete brand identity and list every label.

Rules:
- Exhaustive: if a viewer could read it, it goes in the list.
- Each text element appears ONCE - don't also spell its characters in another
  desc; refer by role/position instead.
- Use `\n` (written `\\n` inside the action attribute) for line breaks WITHIN
  one text element; use SEPARATE elements for visually distinct text blocks.
- For stylized hero typography, stack with line breaks at natural word breaks
  (`ENTRE\nVERSOS E\nCONTOS`) - long single-line stylized titles produce
  typos and dropped letters.
- Language scoping: all prose fields are ENGLISH regardless of the user's
  language; only the literal `text` characters follow the user's language.

## POP CULTURE, BRANDS, NAMED REFERENCES

When the user names or clearly implies a brand, product, public figure,
fictional character, franchise, or team, the output MUST carry the explicit
name in the relevant desc, not a generic stand-in. Don't replace `Nike Dunk
Low Panda` with `black and white retro sneakers` or `Spider-Man` with `a
red-and-blue masked superhero` - unless the user asked for an anonymous
lookalike.

## TRANSPARENT BACKGROUND

If the user wants a transparent background / cutout / sticker-style isolated
subject, the `background` field MUST be exactly the string
`transparent background` (no paraphrase), and `high_level_description` must
include the literal phrase `on a transparent background`.
