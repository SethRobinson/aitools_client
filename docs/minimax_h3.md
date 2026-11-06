# MiniMax H3 (video generation)

Deep-dive for the MiniMax H3 model family. Read before touching any H3 workflow,
preset, or the AI Chat video reference plumbing. Prompt-writing rules (the
official structured format, dialog timing, voice-ref phrasing) live in
`docs/minimax_h3_prompting.md`.

Contents: Model facts, Measured costs, Workflows, Submit-time graph pruning,
Presets, AI Chat wiring, Troubleshooting, Verification checklist.

## Model facts

Open-weights omni-modal video model, ComfyUI 0.30+ native. Output is 24fps with
native stereo audio (dialog in 11 languages); no RIFE in the output path.

- Two checkpoints, different node classes, NOT combinable:
  - **FL2VA** (`minimax_h3_fl2va_pruned_int8_convrot.safetensors`): text/image to
    video via `MiniMaxH3ImageToVideo` (optional `first_frame` / `last_frame`).
    The only way to pin an exact start frame.
  - **Ref2VA** (`minimax_h3_ref2va_pruned_int8_convrot.safetensors`): reference-
    conditioned generation via `MiniMaxH3ReferenceToVideo`. Accepts MIXED
    references in one run: up to 9 images (`ref_images.ref_image_N`), 3 videos
    (`ref_videos.ref_video_N`), 3 clip soundtracks
    (`ref_video_audios.ref_video_audio_N`, index-PAIRED with the same-numbered
    `ref_video_N` and SILENTLY IGNORED without it) and 3 standalone audio refs
    (`ref_audios.ref_audio_N`, the group for wav/music/voice samples). No
    first-frame pinning. Prompt tags are per-type in connection order:
    `<Picture 1>`.., `<Video 1>`.., and `<Audio j>` numbers each clip soundtrack
    (just before its video) then the standalone audios. Give each reference ONE
    job (video = motion/camera, audio = voice/music style, picture =
    identity/setting).
  - "Exact start frame + specific person" therefore needs the two-stage recipe
    (Qwen Image 2.1 2-input person insert, then `Image To Video (MiniMax H3)`);
    documented in `aichat/skills/image_to_movie.md`.
- Shared: `CLIPLoader` `qwen3vl_32b_minimax_h3_nvfp4_awq.safetensors` (type
  `minimax`), `minimax_h3_video_vae_fp16` + `minimax_h3_audio_vae_fp32`,
  `res_multistep`/`simple` 20 steps via `BasicGuider`. There is NO
  negative-prompt path, so H3 presets have no `default_negative_prompt` block.
- `length` is a 24fps frame count on the 17k+5 grid (5, 22, 39, ...; 124 = ~5s,
  trained max 362 = ~15s). Any grid step renders: 5 frames is the still-image
  packet the Reference To Image presets use. Default canvas 864x480 (~0.4MP);
  trained max ~1.03MP (768 short edge, cap 768x1344, multiples of 32). Pixel
  count is the dominant cost knob.
- Reference clips are consumed at 24fps, 2-15s (`force_rate: 24`,
  `frame_load_cap: 360` in the VHS loader).
- Graphs include kjnodes' `MiniMaxH3MemoryEfficientSageAttentionPatch`, which
  needs the `sageattention` pip package on the server (bypass/delete to run
  without).
- H3 model files may be installed on only some of the configured ComfyUI servers
  (which ones locally: `agents_secret.md`).

### Speed variants (turbo and cache)

- **FL2VA turbo** (default i2v/t2v): larryvrh's `MiniMax-H3-Turbo-Lora`
  (`minimax_h3_turbo_v4_step600_ema.safetensors`, strength 1.0) at 8 steps /
  `simple`, ~2.2x faster than 20-step with start-frame pinning, identity and
  audio intact. 6-8 steps is the safe band (4 risks motion smear). Needs the
  `Larryvrh/ComfyUI-MiniMax-H3-Turbo` custom nodes: `MiniMaxH3TurboLoRA` loader
  (bypass default; `low_vram`=merge for OOM) and `MiniMaxH3TurboSampler`
  (replaces `KSamplerSelect` into `SamplerCustomAdvanced`).
