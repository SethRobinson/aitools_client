---
id: video_to_video
summary: Operate on an EXISTING "Movie #N" clip. Two modes - (1) VISUAL-ONLY RESTYLE/EDIT it with Bernini-R, keeping its motion but producing silent output; (2) REFERENCE-generate a brand-NEW MiniMax H3 clip that carries the source's subject/motion/style into a new scene, or creates/replaces dialogue, voice, music, audio, or sound effects. H3 preset: `Reference Video To Video (MiniMax H3) 5s.txt` (already turbo; "high quality" -> the 20-step `Reference Video To Video (MiniMax H3 Quality)` variants; no Cache variant exists); its prompt refers to the source as <Video 1>. For explicit LONG ~15s use the 15s preset; for other lengths use duration="N" (~5-15s). H3 also accepts up to 9 reference photos, a second clip, and up to 3 AUDIO references: audio="N" / audio2 / audio3 point at Audio bubbles (a dropped .wav, generated speech/music) or Movies whose SOUND matters -> <Audio N> tags (clip soundtracks number first, then standalone; up to 3 standalone). Audio refs are STYLE references like video refs - they nudge voice character/music/ambience toward the sample, not exact clones; the words still come from the quoted lines. When the result must keep the SAME people as the source, first extract_still a close-up frame per person (anchored) and stage the stills via chat_image2+ (they become <Picture 1>..); describe people ONLY as they appear in the clip/caption, never from film or actor knowledge. H3 reference prompts are the official SIX-SECTION document (subject_definitions / summary / retention_analysis / detailed_description / overall_soundscape / non_diegetic_music - full spec in image_to_movie): define <Subject N> from the staged assets in subject_definitions, and USE every staged reference by its exact tag (<Video 1>, <Video 2>, <Picture 1>.., <Audio N> in slot order; a clip counts as used via its <Video k> OR its soundtrack's <Audio n>) - the host refuses reference actions whose prompts skip a staged reference's tag. detailed_description targets 350-500 words (~250-350 at 5s) and re-describes the ENTIRE new scene (references pin identity only - never a delta like "the same scene but..."), with dialog as plain prose quoting the exact words and describing the voice around them - he says 'exact line.' in a low gravelly English voice - NEVER <d>[English]...</d> blocks or (S1) IDs (that markup renders as closed-mouth narration; ~2.5 words/sec; a speaker with no quoted line mouths gibberish - or state nobody speaks); ambience goes in overall_soundscape, score in non_diegetic_music (or N/A, or defer a layer to <Audio N>); a VOICE ref carries the voice STYLE only, never the words, and its phrasing must be reference-flavored - "his voice styled like <Audio 2>" + retention "reference" - because copy-flavored wording ("matches <Audio 2>", "the voice from", "reuses", fully_copy) makes H3 splice the sample's actual audio into the clip (observed 2026-08-31); copy wording is only for music/ambience the user asked to keep. Budget the seconds: dialog + described silent action must fill the duration (15s clips end with an explicit silent tail) or gaps grow invented mumbling. Put prompt LAST in the tag. Never use image_to_image on a Movie unless the user explicitly requests one still/current frame and the action has movie_frame="true". For smoothing/FPS use rife_video; for animating a STILL use image_to_movie.
inputs: attachment
autoload: true
triggers: video to video, restyle the video, restyle this clip, edit the video, edit the clip, change the video, change the clip, redo the video, redo the clip, make the video, make the clip, the video but, the clip but, same video but, restyle the clip, turn the video, turn the clip, video into, clip into, re-render the video, regenerate the video, based on this video, based on that video, based on the clip, based on this clip, like this video, like the video, like this clip, same character as the video, from this video, from the clip, restyle the movie, edit the movie, change the movie, redo the movie, make the movie, the movie but, same movie but, turn the movie, movie into, re-render the movie, regenerate the movie, based on this movie, based on that movie, based on the movie, like this movie, like the movie, same character as the movie, from this movie, from the movie, use movie, use the movie, use this movie, use that movie, as reference, as a reference, reference video, reference clip, reference movie
exclude_triggers: animate the image, animate this, make a movie of, make a video of a, turn this image into a video, turn the photo into
template: <aitools_action skill="video_to_video" preset="{{Video To Video (Bernini).txt}}" chat_image="N" prompt="<visual-only changes + motion to keep, 4-8 sentences (Bernini only)>"/>  # Bernini is SILENT. For new/replaced dialogue, voice, audio, music, or sound effects use preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" with the six-section H3 reference document as the prompt (subject_definitions defining <Video 1>/<Picture N>/<Audio N> ... non_diegetic_music; detailed_description ~250-350 words at 5s). chat_image="N" = source Movie. Slot-2+ adds references: Bernini takes one still; H3 takes up to 9 photos and/or a second Movie. Same-people H3 regens: emit extract_still per face (anchored) first, stage the stills via chat_image2+, and describe people only as seen in the clip. Never use an image skill on a Movie unless the user explicitly asks for its still/current frame and supplies movie_frame="true".
---
# Video-to-video (Bernini-R / MiniMax H3)

