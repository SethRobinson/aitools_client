---
id: comics
summary: Recipes for comic panels, speech bubbles, and multi-panel comic strips/grids. Auto-loaded for comic-class requests. Prefer Ideogram 4 single-render pages/strips for new whole comics; use local composition for existing-image assembly, overlays, and repairs.
inputs: none
autoload: true
triggers: comic, comics, comic panel, comic panels, comic strip, comic strips, comic book, comic page, manga panel, manga page, speech bubble, speech bubbles, thought bubble, dialogue bubble
template: <aitools_action skill="read_skill" id="comics"/>
---
# Comics

Recipes for single comic panels with speech bubbles, horizontal multi-
panel strips, and 2x2 comic pages. New whole comic strips/pages default to
Ideogram 4 because it handles panel grids, speech balloons, and rendered text
in one layout-aware pass.

## Mental model

- One bubble per visible deliverable (one panel = one bubble; one strip
  = one bubble).
- Default for a NEW whole comic strip, comic page, manga page, or finished
  multi-panel comic: emit ONE `generate_image` action per requested page/strip
  using `{{Prompt To Image (Ideogram 4).txt}}`. Do not generate loose Z-Image
  panels and assemble them unless one of the fallback cases below applies.
- Use local composition (`new_canvas` / `paste_image` / `draw_shape` /
  `draw_text`) instead of Ideogram when the user is adding a bubble to an
  existing image, combining existing chat images/attachments, requires exact
  anchor/reference identity across separately edited panels, asks for separate
  panel source images, or is repairing/replacing text on an already-composed
  image.
- If the user explicitly asks for Z-Image, Krea, local assembly, or existing
  chat-image panels, honor that explicit request instead of the Ideogram
  default.
- Ideogram comic prompts are structured JSON captions in the `prompt`
  attribute, not prose. Escape every double quote inside the JSON as `\"`,
  keep the JSON on one line, and make `prompt` the last action attribute.
- Ideogram should render the finished page/strip, including title, panel
  frames, speech balloons, and dialog text. Use fixed `[y1,x1,y2,x2]`
  title/panel bboxes, speaker-side speech boxes, and explicit tail endpoint
  coordinates. Repeat the intended speaker, panel, and tail endpoint near the
  speaker's face/mouth in both the speech-balloon desc and matching text desc,
  and explicitly say nearby non-speakers have no bubble/tail. For Ideogram
  speech, use wide horizontal balloon/text bboxes and no manual `\n` line
  breaks; tall oval bubbles make English text stack vertically.
- Speech bubbles are `draw_shape` (rounded rect background) + `draw_text`
  (dialog) chained on top, both at the SAME rect coordinates.
- When fixing/replacing a speech bubble on a composed image from a previous
  turn, check CHAT IMAGES for `clean_base=available`; if present, use
  `clean_base="true"` on the replacement `draw_shape chat_image="N"` step,
  then chain `draw_text`. Do not draw on top of a bubble that already has
  text baked in.
- When fixing a title, caption, label, border, or speech bubble, use local
  composition only (`draw_text`, `draw_shape`, `paste_image`, `new_canvas`).
  Do not use `generate_image`, Ideogram, or image-to-image to repair precise
  typography/layout unless the user explicitly asks for a full rerender.
- If the existing composed image has `clean_base=available`, the first repair
  overlay MUST use `chat_image="N" clean_base="true"` so old title/bubble text
  is removed before the replacement is drawn.
- If the problem is spacing, such as a title touching panel artwork, rebuild
  the layout from the clean base or from the original source panels with a
  larger title band/gutter. Do not simply draw a new title over the old one.
- Do not put `bg_color` on `draw_text` after drawing a rounded speech bubble;
  the text background can square off the rounded corners. Let `draw_shape`
  provide the bubble fill.
- Multi-panel layouts generate the panel art first, then create a
  `new_canvas` immediately before final assembly. Every `paste_image`,
  `draw_shape`, and `draw_text` in that assembly uses `chain="true"` so
  the same canvas becomes the finished comic instead of separate partial
  canvases.
- For recurring characters across panels, use `image_to_image` with
  `chat_image="<character anchor or panel A anchor>"` for panels B+ instead of
  fresh `generate_image` calls, but still set `anchor="<new panel anchor>"` on
  every panel-producing action that will be pasted later.
- For panels created earlier in the SAME reply, name the canvas/panels with
  `anchor="..."` and use those names in `chat_image` / `source_chat_image`.
  Do not predict #K+N future chat_image numbers.

---

## Default recipe: whole new comic with Ideogram

Use this for requests like "make a comic strip about X", "comic page",
"manga page", "two-panel comic", or "four-panel comic" when the comic is a
new finished deliverable.

Steps:
1. Decide one page/strip layout.
2. Emit one `generate_image` action using `{{Prompt To Image (Ideogram 4).txt}}`.
3. Put the complete comic layout in a single-line JSON caption.

