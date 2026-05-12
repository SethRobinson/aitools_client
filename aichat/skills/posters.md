---
id: posters
summary: Recipes for single-page posters built by chaining (motivational poster, magazine cover, quote card, info card, meme). Each is one bubble - generate_image -> add_border -> draw_text. Auto-loaded for poster-class requests.
inputs: none
autoload: true
triggers: poster, posters, motivational poster, motivational posters, meme, memes, funny text, magazine cover, quote card, quote cards, info card, info cards, tweet card, infographic, infographics, captioned image
template: <aitools_action skill="read_skill" id="posters"/>
---
# Posters and single-page layouts

Single-bubble deliverables: motivational posters, magazine covers, quote
cards, info cards, memes. Each is built by chaining `generate_image` (or
starting from an existing chat image) with `add_border`, `draw_shape`,
`draw_text`. Result: ONE final bubble per poster.

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

---

## Recipe: Motivational poster (matches the AIGuide preset look)

User says: "make a motivational poster about dogs", "Star Wars
motivational posters".

Steps per poster:
1. `generate_image` - the illustration
2. `add_border` - thin sides + top, large bottom band for readable text
3. `draw_text` - title (top of bottom band, bold, big)
4. `draw_text` - body sentence (below the title, smaller, wrapped)

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full Z-Image scene description>"/>
<aitools_action skill="add_border" chain="true" left="6%" right="6%" top="6%" bottom="25%" color="#FFFFFF"/>
<aitools_action skill="draw_text" chain="true" text="PERSEVERANCE" x="10%" y="82%" width="80%" height="8%" font_size="900" min_font_size="160" color="#000000" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="The only way through is through." x="10%" y="91%" width="80%" height="7%" font_size="500" min_font_size="90" color="#222222" align="center" valign="middle"/>
```

Keep `font_size` caps high (600-1000 for titles, 300-500 for body) and
set `min_font_size` floors (80-200). Tiny caps make unreadable posters.

For five posters in one reply, repeat the 4-step pattern five times.
Each `generate_image` starts a fresh non-chained Pic; each chained step
stacks onto its own root.

For a printable feel, swap the white border for `color="#F5F2E8"` (cream
paper).

### Existing-image variant

User says: "turn that image into a poster", "add poster text to the
picture you just made".

Use the exact existing number from CHAT IMAGES on the first tag, then
chain. Do not add 1 to the listed image number.

```
<aitools_action skill="add_border" chat_image="<existing_idx>" left="6%" right="6%" top="6%" bottom="25%" color="#FFFFFF"/>
<aitools_action skill="draw_text" chain="true" text="PERSEVERANCE" x="10%" y="82%" width="80%" height="8%" font_size="900" min_font_size="160" color="#000000" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="The only way through is through." x="10%" y="91%" width="80%" height="7%" font_size="500" min_font_size="90" color="#222222" align="center" valign="middle"/>
```

---

## Recipe: Magazine cover

User says: "magazine cover for my band", "Vogue cover for my dog".

Steps:
1. `generate_image` - cover photo (portrait aspect)
2. `draw_shape` - semi-transparent panel for the masthead area so the
   title is legible over the photo
3. `draw_text` - magazine title (huge, top of cover)
4. `draw_text` - issue date / catchline (small, near the title)
5. `draw_text` - 2-3 cover lines lower down

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full portrait subject description, magazine cover style>"/>
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
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<full scene description>"/>
<aitools_action skill="draw_text" chain="true" text="WHEN YOU SAID YOU'D" x="0" y="3%" width="100%" height="14%" font_size="600" min_font_size="120" color="#FFFFFF" outline_color="#000000" outline_width="6" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="BE READY IN FIVE MINUTES" x="0" y="83%" width="100%" height="14%" font_size="600" min_font_size="120" color="#FFFFFF" outline_color="#000000" outline_width="6" bold="true" align="center" valign="middle"/>
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
