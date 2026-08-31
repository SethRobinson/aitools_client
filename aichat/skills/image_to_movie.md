---
id: image_to_movie
summary: Animate a STILL image into a short video. Default to Image To Video (MiniMax H3 Turbo Cache) 5s (native audio/dialog, 8-step turbo + Spectrum cache; progress shows 16 steps - 8 real + 8 cheap replay ticks, that is normal); use the 15s preset only for explicit long clips; duration="N" (seconds) works on EVERY H3 preset for any length. "High quality" requests use the 20-step (MiniMax H3 Quality) presets; plain Image To Video (MiniMax H3) 5s is the cache-free variant. EVERY H3 prompt is the official structured multi-line DOCUMENT, not a short paragraph (H3 trained on it; thin prompts render flat): integrated_multimodal_description: ([Shot 1] style + scene + actions + dialog; start-frame prompts re-anchor the source as <Picture 1> in Shot 1), overall_soundscape: (1-4 sentences of ambience/physical sound), non_diegetic_music: (instruments/tempo, or N/A). TARGET 150-250 words for a 5s clip, 250-450 for 10-15s/multi-shot, and the document RE-DESCRIBES THE WHOLE SCENE every render - H3 carries nothing over between videos, so never write delta prompts ("same scene but..."). Dialog is plain prose with the exact words quoted and the voice described around them - she says 'We open in five minutes.' in English with a warm calm voice - NEVER <d>[English]...</d> blocks or (S1) speaker IDs: that official-API markup renders as off-screen NARRATION with a closed mouth under ComfyUI (lip-sync A/B 2026-08-31). ~2.5 spoken words per second fit; a visible person with no quoted line mouths gibberish, so write the line or state nobody speaks. Camera moves are natural sentences: motion type + amplitude + speed ("the camera pushes in with small amplitude at slow speed"). Reference To Video presets REQUIRE a photo source and use the SIX-SECTION reference document instead (subject_definitions / summary / retention_analysis / detailed_description 350-500 words / overall_soundscape / non_diegetic_music); the prompt MUST use every staged photo/audio via its <Picture N>/<Audio N> tag (define <Subject N> from them in subject_definitions) - prose alone does not bind to a photo, and the host refuses reference actions whose prompts skip a staged reference's tag. Plain Reference To Video is already turbo; Quality = 20-step; there is NO Cache reference variant - never invent preset names. A video with no source at all is generate_movie territory. A Movie #N is not an image source: use video_to_video, except one explicit current frame with movie_frame="true". Put prompt as the LAST attribute in the action tag.
inputs: attachment
autoload: true
triggers: animate, animation, image to video, image-to-video, image to movie, image-to-movie, animate this, animate it, make this move, make it move, make a video, make a movie, make a clip, create a video, create a movie, generate a video, generate a movie, video starring, movie starring, video of, movie of, second video, second movie, reference to video, minimax, mini max, minmax, minimax h3, minmax h3, h3, hailuo, spectrum, turbo cache, spectrum cache
template: <aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" chat_image="N" prompt="integrated_multimodal_description: [Shot 1] <full scene re-described> ... she says 'exact line.' in English with a warm calm voice ... + overall_soundscape: ... + non_diegetic_music: ... (150-250 words; prompt LAST in the tag)"/>  # STILL sources only. attachment= works only in the very message the user pasted the image in; on later turns use chat_image="N". To use an explicit current frame from a Movie add movie_frame="true"; otherwise existing Movies require video_to_video.
---
# Image-to-movie

Use this skill when the user wants you to ANIMATE an image into a short
video clip. The source can be either freshly pasted or already in chat.

If the user asks for a NEW text-described video with no source image
("make a video of X", "generate a MiniMax video of X"),
first emit `generate_image` with `{{Prompt To Image (Z-Image).txt}}`, then emit
this skill with `chain="true"`. Do NOT use direct `generate_movie` /
text-to-video unless the user explicitly asks for direct text-to-video, "no
still first", or a named `Prompt To Video ...` preset.

Specify EXACTLY ONE source via:

- `attachment="N"` - the Nth image pasted INTO THE CURRENT message (1-based,
  per-message; NOT the bubble number, and invalid on any later turn - use
  chat_image then).
- `chat_image="N"` - the Nth STILL chat-image bubble already in this
  conversation. Use when the user says "animate the image you just made";
  the CHAT IMAGES line in the system prompt shows the highest N reachable.

An existing `Movie #N` must use `video_to_video` for scene, motion, dialogue,
voice, audio, or sound changes. The only exception is an explicit request to
animate one single still/current frame from that Movie; add
`movie_frame="true"`. Unmarked Movie-to-image actions are rejected.

## Available presets

- `{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}` - DEFAULT. ~5s, native
  stereo audio + spoken dialog (11 languages), strong motion/identity. The
  fast 8-step turbo LoRA plus the Spectrum step-forecast cache. NOTE: its
  progress shows "Step N/16" (8 real sampler steps + 8 cheap transformer-free
  replay ticks from the cache's two-pass mode) - that is normal, not a longer
  render. For ANY specific duration, keep THIS preset and add `duration="N"`
  (seconds) - the host snaps it to H3's 24fps frame grid (steps of ~0.7s,
  minimum ~0.2s), so `duration="1"` gives a ~0.9s clip and `duration="10"`
  ~10.1s. ~5s is the sweet spot the model trained on; shorter always works,
  and much longer than 15s is allowed but untested. Direct text-to-video with
  the cache is
  `{{Prompt To Video (MiniMax H3 Turbo Cache) 5s.txt}}` via generate_movie;
  there is no 15s or Quality cache variant.
- `{{Image To Video (MiniMax H3) 5s.txt}}` - the same turbo pipeline WITHOUT
  the Spectrum cache ("Step N/8", ~1.4x slower). Use ONLY when the user
  explicitly asks to skip/disable the cache ("no cache", "plain turbo",
  "without spectrum") or blames the cache for artifacts or audio stutter.
  Supports `duration="N"` the same way.
- `{{Image To Video (MiniMax H3) 15s.txt}}` - same model, ~15s single
  generation, plain turbo (there is no 15s cache variant). Only when the user
  explicitly asks for a long clip (~4x the default render time). Takes
  `duration="N"` too.
- `{{Image To Video (MiniMax H3 Quality) 5s.txt}}` / `{{Image To Video (MiniMax H3 Quality) 15s.txt}}` -
  the full 20-step render (~3x the default's render time, slightly finer
  detail). Use when the user asks for high/maximum/highest quality or
  complains about turbo/cache output quality.
- `{{Reference To Video (MiniMax H3) 5s.txt}}` - the SUBJECT of the source
  image doing something new; output does NOT start on the source frame. See
  "Reference-to-video" below. REQUIRES at least one photo source
  (`attachment`/`chat_image`/`chain`) - never emit it sourceless. This plain
  name is already turbo (8-step Ref2V distill). For high/maximum-quality
  reference requests use `{{Reference To Video (MiniMax H3 Quality) 5s.txt}}`
  (full 20-step render, ~2x time). There is NO Cache variant of any Reference
  preset - never invent preset names by combining suffixes; use only names
  listed in this skill or generate_movie.

## Invocation

DEFAULT - stack onto the image you JUST generated in this same reply (chain="true").
Size BOTH actions to the video canvas (see "Sizing" below), and put `prompt`
LAST in each tag:
```
<aitools_action skill="generate_image" preset="Prompt To Image (Z-Image).txt" width="864" height="480" prompt="<full Z-Image scene description>"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" chain="true" width="864" height="480" prompt="<full three-field H3 document - see format below>"/>
```
This stacks the video onto the SAME Pic as the image you just made, so the
chat shows ONE bubble that updates from still -> playing video. Do NOT also pass
attachment / chat_image when you set chain="true" - the prior step's output is
inherited automatically. chain="true" only works as a follow-up to a generate
action emitted earlier in the same reply. This is the right form for any
"<image-model> + <video-model>" combo (e.g. Z-Image + MiniMax H3).