Layout defaults:
- 2-panel or 3-panel horizontal strip: `width="1280" height="640"` and
  `aspect_ratio:"2:1"`. Reserve a title/caption band only if needed.
- 2x2 page: `width="1024" height="1024"` and `aspect_ratio:"1:1"`.
  For a titled square page, title bbox `[20,40,90,984]`; panels
  `[120,60,482,482]`, `[120,542,482,964]`, `[542,60,934,482]`,
  `[542,542,934,964]`.
- If the user explicitly names another format/ratio, choose the nearest
  supported Ideogram size and make the JSON `aspect_ratio` match.

Element rules:
- Top-level JSON is exactly:
  `{"aspect_ratio":"W:H","high_level_description":"...","compositional_deconstruction":{"background":"...","elements":[...]}}`
- Use one `obj` element per panel frame. The panel desc names the panel
  number, the beat, the border, the interior art, and any speaker face/mouth
  coordinates in page coordinates (`x=...`, `y=...`).
- Use one `obj` element for each speech balloon, followed by one matching
  `text` element. Keep every speech/text bbox wide and horizontal.
- Restate recurring people/characters inside every panel desc; do not rely on
  pronouns or "same person from panel 1".

Example two-panel strip:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Ideogram 4).txt}}" width="1280" height="640" prompt="{\"aspect_ratio\":\"2:1\",\"high_level_description\":\"A finished two-panel comic strip titled DEBUG MODE, with two bordered panels and readable horizontal speech balloons about a programmer and a small desk robot finding an error.\",\"compositional_deconstruction\":{\"background\":\"Clean off-white comic strip page with black gutters, a reserved title band across the top, and two equal bordered panel areas below.\",\"elements\":[{\"type\":\"text\",\"bbox\":[35,60,115,940],\"text\":\"DEBUG MODE\",\"desc\":\"Centered uppercase comic-strip title in the top title band, bold black display lettering, fits fully inside the band with clear padding above the panels.\"},{\"type\":\"obj\",\"bbox\":[150,50,900,485],\"desc\":\"Panel 1 frame with thick black border: tired adult programmer with light skin, short dark hair, blue hoodie, and round glasses sits at a cluttered desk on the right, mouth around x=365,y=560; small silver desk robot with square head and blue screen face sits on the left, face around x=170,y=585; monitor shows a red error line.\"},{\"type\":\"obj\",\"bbox\":[150,515,900,950],\"desc\":\"Panel 2 frame with thick black border: same tired adult programmer with light skin, short dark hair, blue hoodie, and round glasses stares at the monitor on the right, mouth closed around x=830,y=565; same small silver desk robot points at the keyboard on the left, face around x=620,y=585.\"},{\"type\":\"obj\",\"bbox\":[180,160,280,450],\"desc\":\"Panel 1 programmer speech balloon, wide horizontal rounded rectangle above the programmer on the right side of panel 1; tail endpoint touches the programmer's mouth near x=365,y=560; the desk robot has no speech bubble and no tail points to it.\"},{\"type\":\"text\",\"bbox\":[195,185,260,425],\"text\":\"The error is here.\",\"desc\":\"Panel 1 programmer speech text, horizontal left-to-right comic lettering; belongs to the programmer; same tail endpoint near x=365,y=560, not the desk robot.\"},{\"type\":\"obj\",\"bbox\":[180,545,280,840],\"desc\":\"Panel 2 robot speech balloon, wide horizontal rounded rectangle above the desk robot on the left side of panel 2; tail endpoint touches the robot's screen face near x=620,y=585; the programmer has no speech bubble and no tail points to him.\"},{\"type\":\"text\",\"bbox\":[195,570,260,815],\"text\":\"It was a semicolon.\",\"desc\":\"Panel 2 robot speech text, horizontal left-to-right comic lettering; belongs to the desk robot; same tail endpoint near x=620,y=585, not the programmer.\"}]}}"/>
```

For four-panel pages, use the same action pattern with the square 2x2 bboxes
above and one balloon+text pair per panel.

---

## Recipe: Single panel with a speech bubble

User says: "add a speech bubble that says X", "make this a comic panel".

If this is a brand-new single-panel comic with rendered dialog, prefer the
Ideogram default recipe above with one panel frame and one balloon+text pair.
Use the local steps below when adding/replacing a bubble on an existing image
or when the user needs editable local overlay text.

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

## Fallback recipe: Horizontal 2-panel comic strip from separate panels

User says: "2-panel comic", "two panel comic strip about X".

Use this only for fallback cases: combining existing chat images/attachments,
exact anchor/reference workflows, user-requested separate source panels, or
local editable overlays. For a normal new two-panel comic, use the Ideogram
default recipe above.

Steps:
1. `generate_image` x2 - the two panel scenes (separate source bubbles).
   For character continuity, generate panel A first, then make panel B
   via `image_to_image chat_image="comic_panel_a"` when the subject must
   stay identical. EVERY panel source action must set its own panel anchor:
   `anchor="comic_panel_a"`, `anchor="comic_panel_b"`, etc. This applies to
   `image_to_image` panel actions too.
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

## Fallback recipe: Horizontal 3-panel comic strip from separate panels

User says: "3-panel comic strip about my cat", "comic strip of X".

Use this only for fallback cases: combining existing chat images/attachments,
exact anchor/reference workflows, user-requested separate source panels, or
local editable overlays. For a normal new three-panel comic, use the Ideogram
default recipe above.

Steps:
1. `generate_image` x3 - the three panel scenes (separate bubbles). For
   character continuity, generate panel A first, then make panels B/C
   via `image_to_image chat_image="CharacterAnchor"` or
   `image_to_image chat_image="comic_panel_a"`, but still set
   `anchor="comic_panel_b"` and `anchor="comic_panel_c"` on those edited panel
   actions. If an action creates a panel that will be pasted later, it must
   carry the matching panel anchor.
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

If panel B/C are image edits rather than fresh text-to-image renders, the
pattern is:

```
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" prompt="<panel A - establishing>" anchor="comic_panel_a"/>
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 1 Input.txt}}" prompt="<panel B edit, keeping the recurring subject consistent>" chat_image="OldMan" anchor="comic_panel_b"/>
<aitools_action skill="image_to_image" preset="{{Image To Image Klein Edit 1 Input.txt}}" prompt="<panel C edit, keeping the recurring subject consistent>" chat_image="OldMan" anchor="comic_panel_c"/>
```

Never paste `source_chat_image="comic_panel_b"` or
`source_chat_image="comic_panel_c"` unless those anchors were actually created
earlier in the same reply or already exist in the ANCHORS line.

---

## Fallback recipe: 2x2 comic page from separate panels

Same idea as the storyboard layout (see the `layouts` skill) but with a
speech bubble per panel.

Use this only for fallback cases: combining existing chat images/attachments,
exact anchor/reference workflows, user-requested separate source panels, or
local editable overlays. For a normal new 2x2 comic page, use the Ideogram
default recipe above.

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

### Optional page title / safe title band

If the user asks for a title, reserve a dedicated top band and push the upper
panels down. The title must not touch or overlap panel art.

```
<aitools_action skill="new_canvas" width="2048" height="1152" color="#FFFFFF" anchor="comic_page_canvas"/>
<aitools_action skill="draw_text" chain="true" text="THE TITLE" x="0" y="1%" width="100%" height="8%" font_size="72" color="#111111" bold="true" align="center" valign="middle"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_page_1" x="2%" y="11%" width="47%" height="39%" mode="fill"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_page_2" x="51%" y="11%" width="47%" height="39%" mode="fill"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_page_3" x="2%" y="55%" width="47%" height="39%" mode="fill"/>
<aitools_action skill="paste_image" chain="true" source_chat_image="comic_page_4" x="51%" y="55%" width="47%" height="39%" mode="fill"/>
```

Keep at least 2% vertical gutter between the title band and the upper panel
rects. If a later inspection says the title touches the art, rebuild with
`y="12%"` or lower for the upper panels, or reduce the title height/font size.

## Repair recipes

### Replace title/text on a composed comic

Use this when only the overlay text is wrong and the base art/layout is fine.

```
<aitools_action skill="draw_text" chat_image="6" clean_base="true" text="THE BETTER TITLE" x="0" y="1%" width="100%" height="8%" font_size="72" color="#111111" bold="true" align="center" valign="middle"/>
```

If the old title was on a busy background, first draw a clean title band from
the clean base, then chain the title:

```
<aitools_action skill="draw_shape" chat_image="6" clean_base="true" shape="rect" x="0" y="0" width="100%" height="10%" fill_color="#FFFFFF"/>
<aitools_action skill="draw_text" chain="true" text="THE BETTER TITLE" x="0" y="1%" width="100%" height="8%" font_size="72" color="#111111" bold="true" align="center" valign="middle"/>
```

### Fix title/panel overlap

Use this when the title touches or covers panel art. Rebuild the comic canvas
from the source panel images or the clean base with a larger top band. Do not
draw a second title over the flawed composite.

## Rules

- For a new whole comic strip/page/manga page, default to one Ideogram 4
  `generate_image` action per finished page/strip using structured JSON.
- Do not use several Z-Image panel renders for a normal new comic; that is a
  fallback for existing images, exact anchor workflows, separately requested
  source panels, or local editable overlays.
- Speech bubbles always pair `draw_shape` (background rect) with a
  chained `draw_text` (dialog) at the SAME rect coordinates.
- If replacing a previous speech bubble/text/title/caption overlay, use
  `clean_base="true"` on the first replacement shape/text action when the
  image advertises `clean_base=available`; otherwise old text remains baked
  underneath.
- For title fixes, the replacement title rect must live inside a reserved band
  with a clear gutter before panel art. If no safe band exists, rebuild the
  layout instead of overlaying.
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
  chat_image="<character anchor or panel A anchor>" anchor="<new panel anchor>"`
  for panels B+ instead of fresh `generate_image` calls.
