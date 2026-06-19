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
        public const string ReadSkill = "read_skill";
        public const string SummarizeWithSmallLlm = "summarize_with_small_llm";
        public const string DescribeImage = "describe_image";
        public const string InspectImage = "inspect_image";

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

        public static readonly HashSet<string> All = new HashSet<string>
        {
            GenerateImage, GenerateMovie, ImageToImage, ImageToMovie,
            ReadSkill, SummarizeWithSmallLlm, DescribeImage, InspectImage,
            DrawText, AddBorder, PasteImage, NewCanvas, CropResize, DrawShape
        };
    }
}
