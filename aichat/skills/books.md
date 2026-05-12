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

## The pattern (read this once)

1. **Write the story prose first** in CHAT TEXT - one short paragraph
   per page (2-4 sentences). Names are fine here.
2. **Page 1**: generate the protagonist via `generate_image` with the
   FULL visual sheet (apparent age / species, ethnicity/complexion,
   build, hair/coat, face, wardrobe, expression). That bubble becomes
   the **anchor image**, e.g. Image #N.
3. **Pages 2+**: use `image_to_image` with `chat_image="<anchor N>"` to
   reuse the anchored character in each new scene. Always restate the
   full visual identity in the prompt and add a "keep ... recognizably
   consistent" preservation clause. (See `scenario_storytelling`
   patterns C / D for the full identity-anchor rules.)
4. **Per-page composition**: chain `add_border` + `draw_text` (body) +
   `draw_text` (page number) onto each page's image so the final result
   is ONE bubble per page.

For a page that needs the protagonist plus a second anchored character
or location, use the 2-input preset and `chat_image2` (Pattern E in
`scenario_storytelling`).

## Per-page chained composition

For each page, after the `image_to_image` (or `generate_image` on page 1):

```
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="55%" color="#FBF7EE"/>
<aitools_action skill="draw_text" chain="true" text="<one paragraph of body prose for this page>" x="10%" y="46%" width="80%" height="48%" font_size="46" min_font_size="22" color="#2A1F12" align="left" valign="top" wrap="true"/>
<aitools_action skill="draw_text" chain="true" text="<page number>" x="92%" y="95%" width="6%" height="4%" font_size="32" color="#777777" align="right" valign="middle"/>
```

`bottom="55%"` of source HEIGHT becomes ~35% of the FINAL canvas height
- a wide bottom band for body text. Tune the percentage to your
illustration's aspect.

For a printable feel use `#FBF7EE` (cream paper) or `#FFFFFF` (white).

## Worked example - 3-page picture book about a fox

Story prose in chat (paragraph per page), then the actions:

```
# page 1 - establishing shot, also the character anchor
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="A small russet-orange fox with a fluffy white-tipped tail, large amber eyes, soft black ear tips, a single white chest patch, and a curious tilted-head expression. He stands on a mossy log at the edge of a sunlit pine forest in early autumn, dappled gold light filtering through the trees, low camera angle, storybook illustration style with warm watercolor textures, soft edges, hand-drawn ink linework."/>
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="55%" color="#FBF7EE"/>
<aitools_action skill="draw_text" chain="true" text="Once upon a time, in a pine forest at the edge of the world, there lived a small russet fox named Fen. Fen had always wondered what it would be like to fly." x="10%" y="46%" width="80%" height="48%" font_size="46" min_font_size="22" color="#2A1F12" align="left" valign="top" wrap="true"/>
<aitools_action skill="draw_text" chain="true" text="1" x="92%" y="95%" width="6%" height="4%" font_size="32" color="#777777" align="right" valign="middle"/>

# page 2 - same fox, new scene; chat_image reuses the page-1 fox identity
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="Use the fox from the reference image: a small russet-orange fox with fluffy white-tipped tail, large amber eyes, soft black ear tips, single white chest patch, curious tilted-head expression; keep the fox's coat color, markings, eye color, and proportions recognizably consistent. Place him at the top of a tall pine tree, paws gripping a swaying branch, looking down at the forest floor far below, wind ruffling his fur, late afternoon golden light, slightly nervous expression. Same warm watercolor storybook style, hand-drawn ink linework." chat_image="<page1 idx>"/>
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="55%" color="#FBF7EE"/>
<aitools_action skill="draw_text" chain="true" text="One blustery morning, Fen climbed to the very top of the tallest pine. The wind tugged at his fur and the world looked very small below." x="10%" y="46%" width="80%" height="48%" font_size="46" min_font_size="22" color="#2A1F12" align="left" valign="top" wrap="true"/>
<aitools_action skill="draw_text" chain="true" text="2" x="92%" y="95%" width="6%" height="4%" font_size="32" color="#777777" align="right" valign="middle"/>

# page 3 - same fox, mid-flight
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit.txt}}" prompt="Use the fox from the reference image: a small russet-orange fox with fluffy white-tipped tail, large amber eyes, soft black ear tips, single white chest patch; keep the fox's coat color, markings, eye color, and proportions recognizably consistent. Show him airborne in a graceful arc, all four paws spread wide, tail streaming, wide delighted eyes and an open-mouthed grin, sailing past pine boughs with autumn leaves swirling around him, low golden sun behind him casting a warm rim light. Same warm watercolor storybook style, hand-drawn ink linework." chat_image="<page1 idx>"/>
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="55%" color="#FBF7EE"/>
<aitools_action skill="draw_text" chain="true" text="And just like that - leaves spinning, wind in his ears - Fen was flying. Or falling. He decided it didn't matter which." x="10%" y="46%" width="80%" height="48%" font_size="46" min_font_size="22" color="#2A1F12" align="left" valign="top" wrap="true"/>
<aitools_action skill="draw_text" chain="true" text="3" x="92%" y="95%" width="6%" height="4%" font_size="32" color="#777777" align="right" valign="middle"/>
```

Final state: 3 page bubbles, fox recognizably the same on all three.

## Variations

- **User pasted a character anchor** (e.g. their dog photo) - use that
  as the anchor on every page (`chat_image="<their image #>"`), no
  initial `generate_image`. Page 1 also uses `image_to_image`.
- **Two recurring characters** - use the 2-input preset
  (`{{Image To Image Klein Edit 2 Input.txt}}`) with
  `chat_image="<charA>"` and `chat_image2="<charB>"`. See
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
<aitools_action skill="draw_text" chain="true" text="Once upon a time, in a small village at the edge of the forest..." x="10%" y="46%" width="80%" height="48%" font_size="46" color="#2A1F12" align="left" valign="top" wrap="true"/>
<aitools_action skill="draw_text" chain="true" text="3" x="92%" y="95%" width="6%" height="4%" font_size="32" color="#777777" align="right" valign="middle"/>
```

## Rules

- Story prose is in CHAT TEXT, not in image prompts. Image prompts must
  be self-contained visual descriptions.
- Page 1 establishes the character anchor; pages 2+ reuse it via
  `chat_image="<anchor>"`.
- Every per-page sequence is fully chained -> ONE bubble per page.
- Each `image_to_image` prompt restates the character visually plus a
  "keep ... recognizably consistent" clause.
- Don't pass a character's name as the only subject of an image prompt.
  The model has no memory of chat.