- **That custom node cannot run on Ref2VA**: it assumes FL2VA's 2-stream latent
  packing and crashes on Ref2VA's 3-stream ("size of tensor a (3) must match b
  (2)"). Plain core `LoraLoaderModelOnly` LoRAs attach to Ref2VA fine.
- **Ref2VA turbo** (default reference path): lightx2v's Ref2V distill
  (`minimax_h3_ref2v_turbo_4step_v0.1_comfyui_bf16.safetensors`) through plain
  `LoraLoaderModelOnly`, 8 steps. alibaba-pai's
  `MiniMax-H3-Ref2VA-Acc-8Step.safetensors` also loads through the core node,
  renders at the same speed with correct speech, and is an untapped fallback if
  the lightx2v distill shows visual artifacts.
- **Spectrum cache** (`SpectrumApplyMiniMaxH3`, xmarre/ComfyUI-Spectrum-MiniMax-H3,
  must be installed on the server) stacks with FL2VA turbo for ~1.4x more with
  A/B-identical frames. Not lossless and has an audio-stutter history: ear-test
  dialog when a clip sounds off. Cache runs report 16 progress steps (8 real + 8
  replay ticks) vs 8 on plain turbo, the quickest tell it ran.
- Kijai's `SolAttnPatch` was tested and was not faster than the sage patch;
  unused.

### Measured costs (RTX PRO 6000 Blackwell, 5s @864x480, warm, uncontended)

| Route | Blackwell | A100 |
| --- | --- | --- |
| i2v/t2v turbo + cache (AI Chat default) | ~50s | |
| i2v/t2v turbo 8-step | ~70s | ~122s |
| i2v/t2v 20-step (Quality) | ~163s | ~1.8x |
| r2v turbo 8-step, 1 photo (reference default) | ~64-68s | ~124s |
| rv2v 20-step, single clip | ~242s | |
| Reference To Image (5 frames, turbo or Quality) | ~22-33s | |

- Cold start the first time a server touches H3: ~20 GB checkpoint stage (tens
  of seconds) plus LoRA load.
- Reference per-step cost tracks TOTAL token count, not just canvas: r2v turbo
  at 1152x640 is ~113s, and a full-resolution reference photo adds more
  (`ref_image_size: match` scales each ref toward the canvas AREA). A
  "reference render took 2x as long" report is almost always a bigger request
  (canvas/duration/refs), not a slower workflow; the skills deliberately raise
  identity-critical reference renders to 1152x640.
- 15s rv2v and two-clip rv2v are unmeasured (expect much worse than 3x the 5s
  cost); re-measure before quoting.

## Workflows (`ComfyUI/`)

- `img_to_video_minimax_h3_turbo.json`, `text_to_video_minimax_h3_turbo.json`:
  FL2VA + turbo LoRA/sampler, 8 steps. Keep the literal `"length": 124` and all
  placeholders so AI Chat duration/dimension overrides work.
- `*_turbo_cache.json` (img/text): turbo + `SpectrumApplyMiniMaxH3`.
- `img_to_video_minimax_h3.json`, `text_to_video_minimax_h3.json`: FL2VA 20-step
  (Quality presets).
- `ref_multi_to_video_minimax_h3.json`: **the universal Ref2VA graph** used by
  all reference presets. Carries EVERY loader the app can wire; unused ones are
  pruned at submit time (below):
  - `<AITOOLS_INPUT_1>` clip 1 (`VHS_LoadVideoPath`) -> `ref_video_0` + `ref_video_audio_0`
  - `<AITOOLS_INPUT_2>` clip 2 -> `ref_video_1` + `ref_video_audio_1`
  - `<AITOOLS_INPUT_3>`..`_11` photos 1-9 (`VHS_LoadImagePath`) -> `ref_image_0..8`
  - `<AITOOLS_INPUT_12>`..`_14` standalone audio refs 1-3 (`VHS_LoadAudio`;
    wav/mp3/mp4 decode) -> `ref_audios.ref_audio_0..2`. MUST be this group: in
    the index-paired `ref_video_audios` group the node silently ignores them and
    the render has zero audio conditioning.
  To run it manually in ComfyUI, delete the loaders you aren't using.
