---
id: books
summary: Recipe for multi-page illustrated books with recurring characters. Combines story prose, per-page illustrations (image_to_image with chat_image anchor for character consistency), and per-page composition (border + body text + page number). One bubble per page. Auto-loaded for book-class requests.
inputs: none
autoload: true
triggers: book, books, picture book, picture books, childrens book, children's book, kids book, kid's book, storybook, story book, story-book, fairy tale, fairy tales, fairytale, page of a book, book page, book pages, multi-page, multipage, several pages, illustrated story
template: <aitools_action skill="read_skill" id="books"/>
---
# Books - multi-page illustrated stories

Use this when the user wants a concrete page-formatted deliverable: a
children's book, a storybook, a fairy tale broken into pages, an
illustrated short story. ONE final bubble per page, with the same
character(s) recognizable across pages.

(For free-form story turns without page formatting, use
`scenario_storytelling` instead. For a single one-off page, see the short
form at the end of this file.)

## Decide FIRST: is there a recurring character?

Before writing any actions, decide whether the SAME character (or object)
appears on more than one page:

- **Recurring character → you MUST use an anchor.** Generate ONE standalone
  `generate_image` portrait tagged `anchor="Name"`, then make EVERY page with
  `image_to_image chat_image="Name"` (reference the anchor by that SAME name).
  This is the ONLY way the character stays visually consistent across pages.
  Emitting an independent `generate_image` per page draws a DIFFERENT-looking
  character each time - that is wrong, even if each prompt re-describes them.
  **Emit the anchor AND all the pages in ONE reply** - the app automatically
  waits for the anchor to finish rendering before each page's `image_to_image`
  reads it, so `chat_image="Name"` works in the same reply. Do NOT stop after
  the anchor to "wait for a later turn" - that is no longer necessary.
- **No recurring character** (each page is a different subject/scene - e.g.
  "how everybody poops" with a different animal per page) → independent
  `generate_image` per page is fine; no anchor needed.

This applies on a REDO too. If the user says "redo it, but make it the same
dragon on every page" / "...with X as the main character on every page", the
story now HAS a recurring character - switch to the anchor workflow even if
your previous attempt used independent generates. Do NOT just swap the new
subject into N independent `generate_image` calls. ("main focus / main
character / same character every page" = recurring character = anchor.)

## The pattern (read this once)

1. **Write the story prose first** in CHAT TEXT - one short paragraph
   per page (2-4 sentences). Names are fine here.
2. **Generate the character anchor as a SEPARATE bubble** via
   `generate_image`, tagged `anchor="Name"` (NO `chain="true"`, no
   `add_border`, no `draw_text`). This bubble is the **raw character
   portrait** with the FULL visual sheet (apparent age / species,
   ethnicity/complexion, build, hair/coat, face, wardrobe, expression). It
   will NOT have a border or text - it stays pristine so it can be re-used as
   the input anchor for every page. **DO NOT** chain border/text onto it.
3. **Each page (including page 1)**: emit `image_to_image` with
   `chat_image="Name"` (the raw anchor, by name), then chain `add_border` +
   `draw_text` (body) + `draw_text` (page number). The chain mutates THIS
   page's Pic only; the anchor is untouched. Result: one finished page bubble
   per page, all referencing the SAME pristine anchor. Emit the anchor AND
   every page in ONE reply - the app waits for the anchor to render before
   each page reads it, so no follow-up turn is needed.
4. Always restate the full visual identity in each page's prompt and add
   a "keep ... recognizably consistent" preservation clause. (See
   `scenario_storytelling` patterns C / D for the full identity-anchor
   rules.)
5. Do not merely say the text was added - emit the `draw_text` actions.

**Why a separate anchor bubble?** `chain="true"` mutates the previous
step's texture in place. If page 1 is `generate_image` followed by
chained border/text, page 1's Pic has been overwritten with a
portrait-aspect bordered + texted image. Reusing that as
`chat_image="1"` for page 2 feeds the bordered page back into Klein,
which preserves the input aspect, and the next `add_border` adds
ANOTHER 60%-of-height band on top. The page gets narrower every
iteration. A standalone anchor bubble (no chain) avoids this entirely.

For a page that needs the protagonist plus a second anchored character
or location, use the 2-input preset and `chat_image2` (Pattern E in
`scenario_storytelling`). The second anchor must ALSO be a separate
no-chain `generate_image` bubble, for the same reason.

**Re-running the storybook in the same conversation** (e.g. user says
"do it again with a different style"): generate a FRESH anchor for the
new style. Do NOT reuse the previous run's anchor or any of its
finished pages as `chat_image=` - they belong to a different visual
direction, and finished pages have border/text baked in.

## Per-page chained composition

For each page, after the `image_to_image` (or `generate_image` on page 1):

```
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="60%" color="#FBF7EE"/>
<aitools_action skill="draw_text" chain="true" text="<one short paragraph of body prose for this page>" x="10%" y="68%" width="80%" height="28%" font_size="10%" color="#2A1F12" align="left" valign="top" wrap="true"/>
<aitools_action skill="draw_text" chain="true" text="<page number>" x="92%" y="95%" width="6%" height="4%" font_size="8%" color="#777777" align="right" valign="middle"/>
```

`bottom="60%"` of source HEIGHT becomes a wide bottom band for body text.
The body text must start inside that band, around `y="68%"`; do not use
`y="46%"`, which lands on top of the illustration on a normal page.

`font_size` is the text's pixel height, or (with a `%`) a percent of canvas
height - here `font_size="10%"` caps a body line at ~10% of canvas height.
It is only the UPPER CAP: draw_text auto-shrinks the text so the whole
paragraph fits the `height="28%"` band, no matter how long it is. The box
always wins, so the text never overflows the band, runs past the page, or
overlaps the page number - you do NOT need `min_font_size` (it's a soft hint
now; the box wins). Keep the cap a little above one line's worth (≈8-12% for
body); too small a cap just makes short pages render smaller than they could.

