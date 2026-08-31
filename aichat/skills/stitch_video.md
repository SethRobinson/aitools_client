---
id: stitch_video
summary: Join two or more existing Movie bubbles into ONE video, back to back, in the order listed (local FFmpeg, no GPU; mixed sizes letterboxed, audio kept). MULTI-CLIP FILM / EPISODE RECIPE ("make N clips that tell a story, then stitch them", "a 1 minute episode"): do it in ONE reply - emit every clip's action, put anchor="sceneN" on each movie-producing action, and END the reply with ONE stitch_video chat_images="scene1,...,sceneN". The host parks the stitch until every clip has rendered: never wait a turn, never emit continue to "check on" the clips, never guess Movie numbers. HOW EACH CLIP IS MADE depends on the cast - (a) REAL / NAMED / EXISTING people (web_image anchors or any anchors listed in ANCHORS): ONE action per clip, image_to_movie preset="Reference To Video (MiniMax H3) 5s.txt" with every person's photo in chat_image, chat_image2.. (they are <Picture 1>, <Picture 2>.. in the prompt), or video_to_video preset="Reference Video To Video (MiniMax H3) 5s.txt" with a talking web_video clip of the speaker as chat_image (<Video 1>/<Audio 1> = a voice-STYLE source: write "styled like <Audio 1>", never "matches"/"the voice from" - copy wording splices the sample's audio verbatim) plus the photos in chat_image2+; NEVER a generate_image still of a lookalike. (b) Invented characters only: generate_image -> image_to_movie chain="true" pairs. EVERY clip's prompt is a FULL structured H3 document that stands alone (see image_to_movie; the renders share no memory - "same diner as before" or "<Picture 1> again" renders a DIFFERENT diner and a changed outfit): write the shared text ONCE - subject_definitions + retention_analysis for reference clips, the Shot 1 style/scene re-anchor sentences for base clips - and paste it VERBATIM into every clip's document, varying only detailed_description's actions, quoted dialog, and camera - and every clip still re-describes the WHOLE scene (H3 carries nothing between renders; dialog is prose-quoted, never <d>/(S1) markup which kills lip sync). Per clip target ~250-350 words (reference) / 150-250 (base i2v). LENGTH MATH: total = clips x duration, with duration="5" on every clip action as the DEFAULT - H3 quality/identity degrades on long single generations, so more 5s clips ALWAYS beats fewer long ones: "about a minute" = 12 clips x duration="5", "~2 minutes" = 24 x duration="5"; use duration="10"/"15" ONLY when the user explicitly asks for longer individual clips. Optional crossfade="0.5" for dissolves instead of hard cuts.
inputs: none
autoload: true
triggers: stitch, stitched, stitching, concatenate, concat, join the clips, join the videos, join them together, join them into, combine the clips, combine the videos, combine them into one, combine the movies, merge the clips, merge the videos, merge the movies, into one video, into one movie, into one long, into a single video, into a single movie, one long video, one long movie, one continuous video, back to back, back-to-back, put them together, put the clips together, sequence of clips, series of clips, series of videos, clips that tell a story, videos that tell a story, short film, mini movie, mini-movie, full movie, feature, episode, an episode of, multi-clip, multiple clips, several clips, 10 videos, ten videos, 5 videos, five videos, 10 clips, ten clips, 5 clips, five clips, each 10 seconds, each 5 seconds, minute long, 1 minute, one minute, 2 minute, two minute, 30 second, thirty second
template: <aitools_action skill="stitch_video" chat_images="scene1,scene2,scene3" anchor="film"/>  # chat_images = comma list, in playback order (min 2, max 60): Movie numbers ("3,5,7"), ranges ("3-12"), anchor names, or "all". Same-reply clips: give each movie-producing action an anchor="sceneN" (real people: image_to_movie with Reference To Video (MiniMax H3) 5s.txt + the photo anchors in chat_image..chat_image9, duration="5"; invented characters: generate_image -> image_to_movie chain="true") and list those names here - the host waits for their renders. Optional: crossfade="0.5" (seconds; default hard cuts), audio="false", width/height/fps overrides, resume="true" to get a turn once the film is done.
---
# Stitch video (join clips into one film)

Joins existing `Movie #N` bubbles into ONE new Movie bubble, played back to
back in the order you list them. This is a LOCAL FFmpeg operation: no GPU, no
ComfyUI, it takes seconds once the source clips exist. Every clip is fit
inside one shared canvas (letterboxed if a size differs), resampled to one
frame rate, and its audio is carried over (silent clips get a silent track so
the timing stays exact). The result is a normal Movie bubble: it can be
clipped, RIFE-smoothed, frame-extracted, or used as `<Video 1>` later, and
the user can save it from its bubble.

## Invocation

```
<aitools_action skill="stitch_video" chat_images="3,5,7"/>
```

- `chat_images` - the playlist, comma separated, in playback order. Each item
  is a Movie number (`7`), an inclusive range (`3-12`), an anchor name
  (`scene1`), or `all` (every Movie bubble in the chat, oldest first). At
  least 2 items, at most 60. A clip may be listed twice to repeat it.
- `anchor="name"` - optional name for the finished film, so later actions can
  reference it (`chat_image="film"`).
- `crossfade="0.5"` - optional dissolve length in seconds between clips.
  Default is a hard cut, which is what a story with distinct shots normally
  wants. Each crossfade shortens the film by that much and needs every clip
  to be at least twice as long as the fade.
- `audio="false"` - optional; drop all audio. Default keeps it.
- `width`/`height` and `fps` - optional overrides. By default the canvas is
  the size most of the clips share and the frame rate is the highest input
  fps, so nothing is cropped or dropped. Only pass them when the user asks.
- `resume="true"` - optional; the host gives you one automatic `(continue)`
  turn after the film lands, if you want to comment on it. Otherwise the
  finished Movie simply appears and CHAT IMAGES describes it next turn.

Slot form also works for a few clips: `chat_image="3" chat_image2="5"
chat_image3="7"`. Prefer `chat_images` for anything longer.

## Planning a film: length math first

Decide the clip count from the requested total BEFORE emitting anything, and
put `duration="5"` on EVERY movie-producing action. **5 seconds per clip is
the default and the sweet spot**: H3 trained on ~5s clips, and longer single
generations degrade - faces/identity drift and unscripted seconds fill with
invented mumbling. A film cut from more 5s shots also paces better. Reach
the total with MORE clips, never longer ones:

| requested total | plan |
|---|---|
| ~30 s | 6 clips x `duration="5"` |
| ~1 minute | 12 clips x `duration="5"` |
| ~2 minutes | 24 clips x `duration="5"` |
| unspecified | 3-5 clips x `duration="5"` |

Use `duration="10"` / `duration="15"` ONLY when the user explicitly asks for
longer individual clips ("15-second shots", "one continuous 10s take"), and
then the promised length MUST be on the action: "four 15-second clips" means
`duration="15"` on each. Without `duration=` a clip is 5 s anyway; keep the
explicit `duration="5"` so the length math is visible. Reference generations
(real people) run an 8-step turbo distill by default but still take a minute
or more each; that is fine - the stitch waits, and clips render in parallel
across idle GPUs.

## Which action makes each clip

**(a) The cast are REAL, NAMED, or EXISTING people** - there are anchors for
them in the ANCHORS line (web_image fetches, user photos, minted portraits).
Each clip is ONE reference action that takes the photos directly. Do NOT
generate a Z-Image still of a lookalike and animate it: text alone produces a
stranger and throws the references away.

- DEFAULT when WEB ACCESS is on (looks AND sounds right) - fetch without
  being asked: per person, ONE `web_image count="2"` from the show itself
  (query "<show> <character> scene still", criteria "in-character scene
  frame from the show itself, in costume on set - not an interview, talk
  show, premiere, red carpet, award show, photoshoot, or headshot"), and per
  SPEAKING character ONE `web_video query="<show> <character> talking
  scene" speech="true" anchor="<name>_clip"` (it auto-continues). Then on
  the continue turn each clip is `video_to_video` +
  `{{Reference Video To Video (MiniMax H3) 5s.txt}}` with the speaker's clip
  in `chat_image` (`<Video 1>` / `<Audio 1>` - a voice-STYLE source), an
  optional second speaker's clip in `chat_image2` (`<Video 2>` / `<Audio 2>`;
  max 2 clips per render; further speakers as `audio="<name>_clip"` ->
  `<Audio 3>`..), and the photo anchors in the following slots
  (`<Picture 1>`..; each person's `name` + `name_2` stills define ONE
  `<Subject N>`). Say in the prompt whose voice is styled like which
  `<Audio N>` and write the exact spoken lines.
- Photos only (nobody speaks, or references already in chat and no web):
  `image_to_movie` + `{{Reference To Video (MiniMax H3) 5s.txt}}`, the
  people's photo anchors in `chat_image`, `chat_image2`.. (up to 9; they are
  `<Picture 1>`, `<Picture 2>`.. in slot order), `duration="N"`,
  `anchor="sceneN"`. Describe each person ONLY from the photo's caption.

