---
id: comics
summary: Recipes for comic panels, speech bubbles, and multi-panel comic strips/grids. Auto-loaded for comic-class requests.
inputs: none
autoload: true
triggers: comic, comics, comic panel, comic panels, comic strip, comic strips, comic book, comic page, manga panel, manga page, speech bubble, speech bubbles, thought bubble, dialogue bubble
template: <aitools_action skill="read_skill" id="comics"/>
---
# Comics

Recipes for single comic panels with speech bubbles, horizontal multi-
panel strips, and 2x2 comic pages.

## Mental model

- One bubble per visible deliverable (one panel = one bubble; one strip
  = one bubble).
- Speech bubbles are `draw_shape` (rounded rect background) + `draw_text`
  (dialog) chained on top, both at the SAME rect coordinates.
- Multi-panel layouts generate the panel art first, then create a
  `new_canvas` immediately before final assembly. Every `paste_image`,
  `draw_shape`, and `draw_text` in that assembly uses `chain="true"` so
  the same canvas becomes the finished comic instead of separate partial
  canvases.
- For recurring characters across panels, use `image_to_image` with
  `chat_image="<panelA_idx>"` for panels B+ instead of fresh
  `generate_image` calls.
- For panels created earlier in the SAME reply, name the canvas/panels with
  `anchor="..."` and use those names in `chat_image` / `source_chat_image`.
  Do not predict #K+N future chat_image numbers.

---

## Recipe: Single panel with a speech bubble

User says: "add a speech bubble that says X", "make this a comic panel".

Steps (one bubble):
1. (Start with an existing `chat_image` OR a new `generate_image`.)
2. `draw_shape` - rounded-rect bubble background.
3. `draw_text` chained - dialog inside the bubble.

```
<aitools_action skill="draw_shape" chat_image="3" shape="rect" x="55%" y="5%" width="42%" height="22%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="4" corner_radius="48"/>
<aitools_action skill="draw_text" chain="true" text="Get to the chopper!" x="55%" y="5%" width="42%" height="22%" font_size="56" color="#000000" bold="true" align="center" valign="middle"/>
```