- `ref_multi_to_video_minimax_h3_turbo.json`: the same universal graph plus the
  lightx2v distill via `LoraLoaderModelOnly` (strength 1.0, between `UNETLoader`
  and the sage patch), `MiniMaxH3SigmaShift` (video 12 / audio 3) on the GUIDER
  branch only, sampler `euler`/`simple` 8 steps. Loaders, placeholders and
  pruning are identical.
- `ref_multi_to_img_minimax_h3_turbo.json` / `ref_multi_to_img_minimax_h3.json`
  (Quality): still-image variants. Same loaders/placeholders/pruning, literal
  `length` 5, the audio latent is sampled but never decoded, and a `SaveImage`
  with an `AITOOLS_UNIQUE_ID` prefix saves all 5 frames. API-format files, so no
  cached copy is generated. Gitignored `test_` mirrors exist for the local test
  stack (`agents_secret.md`).
- `ref_to_video_minimax_h3.json`, `ref_video_to_video_minimax_h3.json`: legacy
  single-reference graphs, unused by presets; kept for manual ComfyUI use.
- rv2v audio requirement is structural: a linked VHS audio output hard-aborts on
  a silent source (`VHS failed to extract audio from <file>`, pre-sampling). The
  app avoids it by pruning the audio input; with it unwired H3 synthesizes the
  soundtrack from the prompt.

## Submit-time graph pruning (one workflow serves all combos)

- Presets mark reference slots `@upload|source|inputN|optional|`. At parse time
  (`PicMain.cs` upload branch + `IsUploadSourceAvailable`) an optional slot whose
  source isn't wired is skipped: no upload job, placeholder left unfilled, and
  `PicJob._allowInputPruning` set.
- `PicTextToImage.PruneWorkflowInputs` (on the parsed API JSON right before
  submit) removes any node whose string inputs still contain `<AITOOLS_INPUT_`,
  cascade-removes inputs referencing removed nodes, applies
  `@prune_input|<name>|` directives, and renumbers autogrow inputs
  (`group.item_N`) so indices stay contiguous. Gated on `_allowInputPruning` or a
  prune directive. Result is visible in `comfyui_workflow_to_send_api.json`.
- `@prune_input` needs no parse-stage support; the AI Chat executor appends it
  via `PicMain.AddWorkflowDirective` (one-shot, rides the next workflow line).
- Silent clips: the executor ffprobes each wired clip
  (`FfmpegTool.VideoInfo.HasAudio`) and appends
  `@prune_input|ref_video_audios.ref_video_audio_N|` for silent ones. A manual
  GUI run on a silent movie pic still hits the VHS abort. The CLI auto-detects
  too (`--no-clip-audio N` is the manual fallback without ffprobe).
- CLI mirror: `cli/workflow.py` `prune_unfilled_inputs` + `prune_named_inputs`;
  flags, slot binding and overrides are documented in `cli/README.md`
  "Generating movies (MiniMax H3)".

## Presets (`Presets/`)

FL2VA (i2v start frame / t2v):

- `Image To Video (MiniMax H3 Turbo Cache) 5s`, `Prompt To Video (MiniMax H3
  Turbo Cache) 5s`: turbo + cache, AI Chat's DEFAULT video route
  (image_to_movie / generate_movie).
- `Image To Video (MiniMax H3) 5s` / `15s`, `Prompt To Video (MiniMax H3) 5s`:
  plain turbo. AI Chat uses them on explicit no-cache requests; the 15s preset
  also serves long-clip requests (no 15s cache variant).
- `... (MiniMax H3 Quality) 5s` / `15s`: full 20-step; routed for "high/maximum
  quality".

