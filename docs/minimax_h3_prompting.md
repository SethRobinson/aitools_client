# MiniMax H3 prompting

How AI Chat skills (and the recommended CLI style) write H3 prompts. Plumbing,
presets and executor wiring: `docs/minimax_h3.md`. The skill text that carries
these rules: `aichat/skills/image_to_movie.md` (the hub: full base + reference
spec), `generate_movie.md`, `video_to_video.md`, `image_to_image.md` (Reference
To Image stills), `stitch_video.md` (per-clip targets + verbatim shared blocks),
`scenario_storytelling.md`, and `aichat/main_prompt.txt` (PER-SKILL PROMPT
LENGTH, VIDEO DIALOG RULE, the multi-line-prompt action-protocol note).

All of these are skill-text rules. Per Seth's standing rule, do NOT add
deterministic executor gates for prompt quality (a gate for "voice ref without
quoted words" was built and removed at his request; do not reintroduce it).

## The official format, minus `<d>` dialog markup

Every H3 prompt is MiniMax's official structured prompt document (source: the
publisher's `skills/h3-prompt-writing` skill, `references/base-en.txt` +
`ref-en.txt`), not a free paragraph. Thin prompts render flat; under ~100 words
is too sparse.

- **Base modes (t2v, i2v start-frame)**: three labeled fields in the one prompt
  string: `integrated_multimodal_description:` (`[Shot 1]` opens with style +
  composition, no timestamp; later shots `[Shot N] At MM:SS.mmm, the camera cuts
  to ...` strictly increasing; ~1 cut per 3s, each shot >= ~3s),
  `overall_soundscape:` (1-4 sentences, ambience/physical/non-verbal only),
  `non_diegetic_music:` (1-3 sentences, or `N/A`). i2v prompts refer to the
  source frame as `<Picture 1>` in Shot 1. The official I2VA "alignment line"
  prefix is NOT used (visually neutral in A/B; ComfyUI's i2v node binds the
  start frame itself).
- **Reference modes (Reference To Video / Reference Video To Video / Reference
  To Image)**: six sections in order: `subject_definitions` (defines
  `<Subject N>` FROM the staged `<Picture N>`/`<Video N>`/`<Audio N>` assets;
  this satisfies the executor's tag gate), `summary` (bracketed task types, e.g.
  `[reference generation + audio reference]`), `retention_analysis` (visible:
  fully_preserved / partially_preserved / attribute_transfer / weak_reference;
  audio: fully_copy / partially_copy / reference / weak_reference),
  `detailed_description` (style opening BEFORE `[Shot 1]`), `overall_soundscape`,
  `non_diegetic_music`.
- **Dialog is plain prose, never `<d>`/`(S1)` markup**: the official `(S1)`
  speaker IDs + `<d>[Language] ...</d>` blocks (and `<scenetrans>`/`<cutoff>`)
  render the line as off-screen NARRATION under ComfyUI: correct audio, but the
  on-screen mouth never moves (4/4 failed vs 3/3 synced with prose in A/B; the
  document format itself is fine). Write `she says 'We open in five minutes.' in
  English with a warm calm voice`; voiceover in prose (`says in an off-screen
  voiceover ... lips remain completely closed`). On-screen text verbatim in
  double quotes (`&quot;` inside the action attribute; the parser decodes XML
  entities).
- **Camera**: natural sentences, motion type + amplitude + speed ("pushes in
  with small amplitude at slow speed"); 14-entry official motion vocabulary.

## Audio and dialog rules

- H3 ALWAYS generates a soundtrack, and an on-screen person whose line is never
  specified mouths GIBBERISH. Every H3 prompt carries three audio layers: WHO
  speaks and their EXACT quoted words (or `No dialog; nobody speaks.`), ambience
  (`overall_soundscape`), and music or `N/A` (`non_diegetic_music`). Reference
  prompts may defer a layer to `<Audio N>`.
- **Fill the clip with dialog**: ~2.5 spoken words/second; target 10-13 words
  per 5s (22-26 / 10s, 32-38 / 15s). A lone short line gets LOOPED, prefixed with
  an invented line, or replaced by gibberish; unbudgeted seconds grow invented
  mumbled filler. Dialog plus described silent action must cover the full
  duration, with an explicit silent tail on long clips. Silence with people on
  screen is a last resort (even an explicit "nobody speaks" on a close standoff
  can produce an invented line).
- When two people are on screen, use a sequential two-line exchange, one speaker
  at a time, speaker's mouth in frame (lines delivered on a hands/eyes-only shot
  looped or were invented). Prefer plain `says 'line' in English with a <voice>
  voice`; expression verbs (`sneering 'line'`) and stacked vocal adjectives
  correlated with failures. Apostrophes inside single-quoted lines are fine.
- The turbo Ref2V distill is NOT a speech-gibberish source: with exact quoted
  dialog every turbo/Quality/step/shift/LoRA variant tested was word-perfect.
  Don't spend 20 steps to "fix" dialog. If filler still bites on 15s dialog
  scenes, route them to the Quality presets (20 steps had real margin there).
- A voice/audio ref with NO quoted words yields invented dialog in a random
  language: H3 never lifts WORDS from an audio ref, only timbre.
- **Voice-ref phrasing decides copy vs style (Seth's test)**: copy wording on a
  voice ("her voice matches <Audio 1>", "the voice from <Audio 1>", "reuses",
  `fully_copy`/`partially_copy`) makes H3 SPLICE the sample's actual audio into
  the clip instead of speaking the new line. Canonical voice phrasing:
  subject_definitions `voice-timbre reference for <Subject N>`,
  retention_analysis `reference - timbre only, signal not copied`,
  detailed_description `styled like <Audio N>`, summary `+ audio reference`.
  Copy wording is reserved for MUSIC/AMBIENCE the user asked to keep.
- For per-speaker voice style, prefer standalone audio refs (`audio=`) over
  whole-clip soundtracks.
- Verify dialog changes by TRANSCRIBING: extract audio (`ffmpeg -vn -ac 1 -ar
  16000`) and POST it to the Settings > Web speech-to-text endpoint (the
  Whisper-compatible API `web_video speech="true"` uses; fields `file`, `model`,
  `language=en`). Never judge by ear or by reading the prompt.

## Content rules

- **Whole-scene rule (Seth)**: every H3 document re-describes the ENTIRE scene;
  H3 carries nothing between videos. "Only describe the changes" is strictly a
  Klein/Bernini EDIT convention.
- Describe people ONLY as they appear in the source clip/caption or photo refs,
  never from film/actor world knowledge: with no negative prompt, unfaithful
  prose beats the visual reference. Defer identity to `<Picture N>` tags and
  keep prose traits minimal.
- **Word targets**: 150-250 words for a 5s single-scene base clip; 250-450 for
  10-15s/multi-shot; reference `detailed_description` officially 350-500 words
  (~250-350 at 5s; dialog-dense clips prioritize fitting the spoken timeline);
  Reference To Image stills ~120-250 words with both audio sections `N/A`.
- Multi-clip films: write the shared `subject_definitions`/style block once and
  paste it VERBATIM into every clip's document (consistency beats bulk). 5s is
  the default film clip length.
- Multi-line `prompt="..."` attributes (blank lines, inner tags, unescaped inner
  quotes, streamed in small chunks) survive `SkillActionParser`
  byte-identically, and CLI `--dry-run` delivers the document intact into the
  H3 node.
