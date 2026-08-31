# MiniMax H3 (video generation)

Deep-dive for the MiniMax H3 model family in this app. Read this before touching any
H3 workflow, preset, or the AI Chat video reference plumbing. Keep it current per
AGENTS.md's "Keeping this file current" section.

## Model facts

Open-weights omni-modal video model, ComfyUI 0.30+ native. Output is 24fps with
native stereo audio (dialog in 11 languages), no RIFE in the output path.

- Two checkpoints, different node classes, NOT combinable:
  - **FL2VA** (`minimax_h3_fl2va_pruned_int8_convrot.safetensors`) - text/image to
    video via `MiniMaxH3ImageToVideo` (optional `first_frame` / `last_frame` inputs).
    This is the only way to pin an exact start frame.
  - **Ref2VA** (`minimax_h3_ref2va_pruned_int8_convrot.safetensors`) - reference-
    conditioned generation via `MiniMaxH3ReferenceToVideo`. Accepts MIXED references
    in one run: up to 9 images (`ref_images.ref_image_N`), 3 videos
    (`ref_videos.ref_video_N`), 3 clip soundtracks (`ref_video_audios.ref_video_audio_N`
    - index-PAIRED with the same-numbered `ref_video_N` and SILENTLY IGNORED without
    it, verified in `comfy_extras/nodes_minimax_h3.py` 2026-08-30), and 3 standalone
    audio refs (`ref_audios.ref_audio_N` - the group for wav/music/voice-style
    samples). No first-frame pinning. Prompt tags are per-type in connection order:
    `<Picture 1>`.., `<Video 1>`.., and `<Audio j>` numbers each clip soundtrack
    (emitted just before its video) then the standalone audios - give each
    reference ONE job (video = motion/camera, audio = voice/music style, picture =
    identity/setting).
  - "Exact start frame + specific person" therefore needs the two-stage recipe
    (Klein 2-input person insert -> `Image To Video (MiniMax H3)`); documented in
    `aichat/skills/image_to_movie.md`.
- Shared: `CLIPLoader` `qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors` (type
  `minimax`), `minimax_h3_video_vae_fp16` + `minimax_h3_audio_vae_fp32`,
  `res_multistep`/`simple` 20 steps via `BasicGuider`. There is NO negative-prompt
  path, so H3 presets have no `default_negative_prompt` block.
- `length` is a 24fps frame count on the 17k+5 grid (5, 22, 39, ... - 124 = ~5s,
  trained max 362 = ~15s). Any grid step renders: 5 frames is the still-image
  packet the Reference To Image presets use, and short movie lengths work too
  (verified 2026-08-31). Default canvas 864x480 (~0.4MP, ComfyUI template default);
  trained max ~1.03MP (768 short edge, cap 768x1344, multiples of 32). Pixel count
  is the dominant cost knob.
- Reference clips are consumed at 24fps, 2-15s (`force_rate: 24`,
  `frame_load_cap: 360` in the VHS loader).
- Graphs include kjnodes' `MiniMaxH3MemoryEfficientSageAttentionPatch`, which needs
  the `sageattention` pip package on the server (bypass/delete to run without).
- H3 model files may be installed on only some of the configured ComfyUI servers
  (which ones locally: `agents_secret.md`).

### Turbo distill LoRA (the default FL2VA path since 2026-08-10)

- The default i2v/t2v presets run larryvrh's `MiniMax-H3-Turbo-Lora`
  (`minimax_h3_turbo_v4_step600_ema.safetensors`, strength 1.0) at 8 steps /
  `simple` scheduler instead of 20-step `res_multistep`: ~2.2x faster with
  start-frame pinning, identity, and stereo audio intact in same-seed A/B.
  4 steps works (~4x) but risks motion smear on fast action; 6-8 is the safe band.
- Server needs the `Larryvrh/ComfyUI-MiniMax-H3-Turbo` custom nodes: a
  `MiniMaxH3TurboLoRA` loader (bypass mode default; `low_vram`=merge for OOM) and a
  `MiniMaxH3TurboSampler` (dual-clock Euler; replaces `KSamplerSelect` into
  `SamplerCustomAdvanced`, fixes 4-step audio noise). Local install
  status and file locations: `agents_secret.md`.
