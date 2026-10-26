---
id: posters
summary: Recipes for single-page posters (motivational poster, magazine cover, quote card, info card, meme). New posters and covers are ONE generate_image with the MOSS-Image preset rendering the text itself (every string quoted exactly in the prompt); draw_text overlay chains are for existing images, memes, pure-typography cards, and non-MOSS presets. Auto-loaded for poster-class requests.
inputs: none
autoload: true
triggers: poster, posters, motivational poster, motivational posters, meme, memes, funny text, magazine cover, quote card, quote cards, info card, info cards, tweet card, infographic, infographics, captioned image
template: <aitools_action skill="read_skill" id="posters"/>
---
# Posters and single-page layouts

Single-bubble deliverables: motivational posters, magazine covers, quote
cards, info cards, memes. A NEW poster or cover is one `generate_image` with
`{{Prompt To Image (MOSS-Image).txt}}`: MOSS-Image renders exact quoted text
(titles, body lines, mastheads, small print) cleanly, so the layout and text
go straight into the prompt. Overlay chains (`add_border`, `draw_shape`,
`draw_text`) are for adding text to an EXISTING image, memes, pure-typography
cards, and requests that name a different image model. Result: ONE final
bubble per poster.

## Mental model

1. **One bubble per visible deliverable.** Five posters = five final
   bubbles, not one with five inside.
2. **`chain="true"` collapses many steps into ONE final bubble.** Use it
   for "build up a single composition" - intermediates aren't separately
   visible.
3. **Coordinates are top-left, percent or pixels.** Prefer percent so
   layouts survive different canvas sizes.
4. **Re-describe characters in every `generate_image` prompt.** Image
   models have no memory of chat.
5. **Text in a MOSS-Image prompt is quoted exactly** with its position,
   size, weight, colour and case (see `generate_image` "Writing good image
   prompts"). Inside the action tag write the quotes as `&quot;`. Keep each
   string short (a title plus a sentence or two); for a long paragraph of
   body text use the overlay variant.

---

## Recipe: Motivational poster (matches the AIGuide preset look)

User says: "make a motivational poster about dogs", "Star Wars
motivational posters".

One `generate_image` per poster. The prompt describes the whole poster:
the framed illustration, the border, and every line of text with its exact
wording and placement.

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (MOSS-Image).txt}}" width="832" height="1248" prompt="A vertical classic motivational poster on a solid black background. A thin white rule frames a photograph that fills the upper three quarters: a muddy golden retriever in its prime climbing a steep rocky trail at dawn, ears back, eyes fixed on the ridge above, mist in the valley behind it. Centred in the black band below the photograph, a large white serif title in widely spaced capitals reads &quot;PERSEVERANCE&quot;. Directly beneath it, a smaller white serif line reads &quot;The only way through is through.&quot; The lighting in the photograph is low warm sunrise light from the upper right with long cool shadows across the rocks. The overall composition is a clean, symmetrical 1990s office poster with a black, white and honey-gold palette."/>
```

For five posters in one reply, emit five of these tags, each a complete
self-contained prompt. For a printable feel, describe a cream paper border
instead of the black band.

### Overlay variant (draw_text)

Use it when the user wants the text set as a separate clean layer, names a
different image model (Qwen, Krea, Z-Image), or the body text is long:
`generate_image` the illustration ONLY (no text in the prompt), then chain
`add_border` and two `draw_text` steps.

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (MOSS-Image).txt}}" width="832" height="1248" prompt="<full MOSS-Image scene description - the ILLUSTRATION ONLY, no text>"/>
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="25%" color="#FFFFFF"/>
<aitools_action skill="draw_text" chain="true" text="PERSEVERANCE" x="10%" y="82%" width="80%" height="8%" font_size="12%" color="#000000" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="The only way through is through." x="10%" y="91%" width="80%" height="7%" font_size="9%" color="#222222" align="center" valign="middle"/>
```

`font_size` is the text's pixel height (or `%` of canvas height) - it is just
the UPPER CAP. The box always wins: draw_text auto-shrinks the text to fit the
rect, so a long line never overflows the band, overlaps the next line, or clips
at the edge. The band `height=` is what actually sets the size, so just pick a
cap a little above each box's own `height=` (here the title and body bands).
You do NOT need `min_font_size` - it's a soft hint now (the box wins regardless).
Want a bigger title? Give it a taller band (bigger `height=`), not a bigger cap.

