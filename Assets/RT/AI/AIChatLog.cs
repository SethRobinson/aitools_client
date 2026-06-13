using System.Collections.Generic;
#if UNITY_EDITOR
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
#endif

/// <summary>
/// Editor-only, append-as-you-go diagnostic log of the WHOLE AI Chat exchange,
/// written to <c>llm_aichat_log.json</c> in the working directory (project root
/// in the editor). It replaces the old "sent only" <c>llm_request_sent_aichat.json</c>,
/// which captured the outgoing request but neither the reply nor the tool calls
/// the model actually emitted - the one thing you usually need when a poster /
/// book page comes out wrong.
///
/// One JSON array of chronological events. Each event has a <c>seq</c> and a
/// <c>kind</c>:
///   - "request"  : an outgoing LLM body (purpose = "chat" for the main turn,
///                  or the sidecar caller label, e.g. "ImageCaption"). The body
///                  is embedded inline when it is already valid JSON.
///   - "response" : a raw LLM reply. For the main turn this is the assistant
///                  text WITH its &lt;aitools_action .../&gt; tool-call tags inline.
///   - "action"   : one parsed tool call about to run (skill id + attributes,
///                  so the generate_image prompt / draw_text rect+font are visible).
///   - "note"     : free-form detail tied to the preceding action, e.g. the
///                  draw_text auto-fit result (chosen font size, overflow px).
///
/// The file is truncated at the start of each play session and DELETED when play
/// mode / the app exits, so it never lingers in the repo. EDITOR ONLY by design:
/// every method body is compiled out of player builds, so calls are cheap no-ops
/// there and nothing is ever written. (The user keeps the editor running while
/// handing the log over, so on-exit deletion doesn't lose anything.)
/// </summary>
public static class AIChatLog
{
    // Plain const so non-editor code (e.g. GameLogic startup cleanup) can still
    // name the file without pulling in the editor-only logging machinery.
    public const string LogFile = "llm_aichat_log.json";

#if UNITY_EDITOR
    private static readonly object _lock = new object();
    private static readonly List<string> _events = new List<string>();
    private static int _seq;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        lock (_lock) { _events.Clear(); _seq = 0; _writeVersion = 0; }
        lock (_writeLock) { _lastWritten = 0; }
        TryDelete();                              // fresh file every play session
        Application.quitting -= Cleanup;
        Application.quitting += Cleanup;          // and gone again on quit
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(UnityEditor.PlayModeStateChange s)
    {
        if (s == UnityEditor.PlayModeStateChange.ExitingPlayMode) Cleanup();
    }

    private static void Cleanup() { TryDelete(); }

    private static void TryDelete()
    {
        try { if (File.Exists(LogFile)) File.Delete(LogFile); } catch { /* diagnostics must never throw */ }
    }
#endif

    /// <summary>An outgoing LLM request body (already-serialized JSON).</summary>
    public static void Request(string purpose, string jsonBody)
    {
#if UNITY_EDITOR
        AddRaw("request", purpose, "body", jsonBody, bodyIsJson: true);
#endif
    }

    /// <summary>A raw LLM reply. For the main chat turn this still carries the
    /// assistant's &lt;aitools_action&gt; tool-call tags inline.</summary>
    public static void Response(string purpose, string text)
    {
#if UNITY_EDITOR
        AddRaw("response", purpose, "text", text, bodyIsJson: false);
#endif
    }

    /// <summary>One parsed tool call about to execute (skill id + raw attributes).</summary>
    public static void Action(string skillId, IReadOnlyDictionary<string, string> args)
    {
#if UNITY_EDITOR
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"seq\":").Append(NextSeq()).Append(',');
        sb.Append("\"kind\":\"action\",");
        sb.Append("\"skill\":").Append(JsonStr(skillId)).Append(',');
        sb.Append("\"args\":{");
        bool first = true;
        if (args != null)
        {
            foreach (var kv in args)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(JsonStr(kv.Key)).Append(':').Append(JsonStr(kv.Value));
            }
        }
        sb.Append("}}");
        Append(sb.ToString());