Animate a freshly-pasted image: `attachment="1"`. Animate an image already in
the chat from earlier: `chat_image="N"`. Same prompt format either way.

## The H3 prompt format (MANDATORY - every H3 video prompt)

H3 was trained on a structured multi-line prompt DOCUMENT, and it reads the
whole thing through a large text encoder: thin prompts render flat, generic
motion. Write the document inside the `prompt="..."` attribute (multi-line
values are fine; put `prompt` as the LAST attribute in the tag).

**Word targets: 150-250 words for a 5s single-scene clip; 250-450 words for a
10-15s or multi-shot clip.** Under ~100 words is too sparse. Spend the words
on things a viewer can literally see or hear - environment, wardrobe
materials, micro-actions, light behavior, reflections, steam, fabric - never
on abstractions ("cinematic energy", "she feels sad" -> "she lowers her gaze
and her shoulders drop").

### Skeleton (start-frame presets - Image To Video, and direct t2v)

```
integrated_multimodal_description: [Shot 1] Live-action, cinematic, <the scene: anchor the style, subjects, wardrobe, and setting of the source frame, then the actions, camera, and dialog along the timeline>. [Shot 2] At 00:03.000, the camera cuts to <next shot - only for multi-shot clips>.

overall_soundscape: <1-4 sentences: ambience, physical action sounds, non-verbal human sounds>.

non_diegetic_music: <1-3 sentences naming instruments, tempo, dynamics - or N/A>.
```

Start-frame prompts refer to the source image as `<Picture 1>` inside Shot 1
("the woman shown in <Picture 1> remains beside...") - no other markup; the
host wires the frame itself. Direct text-to-video uses the same three fields
with no `<Picture 1>`. Reference presets use a different six-section
document - see "Reference-to-video" below.

**Re-describe the WHOLE scene, every render.** H3 carries nothing over
between videos - there is no "the model already knows the scene". Even when
the request is "the same scene but she laughs", or references are staged,
the document describes the complete scene again: setting, every visible
person and their outfit, light, and all three audio layers. Delta-style
prompts ("only change...", "same as before but...") are a Klein/Bernini
EDIT convention and render wrong on H3 - anything undescribed is
re-invented from scratch.

### Field rules

- **integrated_multimodal_description** is the body: everything visible or
  audible along the timeline. `[Shot 1]` opens with the style (`Live-action,
  cinematic, ...` / `2D-animated, ...` / `claymation, ...` - for start-frame
  clips derive it from the source image) and the opening composition, then
  actions in order. For a start-frame clip, first re-anchor what the source
  shows (subject, wardrobe, setting - "preserving her auburn hair, green
  apron, and the chalkboard behind her"), then develop the motion forward.
- **Shots & cuts**: `[Shot 1]` has NO timestamp. Each later shot starts
  `[Shot N] At MM:SS.mmm, the camera cuts to ...` with strictly increasing
  times inside the clip's duration. Budget ~1 cut per 3 seconds and give
  every shot at least ~3s - so a 5s clip is ONE shot (two at most), a 15s
  clip carries 3-5. Decide the duration FIRST, then place cuts. A cut must
  reveal something new (subject, space, viewpoint); for a mere distance or
  angle change use camera motion instead.
- **Camera**: one move per shot, written as a natural sentence with motion
  type + amplitude + speed: `The camera pushes in with small amplitude at
  slow speed toward her hands.` Motion types: Zoom In/Out, Push In/Pull Out,
  Pan Left/Right, Truck Left/Right, Tilt Up/Down, Pedestal Up/Down, Arc
  Shot, Tracking Shot, Static Shot, Shake Slightly/Strongly, POV, Roll
  Clockwise/Counterclockwise. Omit amplitude/speed when medium/normal.
- **Speakers & dialog**: plain prose - describe the speaker's VOICE (age,
  pitch, timbre, pace, accent) around the line and quote the exact words:
  `she looks up and says 'We open in five minutes.' in English with a warm
  calm voice.` With several speakers, name each one's visible identity
  before their line (`the man in the denim jacket replies '...' in a low
  gravelly voice`). Do NOT use the official API's `<d>[English] ...</d>`
  blocks, `(S1)` speaker IDs, or `<scenetrans>`/`<cutoff>` markers: under
  ComfyUI's encoder that markup renders the line as off-screen NARRATION -
  correct audio, closed mouth (lip-sync A/B 2026-08-31: 4/4 `<d>` clips
  failed, 3/3 prose clips synced). NEVER "she says something" - unwritten
  lines come out as gibberish in a random language. **~2.5 spoken words fit
  per second** (5s = one ~12-word line; 10s = 20-25 words); BUDGET THE
  SECONDS - dialog plus described silent action must cover the whole
  duration or H3 invents mumbled filler in the gaps, so long clips end with
  an explicit silent tail ("then she reads quietly; no further dialog"). If
  nobody should talk, SAY SO: `No dialog; nobody speaks.` For deliberate
  narration say it in prose: `says in an off-screen voiceover '...' while
  her lips remain completely closed`. Dialog continuing across a cut is
  prose too: "her line continues seamlessly across the cut".
- **On-screen text** (signs, labels, titles) is spelled out verbatim in
  double quotes - inside the action attribute write them as `&quot;`
  (`a neon sign reading &quot;OPEN ALL NIGHT&quot;`; the host decodes them
  so H3 sees real quotes). Unspecified text renders as letter-shaped noise.
- **overall_soundscape**: 1-4 sentences for the WHOLE clip - ambience,
  physical action sounds (footsteps, fabric, impacts), non-verbal human
  sounds (breathing, laughter). No dialog or music here. `N/A` only for
  requested total silence.
- **non_diegetic_music**: 1-3 sentences naming instrumentation, tempo,
  rhythm, and dynamics (`Sparse piano notes at a slow tempo, joined by
  sustained low strings that swell and fade.`) - never mood words. `N/A`
  when there should be no score (the usual choice for realistic clips).
  Music the CHARACTERS can hear (a radio, a busker) is a diegetic event and
  belongs in the main description instead.
- No negative prompts - H3 has no negative path. State constraints as prose
  ("no subtitles", "his lips remain closed") inside the description.

### Worked example (5s start-frame clip, ~200 words - verified lip-synced 2026-08-31)

```
integrated_multimodal_description: [Shot 1] Live-action, cinematic, the woman shown in <Picture 1> remains behind the walnut espresso bar of the small sunlit cafe, preserving her shoulder-length wavy auburn hair, dark green canvas apron, cream henley shirt, the menu chalkboard, and the stacked white cups behind her. She lifts a folded white cloth and wipes the counter in two slow circular passes, then sets the cloth down beside the chrome portafilter. She raises her eyes toward the front door, and she says 'We open in five minutes.' in English with a warm calm voice. A faint smile forms as she straightens her apron with both hands. The camera pushes in with small amplitude at slow speed toward her at chest height, holding her centered while steam drifts from the espresso machine at the right edge of the frame and dust motes float through the shaft of golden morning light from the front window. Her reflection moves subtly across the polished walnut counter as she leans forward.

overall_soundscape: The espresso machine hisses softly with an occasional metallic tick as it heats. Fabric brushes the counter during the wiping passes, and muffled early-morning street noise continues faintly outside.

non_diegetic_music: N/A
```

For roleplay / recurring characters / identity anchors, the Shot 1 re-anchor
must restate the visible person fully (apparent age, complexion, build,
hair, face, wardrobe, expression) - never animate "Mara" or "the same
person" by name only; the video model has no chat memory.

## Reference-to-video (`{{Reference To Video (MiniMax H3) 5s.txt}}`)

Use INSTEAD of the normal i2v preset when the user wants the PERSON/OBJECT
from an image doing something new - not that exact frame animated. The output
does not start on the source image; the references lock identity/style. This
is ideal for anchors: "make a video of Mara surfing" from an anchored
portrait, and it is **THE way to make a video STARRING several existing
people**: one action, each person's photo(s) in their own slots (`chat_image`,
`chat_image2`..`chat_image9` or `attachment2`.. - up to 9 photos, `<Picture
1>`..`<Picture 9>` in slot order; unused slots are pruned). Do NOT build a
Klein composite still first and animate it - that is only for pinning an
exact start frame (see the two-stage recipe below).

