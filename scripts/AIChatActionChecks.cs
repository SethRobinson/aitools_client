using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AITools.AIChat.Skills;

internal static class AIChatActionChecks
{
    private const string Transcript =
        "<aitools_action skill=\"web_transcript\" url=\"https://example.com/video\" " +
        "language=\"en\" offset=\"40000\" max_chars=\"20000\"/>";
    private const string Outside = "<aitools_action skill=\"read_skill\" id=\"outside\"/>";
    private const string Inside = "<aitools_action skill=\"read_skill\" id=\"inside\"/>";
    private static int _checks;

    private sealed class Run
    {
        public readonly SkillActionParser Parser;
        public readonly List<SkillAction> Actions = new List<SkillAction>();
        public readonly List<SkillAction> Skipped = new List<SkillAction>();
        public readonly StringBuilder Display = new StringBuilder();

        public Run(bool allow)
        {
            Parser = new SkillActionParser { AllowActionsInThinkBlocks = allow };
            Parser.OnActionParsed += Actions.Add;
            Parser.OnActionSkipped += (action, reason) =>
            {
                Check(reason == "inside think block", "skip reason");
                Skipped.Add(action);
            };
        }

        public void Feed(string chunk)
        {
            Parser.Feed(chunk);
            Display.Append(Parser.ConsumeDisplayText());
        }

        public void Finish()
        {
            Display.Append(Parser.Flush());
            Parser.Feed("");
            Parser.Flush(); // Repeated flushes must never execute the same tag again.
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _checks++;
    }

    private static Run Parse(string raw, bool allow, int chunkSize)
    {
        var run = new Run(allow);
        for (int i = 0; i < raw.Length; i += chunkSize)
            run.Feed(raw.Substring(i, Math.Min(chunkSize, raw.Length - i)));
        run.Finish();
        return run;
    }

    private static void VerifyTranscript(Run run, string raw, bool allow)
    {
        Check(run.Actions.Count == (allow ? 2 : 1), "reasoning opt-in action count");
        Check(run.Skipped.Count == (allow ? 0 : 1), "reasoning opt-in skip count");
        Check(run.Actions.Last().GetArg("id") == "outside", "outside action follows reasoning");
        if (allow)
        {
            Check(run.Actions[0].SkillId == "web_transcript", "transcript action routed");
            Check(run.Actions[0].GetArg("offset") == "40000", "transcript offset retained");
            Check(run.Actions[0].GetArg("max_chars") == "20000", "transcript page size retained");
        }
        string history = run.Parser.RemoveThinkingForHistory(raw, preserveActions: allow);
        Check(!history.Contains("PRIVATE_REASONING") && !history.Contains("<think>"), "reasoning omitted from history");
        Check(history.Contains(Transcript) == allow, "accepted reasoning XML retained exactly");
        Check(SkillActionParser.ExtractActions(history).Count == (allow ? 2 : 1), "history action order/count");
        string details = run.Parser.RemoveThinkingForHistory(raw, preserveActions: false);
        Check(SkillActionParser.ExtractActions(details).Single().GetArg("id") == "outside", "visible marker details stay aligned");
    }