- **The FL2VA turbo node cannot run on Ref2VA** (tested 2026-08-10): its
  time-conditioning reinjection for pruned bases (`__init__.py` forward, the
  `h3_silu_temb_grid` replay) assumes FL2VA's 2-stream latent packing and crashes
  on Ref2VA's 3-stream ("size of tensor a (3) must match b (2)"), in both bypass
  and merge modes. That limitation is the CUSTOM NODE only, not LoRA-on-Ref2VA in
  general: plain core `LoraLoaderModelOnly` patches attach to the Ref2VA
  int8-convrot checkpoint cleanly (verified 2026-08-27 - adapters trained on the
  bf16 FL2VA base attached 208 patches with zero `lora key not loaded` warnings
  and sampled normally). Ref2VA turbo distills now exist upstream, both loading
  through the plain core LoRA node with no custom nodes: lightx2v's 4-step Ref2V
  distill (released 2026-08-13; `lightx2v/Minimax-h3-Turbo` ->
  `minimax_h3_ref2v_turbo_4step_v0.1_comfyui_bf16.safetensors`, 1.82 GB, trained
  at 544p, community sharpness comfort zone 6-8 steps) and alibaba-pai's PDD
  8-step (`alibaba-pai/MiniMax-H3-Acc-LoRAs` ->
  `MiniMax-H3-Ref2VA-Acc-8Step.safetensors`, 1.37 GB). The lightx2v distill is
  wired in as the DEFAULT reference path since 2026-08-27 (see Workflows /
  Presets below); the alibaba-pai one is untried.
- The `SpectrumApplyMiniMaxH3` cache node (xmarre/ComfyUI-Spectrum-MiniMax-H3;
  must be installed on the server) stacks with turbo for ~1.4x more (50s vs 70s per 5s clip on a
  Blackwell) with A/B-identical frames; shipped as the `(MiniMax H3 Turbo Cache)
  5s` presets, which are AI Chat's DEFAULT i2v/direct-t2v route since 2026-08-17
  ("high quality" -> the 20-step Quality presets; explicit no-cache requests ->
  the plain turbo presets). The node is not lossless and has an audio-stutter
  history - ear-test dialog when a clip sounds off.
  Kijai's `SolAttnPatch` sparse attention was also tested and was NOT faster than
  the sage patch on the RTX PRO 6000s (72s vs 70s, defaults); node left installed,
  not used by any workflow.

### Measured costs (RTX PRO 6000 Blackwell, uncontended unless noted)

5s @864x480, warm server, CLI wall-clock (2026-08-10):

- i2v/t2v turbo 8-step (the DEFAULT presets): **~70s** Blackwell, **~122s** A100.
- i2v/t2v 20-step (`Quality` presets): ~163s Blackwell (~2.5 min), ~1.8x that on
  an A100 (~4.5 min). t2v within ~5% of i2v; "slow i2v" = oversized canvas or
  slower GPU, not the task type.
- i2v turbo 8-step + Spectrum cache (the AI Chat default): ~50s Blackwell.
- Cold-start adders the first time a server touches H3: ~20 GB checkpoint stage
  (tens of seconds) + LoRA load a few more.
- r2v turbo 8-step, 1 photo (the DEFAULT reference tier since 2026-08-27):
  ~64-68s server-side Blackwell (~6.6-7.0 s/step), ~124s A100 (that one
  included a cold Ref2VA checkpoint stage) - in line with i2v turbo, ~3.7x
  faster than the 20-step reference render. rv2v turbo unmeasured; expect the
  same ratio over its 20-step figure.
- Reference per-step cost tracks TOTAL token count, not just the canvas: the
  same r2v turbo run at 1152x640 measured 12.6 s/step (~113s), and a
  full-resolution reference photo adds more on top (`ref_image_size: match`
  scales each ref toward the canvas AREA, so a big source photo carries ~3x
  the ref tokens of a 512x512 one; observed 14.6-14.8 s/step, ~136-166s, for
  1-photo 1152x640 requests). A "reference render took 2x as long" report is
  almost always a bigger request (canvas/duration/refs), not a slower
  workflow - AI Chat's skill docs deliberately raise identity-critical
  reference renders to 1152x640.
- Single-clip rv2v 5s (20-step, no turbo):
  242s (~4 min), ~1.6x plain 20-step i2v - reference tokens ride every sampling
  step; 359s measured with the Ref2VA checkpoint cold-loading. 15s rv2v: measured
  ~51 min once but CONTENDED (shared host); treat as "much worse than 3x the 5s
  cost" and re-measure before quoting.
- Two-clip rv2v: unmeasured; expect well above single-clip rv2v.

## Workflows (`ComfyUI/`)

- `img_to_video_minimax_h3_turbo.json`, `text_to_video_minimax_h3_turbo.json` -
  FL2VA + turbo LoRA, 8 steps; what the DEFAULT i2v/t2v presets run. Same graphs
  as the base versions plus `MiniMaxH3TurboLoRA` (between `UNETLoader` and the
  sage patch) and `MiniMaxH3TurboSampler` (replacing `KSamplerSelect`). They keep
  the literal `"length": 124` and all placeholders, so the AI Chat
  duration/dimension overrides work unchanged.