Reference prompts use the official SIX-SECTION document, in this order:

```
subject_definitions:
<Subject 1> is the woman in <Picture 1>, with shoulder-length wavy auburn hair and a dark green canvas apron over a cream henley shirt.
<Audio 1> is the voice-timbre reference for <Subject 1>.

summary:
[reference generation + audio reference] The target video shows <Subject 1> greeting the first customer of the morning behind the cafe bar.

retention_analysis:
<Subject 1> (appears in [Shot 1]): fully_preserved - her face, auburn hair, green apron, and cream henley are retained exactly as referenced.
<Audio 1>: reference - vocal timbre guides her delivery without copying the signal.

detailed_description:
The target video uses a live-action cinematic style with warm golden morning light.
[Shot 1] A medium shot frames <Subject 1> behind the walnut espresso bar... <the COMPLETE scene plus actions, camera, and prose-quoted dialog, exactly as in the base format>.

overall_soundscape: <1-4 sentences>.

non_diegetic_music: <or N/A>.
```

- **subject_definitions**: one line per referenced thing. Each `<Subject N>`
  is DEFINED from its asset tags - `<Subject 1> is the man in <Picture 1>
  and <Picture 2>, ...` - naming a FEW recognizable traits from the photo's
  caption only (hair, wardrobe, build). This is where every staged photo's
  `<Picture N>` tag appears; the tags are the ONLY link between your prose
  and the pixels, and **the host refuses the action when a staged photo's
  tag is missing from the prompt or the prompt names a `<Picture N>` with no
  photo behind it**. Multiple photos of the SAME person define ONE subject
  (better identity lock) - never two people standing together.
