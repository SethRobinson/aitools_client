using System.Collections.Generic;

namespace AITools.AIChat.Skills
{
    /// <summary>
    /// Describes what kind of input the skill expects from the user. Used purely as
    /// documentation to the LLM (we don't enforce - the LLM just sees the value in the
    /// system prompt and is told to obey it).
    /// </summary>
    public enum SkillInputs
    {
        None,
        Attachment,
        AttachmentOptional
    }

    /// <summary>
    /// Plain data object describing one skill. Loaded from a markdown file under
    /// <c>aichat/skills/</c> by <see cref="SkillManager"/>. The <see cref="RawMarkdown"/>
    /// is the full body (everything after the front matter); <see cref="Summary"/> is
    /// the short one-liner that gets folded into the system prompt by default;
    /// <see cref="Template"/> is a copy-pasteable canonical action tag (with all required
    /// attributes filled in) so the LLM gets the exact call syntax in turn 1.
    ///
    /// Front matter format (YAML-ish, no real YAML parser - we just split on ':' for
    /// the keys we care about):
    /// <code>
    /// ---
    /// id: generate_image
    /// summary: Generate a brand-new image from a text prompt.
    /// inputs: none
    /// autoload: true
    /// triggers: poster, comic, storyboard
    /// exclude_triggers: poster into a movie, poster into a video
    /// template: &lt;aitools_action skill="generate_image" preset="Prompt To Image (Z-Image).txt" prompt="..."/&gt;
    /// ---
    /// </code>
    /// </summary>
    public class Skill
    {
        public string Id;
        public string Summary;
        public SkillInputs Inputs = SkillInputs.None;
        public string Template;       // copy-pasteable canonical action tag
        public List<string> Triggers = new List<string>();
        public List<string> ExcludeTriggers = new List<string>();
        public bool Autoload;
        public string RawMarkdown;
        public string FilePath;

        public Skill() { }

        public Skill(string id, string summary, SkillInputs inputs, string template, List<string> triggers, List<string> excludeTriggers, bool autoload, string rawMarkdown, string filePath)
        {
            Id = id;
            Summary = summary;
            Inputs = inputs;
            Template = template;
            Triggers = triggers ?? new List<string>();
            ExcludeTriggers = excludeTriggers ?? new List<string>();
            Autoload = autoload;
            RawMarkdown = rawMarkdown;
            FilePath = filePath;
        }

        /// <summary>
        /// True if this skill has the metadata required for the LLM to call it
        /// (non-empty id and summary).
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(Summary);
    }

    /// <summary>
    /// Built-in skill ids the executor handles itself (they don't require a markdown
    /// file to function but ship with one for documentation). Listed here so other code
    /// can reference them without magic strings.
    /// </summary>
    public static class BuiltInSkillIds
    {
        public const string GenerateImage = "generate_image";
        public const string GenerateMovie = "generate_movie";
        public const string ImageToImage = "image_to_image";
        public const string ImageToMovie = "image_to_movie";
        public const string VideoToVideo = "video_to_video";
        public const string RifeVideo = "rife_video";
        public const string ClipVideo = "clip_video";
        public const string ExtractStill = "extract_still";
        // Local FFmpeg concat of several Movie bubbles into one clip (no GPU). The host
        // waits for still-rendering sources, so a "make N clips then stitch them" reply
        // can end with one stitch_video that lands once every clip exists.
        public const string StitchVideo = "stitch_video";
        public const string ReadSkill = "read_skill";
        public const string SummarizeWithSmallLlm = "summarize_with_small_llm";
        public const string DescribeImage = "describe_image";
        public const string InspectImage = "inspect_image";

        // Control action with no image/GPU side effect: the model emits this when it
        // decides it needs another turn to keep working (e.g. it announced an edit it
        // will run but wants the spawned image to settle first, or it has more steps
        // to do). The host registers a synthetic (continue) turn through the same
        // auto-resume path used by read_skill / inspect_image resume="true", with a
        // runaway cap on consecutive self-requested continues.
        public const string Continue = "continue";

        // Composition primitives - C#-side image ops the LLM can chain to build
        // posters, books, storyboards, comic panels, magazine covers, etc. None
        // of these touch ComfyUI; they all run as coroutines on the spawned
        // PicMain (or stack onto a prior Pic via chain="true"). See
        // aichat/skills/composition_recipes.md for worked examples.
        public const string DrawText = "draw_text";
        public const string AddBorder = "add_border";
        public const string PasteImage = "paste_image";
        public const string NewCanvas = "new_canvas";
        public const string CropResize = "crop_resize";
        public const string DrawShape = "draw_shape";

        // Web media fetch (Brave Search API + plain HTTPS downloads + bundled yt-dlp).
        // web_search lists results only; web_image / web_video download into normal
        // image / Movie bubbles. Every step is shown in an always-visible Web bubble.
        // See docs/web_media.md.
        public const string WebSearch = "web_search";
        public const string WebImage = "web_image";
        public const string WebVideo = "web_video";
        /// <summary>Fetch ONE page's readable text + image list (P&lt;n&gt; session) into the prompt.</summary>
        public const string WebPage = "web_page";
        /// <summary>Download one bare sound file (.wav/.mp3/...) into an Audio #N bubble (url= or a web_page audio link).</summary>
        public const string WebAudio = "web_audio";

        /// <summary>Every skill that needs the AI Chat "Web" toggle to be on.</summary>
        public static readonly HashSet<string> WebSkills = new HashSet<string> { WebSearch, WebImage, WebVideo, WebPage, WebAudio };

        // Audio generation through the configurable gateway (Settings > Audio > Audio
        // generation): music, sound effects, speech -> "Audio #N" bubbles. Hidden from the
        // prompt while no gateway is configured. set_video_audio is local FFmpeg (mix /
        // replace a Movie's soundtrack with an Audio bubble) and is always available.
        // See docs/audio_generation.md.
        public const string GenerateMusic = "generate_music";
        public const string GenerateSfx = "generate_sfx";
        public const string GenerateSpeech = "generate_speech";
        public const string SetVideoAudio = "set_video_audio";

        public static readonly HashSet<string> AudioGenSkills = new HashSet<string> { GenerateMusic, GenerateSfx, GenerateSpeech };

        public static readonly HashSet<string> All = new HashSet<string>
        {
            GenerateImage, GenerateMovie, ImageToImage, ImageToMovie, VideoToVideo, RifeVideo, ClipVideo,
            ExtractStill, StitchVideo, ReadSkill, SummarizeWithSmallLlm, DescribeImage, InspectImage, Continue,
            DrawText, AddBorder, PasteImage, NewCanvas, CropResize, DrawShape,
            WebSearch, WebImage, WebVideo, WebPage, WebAudio,
            GenerateMusic, GenerateSfx, GenerateSpeech, SetVideoAudio
        };
    }
}