Use this skill when the user wants to work FROM an EXISTING video clip. It has
two distinct modes - pick by what the user wants:

1. **VISUAL-ONLY RESTYLE / EDIT (Bernini)** - change the clip's style,
   setting, lighting, or visible content while KEEPING its motion and timeline,
   optionally guided by one reference image. Bernini's output is SILENT: it
   cannot create or replace dialogue, voices, music, audio, or sound effects.
   Preset is always `{{Video To Video (Bernini).txt}}`.
2. **REFERENCE-GENERATE (MiniMax H3)** - make a brand-NEW clip that carries
   the source's subject, style, or motion into a NEW scene or action ("make a
   new video based on this clip", "the same cat but chasing a butterfly",
   "another shot of this character"). This is ALSO the required mode whenever
   the user asks to create/replace dialogue, voice, music, audio, or sound
   effects. The output does NOT preserve the source's timeline; it is a fresh
   ~5s generation with native audio. Preset is
   `{{Reference Video To Video (MiniMax H3) 5s.txt}}`, and the prompt MUST
   refer to the source as `<Video 1>`. The source's SOUNDTRACK is a reference
   too: up to 15s of the clip's frames and audio condition the output, so the
   new clip can carry similar music, voices, and ambience - reference the
   soundtrack as `<Audio 1>` when the user wants that ("same music", "keep the
   voices/score", "sounds like the clip"). For an explicitly LONG result
   ("15 second video like this clip") use
   `{{Reference Video To Video (MiniMax H3) 15s.txt}}` (~3x render time).
   For a specific in-between duration ("10 second video like this clip"),
   keep the 5s preset and add `duration="10"` (seconds; snapped to H3's frame
   grid, ~5-15s range). `duration` is ignored on the 15s preset.
   A silent source clip is fine: the host detects it, drops the audio
   reference automatically, and H3 synthesizes a soundtrack from the prompt
   (there is then no `<Audio 1>` to reference).
   The plain preset names above are already turbo (an 8-step Ref2V distill
   is baked in). For high/maximum-quality requests use
   `{{Reference Video To Video (MiniMax H3 Quality) 5s.txt}}` /
   `{{Reference Video To Video (MiniMax H3 Quality) 15s.txt}}` (full 20-step
   render, ~2x time). There is NO Cache variant of any Reference preset -
   never invent such a preset name; if the user asks for the spectrum/cache
   variant on a reference generation, use the plain preset above and tell
   them the cache does not apply here.

   The SAME preset also accepts extra references alongside the clip:
   - **Photo references** (up to 9): stills in `chat_image2`..`chat_image10`
     (existing bubbles or anchors) or `attachment2`..`attachment10` (pasted
     this turn) become `<Picture 1>`..`<Picture 9>` in slot order. This is THE
     way to "put this person into a video like that clip": the clip drives
     motion/camera/audio, the photo locks the person's identity. Multiple
     photos of the SAME person improve the lock, but describe them as ONE
     character (`the man from <Picture 1> and <Picture 2>`), never as two
     people.
   - **A second reference clip**: point `chat_image2` at another MOVIE bubble
     and it becomes `<Video 2>` / `<Audio 2>` (e.g. `<Video 1>` = subject,
     `<Video 2>` = camera style or music source). A movie in `chat_image2`
     always means "second clip"; stills always mean photo references.
   - **Standalone AUDIO references** (up to 3, separate from clip
     soundtracks): `audio="N"` (then `audio2`, `audio3`) points at an
     Audio #N bubble (or a Movie whose SOUND is the reference; numbers or
     anchors). `<Audio N>` tags number the wired audio refs in order - clip
     soundtracks first, then these files. So with one clip staged,
     `<Audio 1>` = the clip's soundtrack and `<Audio 2>` = your first
     standalone file. An audio ref is a STYLE reference, exactly like a
     video ref: it steers voice character, tone, accent, music, or ambience
     TOWARD the sample. It is NOT an exact voice clone and never supplies
     the words - quote each speaker's exact line as always, or H3 invents
     dialog (tested 2026-08-30: it comes out in a random language). The
     PHRASING enforces this: always "her voice styled like <Audio 2>" -
     never "matches <Audio 2>" / "the voice from <Audio 2>" / "reuses
     <Audio 2>", which make H3 splice the sample's actual audio into the
     clip verbatim (observed 2026-08-31). Good jobs for one: a voice style,
     a music bed, an ambience sample.
   Give each reference ONE job in the prompt and unused slots simply don't
   exist - no preset switching needed. EVERY staged reference must appear in
   the prompt as its exact tag at least once (`<Video 1>`, `<Picture 1>`..
   `<Picture N>` for the stills, `<Audio N>` for standalone audio, in slot
   order): the tag is the only link between prose and that reference, and the
   host refuses the action (with a correction turn) when a staged reference's
   tag is missing or the prompt names a tag with nothing staged behind it. A
   staged CLIP counts as used through either its `<Video k>` or its
   soundtrack's `<Audio n>` - a clip staged purely as a voice source can be
   bound with "narrates, his voice styled like <Audio 1>" alone (styled
   like - NEVER "with the voice from" / "matching <Audio 1>"; see the voice
   phrasing rule below).