For a printable feel use `#FBF7EE` (cream paper) or `#FFFFFF` (white).

## Worked example - 3-page picture book about a fox

Story prose in chat (paragraph per page), then ALL the actions in ONE reply.
Note the SEPARATE anchor portrait at the top, tagged `anchor="Fen"`, with NO
border/text chained to it - pages 1, 2, 3 all reference `chat_image="Fen"`,
the pristine raw character. (The app waits for the anchor to render before
page 1 reads it, so this whole block is a single reply.)

```
# anchor portrait - generate_image with NO chain. Becomes Image #A.
# Never chain border or text onto this bubble - it stays pristine so
# every page can use it as the input reference.
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="A small russet-orange fox with a fluffy white-tipped tail, large amber eyes, soft black ear tips, a single white chest patch, and a curious tilted-head expression. He sits in three-quarter view in a soft neutral pine-grove background, dappled gold light filtering through the trees, storybook illustration style with warm watercolor textures, soft edges, hand-drawn ink linework. Full-body character reference portrait." anchor="Fen"/>

# page 1 - image_to_image from the anchor, then chain border + text.
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="Use the fox from the reference image: a small russet-orange fox with fluffy white-tipped tail, large amber eyes, soft black ear tips, single white chest patch, curious tilted-head expression; keep the fox's coat color, markings, eye color, and proportions recognizably consistent. He stands on a mossy log at the edge of a sunlit pine forest in early autumn, dappled gold light filtering through the trees, low camera angle, warm watercolor storybook style, hand-drawn ink linework." chat_image="Fen"/>
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="60%" color="#FBF7EE"/>
<aitools_action skill="draw_text" chain="true" text="Once upon a time, in a pine forest at the edge of the world, there lived a small russet fox named Fen. Fen had always wondered what it would be like to fly." x="10%" y="68%" width="80%" height="28%" font_size="10%" color="#2A1F12" align="left" valign="top" wrap="true"/>
<aitools_action skill="draw_text" chain="true" text="1" x="92%" y="95%" width="6%" height="4%" font_size="8%" color="#777777" align="right" valign="middle"/>

# page 2 - same anchor, new scene
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="Use the fox from the reference image: a small russet-orange fox with fluffy white-tipped tail, large amber eyes, soft black ear tips, single white chest patch, curious tilted-head expression; keep the fox's coat color, markings, eye color, and proportions recognizably consistent. Place him at the top of a tall pine tree, paws gripping a swaying branch, looking down at the forest floor far below, wind ruffling his fur, late afternoon golden light, slightly nervous expression. Same warm watercolor storybook style, hand-drawn ink linework." chat_image="Fen"/>
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="60%" color="#FBF7EE"/>
<aitools_action skill="draw_text" chain="true" text="One blustery morning, Fen climbed to the very top of the tallest pine. The wind tugged at his fur and the world looked very small below." x="10%" y="68%" width="80%" height="28%" font_size="10%" color="#2A1F12" align="left" valign="top" wrap="true"/>
<aitools_action skill="draw_text" chain="true" text="2" x="92%" y="95%" width="6%" height="4%" font_size="8%" color="#777777" align="right" valign="middle"/>

# page 3 - same anchor, mid-flight
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="Use the fox from the reference image: a small russet-orange fox with fluffy white-tipped tail, large amber eyes, soft black ear tips, single white chest patch; keep the fox's coat color, markings, eye color, and proportions recognizably consistent. Show him airborne in a graceful arc, all four paws spread wide, tail streaming, wide delighted eyes and an open-mouthed grin, sailing past pine boughs with autumn leaves swirling around him, low golden sun behind him casting a warm rim light. Same warm watercolor storybook style, hand-drawn ink linework." chat_image="Fen"/>
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="60%" color="#FBF7EE"/>
<aitools_action skill="draw_text" chain="true" text="And just like that - leaves spinning, wind in his ears - Fen was flying. Or falling. He decided it didn't matter which." x="10%" y="68%" width="80%" height="28%" font_size="10%" color="#2A1F12" align="left" valign="top" wrap="true"/>
<aitools_action skill="draw_text" chain="true" text="3" x="92%" y="95%" width="6%" height="4%" font_size="8%" color="#777777" align="right" valign="middle"/>
```