#endif
    }

    /// <summary>Free-form detail tied to the most recent action - e.g. the
    /// draw_text auto-fit result (rect, chosen font size, overflow px).</summary>
    public static void Note(string label, string detail)
    {
#if UNITY_EDITOR
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"seq\":").Append(NextSeq()).Append(',');
        sb.Append("\"kind\":\"note\",");
        sb.Append("\"label\":").Append(JsonStr(label)).Append(',');
        sb.Append("\"detail\":").Append(JsonStr(detail));
        sb.Append('}');
        Append(sb.ToString());
#endif
    }

#if UNITY_EDITOR
    private static void AddRaw(string kind, string purpose, string field, string value, bool bodyIsJson)
    {
        // Image / audio payloads ride in request bodies as multi-MB base64 blobs
        // (pasted chat images, the vision-caption PNG). Elide them - they make the
        // log unreadable and huge while telling us nothing. Stripping the blob
        // contents (not the surrounding JSON quotes) keeps the body valid JSON.
        value = RedactBlobs(value);

        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"seq\":").Append(NextSeq()).Append(',');
        sb.Append("\"kind\":").Append(JsonStr(kind)).Append(',');
        sb.Append("\"purpose\":").Append(JsonStr(purpose)).Append(',');
        sb.Append(JsonStr(field)).Append(':');
        // Embed an already-valid JSON body inline so it stays readable instead of
        // being double-escaped into one giant string; otherwise quote it.
        if (bodyIsJson && LooksLikeJson(value)) sb.Append(value);
        else sb.Append(JsonStr(value));
        sb.Append('}');
        Append(sb.ToString());
    }

    // Replace any run of >=200 contiguous base64 characters (an encoded image /
    // audio blob) with a short marker noting its length. Linear scan, no regex,
    // so there's no backtracking risk on a multi-MB body. Real prose never has
    // 200 unbroken base64 chars (spaces/punctuation break the run), so legitimate
    // prompts and replies are left intact.
    private static string RedactBlobs(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        const int threshold = 200;
        var sb = new StringBuilder(s.Length);
        int i = 0, n = s.Length;
        while (i < n)
        {
            if (IsB64(s[i]))
            {
                int j = i + 1;
                while (j < n && IsB64(s[j])) j++;
                int len = j - i;
                if (len >= threshold) sb.Append("[base64 ").Append(len).Append(" chars elided]");
                else sb.Append(s, i, len);
                i = j;
            }
            else { sb.Append(s[i]); i++; }
        }
        return sb.ToString();
    }

    private static bool IsB64(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
        c == '+' || c == '/' || c == '=' || c == '-' || c == '_';

    private static int NextSeq() { lock (_lock) { return ++_seq; } }

    private static readonly object _writeLock = new object();
    private static int _writeVersion;   // assigned under _lock; latest snapshot id
    private static int _lastWritten;    // guarded by _writeLock; highest id on disk

    private static void Append(string eventJson)
    {
        string snapshot;
        int version;
        lock (_lock)
        {
            _events.Add(eventJson);
            snapshot = "[\n" + string.Join(",\n", _events) + "\n]\n";
            version = ++_writeVersion;
        }
        string f = LogFile;
        // Off the main thread - the accumulated log can grow to MB over a session
        // and a synchronous write per event was historically a source of hitches.
        // Serialize writers and drop any snapshot older than what's already on disk,
        // so out-of-order Task completion can never clobber a newer log with a
        // stale (shorter) one - the final event always survives.
        Task.Run(() =>
        {
            lock (_writeLock)
            {
                if (version <= _lastWritten) return;
                try { File.WriteAllText(f, snapshot); _lastWritten = version; }
                catch { /* never break a request */ }
            }
        });
    }

    private static bool LooksLikeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        s = s.TrimStart();
        return s.Length > 0 && (s[0] == '{' || s[0] == '[');
    }

    private static string JsonStr(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
#endif
}