    public static void Main()
    {
        Check(!new SkillActionParser().AllowActionsInThinkBlocks, "default remains off");
        string raw = "<think>PRIVATE_REASONING\n" + Transcript + "</think>\nDone.\n" + Outside;
        foreach (bool allow in new[] { false, true })
        {
            // Every two-chunk split, including splits within both thinking markers
            // and inside every action attribute. Display consumption trims each prefix.
            for (int split = 0; split <= raw.Length; split++)
            {
                var run = new Run(allow);
                run.Feed(raw.Substring(0, split));
                run.Feed(raw.Substring(split));
                run.Finish();
                VerifyTranscript(run, raw, allow);
            }
            foreach (int size in new[] { 1, 2, 3, 7, 19 })
                VerifyTranscript(Parse(raw, allow, size), raw, allow);

            string unclosed = "<think>PRIVATE_REASONING\n" + Transcript;
            var stopped = Parse(unclosed, allow, 1);
            Check(stopped.Actions.Count == (allow ? 1 : 0), "complete command in unclosed thinking");
            Check(stopped.Parser.RemoveThinkingForHistory(unclosed, allow).Trim() == (allow ? Transcript : ""),
                "stopped history retains only accepted command");

            var incomplete = Parse("<think>PRIVATE_REASONING <aitools_action skill=\"web_transcript\"", allow, 1);
            Check(incomplete.Actions.Count == 0, "incomplete command never runs");

            var plain = Parse(Transcript + "\n" + Outside, allow, 1);
            Check(plain.Actions.Count == 2 && plain.Skipped.Count == 0, "ordinary commands unaffected");
        }

        Check(Parse(raw, true, 1).Display.ToString() == Parse(raw, false, 1).Display.ToString(),
            "execution preference does not change thinking display");

        string multiple = Outside + "<think>PRIVATE_REASONING" + Inside + "</think> between " +
            "<think>PRIVATE_REASONING" + Transcript + "</think>" + Outside;
        var multipleRun = Parse(multiple, true, 1);
        string multipleHistory = multipleRun.Parser.RemoveThinkingForHistory(multiple);
        Check(string.Join(",", SkillActionParser.ExtractActions(multipleHistory).Select(a => a.GetArg("id") ?? a.SkillId))
            == "outside,inside,web_transcript,outside", "multiple thinking blocks keep action order and intentional repeats");
        Check(!multipleHistory.Contains("PRIVATE_REASONING"), "multiple spans remove all reasoning");

        string closeOnly = "PRIVATE_REASONING " + Transcript + "</think>" + Outside;
        foreach (int size in new[] { 1, closeOnly.Length })
        {
            var run = Parse(closeOnly, true, size);
            Check(run.Actions.Count == 2, "close-only opt-in runs both commands");
            Check(run.Parser.RemoveThinkingForHistory(closeOnly).Contains(Transcript),
                "close-only history retains command even when close arrives later");
        }
        Check(Parse(closeOnly, false, closeOnly.Length).Actions.Count == 1, "close-only existing same-chunk skip");

        string paired = "<think><aitools_action skill='read_skill' id='paired'>body</aitools_action></think>";
        Check(Parse(paired, true, 1).Actions.Single().GetArg("id") == "paired", "paired form inside reasoning");
        string video = "<think><aitools_action skill=\"generate_movie\" prompt=\"<Video 1> says \\\"hi\\\"\"/></think>";
        Check(Parse(video, true, 1).Actions.Single().Prompt == "<Video 1> says \"hi\"", "quoted angle brackets fire once");
        string oddQuotes = "<think><aitools_action skill=\"generate_image\" prompt=\"a 6'2\" actor\"/>";
        Check(Parse(oddQuotes, true, 1).Actions.Count == 1, "permissive tag fires once at EOF");
        Check(Parse(oddQuotes, false, 1).Actions.Count == 0, "permissive EOF still obeys off setting");

        var reset = Parse(raw, true, 1);
        reset.Parser.Reset();
        Check(reset.Parser.AllowActionsInThinkBlocks, "reset retains configuration");
        reset.Actions.Clear();
        reset.Feed("<think>" + Inside + "</think>");
        reset.Finish();
        Check(reset.Actions.Count == 1, "reset allows a new turn without duplicate actions");
        Check(!reset.Parser.RemoveThinkingForHistory("<think>" + Inside + "</think>").Contains(Transcript),
            "reset clears prior history bookkeeping");
        reset.Parser.Reset();
        reset.Parser.AllowActionsInThinkBlocks = false;
        reset.Actions.Clear();
        reset.Feed("<think>" + Inside + "</think>");
        reset.Finish();
        Check(reset.Actions.Count == 0, "next turn can disable reasoning actions again");

        Console.WriteLine("PASS: " + _checks + " AI Chat action assertions (streaming, opt-in/off, history, reset, truncation).");
    }
}