Each `generate_image` starts a fresh non-chained Pic; each chained step
stacks onto its own root.

### Existing-image variant

User says: "turn that image into a poster", "add poster text to the
picture you just made".

Use the exact existing number from CHAT IMAGES on the first tag, then
chain. Do not add 1 to the listed image number.

```
<aitools_action skill="add_border" chat_image="<existing_idx>" left="6%" right="6%" top="6%" bottom="25%" color="#FFFFFF"/>
<aitools_action skill="draw_text" chain="true" text="PERSEVERANCE" x="10%" y="82%" width="80%" height="8%" font_size="12%" color="#000000" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="The only way through is through." x="10%" y="91%" width="80%" height="7%" font_size="9%" color="#222222" align="center" valign="middle"/>
```

---

## Recipe: Magazine cover

User says: "magazine cover for my band", "Vogue cover for my dog".

One `generate_image` with MOSS-Image: describe the cover photo, then the
masthead, issue line and two or three cover lines, each quoted exactly with
its position, size and colour (e.g. `a huge white condensed masthead across
the top reads &quot;HOWL&quot;, partly tucked behind the dog's ears`). MOSS
layers type and subject like a real cover.

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (MOSS-Image).txt}}" width="832" height="1248" prompt="<full magazine cover: photo subject and setting, then the masthead, issue line and cover lines quoted exactly with placement and styling>"/>
```

Overlay variant (cover text as a separate layer, or an existing photo):

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (MOSS-Image).txt}}" width="832" height="1248" prompt="<full portrait subject description, magazine cover style, no text>"/>
<aitools_action skill="draw_shape" chain="true" shape="rect" x="0" y="0" width="100%" height="22%" fill_color="#00000080"/>
<aitools_action skill="draw_text" chain="true" text="HOWL" x="0" y="2%" width="100%" height="16%" font_size="220" color="#FFFFFF" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="WINTER 2026  -  NO. 47" x="0" y="18%" width="100%" height="3%" font_size="28" color="#EAEAEA" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="The dogs of Brooklyn   |   bath-time confessions   |   training your retriever in 14 days" x="6%" y="92%" width="88%" height="5%" font_size="32" color="#FFFFFF" outline_color="#000000" outline_width="3" align="center" valign="middle"/>
```

---

## Recipe: Info / quote card

User says: "make a quote card", "info card about X", "tweet card".

Pure-typography, no diffusion needed - perfect for `new_canvas` +
`draw_shape` + `draw_text` chains.

```
<aitools_action skill="new_canvas" width="1080" height="1080" color="#0F1115"/>
<aitools_action skill="draw_shape" chain="true" shape="rect" x="6%" y="20%" width="88%" height="60%" fill_color="#1B1F26" outline_color="#3A4250" outline_width="2" corner_radius="24"/>
<aitools_action skill="draw_text" chain="true" text="\"The expert in anything was once a beginner.\"" x="10%" y="30%" width="80%" height="35%" font_size="56" color="#FFFFFF" italic="true" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="- Helen Hayes" x="10%" y="62%" width="80%" height="8%" font_size="36" color="#A8B1C2" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="@yourname" x="0" y="93%" width="100%" height="4%" font_size="26" color="#566073" align="center" valign="middle"/>
```

---

## Recipe: Meme (image + caption)

User says: "make a meme about X", "add a funny caption".

Top-and-bottom Impact-style caption over an image.

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (MOSS-Image).txt}}" prompt="<full scene description>"/>
<aitools_action skill="draw_text" chain="true" text="WHEN YOU SAID YOU'D" x="0" y="3%" width="100%" height="14%" font_size="14%" color="#FFFFFF" outline_color="#000000" outline_width="6" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="BE READY IN FIVE MINUTES" x="0" y="83%" width="100%" height="14%" font_size="14%" color="#FFFFFF" outline_color="#000000" outline_width="6" bold="true" align="center" valign="middle"/>
```

For an existing image, drop the `generate_image` and use
`chat_image="N"` on the first `draw_text`, then chain the second.

---

## When to break the recipes

- If the user is descriptive ("vintage 1950s magazine cover with a Life-
  magazine block masthead"), follow their cues - colors, fonts,
  proportions.
- For many variants ("5 different covers"), repeat the recipe 5 times
  rather than asking which one to do.
- For an animated poster, build the still poster with a chained
  sequence first, then `image_to_movie chain="true"` at the end.
- If a recipe doesn't quite fit, skip it and just compose from
  primitives.
