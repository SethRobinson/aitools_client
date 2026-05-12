---
id: layouts
summary: Recipes for multi-image grid layouts (2x2 storyboard, before/after diptych, horizontal filmstrip). Auto-loaded for layout-class requests.
inputs: none
autoload: true
triggers: storyboard, storyboards, diptych, before/after, before and after, filmstrip, contact sheet, side by side, side-by-side, multi-panel, multipanel, panel grid, photo grid, image grid, collage, collages
template: <aitools_action skill="read_skill" id="layouts"/>
---
# Layouts - multi-image grids

Recipes for combining multiple images into a single composed bubble:
2x2 storyboard, before/after diptych, horizontal filmstrip.

## Mental model

- The canvas is `new_canvas` (its size + background color = the
  layout's frame).
- Each cell is a `paste_image` with `source_chat_image` or
  `source_attachment` pointing to the image being placed. The CANVAS
  slot stays the same canvas across all paste calls.
- Captions go on top via `draw_text` referencing the canvas.

---

## Recipe: 2x2 storyboard

User says: "storyboard a chase scene", "make a 4-panel storyboard",
"show me 4 frames of a kid's birthday".

Steps:
1. `new_canvas` - 16:9 background (e.g. 2048x1152).
2. `generate_image` x4 - the four panel scenes (separate bubbles).
3. `paste_image` x4 - composite each panel into a quadrant.
4. `draw_text` x4 - caption strip per panel.

```
<aitools_action skill="new_canvas" width="2048" height="1152" color="#1a1a1a"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<scene 1 - establishing shot>"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<scene 2 - action begins>"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<scene 3 - climax>"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<scene 4 - resolution>"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panel1_idx>" x="2%" y="3%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panel2_idx>" x="51%" y="3%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panel3_idx>" x="2%" y="50%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panel4_idx>" x="51%" y="50%" width="47%" height="44%" mode="fill"/>
<aitools_action skill="draw_text" chat_image="<canvas_idx>" text="1. The setup" x="2%" y="93%" width="47%" height="6%" font_size="32" color="#FFFFFF" align="left" valign="middle"/>
<aitools_action skill="draw_text" chat_image="<canvas_idx>" text="2. Trouble begins" x="51%" y="93%" width="47%" height="6%" font_size="32" color="#FFFFFF" align="left" valign="middle"/>
<aitools_action skill="draw_text" chat_image="<canvas_idx>" text="3. Climax" x="2%" y="48%" width="47%" height="3%" font_size="32" color="#FFFFFF" align="left" valign="middle"/>
<aitools_action skill="draw_text" chat_image="<canvas_idx>" text="4. Resolution" x="51%" y="48%" width="47%" height="3%" font_size="32" color="#FFFFFF" align="left" valign="middle"/>
```

`<canvas_idx>` is the canvas (first new bubble in this reply);
`<panelN_idx>` are the four panels (#K+2..#K+5 if CHAT IMAGES showed K
before this reply).

If you want only the final storyboard visible (not the four loose
panels too), chain everything onto the canvas instead - but then the
panels aren't separately reachable.

---

## Recipe: Before/after diptych

User says: "show before and after", "compare the two".

```
<aitools_action skill="new_canvas" width="2400" height="1200" color="#111111"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="<before_idx>" x="1%" y="6%" width="48%" height="80%" mode="fit"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="<after_idx>" x="51%" y="6%" width="48%" height="80%" mode="fit"/>
<aitools_action skill="draw_text" chain="true" text="BEFORE" x="0" y="88%" width="50%" height="8%" font_size="64" color="#FFFFFF" align="center" valign="middle"/>
<aitools_action skill="draw_text" chain="true" text="AFTER" x="50%" y="88%" width="50%" height="8%" font_size="64" color="#FFFFFF" align="center" valign="middle"/>
<aitools_action skill="draw_shape" chain="true" shape="rect" x="49.85%" y="0" width="0.3%" height="100%" fill_color="#444444"/>
```

Replace `<before_idx>` and `<after_idx>` with the actual bubble indices
the user is referring to (CHAT IMAGES tells you the count).

---

## Recipe: Horizontal filmstrip

User says: "make a filmstrip", "show 3 stages side by side".

```
<aitools_action skill="new_canvas" width="3000" height="1000" color="#000000"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel A>"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel B>"/>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel C>"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panelA_idx>" x="1%" y="5%" width="32%" height="90%" mode="fill"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panelB_idx>" x="34%" y="5%" width="32%" height="90%" mode="fill"/>
<aitools_action skill="paste_image" chat_image="<canvas_idx>" source_chat_image="<panelC_idx>" x="67%" y="5%" width="32%" height="90%" mode="fill"/>
```

## Rules

- The canvas is always picked via `chat_image="<canvas_idx>"` (or
  `chain="true"` for the FIRST paste right after `new_canvas`); each
  subsequent paste reuses the same canvas slot.
- Cells use `source_chat_image` / `source_attachment` (a SEPARATE slot
  from the canvas slot) to specify the image being pasted.
- For recurring-character panels, generate the first scene, then
  `image_to_image chat_image="<panel1_idx>"` for the others before
  pasting.
- For storyboard-only output (one final bubble, panels not separately
  visible), chain every paste onto the canvas instead of using
  `chat_image="<canvas_idx>"`.
