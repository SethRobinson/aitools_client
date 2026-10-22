using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using AITools.AIChat.Video;

namespace AITools.AIChat.Web
{
    /// <summary>Local, dedicated Firefox session. Never reads credentials into chat.</summary>
    public static class YouTubeLogin
    {
        public static string ProfileDirectory
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(local) || !Path.IsPathRooted(local))
                    throw new IOException("Local application data is unavailable.");
                return Path.Combine(local, "SethsAITools", "YouTubeFirefox");
            }
        }

        public static bool IsYouTubeUrl(string url)
        {
            if (!WebMediaDownloader.IsAllowedPublicHttpUrl(url, out _)
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            string host = uri.Host.TrimEnd('.');
            return host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
                || host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
        }

        public static bool NeedsSignIn(string url, YtDlpTool.Result result)
        {
            if (result == null || result.Success || result.Cancelled || !IsYouTubeUrl(url)) return false;
            string error = (result.Error ?? "").Replace('\u2019', '\'').ToLowerInvariant();
            string diagnostics = ((result.Stderr ?? "") + "\n" + (result.Stdout ?? "")).ToLowerInvariant();
            // Do not infer authentication from HTTP 403/429, missing tracks, filters,
            // missing formats or a generic recommendation to pass cookies.
            return error.Contains("sign in") || error.Contains("sign-in")
                || error.Contains("log in") || error.Contains("login required")
                || error.Contains("authentication required") || error.Contains("confirm your age")
                || error.Contains("cookies are no longer valid") || error.Contains("cookies have expired")
                || diagnostics.Contains("cookies are no longer valid") || diagnostics.Contains("cookies have expired");
        }

        public static void ResolveCookies(string url, bool enabled, ref string browser, ref string file)
        {
            if (!enabled || !IsYouTubeUrl(url)) return;
            browser = "firefox:" + ProfileDirectory;
            file = "";
        }

        public static string RedactProfile(string diagnostic)
        {
            if (string.IsNullOrEmpty(diagnostic)) return diagnostic;
            try
            {
                string profile = ProfileDirectory;
                return diagnostic.Replace(profile.Replace("\\", "\\\\"), "[dedicated YouTube profile]")
                    .Replace(profile.Replace('\\', '/'), "[dedicated YouTube profile]")
                    .Replace(profile, "[dedicated YouTube profile]");
            }
            catch { return diagnostic; }
        }

        public static bool TryFindFirefox(out string executable)
        {
            foreach (string root in new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                string candidate = Path.Combine(root, "Mozilla Firefox", "firefox.exe");
                if (File.Exists(candidate)) { executable = candidate; return true; }
            }
            foreach (string root in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root.Trim('"'))) continue;
                    string candidate = Path.Combine(root.Trim('"'), "firefox.exe");
                    if (File.Exists(candidate)) { executable = candidate; return true; }
                }
                catch { }
            }
            executable = null;
            return false;
        }

        public static string BuildFirefoxArguments(string profile)
        {
            return "-no-remote -profile " + FfmpegTool.QuoteArg(profile)
                + " -new-window https://www.youtube.com/";
        }

        public static bool TryOpenFirefox(out string error)
        {
            error = null;
            if (!TryFindFirefox(out var executable))
            {
                error = "Firefox was not found. Install Firefox from mozilla.org, then try again.";
                return false;
            }
            try
            {
                Directory.CreateDirectory(ProfileDirectory);
                if (IsProfileOpen())
                {
                    error = "The YouTube Firefox window is already open. Finish signing in there, then close it.";
                    return false;
                }
                // This is a user-invoked interactive login window, never a background helper.
                using (var process = Process.Start(new ProcessStartInfo(executable, BuildFirefoxArguments(ProfileDirectory))
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(executable)
                }))
                {
                    if (process == null) throw new IOException();
                }
                return true;
            }
            catch
            {
                error = "Firefox could not open the dedicated YouTube profile. Check the Firefox installation and folder permissions.";
                return false;
            }
        }

        public static bool IsProfileOpen()
        {
            string path = Path.Combine(ProfileDirectory, "parent.lock");
            if (!File.Exists(path)) return false;
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return false;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        public static bool CanRetry(out string error)
        {
            error = null;
            try
            {
                if (IsProfileOpen())
                    error = "Close the dedicated Firefox window first so its session is saved, then click Done.";
                else if (!File.Exists(Path.Combine(ProfileDirectory, "cookies.sqlite")))
                    error = "Open Firefox and sign in to YouTube first, then close its window.";
            }
            catch { error = "The dedicated Firefox profile could not be accessed."; }
            return error == null;
        }

        public static string BuildTestArguments()
        {
            // A public video can succeed while signed out. Probe the private Liked
            // videos playlist, fetching metadata only and never exporting cookies.
            return "--ignore-config --no-cache-dir --skip-download --flat-playlist --playlist-end 1"
                + " --socket-timeout 15 --retries 0 --extractor-retries 0 --dump-single-json"
                + " --cookies-from-browser " + FfmpegTool.QuoteArg("firefox:" + ProfileDirectory)
                + " -- https://www.youtube.com/playlist?list=LL";
        }

        public static string DescribeTestResult(int exitCode, string stdout, string stderr)
        {
            string diagnostics = (stderr ?? "").ToLowerInvariant();
            if (diagnostics.Contains("cookies are no longer valid") || diagnostics.Contains("cookies have expired")
                || diagnostics.Contains("sign in") || diagnostics.Contains("not logged in")
                || diagnostics.Contains("login required") || diagnostics.Contains("playlist does not exist"))
                return "Not signed in, or the login has expired. Click Sign in / change account.";
            if (exitCode == 0)
            {
                try
                {
                    var data = SimpleJSON.JSON.Parse(stdout);
                    if (data != null && data["id"].Value == "LL")
                        return "Signed in: YouTube accepted the remembered Firefox session.";
                }
                catch { }
            }
            // Never show raw process output: it can contain account details or paths.
            return "Could not verify the login. Check your connection and yt-dlp installation, then try again.";
        }

        public static IEnumerator TestSession(FfmpegTool.CancelToken cancel, Action<string> onDone)
        {
            if (!CanRetry(out var error))
            {
                onDone(error.Replace("then click Done", "then click Test"));
                yield break;
            }
            if (!YtDlpTool.TryGetToolPath(out var executable, out _))
            {
                onDone("Cannot test: yt-dlp is missing. Restore utils/yt-dlp/yt-dlp.exe.");
                yield break;
            }
            string args = BuildTestArguments();
            var task = System.Threading.Tasks.Task.Run(() =>
                FfmpegTool.RunProcessCancellable(executable, args, 45000, cancel, null));
            while (!task.IsCompleted) yield return null;
            if (cancel.CancelRequested) yield break;
            onDone(task.IsFaulted ? "Could not start the YouTube login test. Check the yt-dlp installation."
                : DescribeTestResult(task.Result.ExitCode, task.Result.Stdout, task.Result.Stderr));
        }

        /// <summary>One offer and at most one identical retry. Delegates make the lifecycle testable offline.</summary>
        public static IEnumerator DownloadWithRetry(string url, FfmpegTool.CancelToken cancel,
            Func<Action<YtDlpTool.Result>, IEnumerator> download,
            Func<Action<bool>, IEnumerator> offerSignIn, Action<YtDlpTool.Result> onDone)
        {
            YtDlpTool.Result result = null;
            if (!cancel.CancelRequested) yield return download(r => result = r);
            result = result ?? new YtDlpTool.Result { Error = "No result from yt-dlp." };
            if (!cancel.CancelRequested && NeedsSignIn(url, result))
            {
                result.LoginOffered = true;
                bool retry = false;
                yield return offerSignIn(accepted => retry = accepted);
                if (!cancel.CancelRequested && retry)
                {
                    result = null;
                    yield return download(r => result = r);
                    result = result ?? new YtDlpTool.Result { Error = "No result from yt-dlp." };
                    result.LoginOffered = true;
                    if (!result.Success)
                        result.Error += " The retry after sign-in failed. Do not retry automatically.";
                }
                else if (!cancel.CancelRequested)
                    result.Error += " Sign-in was cancelled or unavailable. Do not retry automatically.";
            }
            if (cancel.CancelRequested)
            {
                result.Success = false;
                result.Cancelled = true;
                result.Error = "cancelled";
            }
            onDone(result);
        }
    }
}