- **Identity stays in the tags**: describe each person ONLY from what is
  visible in their photo/caption - NEVER from outside knowledge of a film,
  show, or actor, even when you recognize them. Invented prose traits
  ("auburn hair" on a blonde) OVERRIDE the photo and produce a stranger;
  when unsure of a trait, leave it out and let the tag carry it. Spend the
  350-500 words on the NEW scene, actions, camera, light, and sound - not on
  guessed facial detail.
- **summary**: one short paragraph opening with the bracketed task type -
  `[reference generation]`, plus ` + audio reference` when a voice/audio ref
  is staged. Reuse the defined labels; introduce no new ones.
- **retention_analysis**: one line per label. Subjects that must look
  exactly like their photos are `fully_preserved`; a deliberate change
  (new wardrobe, new hairstyle) is `partially_preserved - <what changes>`.
  Audio refs are `reference` (style/timbre guidance; H3 never copies an
  audio ref's signal or words).
- **detailed_description**: the body - **350-500 words for a full-length
  clip** (~250-350 is fine for a 5s clip; dialog-dense clips prioritize
  fitting the spoken timeline over word count). Open with 1-2 style
  sentences BEFORE `[Shot 1]`, then describe the ENTIRE scene from scratch -
  the references pin identity ONLY; the setting, wardrobe, light, and action
  all come from this text, so "the same scene as the last video" describes
  nothing. All base-format rules apply: shot/timestamp headers, one camera
  move per shot with amplitude + speed, prose-quoted dialog with the voice
  described around it (never `<d>`/`(S1)` markup - narration bug),
  second-budgeting with an explicit silent tail on long clips. Refer to
  people as `<Subject N>` after defining them.
- **Audio refs** (`audio="N"`, then `audio2`, `audio3` - Audio bubbles or
  Movies with sound; up to 3 standalone refs): each becomes an `<Audio N>`
  tag. Define it in subject_definitions bound to its speaker (`<Audio 1> is
  the voice-timbre reference for <Subject 1>.`), mark it `reference` in
  retention_analysis, and cite it at the dialog moment (`her voice styled
  like <Audio 1>, she says 'exact line.'`). It is a STYLE reference - voice
  character/music/ambience nudged toward the sample, NOT a clone, and it
  NEVER supplies the words: every line still needs its exact quoted words,
  or H3 invents dialog in a random language. The phrasing is load-bearing:
  ALWAYS "styled like <Audio 1>" + retention `reference` - copy-flavored
  wording ("her voice matches <Audio 1>", "the voice from <Audio 1>",
  "reuses <Audio 1>", `fully_copy`) makes H3 SPLICE the sample's actual
  audio into the clip instead of speaking the new line in that voice
  (observed 2026-08-31). Copy wording is reserved for music/ambience the
  user explicitly wants kept as-is. For a REAL / named cast with WEB ACCESS
  on, the default staging is fetched without being asked: two `web_image`
  stills per person from the show itself (`count="2"`, in-character scene
  frames, not interviews) and one `web_video speech="true"` clip per
  speaking character (recipe: the web_image skill). When a voice sample of a
  speaking character already exists in chat, staging it is MANDATORY - the
  user put it there to be used. Every staged audio ref's tag must appear in
  the prompt (same host gate as photos).
- If a reference is a MOVIE bubble rather than a still, use
  `skill="video_to_video"` with `{{Reference Video To Video (MiniMax H3) 5s.txt}}`
  and `<Video 1>` - that preset also takes photo references and a second
  clip; see the video_to_video skill. To pull face stills OUT of a movie for
  photo refs, use `extract_still` (anchored) first.

## Exact START FRAME plus a specific person - two-stage recipe

H3 cannot pin a literal start frame AND take references in one pass (start
frames are the i2v model; references are a different checkpoint). When the
user wants the video to open on an exact frame but with a specific person in
it, build the frame first, then animate it:

1. `image_to_image` with `{{Image To Image Klein Edit 2 Input.txt}}` - insert
   the person (photo in slot 2) into the desired start frame.
2. `image_to_movie` with `{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}` and
   `chain="true"` in the same reply - animates that exact composed frame.

If the opening frame does NOT need to be exact, skip the two-stage recipe and
use reference-to-video: the photo guides identity without pinning frame 1.

## Several clips into one film (stitch_video)

When the user wants a SEQUENCE of clips joined into one video ("make 10
clips that tell a story, then stitch them together", "a 1 minute episode"),
emit every clip in ONE reply, give each MOVIE-producing action its own
`anchor="sceneN"` and `duration="5"` (total = clips x duration; 5 s per clip
is the DEFAULT - "about a minute" = 12 x `duration="5"`; use
`duration="10"`/`"15"` only when the user explicitly asks for longer
individual clips - long single generations drift), and end the reply with one
`stitch_video chat_images="scene1,...,sceneN"`. The host waits for all the
renders and then posts the joined Movie; do not wait a turn or guess Movie
numbers. EVERY clip's prompt is a full structured document that stands alone
(the renders share no memory): write the shared scene/wardrobe/style text
ONCE - for reference clips the `subject_definitions` + `retention_analysis`
sections, for base clips the Shot 1 re-anchor sentences - and paste it
VERBATIM into every clip's document, varying only the actions, dialog, and
camera. "Same diner as before" renders a different diner. Full recipe: the
`stitch_video` skill.

If the cast are REAL / NAMED / anchored people, each clip is ONE reference
action (never a Z-Image lookalike still); invented characters use the
still -> movie pair per clip.

## Sizing

The video presets render at **864x480** (~0.4MP). Video cost scales with pixel
count, so an oversized canvas is the single easiest way to make a clip take
twice as long for no visible benefit.

### Chaining a still into a movie: size BOTH actions

When you generate a still and chain a movie onto it in the same reply (the
default text-to-video flow), put the SAME `width`/`height` on BOTH actions, so
the still is made at exactly the video's canvas.

**The user's request always wins.** If they name a size or format, use it on
both actions: `720p` -> 1280x720, `1080p` -> 1920x1080, `4k`/`2k` -> the
closest sensible size, "vertical/portrait/phone" -> a tall canvas, an explicit
"1024x576" -> exactly that. Only fall back to the defaults below when the user
said nothing about size.

Default canvas when the user did not ask for one:

- landscape (default): `width="864" height="480"`
- portrait / vertical / phone: `width="480" height="864"`
- square: `width="640" height="640"`

Without this the still defaults to 1024x1024 - slower to render than the video
canvas needs, and a needlessly large first frame that just gets resampled.
Bigger canvases cost proportionally more time (1080p is ~5x the pixels of the
default), so don't upsize unless asked.

### Animating an image that already exists

When the source is an `attachment` or `chat_image` (no still made this turn)
and the user named no size, OMIT `width`/`height`. The host automatically
matches the video to the source's aspect while keeping the preset's pixel
budget, so a square source renders a square video with no crop.

When the user DOES name a size (720p, 1080p, "big", an exact WxH), pass it as
`width`/`height` even for existing sources: on start-frame presets the host
treats a size whose aspect differs from the source as a PIXEL BUDGET and
refits it to the source's aspect (720p on a portrait photo renders a ~0.92MP
portrait), so the start frame is never distorted - you don't need to compute
the aspect yourself. To truly CHANGE the shape of the output, crop_resize the
still to the new shape first (start-frame presets stretch, never crop), or
use reference-to-video where no frame is pinned. Both attributes are required
together; they snap to multiples of 32.

### Size keywords

The default canvas is already the fast size. Map vague size words like this,
and put the result on both actions:

- "small" / "low res" -> 640x352 (352x640 portrait, 512x512 square). Don't go
  lower; H3 quality falls apart under ~384p.
- "big" / "high res" / "detailed" -> 1152x640, near H3's trained maximum.
- "720p" -> 1280x720. "1080p" -> 1920x1080. These are above what H3 was
  trained on (~1MP), so they are slower and can look softer, but if the user
  asked for them, use them.
- "high quality" / "highest quality" -> BOTH the `(MiniMax H3 Quality)` preset
  AND a 1280x720-budget canvas (1280x720 landscape / 720x1280 portrait /
  960x960 square). Quality means more steps AND more pixels, not just one.

## Rules

- Use ONLY exact preset names listed in this skill or generate_movie. Never
  construct new names by mixing suffixes (e.g. there is no "Reference To
  Video (MiniMax H3 Turbo Cache)"). If a requested speed/quality variant does
  not exist for a mode, use the closest listed preset and tell the user which
  variant applied (e.g. reference generations ignore the spectrum cache).
- EVERY H3 prompt is the structured document: the three labeled fields for
  i2v/t2v, the six labeled sections for reference presets. 150-250 words
  (5s) / 250-450 (10-15s, multi-shot) / detailed_description 350-500
  (reference), always re-describing the WHOLE scene - never a delta. Put
  `prompt` LAST in the tag.
- Reference To Video / Reference Video To Video always need at least one
  photo/clip source. A brand-new video with NO source: default recipe is
  Z-Image still + chained image_to_movie onto the DEFAULT
  `{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}`; explicit direct
  text-to-video -> generate_movie with a `Prompt To Video ...` preset.
- Chained still -> movie: put the SAME `width`/`height` on BOTH actions
  (864x480 landscape, 480x864 portrait, 640x640 square). Animating an existing
  attachment/chat_image: omit them and let the host match the source aspect.
- Pick exactly ONE source: `attachment`, `chat_image`, OR `chain="true"`.
  If both `attachment` and `chat_image` are set, `chat_image` wins. `chain="true"`
  must NOT be combined with the others - the chained step inherits the prior
  step's output automatically.
- One camera move with magnitude per shot. Two competing moves fight.
- Every prompt covers all three audio layers: prose-quoted dialog lines
  (never `<d>`/`(S1)` markup - it kills lip sync; or an explicit `No dialog;
  nobody speaks.`), overall_soundscape, and non_diegetic_music (or N/A).
  15s preset only on explicit request.
- User asked to animate → just do it.
