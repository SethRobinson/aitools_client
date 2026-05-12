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
- Multi-panel layouts use `new_canvas` + `paste_image` for the cells,
  then `draw_shape` + `draw_text` per bubble.
- For recurring characters across panels, use `image_to_image` with
  `chat_image="<panelA_idx>"` for panels B+ instead of fresh
  `generate_image` calls.

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

## Recipe: Horizontal 3-panel comic strip

User says: "3-panel comic strip about my cat", "comic strip of X".

Steps:
1. `new_canvas` - wide canvas (e.g. 3000x1100) with a paper or dark
   background.
2. `generate_image` x3 - the three panel scenes (separate bubbles). For
   character continuity, generate panel A first, then make panels B/C
   via `image_to_image chat_image="<panelA_idx>"`.
3. `paste_image` x3 - each panel into a third of the canvas.
4. `draw_shape` + `draw_text` per panel - speech bubbles.

```
<aitools_action skill="new_canvas" width="3000" height="1100" color="#FFFFFF"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel A - establishing>"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel B - twist>"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel C - punchline>"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panelA_idx>" x="1%" y="3%" width="32%" height="80%" mode="fill"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panelB_idx>" x="34%" y="3%" width="32%" height="80%" mode="fill"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panelC_idx>" x="67%" y="3%" width="32%" height="80%" mode="fill"/>
<aitools_action skill="draw_shape" chat_image="<canvas_idx>" shape="rect" x="3%" y="6%" width="22%" height="14%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="3" corner_radius="24"/>
<aitools_action skill="draw_text" chain="true" text="Where's my food?" x="3%" y="6%" width="22%" height="14%" font_size="46" color="#000000" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_shape" chain="true" shape="rect" x="36%" y="6%" width="24%" height="14%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="3" corner_radius="24"/>
<aitools_action skill="draw_text" chain="true" text="It's right there." x="36%" y="6%" width="24%" height="14%" font_size="46" color="#000000" bold="true" align="center" valign="middle"/>
<aitools_action skill="draw_shape" chain="true" shape="rect" x="69%" y="6%" width="28%" height="14%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="3" corner_radius="24"/>
<aitools_action skill="draw_text" chain="true" text="Bring it closer." x="69%" y="6%" width="28%" height="14%" font_size="46" color="#000000" bold="true" align="center" valign="middle"/>
```

`<canvas_idx>` is the new_canvas bubble; `<panelN_idx>` are the three
panels. The CHAT IMAGES line tells you the current count; new bubbles
arrive in stream order, so if it currently shows K, the canvas is
#K+1 and the panels are #K+2..#K+4.

---

## Recipe: 2x2 comic page

Same idea as the storyboard layout (see the `layouts` skill) but with a
speech bubble per panel.

```
<aitools_action skill="new_canvas" width="2048" height="1152" color="#FFFFFF"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel 1>"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel 2>"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel 3>"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel 4>"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panel1_idx>" x="2%" y="3%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panel2_idx>" x="51%" y="3%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panel3_idx>" x="2%" y="50%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panel4_idx>" x="51%" y="50%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="draw_shape" chat_image="<canvas_idx>" shape="rect" x="4%" y="6%" width="22%" height="10%" fill_color="#FFFFFFEE" outline_color="#000000" outline_width="3" corner_radius="20"/>
<aitools_action skill="draw_text" chain="true" text="Run!" x="4%" y="6%" width="22%" height="10%" font_size="44" color="#000000" bold="true" align="center" valign="middle"/>
```

Repeat the shape+text pair (chained) for each of the four panels at the
appropriate quadrant coordinates.

## Rules

- Speech bubbles always pair `draw_shape` (background rect) with a
  chained `draw_text` (dialog) at the SAME rect coordinates.
- For multi-panel layouts, panels go in via `paste_image` with
  `source_chat_image` (the canvas stays put as the `chat_image` slot).
- For recurring characters across panels, use `image_to_image
  chat_image="<panelA_idx>"` for panels B+ instead of fresh
  `generate_image` calls.