Reference (Ref2VA; plain names are turbo defaults, `Quality` = 20-step, there is
deliberately NO cache variant):

- `Reference Video To Video (MiniMax H3) 5s` / `15s` (+ Quality): clip
  required (`@upload|video|input1|`), optional `video2`->input2 and
  `image2..image10`->inputs 3-11 (photo refs 1-9).
- `Reference To Video (MiniMax H3) 5s` (+ Quality): photo 1 required
  (`image1`->input3), photos 2-9 optional, both video loaders prune away.
- `Reference To Image (MiniMax H3)` (+ Quality): a STILL from 1-9 photo refs via
  the 5-frame workflows; same slot layout as r2v, `%vid_width%`/`%vid_height%`
  only (no `%vid_length%`, so duration overrides cannot attach). Default 864x480;
  omitted dims refit the budget to photo 1's aspect. The first saved frame is the
  output (frames within the packet are near-identical). 5 frames is out of
  distribution for H3, so watch harder prompts for degradation. AI Chat uses it
  only when the user names H3/MiniMax; the default `image_to_image` model is Qwen
  Image 2.1 (`docs/workflows.md`, `aichat/skills/image_to_image.md`).
- All reference presets declare optional `audio1..audio3`->inputs 12-14
  (`PicMain.SetPendingAudioUpload`, upload sources `audio1`..`audio3`).
- The rv2v 5s presets deliberately have NO length `@replace` (opts out of AI
  Chat's video_to_video source-duration override, which uses WAN's 16fps/4n+1
  cadence); the 15s presets' `124 -> 362` replace runs before appended
  directives, so the duration override targets literal 362 on "15s" presets.

Naming and variable rules:

- The executor keys H3 reference behavior on preset-name substrings: "Reference
  Video To Video" (skips the Bernini restyle auto-select, enables clip refs),
  and "Reference To Video" / "Reference To Image"
  (`SkillActionExecutor.IsReferencePhotoPreset`: reference-tag gate, `audio=`
  slots, exact-dims pass-through). Keep those substrings when adding variants,
  and beware substring gates going stale when variant names change.
- Don't end preset names in `<digit> Input` (`DowngradePresetToInputCount`
  regex).
- Video presets use `%vid_width%`/`%vid_height%`/`%vid_length%` because chained
  presets share one PicMain variable scope (AGENTS.md job-script pitfalls).

## AI Chat wiring (`SkillActionExecutor.cs`)

- Routing preference (prompt/skill rule, not a gate): when a supplied Movie is
  the reference for a new movie/person/voice, use H3 Reference Video To Video
  with the ORIGINAL Movie in `chat_image`, keeping its full clip and soundtrack.
  "Reference to video" must not route to the photo-only preset. Bind a speaker's
  voice to the clip's `<Audio 1>` (don't duplicate it via `audio=`). Extracted
  face stills only supplement via `chat_image2+`; never chain onto an extracted
  still or discard the source video. Source and output durations are
  independent; select a segment when a source exceeds 15s.
- Movie-aware autoload: when the newest live chat medium is a Movie, deictic
  edit phrases ("change this scene") and speech/audio edits load the full
  `video_to_video` skill.
- A Movie may feed `image_to_image` / `image_to_movie` only with explicit
  `movie_frame="true"`; otherwise the executor blocks and requests a correction
  turn.
- Bernini v2v output is silent: a Bernini/default `video_to_video` prompt asking
  for new dialogue/voice/music/sound is blocked and redirected to H3 Ref2VA.
- `video_to_video` + a "Reference Video To Video" preset (`isH3RefVideoPreset`):
  - Primary clip: `chat_image="N"` Movie bubble (or `chain="true"`) ->
    `m_pendingVideoUploadPath` / prevPic's movie.
  - `chat_image2` pointing at a MOVIE = second clip: path goes to
    `PicMain.m_pendingVideoUploadPath2` (`@upload|video2|`; byte resolution is
    skipped because bytes would be the poster texture). A still in `chat_image2`
    (and `attachment2` always) is a photo reference.
  - Stills in slots 2-10 -> `PicMain.SetExtraInputImage(slot, ...)` -> inputs
    3-11 -> `<Picture 1..9>` in slot order (pruning renumbers gaps).
  - Rescue: turn attachments are adopted as photo refs (slots 2-3) when the model
    forgot the slot attributes.
  - `WarnUnconsumedExtraInputSlots` injects a warning when a staged slot has no
    matching `@upload|imageN|` consumer in the resolved preset.
  - Aspect comes from the PRIMARY clip; length stays the preset's unless the
    action carries `duration="N"`.