- `img_to_video_minimax_h3_turbo_cache.json`, `text_to_video_minimax_h3_turbo_cache.json` -
  turbo + `SpectrumApplyMiniMaxH3`; run by the Turbo Cache presets, AI Chat's
  default i2v/t2v route.
- `img_to_video_minimax_h3.json`, `text_to_video_minimax_h3.json` - FL2VA,
  20-step; used by the `(MiniMax H3 Quality)` presets.
- `ref_multi_to_video_minimax_h3.json` - **the universal Ref2VA graph** used by all
  reference presets. Carries EVERY loader the app can wire; unused ones are pruned
  at submit time (below):
  - `<AITOOLS_INPUT_1>` clip 1 (`VHS_LoadVideoPath`) -> `ref_video_0` + `ref_video_audio_0`
  - `<AITOOLS_INPUT_2>` clip 2 -> `ref_video_1` + `ref_video_audio_1`
  - `<AITOOLS_INPUT_3>`..`_11` photos 1-9 (`VHS_LoadImagePath`) -> `ref_image_0..8`
    (the node's full 9-image capacity)
  - `<AITOOLS_INPUT_12>`..`_14` standalone audio refs 1-3 (`VHS_LoadAudio`, since
    2026-08-30) -> `ref_audios.ref_audio_0..2`. Wav/mp3/mp4 all decode. MUST be
    this group: the first wiring shipped them into `ref_video_audio_2..4`, which
    is index-paired with `ref_videos` and the node silently ignored them (fixed
    2026-08-30 after a "didn't use my wav" report - the renders were literally
    unconditioned by the audio). `<Audio N>` numbers clip soundtracks first, then
    standalone.
  To run it manually in ComfyUI, delete the loaders you aren't using.
- `ref_multi_to_video_minimax_h3_turbo.json` - the SAME universal Ref2VA graph
  plus lightx2v's Ref2V turbo distill; run by the DEFAULT reference presets
  since 2026-08-27. Deltas from the 20-step graph, mirroring lightx2v's
  official example workflow: `LoraLoaderModelOnly` (the distill LoRA,
  strength 1.0 - plain core node, no custom nodes) between `UNETLoader` and
  the sage patch; `MiniMaxH3SigmaShift` (video 12 / audio 3, core since
  ComfyUI 0.31) on the GUIDER branch only, so `BasicScheduler` still emits
  unshifted sigmas; sampler `euler`/`simple` at 8 steps (the distill targets
  4 NFE, community sharpness comfort zone is 6-8). Loaders, placeholders, and
  submit-time pruning are identical to the 20-step graph.
- `ref_multi_to_img_minimax_h3_turbo.json` - still-image variant of the turbo
  Ref2VA graph (added 2026-08-30): same loaders/placeholders/pruning, but
  `length` is a literal 5 (H3's minimum packet on the 17k+5 grid, latent
  T=2), the audio latent is sampled but never decoded (the audio VAE stays
  loaded - the node requires the input - there is just no `VAEDecodeAudio`),
  and a `SaveImage` with an `AITOOLS_UNIQUE_ID` filename prefix replaces
  CreateVideo/SaveVideo, saving all 5 decoded frames. API-format file (like the
  BiRefNet workflow), so no conversion/cached copy is generated. Run by
  `Reference To Image (MiniMax H3)`. `ref_multi_to_img_minimax_h3.json` is the
  20-step quality variant (res_multistep, no distill LoRA, no sigma shift), run
  by `Reference To Image (MiniMax H3 Quality)`; gitignored
  `test_ref_multi_to_img_minimax_h3_turbo.json` / `test_ref_multi_to_img_minimax_h3.json`
  mirror the machine-local test_ video reference stack (details:
  `agents_secret.md`). All four verified live at length 5 on 2026-08-30/31.
- `ref_to_video_minimax_h3.json`, `ref_video_to_video_minimax_h3.json` - legacy
  single-reference Ref2VA graphs, no longer referenced by any preset; kept as clean
  manual-use ComfyUI workflows.
- rv2v audio requirement is structural: a linked VHS audio output hard-aborts on a
  silent source (`VHS failed to extract audio from <file>`, pre-sampling) - the app
  avoids it by pruning the audio input (below); with the audio group unwired H3
  synthesizes the soundtrack from the prompt (r2v proves the groups are independent).

## Submit-time graph pruning (the mechanism that makes ONE workflow serve all combos)

- Presets mark reference slots `@upload|source|inputN|optional|`. At parse time
  (`PicMain.cs` upload branch + `IsUploadSourceAvailable`), an optional slot whose
  source isn't wired is skipped: no upload job, placeholder left unfilled, and
  `PicJob._allowInputPruning` set.
- `PicTextToImage.PruneWorkflowInputs` (called on the parsed API JSON right before
  submit) then removes any node whose string inputs still contain `<AITOOLS_INPUT_`,
  cascade-removes inputs referencing removed nodes, applies `@prune_input|<name>|`
  directives (removes that named input on any node), and renumbers autogrow inputs
  (`group.item_N`) so indices stay contiguous. Gated on `_allowInputPruning` or a
  prune directive - workflows without optional slots keep the old "leftover
  placeholder reaches the server" behavior. Result is visible in
  `comfyui_workflow_to_send_api.json`.
- `@prune_input` needs no parse-stage support (unknown directives flow through
  `job._data`); the AI Chat executor appends it via `PicMain.AddWorkflowDirective`
  (one-shot, rides the next workflow line like the dimension overrides).
- Silent clips: the executor ffprobes each wired clip (`FfmpegTool.VideoInfo.HasAudio`)
  and appends `@prune_input|ref_video_audios.ref_video_audio_N|` for silent ones -
  per-clip, automatic, no preset variants. A manual GUI run (preset on a silent
  movie pic) still hits the VHS abort. The CLI auto-detects too (bundled
  `utils/ffmpeg/bin/ffprobe.exe` on Windows, PATH `ffprobe` on Linux) and prunes
  with a message; `--no-clip-audio N` is the manual fallback when ffprobe is
  missing.
- CLI mirror (full H3 support since 2026-08-14, see `cli/README.md` "Generating
  movies"): `cli/workflow.py prune_unfilled_inputs` + `prune_named_inputs`
  (`@prune_input` directive + `--prune-input` flag), optional-aware
  `cli/presets.py` / `aitools_cli.py`. Repeatable `-i` fills imageN slots in
  declared order (all 9 photo refs reachable; `-i2`..`-i10` bind exact slots),
  `--video`/`--video2` supply both clips, and repeatable `--audio` (or
  `--audio2`/`--audio3`) fills the standalone audio refs (each file is
  ffprobe-checked for an audio stream; a post-prune count > 3 wired
  `ref_video_audios.*` inputs is a hard error). `--width`/`--height` (snap /32, clamp
  256..2048, >1.03MP warning) and `--duration` (nearest 17k+5 grid step, min 5
  frames, no clamp; works on 5s and 15s H3 presets, refused only on non-H3
  frame cadences; synthetic length replace on the rv2v 5s preset) are
  HARD errors if their @replace can't apply. Start-frame presets (image1 ->
  input1 + "video" workflow) auto-fit the canvas to the -i image's aspect at
  the preset's pixel budget (`--no-aspect-fit` disables). `AITOOLS_UNIQUE_ID`
  is substituted per run (`cli_<timestamp>_<rand>`). `--dry-run` builds the
  final API JSON offline (needs the cached API version on disk) and writes
  `<output>.api.json`. Temp-slot sources remain unsupported.

## Presets (`Presets/`)

FL2VA presets:

- `Image To Video (MiniMax H3 Turbo Cache) 5s.txt` /
  `Prompt To Video (MiniMax H3 Turbo Cache) 5s.txt` - turbo + Spectrum cache
  (i2v and direct t2v); AI Chat's DEFAULT video route since 2026-08-17
  (image_to_movie / generate_movie skills). Cache runs report 16 progress
  steps (8 real sampler steps + 8 cheap transformer-free replay ticks from
  Spectrum's default offline-smoothing two-pass mode) vs 8 on the plain turbo
  presets - that step count is the quickest tell it ran; render time (~50s vs
  ~70s per 5s clip on a Blackwell) and `SpectrumApplyMiniMaxH3` in
  `comfyui_workflow_to_send_api.json` confirm.
- `Image To Video (MiniMax H3) 5s.txt` / `15s.txt`, `Prompt To Video (MiniMax H3) 5s.txt` -
  the plain `*_turbo.json` workflows (8-step turbo LoRA, no cache). AI Chat
  routes here only on explicit no-cache requests; the 15s preset also serves
  explicit long-clip requests (there is no 15s cache variant).
- `Image To Video (MiniMax H3 Quality) 5s.txt` / `15s.txt`,
  `Prompt To Video (MiniMax H3 Quality) 5s.txt` - the full 20-step render (~2x
  plain turbo, ~3x the cache default); skills route "high quality" /
  "maximum quality" requests here.

Six reference presets, split across the universal workflow pair (since
2026-08-27 the plain names are TURBO defaults, mirroring the FL2VA family):

- `Reference Video To Video (MiniMax H3) 5s.txt` / `15s.txt` and
  `Reference To Video (MiniMax H3) 5s.txt` - the DEFAULTS, running
  `ref_multi_to_video_minimax_h3_turbo.json` (lightx2v Ref2V distill, 8 steps).
- `Reference Video To Video (MiniMax H3 Quality) 5s.txt` / `15s.txt` and
  `Reference To Video (MiniMax H3 Quality) 5s.txt` - the full 20-step graph
  (`ref_multi_to_video_minimax_h3.json`, ~2x the turbo time); skills route
  explicit high/maximum-quality reference requests here. There is deliberately
  NO cache variant of any reference preset.
- `Reference To Image (MiniMax H3).txt` (+ `Quality`, + `test_` pair, since
  2026-08-30/31): a STILL IMAGE from 1-9 photo refs via the 5-frame packet
  workflows above - H3 as a reference-conditioned image generator (the
  reference capability Z-Image lacks until Z-Image-Edit ships). Same r2v slot
  layout (photo 1 required -> input3, photos 2-9 + audio1..3 optional),
  `%vid_width%`/`%vid_height%` (CLI --width/--height apply; no `%vid_length%`,
  duration overrides can't attach by design). Default canvas 864x480 since
  2026-08-31, matching the workflow literals like every other H3 preset (the
  original 1152x640 default made the preset's own width/height replace
  non-identity, the trap behind the 1024x1024 override bug); omitted dims
  refit the budget to photo 1's aspect, and the skill text tells the model to
  raise dims (e.g. 1152x640) for identity-critical faces. Verified on a Blackwell: 1-ref identity transfer, 2-ref
  composites, the 20-step Quality graph, and the test_ stack all
  sharp and faithful; ~22-33s warm wall-clock including CLI upload/download
  (turbo and Quality cost nearly the same at 5 frames); frames within the
  packet are near-identical, so the first saved frame is the output (the CLI
  writes the rest as `_2.._5`). 5 frames is out of distribution for H3
  (trained on ~5-15s clips) - watch harder prompts for degradation.
  **AI Chat wiring**: this is the DEFAULT `image_to_image` preset for a NEW
  image FEATURING existing chat people/anchors/references ("them together",
  group photos, variations, re-poses; "high/maximum quality" -> the Quality
  preset); Klein N-Input remains for in-place edits that must preserve the
  source's composition, logo flows, and exact start-frame builds (rules:
  `aichat/skills/image_to_image.md`, cheat sheet in `main_prompt.txt`).
  `SkillActionExecutor.IsReferencePhotoPreset` matches BOTH "Reference To
  Video" and "Reference To Image", so the still presets get the reference-tag
  gate, `audio=` slots, and exact-dims pass-through on image_to_image exactly
  like r2v gets on image_to_movie.
- Slot layout (same for both tiers): rv2v = clip required
  (`@upload|video|input1|`), then optional `video2`->input2 and `image2..image10`
  ->inputs 3-11 (photo refs 1-9); r2v = photo 1 required (`image1`->input3),
  photos 2-9 (`image2..image9`->inputs 4-11) optional, both video loaders prune
  away. All six presets also declare optional `audio1..audio3`->inputs 12-14
  (standalone audio refs; `PicMain.m_pendingAudioUploadPaths` via
  `SetPendingAudioUpload`, upload sources `audio1`..`audio3`).
- The rv2v 5s presets deliberately have NO length `@replace` (opts out of AI
  Chat's video_to_video source-duration override, which uses WAN's 16fps/4n+1
  cadence - wrong for a reference generation); the 15s presets' `124 -> 362`
  replace runs before appended directives, so the duration override targets
  literal 362 on "15s"-named presets.
- Video presets use `%vid_width%`/`%vid_height%`/`%vid_length%` because chained
  presets share one PicMain variable scope (see AGENTS.md job-script rules).
- Preset names must keep the `"Reference Video To Video"` substring: the executor
  uses it to skip the Bernini restyle auto-select and to enable H3 reference
  behavior. Don't end preset names in `<digit> Input` (DowngradePresetToInputCount
  regex).

## AI Chat wiring (`SkillActionExecutor.cs`)

- Movie-aware routing: when the newest live chat medium is a Movie, deictic
  edit phrases such as "change this scene" and speech/audio edits auto-load
  the full `video_to_video` skill even if the user never says video/clip/movie.
  The volatile CHAT IMAGES context also states that Movie edits stay
  video-native.
- A Movie may feed `image_to_image` / `image_to_movie` only with explicit
  `movie_frame="true"`, reserved for user-requested still/current-frame work.
  Otherwise the executor blocks the action and requests an automatic
  correction turn instead of silently snapshotting the Movie into a still.
- Bernini v2v output is silent. If a Bernini/default `video_to_video` action's
  prompt requests new dialogue, voice, music, audio, or sound effects, the
  executor blocks it and auto-continues with instructions to use H3 Ref2VA.
- `video_to_video` + a "Reference Video To Video" preset (`isH3RefVideoPreset`):
  - Primary clip: `chat_image="N"` Movie bubble (or `chain="true"`) ->
    `m_pendingVideoUploadPath` / prevPic's movie, as before.
  - `chat_image2` pointing at a MOVIE bubble = second reference clip: byte
    resolution for slot 2 is skipped (bytes would be the poster/placeholder
    texture) and the path goes to `PicMain.m_pendingVideoUploadPath2`
    (`@upload|video2|`; no m_picMovie fallback - the pic's own movie is clip 1).
    A still in `chat_image2` (and `attachment2` always) = photo reference.
  - Stills in slots 2-10 land in `PicMain.SetExtraInputImage(slot, ...)` ->
    inputs 3-11 -> `<Picture 1..9>` in slot order (pruning renumbers any gaps).
  - Rescue: turn attachments are adopted as photo refs (slots 2-3) when the model
    forgot the slot attributes, mirroring the Bernini rescue.
  - `WarnUnconsumedExtraInputSlots` compares the action's staged slots against the
    resolved preset's `@upload|imageN|` lines and system-injects a warning when a
    slot has no consumer, so over-slotted actions fail loudly instead of silently
    dropping references (the pre-9-slot `chat_image4` incident).
  - Reference-tag gate (2026-08-27, `BlockH3ReferencePromptTagMismatch`): on any
    Reference preset (photo or clip, chained or not, matched on the RESOLVED
    name) the executor requires the prompt to use EVERY staged reference's tag -
    `<Video 1..2>` / `<Picture 1..N>` / `<Audio N>` in slot order, whitespace
    between word and number, case-insensitive - and to name no tag with nothing
    staged behind it. A mismatch blocks BEFORE the render queues and injects a
    slot->tag map plus a correction turn (`RequestContinueTurn`), because prose
    like "the blond woman" over untagged photos kept rendering different people
    (3-attachment test, 2026-08-27). Since 2026-08-30 a staged CLIP counts as
    bound via its `<Video k>` OR its soundtrack's `<Audio n>` tag ("narrates
    with the voice from <Audio 1>" is legal alone - the old gate pushed models
    off the audio tag, seen in the Dexter test), and every STANDALONE audio ref
    must appear as its own `<Audio n>`. Correction notes translate attachment
    refs to their paste's chat_image bubble (attachments don't survive continue
    turns); a blocked chained action is told to re-point at the chain Pic's
    bubble number.
  - Standalone audio refs (2026-08-30): `audio="N|anchor"` / `audio2` / `audio3`
    on any Reference-preset action stage an Audio bubble's original sound file
    (or a Movie's clip - the soundtrack is the reference) into
    `PicMain.SetPendingAudioUpload(1..3)` -> `@upload|audio1..3|input12..14|`.
    `<Audio N>` numbers clip soundtracks first (silent clips skipped when
    probeable), then standalone files. The executor blocks with a correction
    turn on: an unresolvable ref, a still image, a silent source, or a
    still-rendering Movie with no file. An Audio bubble in a photo slot
    (`chat_imageN`) blocks with "use audio="; audio attrs on non-Reference
    presets inject an ignored-note. Audio refs are STYLE conditioning, same
    contract as the video/photo refs: they nudge voice character / music /
    ambience toward the sample, they are NOT an exact voice clone. Two
    findings from the 2026-08-30 "didn't use my wav" report: (1) the "?"
    Inputs strip showed nothing for audio inputs - it now shows a waveform
    tile (`PicMain.MakeAudioThumbPng`); (2) the REAL bug - standalone audio
    was wired into the index-paired `ref_video_audios` group, which the node
    silently ignores without a same-numbered video, so those renders had ZERO
    audio conditioning. Standalone audio now rides `ref_audios.ref_audio_0..2`.
  - Aspect comes from the PRIMARY clip only; length stays the preset's unless the
    action carries `duration="N"` (seconds).
- Explicit durations: any H3 generation action (t2v/i2v/r2v/rv2v, chained or not)
  accepts `duration="N"` seconds on ANY H3 preset variant. `ApplyH3DurationOverride`
  converts to 24fps frames snapped to the NEAREST 17k+5 grid step (min 5 frames,
  NO clamp - since 2026-08-31; the old 124..362 clamp and the exact-"(MiniMax H3)"
  preset-name gate silently ate `duration` on the Quality/Turbo Cache routes) and
  appends `@replace|"length": <target>|"length": <frames>|` via
  `AddWorkflowDirective`, where `<target>` is the literal the workflow holds after
  the preset's own replaces ran: 362 for "15s"-named presets, the shipped default
  124 for everything else (5s presets replace 124->124, a visible no-op, or carry
  no length replace at all - appended directives run AFTER the preset's, see
  `PicMain.ApplyDimensionOverrideToJoblist`). On video_to_video an explicit
  duration also skips the (already H3-neutralized) source-duration override.
- `image_to_movie` + `Reference To Video (MiniMax H3) 5s`: extra photos via the
  standard slot 2-9 wiring (`chat_image2..9` / `attachment2..9` -> `image2..image9`);
  no executor special-casing needed.
- `extract_still` (model-invocable, local FFmpeg, no GPU): pulls one frame from a
  Movie bubble as a new assistant still bubble - the intended way to self-serve
  IDENTITY photo refs before a same-people rv2v regen (a clip alone locks
  motion/audio well but faces drift). `chat_image="N"` Movie + `time="S"` +
  `anchor="name"`; the pump blocks until the bubble exists, so the same reply can
  stage the still via `chat_image2="name"`. Executor `ExecuteExtractStill` ->
  `IChatHost.StartExtractStillAction` -> `AIChatPanel.ExtractStillActionCoroutine`
  (probe, clamp time, `FfmpegTool.ExtractStillFrame`, `AppendExtractedStillBubble`:
  label `#N`, kind `extracted still`, ALWAYS captioned attachment-style - NOT
  gated on the auto-caption setting - chain target updated). Extraction
  timestamps are guesses (clip captions carry no timecodes), so the skill docs
  prescribe verifying guessed frames with same-reply `inspect_image`
  (`resume="true"`) and rendering on the continue turn; the unconditional
  caption is the next-turn safety net that exposes a frame that missed its
  target. Frames come from the transcoded chat clip (<=832x480), not the
  original source; the manual chooser "Import still" stays the native-res path.
- Dimension overrides: explicit `width`/`height` on any `video_to_video` /
  `image_to_movie` action WIN over the clip-aspect path
  (`SetWorkflowDimensionOverride`; snapped to /32, clamped 256..2048 by
  `PicMain.ApplyDimensionOverrideToJoblist`). Since 2026-08-31 the appended
  override targets the preset @replace's REPLACEMENT half verbatim (literal or
  %var% - vars substitute identically at submit) and resolves the effective
  preset default from the joblist's own `%name%="N"` assignment lines, because
  the preset's replace runs FIRST: on presets whose default differs from the
  workflow's shipped literal the old find-literal targeting went stale and
  explicit dims silently rendered at the preset default (hit by Reference To
  Image while it defaulted 1152x640 over workflow literals 864x480 - since
  re-unified at 864x480, but the mechanism no longer requires that). The
  frame-count override (`TryAppendFrameCountOverride`) got the same treatment. EXCEPTION (2026-08-10): on
  START-FRAME presets with an image source, explicit dims whose aspect differs
  >~5% from the source are refitted as a pixel budget at the SOURCE's aspect
  (`SkillActionExecutor.ApplyBudgetDimensionOverride`, with an info bubble) -
  H3's i2v node stretches the first frame to the canvas with crop disabled, so
  honoring a clashing aspect literally would squish it. Reference presets
  (photo or clip refs) and video sources pin no frame and keep exact dims. Skill docs recommend raising
  identity-critical renders from the 864x480 default toward the trained max
  ~1.03MP: 1152x640 landscape / 640x1152 portrait / 896x896 square (hard trained
  cap 1344x768). Cost scales with pixel count (~2x at 1152x640).