Final state: 1 raw anchor bubble + 3 page bubbles. Every page is
generated from the SAME pristine anchor, so aspects match and the fox
is recognizably the same on all three. The anchor's aspect (square Z-
Image 1024x1024) sets the aspect for the whole book.

## Variations

- **User pasted a character anchor** (e.g. their dog photo) - use that
  as the anchor on every page (`chat_image="<their image #>"`), no
  initial `generate_image`. Page 1 also uses `image_to_image`.
- **Two recurring characters** - use the 2-input preset
  (`{{Image To Image Klein Edit 2 Input.txt}}`) with
  `chat_image="<charA>"` and `chat_image2="<charB>"`. The result IS the
  finished page illustration with BOTH characters already in the scene -
  chain `add_border` + `draw_text` straight onto it, exactly like the
  1-character pattern. Do NOT `new_canvas` + `paste_image` it (that detour is
  for multi-panel comics/layouts, never a single-illustration page). See
  `scenario_storytelling` Pattern E.
- **Cover page** - first page can be a portrait-aspect generate with
  title text instead of body text. Treat it like a poster (see the
  `posters` skill).
- **No illustrations, just text pages** - start each page with
  `new_canvas` (cream/white) instead of `image_to_image`, then chain
  text. Faster (no GPU) but flatter visually.

## Single one-off page

If the user asks for just one book page (no recurring character needed),
this short form is enough:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<illustration scene description>"/>
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="60%" color="#FBF7EE"/>
<aitools_action skill="draw_text" chain="true" text="Once upon a time, in a small village at the edge of the forest..." x="10%" y="68%" width="80%" height="28%" font_size="10%" color="#2A1F12" align="left" valign="top" wrap="true"/>
<aitools_action skill="draw_text" chain="true" text="3" x="92%" y="95%" width="6%" height="4%" font_size="8%" color="#777777" align="right" valign="middle"/>
```

## Rules

- Story prose is in CHAT TEXT, not in image prompts. Image prompts must
  be self-contained visual descriptions.
- Story prose must also be rendered on the final page images with
  `draw_text`; chat text alone is not a finished book page.
- The character anchor is its OWN bubble - a `generate_image` tagged
  `anchor="Name"` with NO `chain="true"` actions stacked on top. Pages 1..N
  each `image_to_image` from `chat_image="Name"` (by name), all in ONE reply.
  NEVER reuse a finished page as the anchor for the next page; the finished
  page has border + text baked in, which corrupts both identity AND aspect.
- Every per-page sequence (image_to_image + add_border + draw_text +
  draw_text) is fully chained -> ONE finished bubble per page.
- A page WITH an illustration is built by chaining border/text directly onto
  the `image_to_image` (or `generate_image`) result - that result already IS
  the page. NEVER `new_canvas` then `paste_image` a single illustration onto a
  blank page; `new_canvas` is only for the text-only-pages variation above.
- Chained steps (`add_border`, `draw_text`, `paste_image`) take their canvas
  from `chain="true"` ALONE - do NOT also put `chat_image=` on a chained step
  (its source_chat_image, for paste_image only, is a separate thing).
- Each `image_to_image` prompt restates the character visually plus a
  "keep ... recognizably consistent" clause.
- Don't pass a character's name as the only subject of an image prompt.
  The model has no memory of chat.
- On a re-run ("do it again, but X") generate a NEW anchor for the new
  style. Don't recycle the previous run's anchor or finished pages.