- Reference-tag gate (`BlockH3ReferencePromptTagMismatch`, structural): on any
  Reference preset (matched on the RESOLVED name) the prompt must use EVERY
  staged reference's tag (`<Video 1..2>` / `<Picture 1..N>` / `<Audio N>`, slot
  order, case-insensitive) and name no tag with nothing behind it. A staged clip
  counts as bound via its `<Video k>` OR its soundtrack's `<Audio n>`; each
  standalone audio ref needs its own `<Audio n>`. A mismatch blocks before
  queueing and injects a slot->tag map plus a correction turn
  (`RequestContinueTurn`). Correction notes translate attachments to chat_image
  numbers. Its regexes ignore `<Subject N>` and `<d>`.
- Standalone audio refs: `audio="N|anchor"` / `audio2` / `audio3` on any
  Reference-preset action stage an Audio bubble's original file (or a Movie's
  clip) into `PicMain.SetPendingAudioUpload(1..3)` -> `@upload|audio1..3|`.
  `<Audio N>` numbers clip soundtracks first (silent clips skipped when
  probeable), then standalone files. Blocks with a correction turn on an
  unresolvable ref, a still, a silent source, or a still-rendering Movie; an
  Audio bubble in a photo slot blocks with "use audio="; audio attrs on
  non-Reference presets inject an ignored-note. The Inputs strip shows a
  waveform tile (`PicMain.MakeAudioThumbPng`). Audio refs are STYLE
  conditioning (voice timbre/music/ambience), never an exact clone and never a
  source of WORDS.