- Prompting rule pushed in the skill docs: describe people ONLY as they appear
  in the source clip/caption or photo refs, never from film/actor world
  knowledge - H3 has no negative prompt and unfaithful prose beats the visual
  reference (the "auburn hair on a blonde actress" failure). Defer identity to
  `<Picture N>` tags and keep prose traits minimal.
- Audio prompting rule (2026-08-27): H3 ALWAYS generates a soundtrack, and an
  on-screen person whose line the prompt never specifies mouths GIBBERISH. The
  movie skills (image_to_movie, generate_movie, video_to_video, storytelling,
  stitch) therefore require every H3 prompt to carry an explicit three-layer
  audio spec: WHO speaks and their EXACT quoted words (or an explicit
  `No dialog; nobody speaks.`), a named ambient sound, and music or `no
  music` (reference prompts may defer a layer to `<Audio N>` instead). For
  per-speaker voice STYLE the skills push standalone audio refs (`audio=`)
  over whole-clip soundtracks - style nudges, not clones.
- Speech-quality A/B findings (2026-08-30, Whisper-judged via the local STT
  worker; matrix in this session's scratchpad, same seed/photo/prompt):
  - **The turbo Ref2V distill is NOT a speech-gibberish source.** With exact
    quoted dialog, EVERY variant was word-perfect: turbo 8/12/16 steps,
    Quality 20, sigma shift_audio 3/5/6, LoRA strength 1.0/0.75, the
    alibaba-pai `MiniMax-H3-Ref2VA-Acc-8Step` LoRA, a voice-ref (`--audio`)
    run, a 15s ~50-word monolog, and a worst-case 15s rv2v (2 speakers +
    voice-from-clip + market walla + music). Do not spend 20 steps to "fix"
    dialog.
  - **A voice/audio ref with NO quoted words = invented dialog in a random
    language** (2/2 such renders came out literal German). H3 never lifts
    WORDS from an audio ref - it carries voice timbre only. This is
    skill-text guidance ONLY (see the AGENTS.md rule against new
    prompt-quality executor gates) - a deterministic gate for it was built,
    verified, and then REMOVED at Seth's request (2026-08-30); do not
    reintroduce it.
  - **Unbudgeted seconds grow invented mumbled filler**: a 15s turbo clip
    with ~8s of scripted lines gained a nonsense line in the dead air
    between lines (the likely mechanism behind the 2026-08-30 Dexter run's
    "half the clips have gibberish" - its 15s scenes carried 2-3 short lines
    each). The SAME prompt/seed at 20 steps (Quality) rendered clean, so the
    Quality tier has real margin here (1-of-4 turbo 15s runs grew filler; the
    matched Quality run did not) - but the primary, free mitigation is
    prompt budgeting: skill docs now require dialog + described silent
    action to cover the full duration, with an explicit silent tail on long
    clips. If filler still bites on 15s dialog scenes, route them to the
    Quality presets (or bump the turbo graph to 12 steps - word-perfect at
    5s, untested at 15s, ~+50% time).
  - All known upstream audio fixes (ComfyUI #15243 audio-sigma carry, #15377
    offload, #15390 wrapper carry) are already in the ComfyUI server's 0.31.0 build; a
    ComfyUI update buys nothing here.
  - The alibaba-pai PDD LoRA renders at turbo speed (~68s/5s clip) with
    correct speech and loads via the same plain core LoRA node (no server
    restart needed after dropping it into models/loras) - a ready fallback
    if the lightx2v distill shows visual artifacts, not needed for audio.
- Skill docs: `aichat/skills/video_to_video.md` (modes, slots, tags, examples,
  same-people identity recipe), `image_to_movie.md` (multi-photo r2v + the
  start-frame two-stage recipe), `extract_still.md` (frame extraction for
  identity refs).
- `SkillActionParser`'s tag regexes are QUOTE-AWARE on purpose: H3 prompts carry raw
  `<Video 1>` / `<Picture 1>` inside the prompt attribute, and a naive `[^>]*`
  attribute span would end the action tag at the first inner `>` and mis-report
  "truncated tool call". Don't regress this when touching the parser.

## Troubleshooting

- **Instant "CUDA error: invalid argument" (`cudaErrorInvalidValue`) from H3 runs** - e.g. in `ComfyUI-MiniMax-H3-Turbo/__init__.py` (`_interp_egrid`), or even a plain tensor copy: a wedged CUDA context on that ONE ComfyUI instance, not a GPU-class problem and not H3's fault. Once a context wedges, EVERY later CUDA call in that process fails instantly with the same error while sibling instances run identical code fine. Diagnosis rule: find the FIRST CUDA error in that instance's log (`logs/comfy_<port>.log` beside the install, or `GET /internal/logs/raw`) - whatever ran there is the trigger; everything after is collateral. Both observed incidents (2026-08-14, 2026-08-17) hit the same instance and first-errored during a BiRefNet `state_dict` load (`bb.layers.*` weight copies); if it recurs, suspect that card/driver. Remedy: restart the wedged instance; meanwhile pin a healthy server (CLI `--server http://<host>:<port>`) - a wedged instance fails instantly, so it always looks idle and becomes a job magnet, making the failure rate look much worse than 1-in-N. Do NOT conclude "turbo is broken" from this symptom. Local hostnames, ports, log paths, and the restart recipe live in `agents_secret.md`.

## Verification checklist for changes here

1. CLI smoke (fast, no editor): `python cli/aitools_cli.py "<prompt with tags>"
   out.mp4 -p "Reference Video To Video (MiniMax H3) 5s" --video clip.mp4 -i2
   photo.png --audio voice.wav -v` against a server with the H3 models - watch for "pruned unused loader node(s)" and check
   the regenerated `*_cached_api_version.json` has all autogrow inputs on node 7
   (photos, the paired `ref_video_audios.*` entries, AND the standalone
   `ref_audios.*` entries - standalone audio in the wrong group renders
   "fine" while conditioning nothing, so check the GROUP, not just that a
   wav loads).
   Offline variant (servers down): append `--dry-run` and inspect the emitted
   `out.mp4.api.json` instead - same pruning/override pipeline, faked upload
   paths, no network.
2. Bridge: `/chat_import_video` + `/chat_import_image` to stage media, `/chat` to
   drive the action, then read `llm_aichat_log.json` (parsed action) and
   `comfyui_workflow_to_send_api.json` (pruned submitted graph).
3. Silent-clip path: `/chat_import_video ... audio=false`, expect an info bubble
   and a graph with that `ref_video_audio_N` input absent.