For thought bubbles, draw two stacked rounded rects of decreasing size
for a "cloud trail" feel. (No rotated-triangle primitive yet for the
bubble's tail - workaround: a small filled circle near the speaker.)

---

## Recipe: Horizontal 2-panel comic strip

User says: "2-panel comic", "two panel comic strip about X".

Steps:
1. `generate_image` x2 - the two panel scenes (separate source bubbles).
   For character continuity, generate panel A first, then make panel B
   via `image_to_image chat_image="comic_panel_a"` when the subject must
   stay identical.
2. `new_canvas` - wide canvas, created immediately before assembly.
3. `paste_image chain="true"` x2 - each panel into half of the canvas.
4. `draw_shape` + `draw_text` per panel - speech bubbles, all chained.

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel A - full self-contained Z-Image prompt, not just the topic>" anchor="comic_panel_a"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel B - full self-contained Z-Image prompt with the punchline/action>" anchor="comic_panel_b"/>
<aitools_action skill="new_canvas" width="2400" height="1200" color="#FFFFFF" anchor="comic_canvas"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_panel_a" x="2%" y="4%" width="46%" height="78%" mode="fill"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_panel_b" x="52%" y="4%" width="46%" height="78%" mode="fill"/>
<aitools_action skill="draw_shape" chain="true" shape="rect" x="5%" y="7%" width="34%" height="13%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="4" corner_radius="28"/>
<aitools_action skill="draw_text" chain="true" text="I typed it exactly!" x="5%" y="7%" width="34%" height="13%" font_size="46" color="#000000" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_shape" chain="true" shape="rect" x="56%" y="7%" width="38%" height="13%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="4" corner_radius="28"/>
<aitools_action skill="draw_text" chain="true" text="LOAD &quot;*&quot;,8,1" x="56%" y="7%" width="38%" height="13%" font_size="46" color="#000000" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="COMMODORE 64" x="0" y="86%" width="100%" height="10%" font_size="52" color="#111111" bold="true" align="center" valign="middle"/>
```

The canvas is the most recent Pic because it is created right before the
paste calls, so `chain="true"` stacks the whole assembly onto it.

---

## Recipe: Horizontal 3-panel comic strip

User says: "3-panel comic strip about my cat", "comic strip of X".

Steps:
1. `generate_image` x3 - the three panel scenes (separate bubbles). For
   character continuity, generate panel A first, then make panels B/C
   via `image_to_image chat_image="<panelA_idx>"`.
2. `new_canvas` - wide canvas (e.g. 3000x1100) with a paper or dark
   background, created immediately before final assembly.
3. `paste_image chain="true"` x3 - each panel into a third of the canvas.
4. `draw_shape` + `draw_text` per panel - speech bubbles, all chained.

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel A - establishing>" anchor="comic_panel_a"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel B - twist>" anchor="comic_panel_b"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel C - punchline>" anchor="comic_panel_c"/>
<aitools_action skill="new_canvas" width="3000" height="1100" color="#FFFFFF" anchor="comic_canvas"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_panel_a" x="1%" y="3%" width="32%" height="80%" mode="fill"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_panel_b" x="34%" y="3%" width="32%" height="80%" mode="fill"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_panel_c" x="67%" y="3%" width="32%" height="80%" mode="fill"/>
<aitools_action skill="draw_shape" chain="true" shape="rect" x="3%" y="6%" width="22%" height="14%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="3" corner_radius="24"/>
<aitools_action skill="draw_text" chain="true" text="Where's my food?" x="3%" y="6%" width="22%" height="14%" font_size="46" color="#000000" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_shape" chain="true" shape="rect" x="36%" y="6%" width="24%" height="14%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="3" corner_radius="24"/>
<aitools_action skill="draw_text" chain="true" text="It's right there." x="36%" y="6%" width="24%" height="14%" font_size="46" color="#000000" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_shape" chain="true" shape="rect" x="69%" y="6%" width="28%" height="14%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="3" corner_radius="24"/>
<aitools_action skill="draw_text" chain="true" text="Bring it closer." x="69%" y="6%" width="28%" height="14%" font_size="46" color="#000000" bold="true" align="center" valign="middle"/>
```

The anchor names are resolved by the host to the actual current chat_image
numbers. Do not calculate future numeric ids.

---

## Recipe: 2x2 comic page

Same idea as the storyboard layout (see the `layouts` skill) but with a
speech bubble per panel.

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel 1>" anchor="comic_page_1"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel 2>" anchor="comic_page_2"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel 3>" anchor="comic_page_3"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel 4>" anchor="comic_page_4"/>
<aitools_action skill="new_canvas" width="2048" height="1152" color="#FFFFFF" anchor="comic_page_canvas"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_page_1" x="2%" y="3%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_page_2" x="51%" y="3%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_page_3" x="2%" y="50%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_page_4" x="51%" y="50%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="draw_shape" chain="true" shape="rect" x="4%" y="6%" width="22%" height="10%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="3" corner_radius="20"/>
<aitools_action skill="draw_text" chain="true" text="Run!" x="4%" y="6%" width="22%" height="10%" font_size="44" color="#000000" bold="true" align="center" valign="middle"/>
```

Repeat the shape+text pair (chained) for each of the four panels at the
appropriate quadrant coordinates.

## Rules

- Speech bubbles always pair `draw_shape` (background rect) with a
  chained `draw_text` (dialog) at the SAME rect coordinates.
- For multi-panel layouts, generate source panels first, then create the
  canvas immediately before final assembly. Use `paste_image chain="true"`
  with `source_chat_image` for each panel, then keep chaining bubble/text
  operations onto that same canvas.
- Do not emit several unchained `paste_image chat_image="comic_canvas"`
  calls in a row. That creates separate partial canvases and often leaves
  black/blank boxes instead of a finished comic.
- Use anchor names for any same-reply generated panel/canvas, especially when
  pasting several new panels into a layout.
- For recurring characters across panels, use `image_to_image
  chat_image="<panelA_idx>"` for panels B+ instead of fresh
  `generate_image` calls.