**(b) Invented characters** (nobody in ANCHORS, no real names): the standard
still -> movie pair per clip, `duration="N"` and `anchor="sceneN"` on the
MOVIE action. Restate the character's full appearance in every prompt.

Either way: an explicit clip count or total length from the user overrides
the roleplay pacing rule about idle GPUs - emit every clip now.

## Every clip prompt stands alone (MANDATORY, like the audio spec)

Each clip is an INDEPENDENT render: the model has no memory of the other
clips, and the stitcher just plays the results back to back. Anything a
prompt leaves out is re-invented for that clip, which shows up as continuity
errors in the film (the diner changes, the dress changes color, the lighting
jumps). Every clip's prompt is a FULL structured H3 document (the formats
live in `image_to_movie`: six sections for reference clips, three fields for
still->movie pairs), and the structure makes consistency cheap - **write the
shared block ONCE and paste it VERBATIM into every clip's document**:

- reference clips: identical `subject_definitions` + `retention_analysis`
  sections in all 12 documents (the `<Picture N>` tags pin faces but NOT
  wardrobe or scene - the definitions carry outfit and setting), plus the
  same style-opening sentence of `detailed_description`;
- still->movie pairs: the same character-appearance/outfit/setting sentences
  in every Z-Image still prompt AND every movie document's Shot 1 re-anchor;