Rule of thumb: "this video, but different-looking, no sound requested" ->
Bernini restyle. "A NEW video of the same subject/motion" OR any new/replaced
speech/audio/sound -> H3 reference-generate.

If the user only wants smoother playback, interpolation, or higher FPS without
changing the pixels/content, use `rife_video` instead.

If the user instead wants to ANIMATE a still image into a video, that is
`image_to_movie`, not this skill.

## Source selection

The source MUST be a short imported/clipped video. Pick EXACTLY ONE of:

- `chat_image="N"` - the Nth chat bubble that is a VIDEO ("Movie #N" label).
  Use when the user says "restyle the clip you just made" or "edit movie 1" - the
  CHAT IMAGES line in the system prompt shows the reachable Movie numbers. Pointing
  it at a still IMAGE bubble will not work - that is `image_to_movie`, not this skill.
- `chain="true"` - a movie produced by a generate/animate/clip action emitted earlier
  in THIS SAME reply (do not also pass chat_image). Example: clip Movie 1 from 30s
  for 5s, then in the same reply restyle the resulting short clip.

Freshly dropped `.mov` / `.mp4` / `.avi` files are not image attachments. The host
imports them as Movie bubbles. If the user requests a specific segment, call
`clip_video` first, then run this skill with `chain="true"` in the same reply.

## Extra references - faces, characters, looks, second clips

The source MOVIE always stays in `chat_image="N"` (or `chain="true"`); references
ride the SLOT-2+ attributes, NOT the primary one:

- `chat_image2="M"` (then `chat_image3`..`chat_image10`) - existing bubbles
  (number or anchor name).
