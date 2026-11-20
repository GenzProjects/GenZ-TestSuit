// GenZ-TestSuit-v1.0.1
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GenZ.TestSuit
{
    /// <summary>What `aapt2 dump badging` says about an APK.</summary>
    [Serializable]
    public class ApkInfo
    {
        public string path, package, versionName, launchActivity;
        public int versionCode, minSdk, targetSdk;
        public bool debuggable;
        public List<string> abis = new List<string>(), permissions = new List<string>();

        public static ApkInfo FromBadging(string path, string badging)
        {
            var a = new ApkInfo { path = path };
            foreach (var raw in (badging ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("package:"))
                {
                    a.package = Attr(line, "name");
                    a.versionName = Attr(line, "versionName");
                    int.TryParse(Attr(line, "versionCode"), out a.versionCode);
                }
                else if (line.StartsWith("minSdkVersion:") || line.StartsWith("sdkVersion:")) int.TryParse(Quoted(line), out a.minSdk);
                else if (line.StartsWith("targetSdkVersion:")) int.TryParse(Quoted(line), out a.targetSdk);
                else if (line.StartsWith("uses-permission:")) a.permissions.Add(Attr(line, "name"));
                else if (line.StartsWith("application-debuggable")) a.debuggable = true;
                else if (line.StartsWith("launchable-activity:")) a.launchActivity = Attr(line, "name");
                else if (line.StartsWith("native-code:"))
                    a.abis.AddRange(Regex.Matches(line, "'([^']+)'").Cast<Match>().Select(m => m.Groups[1].Value));
            }
            return a;
        }

        /// <summary>
        /// The installed app, from `adb shell dumpsys package &lt;package&gt;` (when no APK file is given).
        /// Null when the package isn't installed.
        /// </summary>
        public static ApkInfo FromDumpsys(string package, string dumpsys)
        {
            dumpsys = dumpsys ?? "";
            if (!dumpsys.Contains("Package [" + package + "]")) return null;
            var a = new ApkInfo { package = package, path = "" };
            var m = Regex.Match(dumpsys, @"versionCode=(\d+)");
            if (m.Success) a.versionCode = int.Parse(m.Groups[1].Value);
            m = Regex.Match(dumpsys, @"minSdk=(\d+)");
            if (m.Success) a.minSdk = int.Parse(m.Groups[1].Value);
            m = Regex.Match(dumpsys, @"targetSdk=(\d+)");
            if (m.Success) a.targetSdk = int.Parse(m.Groups[1].Value);
            m = Regex.Match(dumpsys, @"versionName=(\S+)");
            if (m.Success) a.versionName = m.Groups[1].Value;
            m = Regex.Match(dumpsys, @"\bflags=\[([^\]]*)\]");
            a.debuggable = m.Success && m.Groups[1].Value.Contains("DEBUGGABLE");
            m = Regex.Match(dumpsys, @"primaryCpuAbi=(\S+)");
            if (m.Success && m.Groups[1].Value != "null") a.abis.Add(m.Groups[1].Value);

            // "requested permissions:" then one indented permission per line, until the next "...:" header
            var lines = dumpsys.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            int start = lines.FindIndex(l => l.Trim() == "requested permissions:");
            if (start >= 0)
                for (int i = start + 1; i < lines.Count; i++)
                {
                    var t = lines[i].Trim();
                    if (t.Length == 0 || t.EndsWith(":")) break;
                    a.permissions.Add(t.Split(':')[0].Trim());
                }
            return a;
        }

        static string Attr(string line, string name)
        {
            var m = Regex.Match(line, @"\b" + name + "='([^']*)'");
            return m.Success ? m.Groups[1].Value : "";
        }

        static string Quoted(string line)
        {
            var m = Regex.Match(line, "'([^']*)'");
            return m.Success ? m.Groups[1].Value : "";
        }
    }

    /// <summary>The phone the session runs on, and how the install and launch went.</summary>
    [Serializable]
    public class DeviceInfo
    {
        public string serial, model, android, abis;
        public int sdk;
        public bool installed;
        public string installOutput;
        public int installedVersionCode;
        public int launchMs = -1;
        public string launchOutput;
    }

    /// <summary>The game under test (picked from the sheet), and its version when it's this project (Player Settings).</summary>
    public class ExpectedBuild
    {
        public string package, gameName;
        public string versionName; // null = another game than this project: the version isn't compared
        public int versionCode;
        public string taichiImpressions; // the sheet's Taichi 2.0 count for this game, or null
        public string certSha256;        // the release key's certificate (lower-case hex), or null when unknown
        public string certSource;        // "GamesToPlay.keystore (gamestoplay)" — where certSha256 came from
        public string certNote;          // why certSha256 is unknown
    }

    /// <summary>One logcat line (-v threadtime).</summary>
    public class LogLine
    {
        public string time, tag, msg;
        public int pid;
        public char level;
    }

    /// <summary>A "[GenZ] sdk|what|STATUS|detail" line written by GenZLog in the ad scripts.</summary>
    public class GenZLine
    {
        public string sdk, what, status, detail, time;
        public bool Ok => status == "OK";
        public bool Failed => status == "FAIL";
    }

    /// <summary>
    /// The APK suite's checks: the APK file (aapt2 / apksigner output) and the play session (logcat).
    /// No UnityEditor dependency, so the console harness can run them on a saved log.
    /// </summary>
    public static class GenZApkChecks
    {
        public const string GenZPrefix = "[GenZ] ";

        static readonly Regex LineRx = new Regex(@"^(\d\d-\d\d\s+\d\d:\d\d:\d\d\.\d+)\s+(\d+)\s+(\d+)\s+([VDIWEFA])\s+(.*?)\s*:\s?(.*)$", RegexOptions.Compiled);

        public static List<LogLine> ParseLogcat(IEnumerable<string> lines)
        {
            var list = new List<LogLine>();
            foreach (var raw in lines)
            {
                var m = LineRx.Match(raw.TrimEnd('\r'));
                if (!m.Success) continue;
                list.Add(new LogLine
                {
                    time = m.Groups[1].Value,
                    pid = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    level = m.Groups[4].Value[0],
                    tag = m.Groups[5].Value.Trim(),
                    msg = m.Groups[6].Value
                });
            }
            return list;
        }

        public static List<GenZLine> GenZLines(IEnumerable<LogLine> log)
        {
            var list = new List<GenZLine>();
            foreach (var l in log)
            {
                int i = l.msg.IndexOf(GenZPrefix, StringComparison.Ordinal);
                if (i < 0) continue;
                var parts = l.msg.Substring(i + GenZPrefix.Length).Split(new[] { '|' }, 4);
                if (parts.Length < 3) continue;
                list.Add(new GenZLine { sdk = parts[0], what = parts[1], status = parts[2], detail = parts.Length > 3 ? parts[3].Trim() : "", time = l.time });
            }
            return list;
        }

        // ---------------------------------------------------------------- Step 1 · Build & Device

        /// <param name="fromApk">True: <paramref name="apk"/> was read from an APK file (aapt2). False: from the app installed on the phone (dumpsys); then there's no file to check the signature of.</param>
        public static void CheckBuild(TestReport report, ApkInfo apk, ExpectedBuild expected, ApkSettings s, string signerOutput, bool fromApk = true)
        {
            var sec = report.Section(fromApk ? "APK file" : "Installed game", TestStage.ApkBuild);
            sec.Add("Game", CheckStatus.Info, string.IsNullOrEmpty(expected.gameName) ? expected.package : $"{expected.gameName} · {expected.package}",
                fromApk ? apk.path : "Already installed on the phone; no APK file");
            if (string.IsNullOrEmpty(apk.package))
            {
                sec.Add("Read with aapt2", CheckStatus.Fail, "", "aapt2 couldn't read the APK: it's missing, not an APK, or the Android build-tools aren't installed.");
                return;
            }

            sec.Add("Package", apk.package == expected.package ? CheckStatus.Pass : CheckStatus.Fail, apk.package,
                apk.package == expected.package ? "The selected game" : $"The selected game is {expected.package}: this APK is another game");
            if (expected.versionName == null)
                sec.Add("Version", CheckStatus.Info, $"{apk.versionName} ({apk.versionCode})", "Not compared: the selected game isn't this Unity project");
            else
            {
                bool sameVersion = apk.versionName == expected.versionName && apk.versionCode == expected.versionCode;
                sec.Add("Version", sameVersion ? CheckStatus.Pass : CheckStatus.Fail, $"{apk.versionName} ({apk.versionCode})",
                    sameVersion ? "Same as Player Settings" : $"Player Settings say {expected.versionName} ({expected.versionCode}): " + (fromApk ? "rebuild, or pick the right APK" : "install the new build"));
            }
            sec.Add("Not debuggable", apk.debuggable ? CheckStatus.Fail : CheckStatus.Pass, apk.debuggable ? "debuggable" : "release",
                apk.debuggable ? "This is a Development Build. Test the release build." : "");
            sec.Add("Target SDK", apk.targetSdk >= s.minTargetSdk ? CheckStatus.Pass : CheckStatus.Fail, apk.targetSdk.ToString(),
                apk.targetSdk >= s.minTargetSdk ? $"The studio needs {s.minTargetSdk} or higher" : $"Below the studio's target API level {s.minTargetSdk}: set Target API Level in Player Settings and rebuild");
            bool arm64 = apk.abis.Contains("arm64-v8a");
            sec.Add("64-bit (arm64-v8a)", arm64 ? CheckStatus.Pass : CheckStatus.Fail, string.Join(" ", apk.abis),
                arm64 ? "" : "Google Play needs arm64-v8a: Player Settings → Target Architectures (IL2CPP)");
            foreach (var p in s.requiredPermissions)
            {
                bool has = apk.permissions.Contains(p);
                sec.Add("Permission " + p.Substring(p.LastIndexOf('.') + 1), has ? CheckStatus.Pass : CheckStatus.Fail, has ? "present" : "missing",
                    has ? "" : p + " isn't in the merged manifest");
            }

            if (string.IsNullOrEmpty(signerOutput))
                sec.Add("Signing", CheckStatus.Warn, "", fromApk ? "apksigner couldn't run, so the certificate wasn't checked"
                                                                 : "The installed game's APK couldn't be copied from the phone, so its signature wasn't checked");
            else
            {
                var dn = Regex.Match(signerOutput, @"certificate DN:\s*(.+)");
                var sha = Regex.Match(signerOutput, @"certificate SHA-256 digest:\s*([0-9a-fA-F]+)");
                bool debugCert = dn.Success && dn.Groups[1].Value.Contains("CN=Android Debug");
                if (!dn.Success)
                    sec.Add("Signing", CheckStatus.Fail, "", "Not signed, or apksigner failed: " + FirstLine(signerOutput));
                else if (debugCert)
                    sec.Add("Signing", CheckStatus.Fail, dn.Groups[1].Value.Trim(), "Signed with the debug key: set the release keystore in Player Settings → Publishing Settings");
                else
                {
                    sec.Add("Signing", CheckStatus.Pass, dn.Groups[1].Value.Trim(), sha.Success ? "SHA-256 " + sha.Groups[1].Value : "");
                    // The right keystore: the APK's signer must be the release key's certificate.
                    var signer = sha.Success ? sha.Groups[1].Value.ToLowerInvariant() : null;
                    if (string.IsNullOrEmpty(expected.certSha256))
                        sec.Add("Signed with the right keystore", CheckStatus.Info, "", "Not compared: " + (expected.certNote ?? "the expected keystore is unknown"));
                    else if (signer == null)
                        sec.Add("Signed with the right keystore", CheckStatus.Warn, "", "apksigner printed no SHA-256 digest");
                    else if (signer == expected.certSha256)
                        sec.Add("Signed with the right keystore", CheckStatus.Pass, expected.certSource, "The APK's certificate is this keystore's");
                    else
                        sec.Add("Signed with the right keystore", CheckStatus.Fail, expected.certSource,
                            $"Signed with another key (SHA-256 {signer}), not {expected.certSource} (SHA-256 {expected.certSha256}). Rebuild with the right keystore in Publishing Settings");
                }
            }
        }

        public static void CheckDevice(TestReport report, DeviceInfo d, ApkInfo apk)
        {
            var sec = report.Section("Device", TestStage.ApkBuild);
            sec.Add("Phone", CheckStatus.Info, $"{d.model} · Android {d.android} (API {d.sdk})", d.serial + (string.IsNullOrEmpty(d.abis) ? "" : " · " + d.abis));
            if (!d.installed)
            {
                sec.Add("Installed", CheckStatus.Fail, "", "Install failed: " + FirstLine(d.installOutput) + InstallHint(d.installOutput));
                return;
            }
            sec.Add("Installed", d.installedVersionCode == apk.versionCode ? CheckStatus.Pass : CheckStatus.Fail, "versionCode " + d.installedVersionCode,
                d.installedVersionCode == apk.versionCode ? "This APK is the installed version" : $"The phone has versionCode {d.installedVersionCode}, the APK {apk.versionCode}");
            if (d.launchMs >= 0)
                sec.Add("Launched", CheckStatus.Pass, $"{d.launchMs} ms", "Cold start time reported by the phone");
            else
                sec.Add("Launched", CheckStatus.Warn, "", "The launch wasn't confirmed: " + FirstLine(d.launchOutput));
        }

        static string InstallHint(string output)
        {
            output = output ?? "";
            if (output.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE")) return ". The installed app has another signature: turn on Fresh Install.";
            if (output.Contains("INSTALL_FAILED_INSUFFICIENT_STORAGE")) return ". Free some space on the phone.";
            if (output.Contains("INSTALL_FAILED_NO_MATCHING_ABIS")) return ". The APK has no library for this phone's CPU.";
            return "";
        }

        // ---------------------------------------------------------------- Steps 2–5 · the play session

        /// <summary>
        /// Checks the session log. While recording (<paramref name="final"/> false) missing evidence is "waiting",
        /// not a failure; on Finish it counts.
        /// </summary>
        public static void CheckSession(TestReport report, List<LogLine> log, ApkInfo apk, ApkSettings s, TaichiSettings taichi, bool final, string sheetTaichi = null)
        {
            var genz = GenZLines(log);
            // GenZ Ads v1.32 builds have no GenZLog: the game ran (Unity lines) but wrote no [GenZ] line. Then the checks that
            // read [GenZ] lines (SDK starts, server answers, impressions, revenue reports) are skipped, the others still run.
            bool legacy = final && genz.Count == 0 && log.Any(l => l.tag == "Unity");
            CheckNotTestBuild(report, genz, final);
            if (legacy) ReportLegacyBuild(report);
            else CheckSdkInit(report, genz, s, final);
            CheckFirebase(report, log, s, final);
            CheckGameAnalytics(report, log, genz, apk, s, final);
            var impressions = genz.Where(g => g.sdk == "Ads" && g.what == "impression").ToList();
            CheckAdjust(report, log, genz, impressions, s, final, legacy);
            CheckAppMetrica(report, genz, impressions, final, legacy);
            CheckAds(report, log, genz, impressions, s, taichi, final, sheetTaichi, legacy);
            CheckStability(report, log, apk, final);
        }

        const string NeedsV133 = "Needs the [GenZ] log lines of GenZ Ads v1.33 (GenZLog); this build has none, so it's read as a v1.32 build";

        static void ReportLegacyBuild(TestReport report)
        {
            report.Section("SDK initialization", TestStage.SdkInit).Add("GenZ Ads package", CheckStatus.Warn, "v1.32 build (no [GenZ] lines)",
                "The game ran but wrote no [GenZ] lines, so it's checked as a GenZ Ads v1.32 build: SDK starts, server answers, impressions and revenue "
                + "reports can't be read and are skipped; Firebase, GameAnalytics, Taichi events and crashes are still checked. "
                + "If this game uses v1.33, GenZLog isn't working: check GenZLog.cs and its calls are in AdsScripts-GenzAds.");
        }

        /// <summary>A build made in GenZ-DeepTestSuit's Test Environment writes "[GenZ] DeepTest|build|…" when it starts.</summary>
        static void CheckNotTestBuild(TestReport report, List<GenZLine> genz, bool final)
        {
            bool test = genz.Any(g => g.sdk == "DeepTest");
            if (!test && !final) return;
            report.Section("Release build", TestStage.ApkBuild).Add("Not a Test Environment build", test ? CheckStatus.Fail : CheckStatus.Pass,
                test ? "test build" : "release",
                test ? "Built with GenZ-DeepTestSuit's Test Environment (shake panel, verbose logs, Adjust sandbox). Apply Release Environment and rebuild." : "");
        }

        static CheckStatus Missing(bool final, CheckStatus whenFinal = CheckStatus.Fail) => final ? whenFinal : CheckStatus.Info;
        static string Waiting(bool final, string message) => final ? message : "Waiting…";

        static void CheckSdkInit(TestReport report, List<GenZLine> genz, ApkSettings s, bool final)
        {
            var sec = report.Section("SDK initialization", TestStage.SdkInit);
            if (genz.Count == 0)
            {
                sec.Add("[GenZ] logs", Missing(final), "none",
                    Waiting(final, "The build wrote no [GenZ] lines. Either the game didn't start, or its ad scripts are older than the GenZLog update (copy GenZLog.cs and its calls from AdsScripts-GenzAds)."));
                return;
            }

            foreach (var sdk in s.requiredSdks.Concat(s.optionalSdks))
            {
                bool optional = !s.requiredSdks.Contains(sdk);
                var lines = genz.Where(g => g.sdk == sdk && g.what == "init").ToList();
                var ok = lines.LastOrDefault(g => g.Ok);
                var fail = lines.LastOrDefault(g => g.Failed);
                if (ok != null)
                    sec.Add(sdk, fail != null ? CheckStatus.Warn : CheckStatus.Pass, ok.detail,
                        fail != null ? "Started after an error: " + fail.detail : "Started without errors");
                else if (fail != null)
                    sec.Add(sdk, optional ? CheckStatus.Warn : CheckStatus.Fail, fail.detail, "Failed to start");
                else
                {
                    var info = lines.LastOrDefault();
                    sec.Add(sdk, optional ? Missing(final, CheckStatus.Warn) : Missing(final), info?.detail ?? "",
                        Waiting(final, optional ? "No start reported (optional)" : "No start reported: the SDK didn't start or never answered"));
                }
            }

            var adapters = genz.Where(g => g.sdk == "AdMob" && g.what == "adapter")
                               .GroupBy(g => g.detail.Split(' ')[0]).Select(g => g.Last()).ToList();
            if (adapters.Count > 0)
            {
                var asec = report.Section("AdMob mediation adapters", TestStage.SdkInit);
                foreach (var a in adapters)
                {
                    int sp = a.detail.IndexOf(' ');
                    string name = sp < 0 ? a.detail : a.detail.Substring(0, sp);
                    string desc = sp < 0 ? "" : a.detail.Substring(sp + 1);
                    asec.Add(name.Substring(name.LastIndexOf('.') + 1), a.Ok ? CheckStatus.Pass : CheckStatus.Warn, a.Ok ? "Ready" : "Not ready", desc);
                }
            }

            var values = genz.LastOrDefault(g => g.sdk == "RemoteConfig" && g.what == "values");
            if (values != null) sec.Add("Remote Config values", CheckStatus.Info, values.detail, "Applied after fetch");
        }

        static void CheckFirebase(TestReport report, List<LogLine> log, ApkSettings s, bool final)
        {
            var sec = report.Section("Firebase", TestStage.Events);
            var fa = log.Where(l => l.tag == "FA" || l.tag == "FA-SVC").ToList();
            var eventRx = new Regex(s.firebaseEventPattern);
            var events = fa.Select(l => eventRx.Match(l.msg)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();

            if (events.Count == 0)
            {
                sec.Add("Events logged", Missing(final), "0",
                    Waiting(final, "No Firebase event in the log. Firebase didn't start, or the debug property wasn't set before launch."));
            }
            else
                sec.Add("Events logged", CheckStatus.Pass, events.Count.ToString(), Summary(events));

            foreach (var e in s.firebaseEvents)
                sec.Add($"Sends \"{e}\"", events.Contains(e) ? CheckStatus.Pass : Missing(final, CheckStatus.Warn), events.Count(x => x == e).ToString(),
                    events.Contains(e) ? "" : Waiting(final, "Not logged in this session"));

            var okRx = new Regex(s.firebaseUploadOkPattern);
            var failRx = new Regex(s.firebaseUploadFailPattern);
            int uploads = fa.Count(l => okRx.IsMatch(l.msg));
            var failed = fa.Where(l => failRx.IsMatch(l.msg)).ToList();
            if (failed.Count > 0)
                sec.Add("Uploaded to Firebase", uploads > 0 ? CheckStatus.Warn : CheckStatus.Fail, $"{uploads} ok, {failed.Count} failed", failed.Last().msg);
            else if (uploads > 0)
                sec.Add("Uploaded to Firebase", CheckStatus.Pass, uploads + " upload" + (uploads == 1 ? "" : "s"), "Firebase's server accepted the events (also visible in DebugView)");
            else
                sec.Add("Uploaded to Firebase", Missing(final, CheckStatus.Warn), "0", Waiting(final, "No upload seen. Check the phone has internet, and play a little longer."));
        }

        static void CheckGameAnalytics(TestReport report, List<LogLine> log, List<GenZLine> genz, ApkInfo apk, ApkSettings s, bool final)
        {
            var sec = report.Section("GameAnalytics", TestStage.Events);
            var init = genz.LastOrDefault(g => g.sdk == "GameAnalytics" && g.what == "init");
            var build = init == null ? null : Regex.Match(init.detail, @"build=(\S*)");
            if (build != null && build.Success)
                sec.Add("Build sent = APK version", build.Groups[1].Value == apk.versionName ? CheckStatus.Pass : CheckStatus.Fail, build.Groups[1].Value,
                    build.Groups[1].Value == apk.versionName ? "Filter the GA realtime dashboard by this build" : $"The APK is {apk.versionName}: turn on Send Version in GA Settings, or fix the Build field");

            var ga = log.Where(l => l.tag == s.gaTag).ToList();
            var buildRx = new Regex(s.gaBuildPattern);
            var builds = ga.Select(l => buildRx.Match(l.msg)).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().ToList();
            if (builds.Count > 0)
                sec.Add("Build in GA events", builds.All(b => b == apk.versionName) ? CheckStatus.Pass : CheckStatus.Fail, string.Join(", ", builds),
                    builds.All(b => b == apk.versionName) ? "" : $"Events carry another build than the APK's {apk.versionName}");

            if (ga.Count == 0)
            {
                sec.Add("SDK log", Missing(final, CheckStatus.Warn), "0 lines",
                    Waiting(final, "GameAnalytics wrote nothing. Turn on Info Log Build in GA Settings to see its events here."));
                return;
            }
            var eventRx = new Regex(s.gaEventPattern);
            int events = ga.Count(l => eventRx.IsMatch(l.msg));
            sec.Add("Events sent", events > 0 ? CheckStatus.Pass : Missing(final, CheckStatus.Warn), events.ToString(),
                events > 0 ? $"{ga.Count} GameAnalytics log lines" : Waiting(final, "No GA event line matched gaEventPattern; check the log and adjust the pattern in the config"));
            var errRx = new Regex(s.gaErrorPattern);
            var errors = ga.Where(l => l.level == 'E' || l.level == 'W' || errRx.IsMatch(l.msg)).Select(l => l.msg).ToList();
            sec.Add("No GA errors", errors.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail, errors.Count.ToString(), errors.Count == 0 ? "" : FirstFew(errors));
        }

        static void CheckAdjust(TestReport report, List<LogLine> log, List<GenZLine> genz, List<GenZLine> impressions, ApkSettings s, bool final, bool legacy)
        {
            var sec = report.Section("Adjust", TestStage.Events);
            var errors = log.Where(l => l.tag == s.adjustTag && (l.level == 'E' || l.level == 'W')).Select(l => l.msg).ToList();
            if (legacy)
            {
                sec.Add("Server answer and ad revenue", CheckStatus.Info, "v1.32 build", NeedsV133);
                if (errors.Count > 0) sec.Add("SDK errors", CheckStatus.Warn, errors.Count.ToString(), FirstFew(errors));
                return;
            }
            var init = genz.LastOrDefault(g => g.sdk == "Adjust" && g.what == "init");
            sec.Add("Server answered", init == null ? Missing(final) : init.Ok ? CheckStatus.Pass : CheckStatus.Fail, init?.detail ?? "",
                init == null ? Waiting(final, "No answer reported (GenZLog.ProbeServers waits up to 60 s)") : init.Ok ? "Adjust tracked the session" : "");
            int sent = genz.Count(g => g.sdk == "Adjust" && g.what == "ad_revenue");
            sec.Add("Ad revenue sent", RevenueStatus(sent, impressions.Count, final), $"{sent} of {impressions.Count} impressions", RevenueMessage("Adjust", sent, impressions.Count, final));
            if (errors.Count > 0) sec.Add("SDK errors", CheckStatus.Warn, errors.Count.ToString(), FirstFew(errors));
        }

        static void CheckAppMetrica(TestReport report, List<GenZLine> genz, List<GenZLine> impressions, bool final, bool legacy)
        {
            var sec = report.Section("AppMetrica", TestStage.Events);
            if (legacy) { sec.Add("Server answer, events and ad revenue", CheckStatus.Info, "v1.32 build", NeedsV133); return; }
            var init = genz.LastOrDefault(g => g.sdk == "AppMetrica" && g.what == "init");
            sec.Add("Server answered", init == null ? Missing(final) : init.Ok ? CheckStatus.Pass : CheckStatus.Fail, init?.detail ?? "",
                init == null ? Waiting(final, "No answer reported (GenZLog.ProbeServers waits up to 60 s)") : init.Ok ? "AppMetrica's startup server answered" : "");
            var events = genz.Where(g => g.sdk == "AppMetrica" && g.what == "event").Select(g => g.detail).ToList();
            sec.Add("Events sent", events.Count > 0 ? CheckStatus.Pass : Missing(final, CheckStatus.Warn), events.Count.ToString(),
                events.Count > 0 ? Summary(events) : Waiting(final, "No event in this session: play a level"));
            int sent = genz.Count(g => g.sdk == "AppMetrica" && g.what == "ad_revenue");
            sec.Add("Ad revenue sent", RevenueStatus(sent, impressions.Count, final), $"{sent} of {impressions.Count} impressions", RevenueMessage("AppMetrica", sent, impressions.Count, final));
        }

        static CheckStatus RevenueStatus(int sent, int impressions, bool final) =>
            impressions == 0 ? Missing(final, CheckStatus.Warn) : sent >= impressions ? CheckStatus.Pass : CheckStatus.Fail;

        static string RevenueMessage(string sdk, int sent, int impressions, bool final) =>
            impressions == 0 ? Waiting(final, "No ad impression in this session") :
            sent >= impressions ? "Every impression reported" : $"{impressions - sent} impression(s) not reported to {sdk}";

        static void CheckAds(TestReport report, List<LogLine> log, List<GenZLine> genz, List<GenZLine> impressions, ApkSettings s, TaichiSettings taichi, bool final, string sheetTaichi, bool legacy = false)
        {
            var sec = report.Section("Ad impressions", TestStage.Ads);
            var fa = log.Where(l => l.tag == "FA" || l.tag == "FA-SVC").ToList();
            var eventRx = new Regex(s.firebaseEventPattern);
            var events = fa.Select(l => eventRx.Match(l.msg)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
            int adImpressions = events.Count(e => e == "ad_impression");

            if (legacy)
            {
                // v1.32: impressions per format can't be read; Firebase's ad_impression events still show that ads were shown.
                sec.Add("Impressions per format", CheckStatus.Info, "v1.32 build", NeedsV133);
                sec.Add("Firebase ad_impression", adImpressions > 0 ? CheckStatus.Pass : Missing(final, CheckStatus.Warn), adImpressions.ToString(),
                    adImpressions > 0 ? "Ads were shown and reported to Firebase" : "No ad_impression event in this session: show an ad to test it");
            }
            foreach (var g in impressions.GroupBy(i => i.detail).OrderBy(g => g.Key))
                sec.Add(g.Key, CheckStatus.Pass, "×" + g.Count(), "Shown, and its revenue reported");
            if (!legacy)
            foreach (var f in s.adFormats)
                if (!impressions.Any(i => i.detail.EndsWith(" " + f, StringComparison.OrdinalIgnoreCase)))
                    sec.Add(f, Missing(final, CheckStatus.Warn), "not shown", Waiting(final, "No impression of this format in the session: show one to test it"));

            if (impressions.Count > 0)
                sec.Add("Firebase ad_impression", adImpressions >= impressions.Count ? CheckStatus.Pass : CheckStatus.Warn, $"{adImpressions} of {impressions.Count}",
                    adImpressions >= impressions.Count ? "Every impression reached Firebase" : "Fewer ad_impression events than impressions (Firebase may log them a few seconds later)");

            var tsec = report.Section("Taichi", TestStage.Ads);
            var customImp = genz.LastOrDefault(g => g.sdk == "RemoteConfig" && g.what == "values");
            var n = customImp == null ? null : Regex.Match(customImp.detail, @"custom_imp=(\d+)");
            if (!string.IsNullOrEmpty(sheetTaichi))
            {
                if (n != null && n.Success)
                    tsec.Add("2.0 impression count = sheet", n.Groups[1].Value == sheetTaichi.Trim() ? CheckStatus.Pass : CheckStatus.Fail, n.Groups[1].Value,
                        n.Groups[1].Value == sheetTaichi.Trim() ? "Remote Config gave the sheet's value" : $"The sheet says {sheetTaichi.Trim()}: fix custom_imp in Remote Config");
                else if (legacy)
                    tsec.Add("2.0 impression count = sheet", CheckStatus.Info, "v1.32 build", $"The sheet says {sheetTaichi.Trim()}. " + NeedsV133 + " (the Remote Config values line)");
                else
                    tsec.Add("2.0 impression count = sheet", Missing(final, CheckStatus.Warn), "", Waiting(final, $"The sheet says {sheetTaichi.Trim()}; no Remote Config values were logged to compare"));
            }
            foreach (var e in new[] { taichi.taichi2Event, taichi.taichi1Event })
            {
                int count = events.Count(x => x == e);
                if (count > 0) tsec.Add($"Sends \"{e}\"", CheckStatus.Pass, "×" + count, "");
                else if (e == taichi.taichi2Event)
                    tsec.Add($"Sends \"{e}\"", CheckStatus.Info, "not yet",
                        $"Fires after {(n != null && n.Success ? n.Groups[1].Value : "custom_imp")} impressions; this session had {impressions.Count}");
            }
        }

        static void CheckStability(TestReport report, List<LogLine> log, ApkInfo apk, bool final)
        {
            var sec = report.Section("Crashes and errors", TestStage.Stability);
            var appPids = new HashSet<int>(log.Where(l => l.tag == "Unity").Select(l => l.pid));
            string pkg = apk.package ?? "";

            var javaCrashes = new List<string>();
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i].tag != "AndroidRuntime" || !log[i].msg.Contains("FATAL EXCEPTION")) continue;
                bool ours = appPids.Contains(log[i].pid) || log.Skip(i + 1).Take(3).Any(l => l.msg.Contains("Process: " + pkg));
                if (!ours) continue;
                var cause = log.Skip(i + 1).Take(6).FirstOrDefault(l => l.tag == "AndroidRuntime" && !l.msg.StartsWith("Process:") && l.msg.Trim().Length > 0);
                javaCrashes.Add(cause?.msg ?? log[i].msg);
            }
            sec.Add("No Java crash", javaCrashes.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail, javaCrashes.Count.ToString(), FirstFew(javaCrashes));

            var native = log.Where(l => (l.msg.Contains("Fatal signal") && appPids.Contains(l.pid)) || l.msg.Contains(">>> " + pkg + " <<<"))
                            .Select(l => l.msg).ToList();
            sec.Add("No native crash", native.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail, native.Count.ToString(), FirstFew(native));

            var anrs = log.Where(l => (l.tag == "am_anr" || l.msg.StartsWith("ANR in ")) && l.msg.Contains(pkg)).Select(l => l.msg).ToList();
            sec.Add("No ANR", anrs.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail, anrs.Count.ToString(), FirstFew(anrs));

            var exceptions = log.Where(l => l.tag == "Unity" && (l.level == 'E' || l.level == 'W') && Regex.IsMatch(l.msg, @"^\w+(\.\w+)*Exception\b"))
                                .Select(l => l.msg).ToList();
            sec.Add("No script exceptions", exceptions.Count == 0 ? CheckStatus.Pass : CheckStatus.Warn, exceptions.Count.ToString(),
                exceptions.Count == 0 ? "" : FirstFew(exceptions.Distinct().ToList()));

            if (log.Count > 0)
                sec.Add("Session", CheckStatus.Info, $"{log.First().time} → {log.Last().time}", $"{log.Count} log lines" + (final ? "" : " so far"));
        }

        // ---------------------------------------------------------------- helpers

        static string Summary(List<string> names) =>
            string.Join(", ", names.GroupBy(x => x).OrderByDescending(g => g.Count()).Take(12).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key))
            + (names.Distinct().Count() > 12 ? ", …" : "");

        static string FirstFew(List<string> lines) =>
            lines.Count == 0 ? "" : string.Join("\n", lines.Take(3)) + (lines.Count > 3 ? $"\n…and {lines.Count - 3} more" : "");

        static string FirstLine(string s) =>
            string.IsNullOrEmpty(s) ? "(no output)" : s.Trim().Split('\n')[0].Trim();
    }
}