- Explicit durations: any H3 action accepts `duration="N"` seconds on ANY H3
  preset. `ApplyH3DurationOverride` converts to 24fps frames snapped to the
  NEAREST 17k+5 step (min 5, NO clamp: Seth's rule against range-clamping
  model numbers) and appends `@replace|"length": <target>|"length": <frames>|`
  via `AddWorkflowDirective`. `<target>` is the literal after the preset's own
  replaces: 362 for "15s" presets, 124 otherwise (appended directives run AFTER
  the preset's, see `PicMain.ApplyDimensionOverrideToJoblist`). On
  video_to_video an explicit duration also skips the source-duration override.
- `image_to_movie` + `Reference To Video (MiniMax H3) 5s`: extra photos via the
  standard slot 2-9 wiring (`chat_image2..9` / `attachment2..9`).
- `extract_still` (local FFmpeg, no GPU): pulls one frame from a Movie bubble as
  a new still bubble, the intended way to self-serve identity photo refs before a
  same-people rv2v regen (a clip locks motion/audio well but faces drift).
  `chat_image="N"` + `time="S"` + `anchor="name"`; the same reply can stage it
  via `chat_image2="name"`. Path: `ExecuteExtractStill` ->
  `IChatHost.StartExtractStillAction` -> `AIChatPanel.ExtractStillActionCoroutine`
  (`FfmpegTool.ExtractStillFrame`, `AppendExtractedStillBubble`, always
  captioned). Timestamps are guesses, so skills prescribe same-reply
  `inspect_image` (`resume="true"`) verification. Frames come from the
  transcoded chat clip (<=832x480); the manual chooser "Import still" is the
  native-res path.
- Dimension overrides: explicit `width`/`height` on `video_to_video` /
  `image_to_movie` WIN over the clip-aspect path (`SetWorkflowDimensionOverride`;
  snapped /32, clamped 256..2048 by `PicMain.ApplyDimensionOverrideToJoblist`).
  The override targets the preset @replace's REPLACEMENT half and resolves the
  effective default from the joblist's `%name%="N"` lines
  (`TryAppendFrameCountOverride` does the same for frames). Still prefer preset
  defaults equal to the workflow literal. EXCEPTION: on START-FRAME presets with
  an image source, dims whose aspect differs >~5% from the source are refitted as
  a pixel budget at the source aspect (`ApplyBudgetDimensionOverride`, info
  bubble), because H3's i2v node stretches the first frame with crop disabled.
  Reference presets and video sources keep exact dims. For identity-critical
  renders skills suggest 1152x640 / 640x1152 / 896x896 (~2x cost).
- `SkillActionParser`'s tag regexes are QUOTE-AWARE on purpose: H3 prompts carry
  raw `<Video 1>` / `<Picture 1>` and multi-line text inside the prompt
  attribute; a naive `[^>]*` span would end the tag at the first inner `>`.
- Skill docs: `aichat/skills/video_to_video.md` (modes, slots, tags, identity
  recipe), `image_to_movie.md` (prompt-format hub, multi-photo r2v, two-stage
  start-frame recipe), `extract_still.md`.

## Troubleshooting

- **Instant "CUDA error: invalid argument" (`cudaErrorInvalidValue`) from H3
  runs** (e.g. in `ComfyUI-MiniMax-H3-Turbo/__init__.py` `_interp_egrid`, or a
  plain tensor copy): a wedged CUDA context on ONE ComfyUI instance, not an H3
  or GPU-class problem. Once wedged, every later CUDA call in that process fails
  instantly while sibling instances run the same code fine.
  - Diagnose: find the FIRST CUDA error in that instance's log
    (`logs/comfy_<port>.log` beside the install, or `GET /internal/logs/raw`);
    whatever ran there is the trigger, everything after is collateral. Observed
    triggers include BiRefNet and H3 VAE weight loads, so it is not
    model-specific; if it recurs on the same card, suspect that card/driver.
  - Remedy: restart the wedged instance; meanwhile pin a healthy server (CLI
    `--server http://<host>:<port>`). A wedged instance fails instantly, so it
    always looks idle and becomes a job magnet, making failure rates look far
    worse than 1-in-N.
  - `PicTextToImage` captures the `execution_error` (node type + message +
    server) into `PicMain.m_lastRenderError` and logs it to `log.txt`
    (`ComfyUI execution error on server N: ...`); AI Chat's stitch_video /
    set_video_audio "finished without producing a video file" note quotes it
    with a recovery hint (re-emit unchanged; pin a different GPU only if the
    same one fails twice).
  - Do NOT conclude "turbo is broken" from this symptom. Local hostnames, ports,
    log paths and the restart recipe live in `agents_secret.md`.

## Verification checklist for changes here

1. CLI smoke (fast, no editor): `python cli/aitools_cli.py "<prompt with tags>"
   out.mp4 -p "Reference Video To Video (MiniMax H3) 5s" --video clip.mp4 -i2
   photo.png --audio voice.wav -v` against a server with the H3 models. Watch for
   "pruned unused loader node(s)" and check the regenerated
   `*_cached_api_version.json` has all autogrow inputs on node 7 (photos, the
   paired `ref_video_audios.*`, AND the standalone `ref_audios.*`; standalone
   audio in the wrong group renders "fine" while conditioning nothing, so check
   the GROUP). Offline: append `--dry-run` and inspect `out.mp4.api.json`.
2. Bridge: `/chat_import_video` + `/chat_import_image` to stage media, `/chat` to
   drive the action, then read `llm_aichat_log.json` (parsed action) and
   `comfyui_workflow_to_send_api.json` (pruned submitted graph).
3. Silent-clip path: `/chat_import_video ... audio=false`, expect an info bubble
   and a graph without that `ref_video_audio_N` input.
4. Dialog changes: verify by TRANSCRIBING the output (see
   `docs/minimax_h3_prompting.md`), never by ear or by reading the prompt.