- `attachment2="M"` (then `attachment3`..`attachment10`) - stills the user pasted
  THIS turn (use `attachment2="1"` for a single pasted face). Do NOT use
  `attachment="2"` / `chat_image="2"` for a reference - those are PRIMARY-source
  attributes and will be ignored here.

What the slots mean depends on the mode:

- **Bernini restyle**: only slot 2 is used, and it must be a STILL. Keep
  `preset="{{Video To Video (Bernini).txt}}"` - the host switches to the
  reference-guided workflow automatically when a reference still is present.
  In the prompt, say what to take from the reference, e.g. "give the runner the
  face and hair of the person in image 2, keeping the original running motion
  and camera exactly."
- **H3 reference-generate**: stills in slots 2-4 are `<Picture 1>`..`<Picture 3>`
  photo references; a MOVIE bubble in `chat_image2` is the second reference clip
  `<Video 2>`. Address them by tag in the prompt and give each one job.

A still the user pastes this turn is auto-detected as a reference even if the slot
syntax is off, but prefer the explicit `attachment2` / `chat_image2` form.

## Keeping the SAME people from the source clip (identity-critical regens)

The clip alone is a WEAK identity lock: H3 reads it mostly for motion, camera,
and audio, and faces drift, especially over 10-15s. When the user wants the
same people/characters to reappear ("more versions with the same actors",
"keep her looking like the original"):

1. Stage photo references. If matching stills already exist in chat (bubbles
   or anchors), put them in `chat_image2+`. If none exist, emit `extract_still`
   actions FIRST - one close-up frame per person, each with an `anchor` - then
   reference them: `chat_image2="man_face"` `chat_image3="woman_face"`. Two or
   three frames of the SAME person strengthen the lock further (describe them
   as ONE character).
2. VERIFY guessed frames before rendering. Extraction timestamps are guesses
   (clip captions have no timecodes), and a frame with the wrong shot or
   nobody in it silently ruins the identity lock. In the extraction reply,
   `inspect_image` each still (last one `resume="true"`) and emit the render
   on the continue turn only if the frames show the right people; re-extract
   otherwise. Extracted stills are also auto-captioned (visible in CHAT
   IMAGES by the next turn) - never reference a still whose caption/inspection
   contradicts its purpose.
3. Defer identity to the tags: "the man from <Picture 1>, exactly as he
   appears there". Keep prose traits minimal and FAITHFUL (see the rule below).
4. Prefer 5s over 10-15s when the user did not ask for length: identity drift
   compounds with duration.
5. For face-critical shots you may raise the canvas: add `width`/`height`
   matching the source aspect - `width="1152" height="640"` landscape,
   `640x1152` portrait, `896x896` square (H3 trained cap 1344x768; default is
   864x480). Roughly 2x render time at 1152x640, so reserve it for identity
   work the user cares about.

**Describe people ONLY from what is visible in the clip and its caption. NEVER
from outside knowledge of the film, show, or actor - even when you recognize
them.** Text contradicting the references loses: inventing "auburn hair" for a
blonde woman, or omitting a red neckerchief the caption mentions, actively
overrides the visual reference and produces a stranger. When unsure of a trait,
leave it out and let the tag carry it.

## Writing Bernini restyle prompts (Bernini ONLY - H3 uses a different format)

Bernini keeps the source video's motion and timing; the prompt describes what
should CHANGE and what should stay. Lead with the transformation (style,
setting, wardrobe, lighting), then state what motion / framing to preserve.
Do not put spoken lines, music, audio, or sound effects in a Bernini prompt;
use the H3 Reference Video To Video preset for those requests.

- 4-8 sentences, single flowing paragraph (this size rule is for Bernini
  only; H3 reference prompts are much longer - see below).
- Restate the visible subject so the edit stays anchored (apparent age, build,
  hair, wardrobe), then state what changes and what motion to preserve.