- both: consistent `overall_soundscape` and `non_diegetic_music` text across
  scenes so the soundtrack doesn't lurch at every cut.

Vary ONLY the actions, quoted dialog lines, and camera between clips. Never
write "same diner as before", "<Picture 1> again", "still in the red dress",
or "continuing from clip 2" - the render cannot see clip 2. Per-clip length:
~250-350 words for a 5s reference clip, 150-250 for a base-mode clip
(consistency beats bulk; the copied block does most of the work).

## Worked example - real cast, photos only

User: "Make a 1 minute mini episode where these two argue about pizza, use
their real looks." ANCHORS: `alex=#1, sam=#2` (web_image fetches).

Plan: 12 clips x 5 s. Write the SHARED BLOCK once (subject_definitions from
the anchors' captions + retention_analysis + the style opening), then ONE
reply where every scene pastes it verbatim:

```
Scene 1 - <one line of story for the user>
<aitools_action skill="image_to_movie" preset="{{Reference To Video (MiniMax H3) 5s.txt}}" chat_image="alex" chat_image2="sam" duration="5" width="864" height="480" anchor="scene1" prompt="subject_definitions:
<Subject 1> is the man in <Picture 1>, mid-30s with short dark hair, a gray hoodie, and a silver watch.
<Subject 2> is the woman in <Picture 2>, early 30s with a blond ponytail, a red flannel shirt, and small gold earrings.

summary:
[reference generation] The target video shows <Subject 1> and <Subject 2> arguing about pizza in a red vinyl diner booth.

retention_analysis:
<Subject 1> (appears in [Shot 1]): fully_preserved - his face, hair, hoodie, and watch are retained.
<Subject 2> (appears in [Shot 1]): fully_preserved - her face, ponytail, flannel, and earrings are retained.

detailed_description:
The target video uses a live-action sitcom style with warm tungsten light in a red vinyl diner booth beside a rain-streaked window.
[Shot 1] A medium two-shot frames <Subject 1> and <Subject 2> facing each other across the formica table, a steaming pizza box between them. He flips the lid open, slides the box toward her, and says 'Pineapple. Deal with it.' in English with a flat New York accent. She plants one palm on the lid, pushes it straight back, and snaps 'Absolutely not.' in a dry deadpan voice. He raises both hands in mock surrender as she pulls her milkshake closer. The camera pushes in with small amplitude at slow speed at table height while rain streaks the window light behind them.

overall_soundscape: Plates clatter softly from the diner counter, rain patters against the window, and the vinyl seat creaks as she leans forward.

non_diegetic_music: N/A"/>

Scene 2 - <one line>
<aitools_action skill="image_to_movie" preset="{{Reference To Video (MiniMax H3) 5s.txt}}" chat_image="alex" chat_image2="sam" duration="5" width="864" height="480" anchor="scene2" prompt="<IDENTICAL subject_definitions + retention_analysis + the same diner/booth style opening, pasted verbatim - then scene 2's own [Shot 1] actions, prose-quoted lines, and camera move, plus the same soundscape/music text>"/>

... scenes 3-12 the same way, each pasting the shared block verbatim ...

<aitools_action skill="stitch_video" chat_images="scene1,scene2,scene3,scene4,scene5,scene6,scene7,scene8,scene9,scene10,scene11,scene12" anchor="pizza_episode"/>
```

## Worked example - real cast with their voices

Reply 1 (fetch voice references; web_video auto-continues):
```
<aitools_action skill="web_video" query="<show> <character A> talking scene" speech="true" duration="5" anchor="a_clip"/>
<aitools_action skill="web_video" query="<show> <character B> talking scene" speech="true" duration="5" anchor="b_clip"/>
```

Continue turn (each scene one rv2v action, then the stitch):
```
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" chat_image="a_clip" chat_image2="b_clip" chat_image3="charA" chat_image4="charB" duration="5" anchor="scene1" prompt="subject_definitions:
<Subject 1> is the woman in <Picture 1>, <a few traits from her caption>.
<Subject 2> is the man in <Picture 2>, <a few traits from his caption>.
<Video 1> and <Video 2> are talking-scene sources for the voices only.
<Audio 1> is the voice-timbre reference for <Subject 1>; <Audio 2> is the voice-timbre reference for <Subject 2>.

summary:
[reference generation + audio reference] The target video shows <Subject 1> and <Subject 2> arguing on a couch, voices styled by <Audio 1> and <Audio 2>.

retention_analysis:
<Subject 1> / <Subject 2>: fully_preserved - faces, hair, and wardrobe retained. <Video 1> / <Video 2> (voice sources): weak_reference. <Audio 1> / <Audio 2>: reference - timbre only, no signal copied.

detailed_description:
The target video uses a warm multi-camera sitcom style in a living room with a brown leather couch.
[Shot 1] A medium two-shot frames <Subject 1> and <Subject 2> on the couch... her voice styled like <Audio 1>, she shouts 'exact line' in English ... his voice styled like <Audio 2>, he mutters 'exact line' ...<continue to ~250 words: actions, one camera move>.

overall_soundscape: Soft apartment room tone with a ticking wall clock.

non_diegetic_music: N/A"/>
... more scenes, each pasting the same subject_definitions/retention/style block verbatim ...
<aitools_action skill="stitch_video" chat_images="scene1,scene2,scene3,scene4" anchor="episode"/>
```

## Worked example - invented characters

User: "Make 5 clips, 10 seconds each, that tell a story about a lighthouse
keeper, then stitch them together into one video." (The user explicitly
asked for 10 s clips, so `duration="10"`; without that ask it would be
`duration="5"`.)

