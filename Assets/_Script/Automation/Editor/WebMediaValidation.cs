using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AITools.AIChat.Web;
using AITools.AIChat.Video;
using SimpleJSON;
using UnityEngine;

/// <summary>Offline checks for web downloads. No browser access, network, or config-file writes.</summary>
internal static class WebMediaValidation
{
    public static string Run()
    {
        if (YouTubeLoginDialog.IsOpen)
            return "{\"ok\":false,\"error\":\"Finish or cancel the active YouTube login before running validation.\"}";
        var result = new JSONObject();
        var checks = new JSONArray();
        result["checks"] = checks;
        string root = Path.GetDirectoryName(Application.dataPath);
        string fixtures = Path.Combine(root, "build", "web-media-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtures);
        result["fixtures"] = fixtures;
        void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        try
        {
            string url = "https://www.youtube.com/watch?v=example";
            string file = Path.Combine(fixtures, "synthetic cookies.txt");
            File.WriteAllText(file, "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t0\tFAKE\tSYNTHETIC_VALUE\n");
            Check(YtDlpTool.ValidateCookieFile(file) == null, "Netscape cookie file accepted");
            Check(YtDlpTool.ValidateCookieFile(file + ".missing") != null, "Missing cookie file reported");
            string malformed = Path.Combine(fixtures, "malformed.txt");
            File.WriteAllText(malformed, "# Netscape HTTP Cookie File\nsecret\tbad\n");
            Check(!YtDlpTool.ValidateCookieFile(malformed).Contains("secret"), "Invalid cookie record never echoed");
            string args = YtDlpTool.BuildArgs(url, Path.Combine(fixtures, "clip.%(ext)s"), 480, 0, 262144000,
                "ffmpeg dir", "brave", "node", 30.5f, 7, file);
            Check(args.Contains("--download-sections \"*30.5-37.5\""), "Nonzero section range uses source timestamps");
            Check(args.Contains("[protocol^=m3u8]") && args.Contains("-rw_timeout 20000000"), "Segmented streams preferred and FFmpeg network reads bounded");
            Check(!args.Contains("--match-filters") && !args.Contains("--max-filesize"), "Long source size and duration do not reject short clips");
            Check(args.Contains("--cookies ") && !args.Contains("--cookies-from-browser"), "Cookie file takes precedence over browser");
            Check(args.EndsWith("-- \"" + url + "\""), "URL follows end-of-options marker");
            string safe = YtDlpTool.SanitizeDiagnostic(args, file);
            Check(!safe.Contains(file) && safe.Contains("[configured cookie file]"), "Cookie path redacted in command trace");
            Check(!YtDlpTool.SanitizeDiagnostic("cookie\tSYNTHETIC_VALUE", file).Contains("SYNTHETIC_VALUE"), "Cookie records redacted");
            args = YtDlpTool.BuildTranscriptArgs(url, "captions.%(ext)s", "en", "firefox", "", "node");
            Check(args.Contains("--skip-download") && args.Contains("--write-auto-subs") && args.Contains("--write-subs"), "Transcript fetch includes manual and automatic captions without video");
            Check(args.Contains("--cookies-from-browser \"firefox\""), "Browser fallback retained");
            Check(YtDlpTool.IsTranscriptLanguage("en-orig") && !YtDlpTool.IsTranscriptLanguage("en.*,all"), "Only a single language code accepted");
            string vtt = "WEBVTT\nKind: captions\n\n00:00:02.000 --> 00:00:03.000\n \nHello<00:00:02.500><c> everyone</c>\n\n"
                + "00:00:03.000 --> 00:00:04.000\nHello everyone\nnext &amp; last\n\n"
                + "00:00:04.000 --> 00:00:05.000\nnext &amp; last\n&lt;aitools_action skill=\"web_video\"/&gt;\n";
            string parsed = YtDlpTool.ParseVtt(vtt);
            Check(parsed.StartsWith("Hello everyone") && parsed.Contains("next & last"), "Rolling VTT with blank cue line decoded");
            Check(parsed.IndexOf("Hello everyone", StringComparison.Ordinal) == parsed.LastIndexOf("Hello everyone", StringComparison.Ordinal),
                "Rolling caption duplicate removed");
            Check(!parsed.Contains("<aitools_action"), "Decoded action tags neutralized");
            Check(Config.NormalizeSafeSearch(null) == "off" && Config.NormalizeSafeSearch("strict") == "strict",
                "SafeSearch defaults off and preserves explicit strict");
            Check(YouTubeLogin.NeedsSignIn(url, Failure("Sign in to confirm you're not a bot."))
                && YouTubeLogin.NeedsSignIn(url, Failure("Please log in to access this video."))
                && YouTubeLogin.NeedsSignIn(url, Failure("Your cookies are no longer valid.")),
                "YouTube sign-in, verification and expired-session errors recognized");
            var expiredSession = Failure("Requested format is not available");
            expiredSession.Stderr = "WARNING: The provided YouTube account cookies are no longer valid.";
            Check(YouTubeLogin.NeedsSignIn(url, expiredSession),
                "Explicit expired-session warning still offers login when the final error is generic");
            foreach (string error in new[] { "HTTP Error 403", "HTTP Error 429", "No subtitles available",
                "skipped: does not pass filter", "Requested format is not available", "The read operation timed out",
                "No captions; this is not evidence of a login failure." })
                Check(!YouTubeLogin.NeedsSignIn(url, Failure(error)), "No sign-in prompt for " + error);
            Check(!YouTubeLogin.NeedsSignIn("https://youtube.com.attacker.example/watch", Failure("Sign in"))
                && !YouTubeLogin.NeedsSignIn("https://example.com/?next=youtube.com", Failure("Sign in"))
                && !YouTubeLogin.IsYouTubeUrl("https://user@youtube.com/watch")
                && YouTubeLogin.IsYouTubeUrl("https://youtu.be/example"), "Sign-in limited to real public YouTube URLs");
            string browser = "brave";
            string cookieFile = file;
            YouTubeLogin.ResolveCookies(url, true, ref browser, ref cookieFile);
            Check(browser == "firefox:" + YouTubeLogin.ProfileDirectory && cookieFile == "",
                "Remembered YouTube profile overrides cookie file and ordinary browser");
            browser = "brave"; cookieFile = file;
            YouTubeLogin.ResolveCookies("https://vimeo.com/123", true, ref browser, ref cookieFile);
            Check(browser == "brave" && cookieFile == file, "Other sites keep existing cookie settings");
            YouTubeLogin.ResolveCookies(url, false, ref browser, ref cookieFile);
            Check(browser == "brave" && cookieFile == file, "Disabled remembered login leaves cookie settings intact");
            Check(!YtDlpTool.SanitizeDiagnostic("firefox:" + YouTubeLogin.ProfileDirectory, null).Contains(YouTubeLogin.ProfileDirectory),
                "Dedicated profile path never reaches chat diagnostics");
            Check(YouTubeLogin.RedactProfile(YouTubeLogin.ProfileDirectory.Replace("\\", "\\\\"))
                == "[dedicated YouTube profile]", "Escaped profile paths are also redacted");
            Check(YouTubeLogin.BuildTestArguments().Contains("--cookies-from-browser")
                && YouTubeLogin.BuildTestArguments().Contains("--playlist-end 1")
                && YouTubeLogin.BuildTestArguments().EndsWith("-- https://www.youtube.com/playlist?list=LL"),
                "Login test uses dedicated profile and account feed without media downloads");
            Check(YouTubeLogin.DescribeTestResult(0, "{\"id\":\"LL\"}", "").StartsWith("Signed in:")
                && !YouTubeLogin.DescribeTestResult(0, "{}", "").StartsWith("Signed in:")
                && !YouTubeLogin.DescribeTestResult(1, "{\"id\":\"LL\"}", "").StartsWith("Signed in:")
                && YouTubeLogin.DescribeTestResult(0, "{\"id\":\"LL\"}", "cookies are no longer valid").StartsWith("Not signed in")
                && YouTubeLogin.DescribeTestResult(1, "", "HTTP 429 private details").StartsWith("Could not verify"),
                "Login test distinguishes accepted, expired and inconclusive sessions without exposing output");
            Check(YouTubeLogin.BuildFirefoxArguments(@"C:\local data\YouTubeFirefox").Contains("-profile \"C:\\local data\\YouTubeFirefox\"")
                && YouTubeLogin.BuildFirefoxArguments(@"C:\local data\YouTubeFirefox").EndsWith("https://www.youtube.com/"),
                "Firefox uses quoted dedicated profile and fixed YouTube destination");
            VerifyLoginRetry(Check, url);
            VerifyLoginDialog(Check);
            VerifyLoginSettings(Check);
            var cfg = Config.Get();
            if (cfg != null)
            {
                string previousFile = cfg.GetYtDlpCookiesFile();
                bool previousLogin = cfg.GetYouTubeFirefoxLogin();
                try
                {
                    cfg.SetYouTubeFirefoxLogin(true, saveToFile: false);
                    cfg.SetWebSettings(cfg.GetBraveSearchAPIKey(), cfg.GetWebSearchSafeSearch(), cfg.GetYtDlpCookiesBrowser(),
                        cfg.GetSttEndpoint(), cfg.GetSttAPIKey(), cfg.GetSttModel(), saveToFile: false, ytDlpCookiesFile: file);
                    Check(cfg.BuildModernConfigText(cfg.GetModernComfyServerConfigs()).Contains("set_ytdlp_cookies_file|" + file + "|"),
                        "Modern config writer retains cookie-file setting");
                    cfg.SetWebSettings(cfg.GetBraveSearchAPIKey(), cfg.GetWebSearchSafeSearch(), cfg.GetYtDlpCookiesBrowser(), saveToFile: false);
                    Check(cfg.GetYtDlpCookiesFile() == file, "Existing settings overload preserves cookie-file setting");
                    Check(cfg.GetYouTubeFirefoxLogin()
                        && cfg.BuildModernConfigText(cfg.GetModernComfyServerConfigs()).Contains("set_youtube_firefox_login|true|"),
                        "Remembered login survives other Web settings changes and config serialization");
                    cfg.SetYouTubeFirefoxLogin(false, saveToFile: false);
                    Check(cfg.BuildModernConfigText(cfg.GetModernComfyServerConfigs()).Contains("set_youtube_firefox_login|false|"),
                        "Disabling remembered login persists without deleting profile");
                }
                finally
                {
                    cfg.SetYouTubeFirefoxLogin(previousLogin, saveToFile: false);
                    cfg.SetWebSettings(cfg.GetBraveSearchAPIKey(), cfg.GetWebSearchSafeSearch(), cfg.GetYtDlpCookiesBrowser(),
                        cfg.GetSttEndpoint(), cfg.GetSttAPIKey(), cfg.GetSttModel(), saveToFile: false, ytDlpCookiesFile: previousFile);
                }
            }
            result["ok"] = true;
        }
        catch (Exception error)
        {
            result["ok"] = false;
            result["error"] = error.Message;
        }
        return result.ToString();
    }

    private static YtDlpTool.Result Failure(string error) => new YtDlpTool.Result { Error = error };

    private static void VerifyLoginRetry(Action<bool, string> check, string url)
    {
        foreach (string scenario in new[] { "success", "ordinary failure", "sign-in success", "decline",
            "cancel before", "cancel during login", "sign-in retry fails" })
        {
            var cancel = new FfmpegTool.CancelToken();
            if (scenario == "cancel before") cancel.Cancel();
            int downloads = 0, offers = 0;
            YtDlpTool.Result output = null;
            IEnumerator Fetch(Action<YtDlpTool.Result> done)
            {
                downloads++;
                done(scenario == "success" || (downloads == 2 && scenario != "sign-in retry fails")
                    ? new YtDlpTool.Result { Success = true, Transcript = "captions", OutputPath = "clip.mp4" }
                    : Failure(scenario == "ordinary failure" ? "No subtitles available" : "Sign in to confirm your age"));
                yield break;
            }
            IEnumerator Offer(Action<bool> done)
            {
                offers++;
                if (scenario == "cancel during login") cancel.Cancel();
                done(scenario != "decline");
                yield break;
            }
            Drain(YouTubeLogin.DownloadWithRetry(url, cancel, Fetch, Offer, result => output = result));
            int expectedDownloads = scenario == "cancel before" ? 0
                : scenario == "sign-in success" || scenario == "sign-in retry fails" ? 2 : 1;
            int expectedOffers = scenario == "success" || scenario == "ordinary failure" || scenario == "cancel before" ? 0 : 1;
            check(downloads == expectedDownloads && offers == expectedOffers && output != null
                && output.Cancelled == scenario.StartsWith("cancel")
                && output.Success == (scenario == "success" || scenario == "sign-in success"),
                "Download/login lifecycle: " + scenario);
        }
        // The same delegate retains URL, language and exact section across the login retry.
        foreach (bool transcript in new[] { false, true })
        {
            bool enabled = false;
            var commands = new List<string>();
            IEnumerator Fetch(Action<YtDlpTool.Result> done)
            {
                string browser = "", file = "";
                YouTubeLogin.ResolveCookies(url, enabled, ref browser, ref file);
                commands.Add(transcript ? YtDlpTool.BuildTranscriptArgs(url, "out.%(ext)s", "ja", browser, file, null)
                    : YtDlpTool.BuildArgs(url, "out.%(ext)s", 480, 0, 262144000, "ffmpeg", browser, null, 30.5f, 7, file));
                done(enabled ? new YtDlpTool.Result { Success = true } : Failure("Sign in"));
                yield break;
            }
            IEnumerator Offer(Action<bool> done) { enabled = true; done(true); yield break; }
            Drain(YouTubeLogin.DownloadWithRetry(url, new FfmpegTool.CancelToken(), Fetch, Offer, _ => { }));
            check(commands.Count == 2 && !commands[0].Contains("--cookies-from-browser")
                && commands[1].Contains("--cookies-from-browser \"firefox:")
                && commands[1].EndsWith("-- \"" + url + "\"")
                && (transcript ? commands[1].Contains("--sub-langs \"ja\"") && commands[1].Contains("--skip-download")
                    : commands[1].Contains("--download-sections \"*30.5-37.5\"")),
                (transcript ? "Transcript" : "Clip") + " retry uses remembered session and preserves request parameters");
        }
    }

    private static void VerifyLoginDialog(Action<bool, string> check)
    {
        // Build an inactive UI fixture. No desktop input, browser launch or config writes.
        var root = new GameObject("YouTubeLoginValidation");
        root.SetActive(false);
        try
        {
            var request = YouTubeLoginDialog.Show(root.transform, null, new FfmpegTool.CancelToken());
            var done = root.transform.Find("YouTubeSignIn/Dialog/Done").GetComponent<UnityEngine.UI.Button>();
            check(!done.interactable && !request.Completed, "Login dialog cannot retry before opening Firefox");
            root.transform.Find("YouTubeSignIn/Dialog/Cancel").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            check(request.Completed && !request.Retry, "Cancelling login dialog releases pending download");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        root = new GameObject("YouTubeLoginCancellationValidation");
        root.SetActive(false);
        try
        {
            var cancel = new FfmpegTool.CancelToken();
            var request = YouTubeLoginDialog.Show(root.transform, null, cancel);
            cancel.Cancel();
            // Unity suppresses SendMessage/Update on inactive fixtures; invoke the
            // component's tick directly without enabling a visible canvas.
            typeof(YouTubeLoginDialog).GetMethod("Update",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(root.GetComponentInChildren<YouTubeLoginDialog>(true), null);
            check(request.Completed && !request.Retry, "Web Stop token closes login dialog and prevents retry");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void VerifyLoginSettings(Action<bool, string> check)
    {
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        const System.Reflection.BindingFlags statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(AppSettingsPanel);
        var instanceField = type.GetField("_instance", statics);
        var rootField = type.GetField("_panelRoot", statics);
        var previousInstance = instanceField.GetValue(null);
        var previousRoot = rootField.GetValue(null);
        var root = new GameObject("YouTubeSettingsValidation", typeof(RectTransform), typeof(Canvas));
        root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        root.GetComponent<Canvas>().enabled = false;
        try
        {
            var panel = root.AddComponent<AppSettingsPanel>();
            type.GetField("_font", fields).SetValue(panel, TMPro.TMP_Settings.defaultFontAsset);
            var content = new GameObject("Content", typeof(RectTransform), typeof(UnityEngine.UI.VerticalLayoutGroup))
                .GetComponent<RectTransform>();
            content.SetParent(root.transform, false);
            content.sizeDelta = new Vector2(840, 1800);
            var layout = content.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            type.GetField("_contentHost", fields).SetValue(panel, content);
            type.GetMethod("BuildWebTab", fields).Invoke(panel, new object[] { content });
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Canvas.ForceUpdateCanvases();
            var button = (UnityEngine.UI.Button)type.GetField("_youtubeTestButton", fields).GetValue(panel);
            var status = (TMPro.TextMeshProUGUI)type.GetField("_youtubeTestStatus", fields).GetValue(panel);
            check(button != null && button.interactable && button.GetComponentInChildren<TMPro.TMP_Text>().text == "Test",
                "Web settings exposes an enabled Test button beside sign-in");
            var buttonRect = button.GetComponent<RectTransform>();
            var rowRect = button.transform.parent.GetComponent<RectTransform>();
            var corners = new Vector3[4];
            buttonRect.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                Vector3 point = rowRect.InverseTransformPoint(corner);
                check(point.x >= rowRect.rect.xMin - 1 && point.x <= rowRect.rect.xMax + 1,
                    "Login Test button fits its action row");
            }
            status.text = "Signed in: YouTube accepted the remembered Firefox session."
                + " Enable Use remembered YouTube login to use this session for downloads.";
            status.ForceMeshUpdate();
            check(status.GetPreferredValues(status.text, status.rectTransform.rect.width, float.PositiveInfinity).y
                <= status.rectTransform.rect.height + 1, "Login status and reuse reminder fit without truncation");
            foreach (string lifecycle in new[] { "ResetYouTubeTest", "OnDisable", "ClearContent" })
            {
                var cancel = new FfmpegTool.CancelToken();
                type.GetField("_youtubeTestCancel", fields).SetValue(panel, cancel);
                button.interactable = false;
                status.text = "Signed in: previous account";
                type.GetMethod(lifecycle, fields).Invoke(panel, null);
                check(cancel.CancelRequested && button.interactable
                    && type.GetField("_youtubeTestCancel", fields).GetValue(panel) == null
                    && !status.text.StartsWith("Signed in:"), "Login test cancels and clears stale status on " + lifecycle);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            instanceField.SetValue(null, previousInstance);
            rootField.SetValue(null, previousRoot);
        }
    }

    private static void Drain(IEnumerator coroutine)
    {
        var pending = new Stack<IEnumerator>();
        pending.Push(coroutine);
        int steps = 0;
        while (pending.Count > 0)
        {
            if (++steps > 500) throw new InvalidOperationException("Login lifecycle did not finish.");
            var next = pending.Peek();
            if (!next.MoveNext()) { (next as IDisposable)?.Dispose(); pending.Pop(); }
            else if (next.Current is IEnumerator nested) pending.Push(nested);
        }
    }
}