- Keep it a tight DELTA when the user wants a small change ("only change the
  season to winter, keep everyone's motion and positions identical"); describe
  a full new scene only when they truly want a full restyle.
- Avoid hard-cut words ("suddenly", "cuts to"); v2v preserves the original cut.

## Writing H3 reference prompts (the six-section document)

Every H3 Reference Video To Video prompt is the official six-section
document - the full spec and field rules live in `image_to_movie` ->
"Reference-to-video"; the base shot/camera/dialog rules in "The H3 prompt
format". The v2v-specific parts:

- `subject_definitions`: one line per staged reference. The source clip is
  `<Video 1> is the source video providing <the motion/camera/subject role>.`
  Reusable visible content from it (the cat, the setting) becomes a
  `<Subject N>` (`<Subject 1> is the fluffy orange tabby cat in <Video 1>.`);
  photo refs define subjects from their `<Picture N>` tags; standalone audio
  refs and used clip soundtracks get `<Audio N>` lines naming whose voice or
  which layer they steer.
- `summary`: bracketed task types - a new clip guided by the source is
  `[reference generation]`; add ` + audio reference` when a soundtrack/audio
  ref steers voices or music.
- `retention_analysis`: subjects that must look like their references are
  `fully_preserved`; a source clip used only for camera/pacing is
  `<Video 1> (camera and pacing structure): weak_reference - ...`; audio
  refs are `reference`.
- `detailed_description`: 350-500 words for a full-length clip (~250-350 at
  5s), opening with 1-2 style sentences before `[Shot 1]`, then the ENTIRE
  new scene described from scratch - the clip/photos pin identity and
  motion/camera style only; setting, wardrobe, light, and action all come
  from this text, never "the same scene as the clip but...". All base rules
  apply - prose-quoted dialog with the voice described around it (never
  `<d>`/`(S1)` markup - it renders as closed-mouth narration; ~2.5
  words/sec), one camera move per shot with amplitude + speed,
  second-budgeting with an explicit silent tail on long clips.
- **VOICE refs are STYLE-ONLY - phrasing decides copy vs reference.** H3
  takes copy-flavored wording literally: "her voice matches <Audio 1>",
  "the voice from <Audio 1>", "reuses/copies <Audio 1>", or a
  `fully_copy`/`partially_copy` retention marker on a voice makes it SPLICE
  the reference audio's actual signal into the clip instead of generating
  the new line in that voice (observed 2026-08-31: the render played the
  exact sample). For a VOICE, always and only:
  - subject_definitions: `<Audio 2> is the voice-timbre reference for
    <Subject 1>.`
  - retention_analysis: `<Audio 2>: reference - timbre guides <Subject 1>'s
    delivery without copying the signal.` (voices are NEVER
    fully_copy/partially_copy)
  - detailed_description: `his voice styled like <Audio 2>, he says 'exact
    new line.'`
  - summary task type: ` + audio reference` (never ` + audio reuse` for a
    voice).
- `overall_soundscape` / `non_diegetic_music`: copy-flavored phrasing IS
  correct here, but only for deliberate signal reuse of MUSIC or AMBIENCE
  the user asked to keep (`The copied ambience layer from <Audio 1>
  continues throughout.` / `<Audio 2> is directly reused as the
  audience-only score.` with `fully_copy`/`partially_copy` and ` + audio
  reuse`); otherwise describe the new clip's own sound, `N/A` for no score.

## Invocation examples

Restyle the clip just made (chain):
```
<aitools_action skill="image_to_movie" preset="{{Image To Video (MiniMax H3 Turbo Cache) 5s.txt}}" prompt="<motion beat>" chat_image="2"/>
<aitools_action skill="video_to_video" preset="{{Video To Video (Bernini).txt}}" prompt="Re-render the same clip as a hand-painted watercolor animation, keeping every motion, pose, and camera move identical; soft paper texture, muted autumn palette." chain="true"/>
```

Restyle / edit an existing Movie bubble (most common - e.g. "add a hat to the dog in movie 1"):
```
<aitools_action skill="video_to_video" preset="{{Video To Video (Bernini).txt}}" prompt="Keep the woman's exact motion and the camera push-in, but change the setting from a sunny park to a snowy night street, add falling snow, cool blue moonlight, and a wool coat over her outfit. Preserve her face and build." chat_image="1"/>
```

Reference-guided - put the face/look from a still onto the person in the clip
("make the woman in movie 1 have image 2's face"):
```
<aitools_action skill="video_to_video" preset="{{Video To Video (Bernini).txt}}" prompt="Replace the woman's face and hairstyle with the person in image 2 - olive skin, ~30, long dark hair - while keeping her exact body, walking motion, the beach setting, waves, and golden-hour light unchanged." chat_image="1" chat_image2="2"/>
```

Reference-generate a NEW clip from a movie ("make a new video based on movie 1
where the cat plays in snow") - the flagship example, a complete six-section
document (~230 words; scale detailed_description toward 350-500 for longer
clips):
```
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" chat_image="1" prompt="subject_definitions:
<Subject 1> is the fluffy orange tabby cat in <Video 1>, with amber eyes, a white chest patch, and a ringed tail.

summary:
[reference generation] The target video shows <Subject 1> pouncing at snowflakes in a snowy garden at dusk.

retention_analysis:
<Subject 1> (appears in [Shot 1]): fully_preserved - the tabby's orange coat, white chest patch, ringed tail, and amber eyes are retained.
<Video 1> (subject reference): weak_reference - only the cat is carried over; the setting and camera are new.

detailed_description:
The target video uses a live-action naturalistic style with cool blue twilight and soft falling snow.
[Shot 1] A low shot at cat height frames <Subject 1> crouched on a snow-dusted stone path in a small walled garden, bare rose bushes and a wooden bench behind it. The cat's pupils widen as a large snowflake drifts down; it wiggles its hindquarters twice, pounces with front paws extended, and lands in a shallow drift that puffs powder over its muzzle. It shakes the snow off in a quick full-body ripple that starts at the head and travels down the ringed tail, then bats at another falling flake with one white-tipped paw. The camera trucks right with small amplitude at slow speed, keeping the cat centered as its breath fogs in the cold air and the garden lamps switch on warm in the background. No dialog; no human is present.

overall_soundscape: Soft wind moves through the bare branches, snow crunches under the cat's paws on the pounce and landing, and an excited chirping meow follows the shake.

non_diegetic_music: N/A"/>
```
Six sections, always: define every staged reference in subject_definitions
(the host still requires each staged reference's exact tag - `<Video 1>`,
`<Picture N>`, `<Audio N>` - to appear in the prompt), classify how each is
kept in retention_analysis, and write ALL speech as prose-quoted lines with
the voice described around them (never `<d>`/`(S1)` markup, or state that
nobody speaks). Ambience belongs in overall_soundscape, score in
non_diegetic_music (or `N/A`, or defer a layer to an `<Audio N>` as in the
examples below).

Put a specific person (from a photo) into a new clip guided by the source
("make a video like movie 1 but starring the person in image 3") - skeleton;
write detailed_description out in full (~250-350 words at 5s) like the
flagship above:
```
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" chat_image="1" chat_image2="3" prompt="subject_definitions:
<Subject 1> is the woman in <Picture 1>, with shoulder-length copper hair and a green jacket.
<Video 1> is the source video providing the beachside path, tracking camera, and pace.
<Audio 1> is the synchronized ambience of <Video 1> and is reused in the target video.

summary:
[reference generation + audio reuse] The target video shows <Subject 1> walking the beachside path of <Video 1> at its relaxed pace, keeping the source ambience.

retention_analysis:
<Subject 1> (appears in [Shot 1]): fully_preserved - her face, copper hair, and green jacket are retained.
<Video 1> (path, camera, pacing): weak_reference - setting and camera guide the new clip; the walker is replaced.
<Audio 1>: partially_copy - the waves and gulls carry over beneath the new dialog.

detailed_description:
The target video uses a live-action coastal style with bright mid-morning light.
[Shot 1] A steady tracking shot follows <Subject 1> along the sandy path from <Video 1>... <write the walk, wind in her hair, the smile, then: she says 'What a morning.' in English with a bright warm voice ...one camera move, ~250 words total>.

overall_soundscape: The copied wave wash and distant gulls from <Audio 1> continue throughout, joined by soft footsteps on damp sand.

non_diegetic_music: N/A"/>
```

Two reference clips - subject from one, camera/music from the other: define
`<Subject 1>` (the husky) from `<Video 1>`, add `<Video 2> is the source of
the orbiting drone move.` and `<Audio 2> is the synchronized acoustic track
of <Video 2>, reused as the score.` in subject_definitions; summary
`[reference generation + audio reuse]`; mark `<Video 1> (subject):
fully_preserved`, `<Video 2> (camera structure): weak_reference`,
`<Audio 2>: fully_copy`; then a full detailed_description of the sunflower
run (`No dialog; no human is present.`), overall_soundscape for wind and
paws, and `non_diegetic_music: <Audio 2> is directly reused as the
audience-only score.` Stage as `chat_image="1" chat_image2="4"`.

Per-character voice-STYLE samples via standalone audio refs (two speakers,
each nudged toward their own sample; the clip supplies motion only). The
refs steer voice style, not exact words - each line is still quoted in
full:
```
<aitools_action skill="generate_speech" text="Tonight, I walk the aisles looking for something perfect." voice="chadwick" anchor="dexter_line"/>
<aitools_action skill="generate_speech" text="This one. Perfect." voice="yuki" anchor="akiko_line"/>
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" chat_image="5" chat_image2="dexter" chat_image3="akiko" audio="dexter_line" audio2="akiko_line" width="1152" height="640" prompt="subject_definitions:
<Subject 1> is the man in <Picture 1>, with a trimmed beard and a denim jacket.
<Subject 2> is the woman in <Picture 2>, with a dark bob and a mustard scarf.
<Video 1> is the source video providing the steady market tracking shot.
<Audio 2> is the voice-timbre reference for <Subject 1>.
<Audio 3> is the voice-timbre reference for <Subject 2>.

summary:
[reference generation + audio reference] The target video shows <Subject 1> and <Subject 2> shopping through the market of <Video 1>, their voices styled by <Audio 2> and <Audio 3>.

retention_analysis:
<Subject 1> (appears in [Shot 1]): fully_preserved - beard, denim jacket, and face are retained.
<Subject 2> (appears in [Shot 1]): fully_preserved - bob, scarf, and face are retained.
<Video 1> (camera and pacing): weak_reference - only the tracking move and pace are followed.
<Audio 2>: reference - timbre guides <Subject 1>'s delivery without copying the signal.
<Audio 3>: reference - timbre guides <Subject 2>'s delivery without copying the signal.

detailed_description:
The target video uses a live-action handheld documentary style in a sunny open-air market.
[Shot 1] A steady tracking shot follows <Subject 1> pushing a cart past fruit stalls... his voice styled like <Audio 2>, he says 'Tonight, I walk the aisles looking for something perfect.' in English with an easy conversational pace. <Subject 2> holds up a mango and, her voice styled like <Audio 3>, replies 'This one. Perfect.' in English with a playful tone. ...<continue: stall colors, light, the camera move, ~250 words total>.

overall_soundscape: Ambient market chatter, cart wheels on brick, and paper bags rustling continue throughout.

non_diegetic_music: N/A"/>
```
(The clip's own soundtrack is `<Audio 1>` here, so the staged files land on
`<Audio 2>` / `<Audio 3>`. With no clip staged - e.g. on the photo-only
Reference To Video preset - they would be `<Audio 1>` / `<Audio 2>`. A
dropped .wav / Audio bubble works the same as the generated ones here.)

Same people as the clip, new dialog (identity-critical - extract face refs
first, then regenerate with them): emit the `extract_still` actions, then a
six-section document defining `<Subject 1> is the man in <Picture 1>` /
`<Subject 2> is the woman in <Picture 2>` (traits from the clip/captions
ONLY), `<Video 1> is the source video providing the sunlit field, two-shot
framing, and daylight`, both subjects `fully_preserved`, and a full
detailed_description quoting each speaker's exact line in prose:
```
<aitools_action skill="extract_still" chat_image="1" time="1.0" anchor="man_face"/>
<aitools_action skill="extract_still" chat_image="1" time="2.5" anchor="woman_face"/>
<aitools_action skill="video_to_video" preset="{{Reference Video To Video (MiniMax H3) 5s.txt}}" chat_image="1" chat_image2="man_face" chat_image3="woman_face" width="1152" height="640" prompt="<the six-section document as above>"/>
```

## Rules

- v2v source must be a VIDEO (a "Movie #N" bubble or a chained movie), not a still
  image. If the user only has a still, use image_to_movie.
- Visual-only restyle/edit (keep the timeline, silent result) -> `{{Video To Video (Bernini).txt}}` - the host
  swaps to the reference-guided workflow automatically when you add a `chat_image2`
  still. NEW clip from the source's subject/motion, or any new dialogue/voice/audio/
  music/sound effect -> `{{Reference Video To Video (MiniMax H3) 5s.txt}}` with
  `<Video 1>` in the prompt. Pick by intent; never mix the two presets up.
- To inject a face/character/style from a still: Bernini uses ONE `chat_image2`
  still; the H3 reference presets take up to nine (`chat_image2`..`chat_image10`
  -> `<Picture 1>`..`<Picture 9>`) plus optionally a second MOVIE in `chat_image2`
  (-> `<Video 2>`). The source MOVIE always stays in `chat_image="N"`.
- When the result must keep the SAME people as the source clip, stage photo
  refs (anchored `extract_still` frames or existing stills) in `chat_image2+`
  and describe each person only as seen in the clip/caption - never from
  knowledge of the film or actor. Unfaithful text beats the reference and
  changes the person.
- Every H3 reference prompt is the six-section document with all audio
  layers explicit: prose-quoted dialog per speaker (never `<d>`/`(S1)`
  markup - it renders as closed-mouth narration; or an explicit `No dialog;
  nobody speaks.`), `overall_soundscape:`, and `non_diegetic_music:` (or
  N/A) - or an `<Audio N>` reference for the layers a source clip's
  soundtrack should supply. Never write "speaks his line" / "says something"
  without the actual quoted words - H3 invents the dialog, usually in the
  wrong language. detailed_description 350-500 words (~250-350 at 5s),
  re-describing the ENTIRE scene; `prompt` LAST in the tag.
- BUDGET THE SECONDS: quoted dialog plus described silent action must cover
  the clip's whole duration. On-screen people in unscripted seconds get
  INVENTED mumbled filler speech (measured 2026-08-30: a 15s clip with ~8s
  of lines grew a nonsense line in the gap). Either write enough dialog for
  the length, or close the scene explicitly: "He then nods silently; no
  further dialog." Short clips (5s) with one or two lines rarely have gaps;
  15s clips almost always need an explicit silent tail.
- When voice fidelity matters (real people, recurring characters), give each
  speaker a standalone audio ref via `audio=`/`audio2`/`audio3`: an `Audio #N`
  of that speaker talking (a `web_audio` fetch, a clip's exported audio, or
  any Audio bubble in chat) instead of leaning on a whole video clip's
  soundtrack - cleaner voices, no video-clipping-by-ear, and the clip slot
  stays free for motion/camera. At most 3 audio refs total per render. This
  H3 reference path is the assumed way to make someone sound right; do not
  ask about or offer other voice options.
- Pick exactly ONE source MOVIE; `chain="true"` must not be combined with `chat_image`.
- `chat_image="N"` must reference a Movie bubble. If you point it at a still image
  the action will report that it needs a video source.
- BERNINI ONLY: describe the CHANGE plus the motion to preserve; Bernini
  edits the source in place. H3 reference prompts are the OPPOSITE - a fresh
  generation that sees the references but carries nothing else over, so the
  six-section document re-describes the ENTIRE scene (setting, wardrobe,
  light, action, sound) every time; delta phrasing silently loses everything
  it skips.