```
Scene 1 - <one line of story text for the user>
<aitools_action skill="generate_image" preset="{{Prompt To Image (Z-Image).txt}}" width="864" height="480" prompt="<full self-contained Z-Image scene 1: the keeper's complete appearance + outfit + setting>"/>
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" chain="true" width="864" height="480" duration="10" anchor="scene1" prompt="<the full three-field H3 document (150-250 words): integrated_multimodal_description: [Shot 1] restating the keeper/outfit/setting + actions + one camera move + a prose-quoted line or explicit no-dialog, then overall_soundscape:, then non_diegetic_music: - see image_to_movie>"/>

... scenes 2-5 the same way ...

<aitools_action skill="stitch_video" chat_images="scene1,scene2,scene3,scene4,scene5" anchor="lighthouse_film"/>
```

Why this shape works:

- The host runs actions in order and PARKS the `stitch_video` until every
  listed clip has finished rendering. You do NOT need `continue`, you do NOT
  need to check CHAT IMAGES for "STILL RENDERING", and you must NOT guess
  future Movie numbers - the anchor names are the only reliable same-reply
  reference.
- Keep the film coherent: per "Every clip prompt stands alone" above, the
  SAME full setting/palette/wardrobe text goes into EVERY prompt and invented
  characters get their complete appearance restated every time (the models
  have no memory between clips). Vary the camera and the beat, not the look.
- Every clip document covers all three audio layers: prose-quoted dialog
  per speaker (never `<d>`/`(S1)` markup - it kills lip sync; ~2.5
  words/sec; or an explicit `No dialog; nobody speaks.`),
  `overall_soundscape:`, and `non_diegetic_music:` (or N/A) - kept
  consistent across scenes so the stitched film's soundtrack doesn't lurch.
  Unstated audio is invented and people mouth gibberish.

Say in the chat text that the finished film will appear as a new Movie once
all clips are rendered; the host posts the stitched Movie bubble by itself.

## Stitching clips that already exist

On a later turn, use the real numbers from CHAT IMAGES:

```
<aitools_action skill="stitch_video" chat_images="4,6,8,10"/>
<aitools_action skill="stitch_video" chat_images="3-12" crossfade="0.5"/>
<aitools_action skill="stitch_video" chat_images="all"/>
```

A listed clip that is STILL RENDERING is fine - the host waits for it. A
still image is not: animate it first (`image_to_movie` with `anchor=`) and
list the anchor instead.

## Rules

- Minimum 2 clips, maximum 60 per action; for more, stitch in batches, then
  stitch the batch results.
- Order in `chat_images` is playback order. Same-reply clips are referenced
  by anchor name, earlier clips by Movie number; never by a predicted number.
- Emit the `stitch_video` AFTER all of its clips' actions in the same reply.
- Do not add `continue` "to wait for the clips" - the stitch waits by itself.
  The user can keep chatting while it waits; Stop or Clear cancels it.
- Do not put `chain="true"` on `stitch_video`; it takes a list, not a chain.
  Later actions in the same reply MAY chain onto the finished film (for
  example `rife_video chain="true"`).
- Crossfades are opt-in (`crossfade="S"`), hard cuts are the default.
