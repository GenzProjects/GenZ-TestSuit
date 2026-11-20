// GenZ-TestSuit-v1.0.1
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using static GenZ.TestSuit.GenZSourceModel;

namespace GenZ.TestSuit
{
    /// <summary>
    /// Code checks for the studio ad setup, read straight from the scripts (no compilation or play mode):
    /// MAX-first waterfall with AdMob fallback, one banner at a time, every format reloads after show/fail,
    /// AdsGapTimer reset on every interstitial and rewarded grant, Remote Config driving the cooldown,
    /// plus general ad-code pitfalls. Anchors are SDK API calls (MaxSdk.*, InterstitialAd.Load…) and the
    /// names in <see cref="CodeCheckSettings"/>, so renamed wrapper methods are still found.
    /// </summary>
    public static partial class GenZCodeChecks
    {
        // ---- SDK anchors
        static readonly Regex MaxInterShow = new Regex(@"\bMaxSdk\s*\.\s*ShowInterstitial\s*\(");
        static readonly Regex MaxInterLoad = new Regex(@"\bMaxSdk\s*\.\s*LoadInterstitial\s*\(");
        static readonly Regex MaxInterReady = new Regex(@"\bMaxSdk\s*\.\s*IsInterstitialReady\s*\(");
        static readonly Regex MaxRewShow = new Regex(@"\bMaxSdk\s*\.\s*ShowRewardedAd\s*\(");
        static readonly Regex MaxRewLoad = new Regex(@"\bMaxSdk\s*\.\s*LoadRewardedAd\s*\(");
        static readonly Regex MaxRewReady = new Regex(@"\bMaxSdk\s*\.\s*IsRewardedAdReady\s*\(");
        static readonly Regex MaxBannerCreate = new Regex(@"\bMaxSdk\s*\.\s*CreateBanner\s*\(");
        static readonly Regex MaxBannerShow = new Regex(@"\bMaxSdk\s*\.\s*ShowBanner\s*\(");
        static readonly Regex MaxMrecCreate = new Regex(@"\bMaxSdk\s*\.\s*CreateMRec\s*\(");
        static readonly Regex MaxMrecShow = new Regex(@"\bMaxSdk\s*\.\s*ShowMRec\s*\(");
        static readonly Regex MaxAppOpen = new Regex(@"\bMaxSdk\s*\.\s*(Show|Load)AppOpenAd\s*\(");
        static readonly Regex AdmobInterLoad = new Regex(@"\bInterstitialAd\s*\.\s*Load\s*\(");
        static readonly Regex AdmobRewLoad = new Regex(@"\bRewardedAd\s*\.\s*Load\s*\(");
        static readonly Regex AdmobAppOpenLoad = new Regex(@"\bAppOpenAd\s*\.\s*Load\s*\(");
        static readonly Regex AdmobBannerNew = new Regex(@"\bnew\s+BannerView\s*\(");
        static readonly Regex AdmobInterShow = new Regex(@"\b\w*[Ii]nterst\w*\s*\.\s*Show\s*\(");
        static readonly Regex AdmobRewShow = new Regex(@"\b\w*[Rr]eward\w*\s*\.\s*Show\s*\(");
        static readonly Regex AdmobBannerShowCall = new Regex(@"\b\w*[Bb]anner\w*\s*\.\s*Show\s*\(");
        static readonly Regex AdmobBannerHideCall = new Regex(@"\b\w*[Bb]anner\w*\s*\.\s*Hide\s*\(");
        static readonly Regex AdmobMrec = new Regex(@"\bAdSize\s*\.\s*MediumRectangle\b");
        static readonly Regex MainThreadFlag = new Regex(@"\bRaiseAdEventsOnUnityMainThread\s*=\s*true\b");
        static readonly Regex UsesAdmob = new Regex(@"\b(InterstitialAd|RewardedAd|AppOpenAd)\s*\.\s*Load\s*\(|\bnew\s+BannerView\s*\(");

        static readonly Regex AdmobAdField = new Regex(@"\b(InterstitialAd|RewardedAd|AppOpenAd|BannerView|RewardedInterstitialAd)\s+(\w+)\s*(=\s*null\s*)?;");
        static readonly Regex InvokeByString = new Regex(@"\b(Invoke|InvokeRepeating|CancelInvoke|IsInvoking|StartCoroutine|StopCoroutine)\s*\(\s*""(\w+)""");
        static readonly Regex EmptyCatch = new Regex(@"\bcatch\s*(\([^)]*\))?\s*\{\s*\}");

        const string SectionScripts = "Code · Scripts";
        const string SectionWaterfall = "Code · Ad waterfall (MAX first, AdMob fallback)";
        const string SectionBanner = "Code · Banner (never both networks at once)";
        const string SectionReload = "Code · Every format keeps loading (none missed)";
        const string SectionRevenue = "Code · Ad revenue (paid) events";
        const string SectionTimer = "Code · AdsGapTimer reset (every interstitial and rewarded grant)";
        const string SectionRewardCallback = "Code · Rewarded: reward goes to the right callback";
        const string SectionRemote = "Code · Remote Config → AdsGapTimer";
        const string SectionRemoteDefaults = "Code · Remote Config defaults";
        const string SectionLogic = "Code · Other logic checks";
        const string SectionAdsVersion = "Code · Ad scripts version (GenzMonetization)";
        const string SectionAppOpenSkip = "Code · No App Open ad after a purchase or review";
        const string SectionPlatform = "Code · Platform-only code (Android and iOS builds)";
        const string SectionStartup = "Code · Startup order (consent, App Tracking, Remote Config)";

        class Ctx
        {
            public GenZSourceModel model;
            public CodeCheckSettings s;
            public List<ClassInfo> ad;     // ad / analytics scripts + the gap timer
            public List<ClassInfo> game;   // everything else that was scanned
            public List<MethodInfo> adMethods, gameMethods;
            public Regex resetRx, gateRx;
            public HashSet<string> delegateFields;   // callback fields declared in the ad scripts (e.g. _rewarded)
            public bool? ios;                        // build target: true = iOS, false = Android, null = both
            public AdsPackage pkg;                   // the GenZ Ads version the project uses: checks follow it
            HashSet<string> uiMethods;
            /// <summary>Method names wired to UI/animation events in scenes and prefabs (m_MethodName), read once.</summary>
            public HashSet<string> UiMethods
            {
                get
                {
                    if (uiMethods != null) return uiMethods;
                    uiMethods = new HashSet<string>();
                    var excludes = s.excludeFolders.Select(e => "/" + e.Trim('/').ToLowerInvariant() + "/").ToList();
                    foreach (var pattern in new[] { "*.unity", "*.prefab" })
                    foreach (var full in System.IO.Directory.GetFiles("Assets", pattern, System.IO.SearchOption.AllDirectories))
                    {
                        var lower = "/" + full.Replace('\\', '/').ToLowerInvariant();
                        if (excludes.Any(lower.Contains)) continue;
                        var text = System.IO.File.ReadAllText(full);
                        const string key = "m_MethodName: ";
                        for (int i = text.IndexOf(key, StringComparison.Ordinal); i >= 0; i = text.IndexOf(key, i + key.Length, StringComparison.Ordinal))
                        {
                            int end = text.IndexOfAny(new[] { '\r', '\n' }, i);
                            var name = text.Substring(i + key.Length, (end < 0 ? text.Length : end) - i - key.Length).Trim();
                            if (name.Length > 0) uiMethods.Add(name);
                        }
                    }
                    return uiMethods;
                }
            }
        }

        static readonly HashSet<string> UnityMessages = new HashSet<string>
        {
            "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy",
            "OnApplicationPause", "OnApplicationFocus", "OnApplicationQuit", "OnMouseDown", "OnMouseUp",
            "OnTriggerEnter", "OnTriggerEnter2D", "OnCollisionEnter", "OnCollisionEnter2D", "OnBecameVisible"
        };

        /// <param name="ios">The build target: true = iOS, false = Android, null = both (platform-only code then fails for both builds).</param>
        public static void Run(TestReport report, CodeCheckSettings settings, GenZSourceModel model = null, bool? ios = null)
        {
            var ctx = MakeCtx(settings, model ?? GenZSourceModel.Load(settings.excludeFolders));
            ctx.ios = ios;
            ctx.pkg = DetectAdsVersion(AdsScriptsFolder(ctx));
            CheckScripts(report.Section(SectionScripts, TestStage.Integration), ctx);
            CheckAdsScriptsVersion(report.Section(SectionAdsVersion, TestStage.AdCalling), ctx);
            CheckWaterfall(report.Section(SectionWaterfall, TestStage.AdCalling), ctx);
            CheckBanner(report.Section(SectionBanner, TestStage.AdCalling), ctx);
            CheckReloads(report.Section(SectionReload, TestStage.AdCalling), ctx);
            CheckRevenue(report.Section(SectionRevenue, TestStage.AdCalling), ctx);
            CheckTimerResets(report.Section(SectionTimer, TestStage.AdCalling), report.Section(SectionRewardCallback, TestStage.AdCalling), ctx);
            CheckRemoteConfig(report.Section(SectionRemote, TestStage.AdCalling), ctx);
            CheckRemoteDefaults(report.Section(SectionRemoteDefaults, TestStage.AdCalling), ctx);
            CheckAppOpenSkip(report.Section(SectionAppOpenSkip, TestStage.AdCalling), ctx);
            CheckPlatformCode(report.Section(SectionPlatform, TestStage.AdCalling), ctx);
            CheckStartupOrder(report.Section(SectionStartup, TestStage.AdCalling), ctx);
            CheckLogic(report.Section(SectionLogic, TestStage.AdCalling), ctx);
        }

        // ================================================================= ad scripts version

        // Every script in the GenZ ad scripts folder starts with "// GenzMonetization-v1.33". All must state the same
        // version (a script from another release mixed in fails), and it must be the version this suite expects.
        // Scripts from before v1.33 have no header at all: that's a warning, so older games still pass (plug and play).
        static void CheckAdsScriptsVersion(ReportSection sec, Ctx ctx)
        {
            string expected = string.IsNullOrWhiteSpace(ctx.s.adsScriptsVersion) ? GenZTestSuitInfo.AdsScriptsVersion : ctx.s.adsScriptsVersion.Trim().TrimStart('v', 'V');
            string header = GenZTestSuitInfo.AdsScriptsHeader;
            var headerRx = new Regex(@"^\s*//\s*" + Regex.Escape(header) + @"(\d+(?:\.\d+)*)\b", RegexOptions.IgnoreCase);

            var folder = AdsScriptsFolder(ctx);
            sec.Add("Checked as", CheckStatus.Info, "GenZ Ads " + ctx.pkg.Label,
                $"Found from {ctx.pkg.how}. " + (ctx.pkg.V133
                    ? "v1.33 features (consent screen, App Open skip on purchases, GenZLog, RemoteConfigManager) are required."
                    : "Features added in v1.33 are only reported, not required."));
            if (folder == null)
            {
                sec.Add("Ad scripts folder", CheckStatus.Warn, "not found",
                    "Couldn't find the GenZ ad scripts folder (the folder with AdsManager.cs, or one named AdsScripts…): versions not checked");
                return;
            }

            // file → version ("" = no header)
            var versions = new List<(string file, string version)>();
            foreach (var full in System.IO.Directory.GetFiles(folder, "*.cs", System.IO.SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var path = full.Replace('\\', '/');
                if (path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var first = System.IO.File.ReadLines(full).Select(l => l.TrimStart('﻿')).FirstOrDefault(l => l.Trim().Length > 0) ?? "";
                var m = headerRx.Match(first);
                versions.Add((path, m.Success ? m.Groups[1].Value : ""));
            }
            if (versions.Count == 0)
            {
                sec.Add("Ad scripts folder", CheckStatus.Warn, folder, "No scripts in the ad scripts folder");
                return;
            }

            var stated = versions.Where(v => v.version.Length > 0).ToList();
            string shortList(IEnumerable<(string file, string version)> list) =>
                string.Join(", ", list.Take(8).Select(v => System.IO.Path.GetFileName(v.file))) + (list.Count() > 8 ? $" …+{list.Count() - 8}" : "");

            if (stated.Count == 0)
            {
                // v1.32 scripts have no version line; a v1.33 package without them (found from Release.txt / GenZLog) must add them.
                sec.Add("Version header", ctx.pkg.V133 ? CheckStatus.Fail : CheckStatus.Info, "none",
                    ctx.pkg.V133
                        ? $"The scripts are v{ctx.pkg.version} ({ctx.pkg.how}) but none starts with \"// {header}{ctx.pkg.version}\": add it as the first line of every script in {folder}"
                        : $"No script in {folder} starts with \"// {header}…\": a GenZ Ads v1.32 package (the line starts with v1.33). Checked as v1.32; the latest release is {header}{expected}");
                return;
            }

            // The scripts' version = the one most scripts state; every other script is out of line.
            var main = stated.GroupBy(v => v.version).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key, VersionComparer).First().Key;
            var others = versions.Where(v => v.version != main).ToList();
            var summary = string.Join(", ", versions.GroupBy(v => v.version).OrderByDescending(g => g.Count())
                .Select(g => (g.Key.Length > 0 ? "v" + g.Key : "no header") + " ×" + g.Count()));

            if (others.Count == 0)
                sec.Add("Same version in every script", CheckStatus.Pass, $"v{main} ×{versions.Count}",
                    $"All {versions.Count} scripts in {folder} start with // {header}{main}");
            else
            {
                sec.Add("Same version in every script", CheckStatus.Fail, summary,
                    $"{others.Count} of {versions.Count} scripts in {folder} aren't {header}{main}: {shortList(others)}. "
                    + "Mixed versions mean some scripts were copied from another release: replace the whole folder with one GenZ Ads release");
                foreach (var o in others.Take(20))
                    sec.Add("Version · " + System.IO.Path.GetFileName(o.file), CheckStatus.Fail, o.version.Length > 0 ? "v" + o.version : "no header",
                        o.version.Length > 0 ? $"States {header}{o.version}; the other scripts are {header}{main}"
                                             : $"First line isn't \"// {header}{main}\": a script from another release, or the header was removed",
                        o.file, 1);
            }

            int cmp = VersionComparer.Compare(main, expected);
            if (cmp == 0)
                sec.Add("Expected version", CheckStatus.Pass, $"v{main}", $"Matches the GenZ Ads version this suite checks for ({header}{expected})");
            else if (cmp < 0)
                sec.Add("Expected version", CheckStatus.Warn, $"v{main} < v{expected}",
                    $"The ad scripts are {header}{main}, older than {header}{expected}: update AdsScripts-GenzAds to the latest GenZ Ads release");
            else
                sec.Add("Expected version", CheckStatus.Info, $"v{main} > v{expected}",
                    $"The ad scripts are newer ({header}{main}) than this suite knows ({header}{expected}): update the GenZ Test Suit package");

            // Release.txt's newest entry ("GenZ Ads v1.33 …") should be the same release.
            var release = System.IO.Path.Combine(folder, "Release.txt");
            if (System.IO.File.Exists(release))
            {
                var rm = Regex.Match(System.IO.File.ReadAllText(release), @"\bv(\d+(?:\.\d+)+)");
                if (rm.Success)
                {
                    bool same = VersionComparer.Compare(rm.Groups[1].Value, main) == 0;
                    sec.Add("Release.txt", same ? CheckStatus.Pass : CheckStatus.Warn, "v" + rm.Groups[1].Value,
                        same ? "Its newest entry is the same version as the scripts"
                             : $"Its newest entry is v{rm.Groups[1].Value} but the scripts say {header}{main}",
                        release.Replace('\\', '/'), 1);
                }
            }
        }

        /// <summary>The GenZ Ads package a project uses, and how that was found. The checks follow it: features added in v1.33
        /// (consent screen, App Open skip on purchases, GenZLog…) are required from a v1.33 project and only reported for v1.32.</summary>
        public class AdsPackage
        {
            public string version = "1.32";
            public string how = "";
            public string folder;
            public bool AtLeast(string v) => VersionComparer.Compare(version, v) >= 0;
            public bool V133 => AtLeast("1.33");
            public string Label => "v" + version;
        }

        /// <summary>
        /// Which GenZ Ads version the project's ad scripts are: the "// GenzMonetization-v…" line most scripts start with; else the
        /// newest version in the folder's Release.txt; else v1.33 when GenZLog / RemoteConfigManager is there; else v1.32.
        /// Reads files only (no model, no UnityEditor), so the Editor and APK suites can call it too.
        /// </summary>
        public static AdsPackage DetectAdsVersion(string folder = null)
        {
            var p = new AdsPackage { folder = folder ?? FindAdsScriptsFolder() };
            if (p.folder == null || !System.IO.Directory.Exists(p.folder)) { p.how = "no ad scripts folder found, so v1.32 is assumed"; return p; }

            var headerRx = new Regex(@"^\s*//\s*" + Regex.Escape(GenZTestSuitInfo.AdsScriptsHeader) + @"(\d+(?:\.\d+)*)\b", RegexOptions.IgnoreCase);
            var files = System.IO.Directory.GetFiles(p.folder, "*.cs", System.IO.SearchOption.AllDirectories)
                .Where(f => f.Replace('\\', '/').IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0).ToList();
            var stated = files.Select(f => headerRx.Match(System.IO.File.ReadLines(f).Select(l => l.TrimStart('﻿')).FirstOrDefault(l => l.Trim().Length > 0) ?? ""))
                .Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
            if (stated.Count > 0)
            {
                p.version = stated.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key, VersionComparer).First().Key;
                p.how = $"the \"// {GenZTestSuitInfo.AdsScriptsHeader}{p.version}\" line in {stated.Count(v => v == p.version)} of {files.Count} scripts";
                return p;
            }
            var release = System.IO.Path.Combine(p.folder, "Release.txt");
            var rm = System.IO.File.Exists(release) ? Regex.Match(System.IO.File.ReadAllText(release), @"\bv(\d+(?:\.\d+)+)") : Match.Empty;
            if (rm.Success) { p.version = rm.Groups[1].Value; p.how = "the newest entry in Release.txt (scripts have no version line)"; return p; }
            bool newer = files.Any(f => { var n = System.IO.Path.GetFileNameWithoutExtension(f); return n == "GenZLog" || n == "RemoteConfigManager"; });
            p.version = newer ? "1.33" : "1.32";
            p.how = newer ? "GenZLog / RemoteConfigManager are there (scripts have no version line)" : "no version line, no Release.txt, no GenZLog: an older package";
            return p;
        }

        static string FindAdsScriptsFolder()
        {
            if (!System.IO.Directory.Exists("Assets")) return null;
            var manager = System.IO.Directory.GetFiles("Assets", "AdsManager.cs", System.IO.SearchOption.AllDirectories).Select(f => f.Replace('\\', '/'))
                .Where(f => f.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0).OrderBy(f => f.Length).FirstOrDefault();
            if (manager != null) return System.IO.Path.GetDirectoryName(manager)?.Replace('\\', '/');
            return System.IO.Directory.GetDirectories("Assets", "AdsScripts*", System.IO.SearchOption.AllDirectories)
                .Select(d => d.Replace('\\', '/')).Where(d => d.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0)
                .OrderBy(d => d.Length).FirstOrDefault();
        }

        // The folder holding AdsManager (the main ad script); else a folder under Assets named AdsScripts….
        static string AdsScriptsFolder(Ctx ctx)
        {
            var manager = ctx.model.classes.FirstOrDefault(c => string.Equals(c.name, "AdsManager", StringComparison.OrdinalIgnoreCase));
            if (manager != null) return System.IO.Path.GetDirectoryName(manager.file.path)?.Replace('\\', '/');
            return System.IO.Directory.GetDirectories("Assets", "AdsScripts*", System.IO.SearchOption.AllDirectories)
                .Select(d => d.Replace('\\', '/')).Where(d => d.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0)
                .OrderBy(d => d.Length).FirstOrDefault();
        }

        // "1.33" vs "1.4": compared part by part as integers (1.33 is newer than 1.4, as the studio numbers releases).
        static readonly Comparer<string> VersionComparer = Comparer<string>.Create((a, b) =>
        {
            var pa = (a ?? "").Split('.'); var pb = (b ?? "").Split('.');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = i < pa.Length && int.TryParse(pa[i], out var xa) ? xa : 0;
                int y = i < pb.Length && int.TryParse(pb[i], out var yb) ? yb : 0;
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        });

        static Ctx MakeCtx(CodeCheckSettings settings, GenZSourceModel model)
        {
            var adNames = new HashSet<string>(settings.adScripts, StringComparer.OrdinalIgnoreCase) { settings.gapTimerClass };
            var ctx = new Ctx
            {
                model = model,
                s = settings,
                ad = model.classes.Where(c => adNames.Contains(c.name)).ToList(),
                game = model.classes.Where(c => !adNames.Contains(c.name)).ToList(),
                resetRx = new Regex(@"\b(" + string.Join("|", settings.resetMethods.Select(Regex.Escape)) + @")\s*\("),
                gateRx = new Regex(@"\b(" + string.Join("|", settings.gateFields.Select(Regex.Escape)) + @")\b"),
            };
            ctx.adMethods = ctx.ad.SelectMany(c => c.methods).ToList();
            ctx.gameMethods = ctx.game.SelectMany(c => c.methods).ToList();
            ctx.delegateFields = DelegateFields(ctx.ad);
            return ctx;
        }

        // ================================================================= scripts

        static void CheckScripts(ReportSection sec, Ctx c)
        {
            var missing = new List<string>();
            foreach (var entry in c.s.requiredScripts)
            {
                var names = entry.Split('|');
                if (c.model.Class(names) == null) missing.Add(names[0]);
            }
            sec.Add("Required scripts", missing.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail,
                missing.Count == 0 ? c.s.requiredScripts.Length + " found" : "Missing: " + string.Join(", ", missing),
                $"Scanned {c.model.files.Count} scripts ({c.ad.Count} ad/analytics classes); SDK and kit folders skipped");

            // Scripts a v1.33 package must have (v1.32 doesn't have them, so they're only listed there).
            var v133 = (c.s.requiredScriptsV133 ?? new string[0]).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            if (v133.Count == 0) return;
            var missing133 = v133.Where(e => c.model.Class(e.Split('|')) == null).Select(e => e.Split('|')[0]).ToList();
            if (c.pkg.V133)
                sec.Add("Required scripts (v1.33)", missing133.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail,
                    missing133.Count == 0 ? v133.Count + " found" : "Missing: " + string.Join(", ", missing133),
                    missing133.Count == 0 ? "Every script GenZ Ads v1.33 adds is there"
                                          : "These come with GenZ Ads v1.33: copy them from the release (the package's other scripts call them)");
            else
                sec.Add("Required scripts (v1.33)", CheckStatus.Info, "v1.32 package",
                    $"Not required for v1.32 (they come with v1.33: {string.Join(", ", v133.Select(e => e.Split('|')[0]))})");
        }

        // ================================================================= waterfall

        static void CheckWaterfall(ReportSection sec, Ctx c)
        {
            var admobInterShows = c.adMethods.Where(m => AdmobInterShow.IsMatch(m.Code) && !MaxInterShow.IsMatch(m.Code)).ToList();
            var admobRewShows = c.adMethods.Where(m => AdmobRewShow.IsMatch(m.Code) && !MaxRewShow.IsMatch(m.Code)).ToList();

            var interEntry = Waterfall(sec, c, "Interstitial", MaxInterReady, MaxInterShow, admobInterShows);
            var rewEntry = Waterfall(sec, c, "Rewarded", MaxRewReady, MaxRewShow, admobRewShows);

            // App Open: AdMob only.
            var appOpenLoad = c.adMethods.FirstOrDefault(m => AdmobAppOpenLoad.IsMatch(m.Code));
            var maxAppOpen = c.adMethods.FirstOrDefault(m => MaxAppOpen.IsMatch(m.Code));
            if (appOpenLoad == null) sec.Add("App Open is AdMob only", CheckStatus.Fail, "", "No AppOpenAd.Load found — App Open ads never load");
            else if (maxAppOpen != null) sec.Add("App Open is AdMob only", CheckStatus.Warn, maxAppOpen.FullName, "MAX App Open is also used", maxAppOpen.File.path, maxAppOpen.Line);
            else sec.Add("App Open is AdMob only", CheckStatus.Pass, appOpenLoad.FullName, "AdMob AppOpenAd; no MAX App Open", appOpenLoad.File.path, appOpenLoad.Line);

            // MREC: MAX only.
            var mrec = c.adMethods.FirstOrDefault(m => MaxMrecCreate.IsMatch(m.Code));
            var admobMrec = c.adMethods.FirstOrDefault(m => AdmobMrec.IsMatch(m.Code));
            if (mrec == null) sec.Add("MREC is MAX only", CheckStatus.Fail, "", "No MaxSdk.CreateMRec found — MREC never shows");
            else if (admobMrec != null) sec.Add("MREC is MAX only", CheckStatus.Warn, admobMrec.FullName, "An AdMob MediumRectangle banner is also used", admobMrec.File.path, admobMrec.Line);
            else sec.Add("MREC is MAX only", CheckStatus.Pass, mrec.FullName, "MaxSdk.CreateMRec; no AdMob MREC", mrec.File.path, mrec.Line);

            // App Open must not pop right after an interstitial / rewarded returns the app to foreground.
            var appOpenShow = c.ad.Where(k => k.methods.Any(m => AdmobAppOpenLoad.IsMatch(m.Code)))
                .SelectMany(k => k.methods).FirstOrDefault(m => Regex.IsMatch(m.Code, @"\.\s*Show\s*\(\s*\)"));
            if (appOpenShow != null && (interEntry != null || rewEntry != null))
            {
                var flags = new HashSet<string>();
                foreach (var e in new[] { interEntry, rewEntry }.Where(e => e != null))
                    foreach (Match fm in Regex.Matches(e.Code, @"\b(\w+)\s*=\s*true\b")) flags.Add(fm.Groups[1].Value);
                var used = flags.FirstOrDefault(f => Regex.IsMatch(appOpenShow.Code, @"\b" + Regex.Escape(f) + @"\b"));
                sec.Add("App Open skipped after a fullscreen ad", used != null ? CheckStatus.Pass : CheckStatus.Warn, appOpenShow.FullName,
                    used != null ? $"Uses '{used}', set when an interstitial/rewarded is shown"
                        : "No flag from the interstitial/rewarded show code is checked — App Open may show right after closing an interstitial",
                    appOpenShow.File.path, appOpenShow.Line);
            }
        }

        /// <summary>
        /// Checks one fullscreen format's waterfall and returns the entry method: MAX first with AdMob fallback, and,
        /// when the project has a vendor switch (<see cref="CodeCheckSettings.vendorSwitchFields"/>, e.g. useSdkVendorSwitch
        /// alternating MAX/AdMob), an "AdMob turn" branch that tries AdMob first and falls back to a ready MAX ad.
        /// </summary>
        static MethodInfo Waterfall(ReportSection sec, Ctx c, string format, Regex maxReady, Regex maxShow, List<MethodInfo> admobShows)
        {
            // MAX readiness may sit in a helper: "bool IsMaxInterstitialReady() { return isMaxInitialized && MaxSdk.IsInterstitialReady(id) ...; }".
            var readyHelperRx = new Regex(@"\breturn\b[^;]*" + maxReady);
            var readyHelpers = c.adMethods.Where(m => !maxShow.IsMatch(m.Code) && readyHelperRx.IsMatch(m.Code)).ToList();
            int ReadyIndex(string code) => FirstIndex(code, maxReady, readyHelpers);

            // Entry = an ad-script method that checks MAX readiness and (directly or via a wrapper) shows MAX.
            // Prefer public methods, then the top of the call chain (not called by another candidate).
            var candidates = c.adMethods
                .Where(m => !readyHelpers.Contains(m) && ReadyIndex(m.Code) >= 0 && c.model.Reaches(m, maxShow, c.ad, 2, out _))
                .ToList();
            var entry = candidates
                .OrderByDescending(m => m.isPublic)
                .ThenBy(m => candidates.Count(o => o != m && ReferenceRegex(m.name).IsMatch(o.Code)))
                .FirstOrDefault();
            if (entry == null)
            {
                sec.Add($"{format}: MAX first", CheckStatus.Fail, "", $"No method checks MAX {format} readiness before showing it");
                return null;
            }

            // Vendor switch: the "AdMob turn" branch is cut out of the entry so the rest is checked as the MAX-first path.
            var code = entry.Code;
            VendorSwitch(sec, c, format, entry, maxShow, admobShows, ReadyIndex, ref code);

            var wrappers = c.adMethods.Where(m => m != entry && c.model.Reaches(m, maxShow, c.ad, 1, out _)).ToList();
            int maxIdx = FirstIndex(code, maxShow, wrappers);
            var fallback = admobShows.FirstOrDefault(a => ReferenceRegex(a.name).IsMatch(code));
            if (fallback == null)
            {
                sec.Add($"{format}: AdMob fallback", CheckStatus.Fail, entry.FullName,
                    $"When MAX has no {format.ToLower()} ready, AdMob is never tried — the {format.ToLower()} is lost", entry.File.path, entry.Line);
                return entry;
            }

            int admobIdx = ReferenceRegex(fallback.name).Match(code).Index;
            sec.Add($"{format}: MAX first, AdMob fallback", maxIdx >= 0 && maxIdx < admobIdx ? CheckStatus.Pass : CheckStatus.Fail, entry.FullName,
                maxIdx >= 0 && maxIdx < admobIdx ? $"MAX if ready, else {fallback.FullName}" : $"{fallback.FullName} is tried before MAX — MAX must have priority",
                entry.File.path, entry.Line);

            // An early "if (!maxInitialized) return;" before the fallback skips AdMob entirely.
            foreach (Match rm in Regex.Matches(code, @"if\s*\(\s*(!\s*\w*[Ii]nit\w*|\w*[Ii]nit\w*\s*==\s*false)\s*\)\s*\{?\s*return\s*;"))
            {
                if (rm.Index > admobIdx) break;
                var line = entry.File.LineOf(entry.bodyStart + rm.Index);
                sec.Add($"{format}: AdMob fallback when MAX isn't initialized", CheckStatus.Fail, rm.Value.Trim(),
                    $"Returns before the AdMob fallback while MAX is still initializing (or failed to) — no {format.ToLower()} ad from either network",
                    entry.File.path, line);
                return entry;
            }
            sec.Add($"{format}: AdMob fallback when MAX isn't initialized", CheckStatus.Pass, entry.FullName, "No early return before the fallback", entry.File.path, entry.Line);
            return entry;
        }

        static int FirstIndex(string code, Regex direct, IEnumerable<MethodInfo> wrappers)
        {
            var idx = new List<int>();
            var m = direct.Match(code);
            if (m.Success) idx.Add(m.Index);
            foreach (var w in wrappers)
            {
                var wm = ReferenceRegex(w.name).Match(code);
                if (wm.Success) idx.Add(wm.Index);
            }
            return idx.Count == 0 ? -1 : idx.Min();
        }

        /// <summary>
        /// Vendor switch part of the waterfall (e.g. useSdkVendorSwitch: 0 = MAX first, 1 = MAX/AdMob one by one,
        /// 2 = two MAX then one AdMob). Projects without the switch are left to the plain MAX-first check.
        /// The "if (AdMob turn) { … }" branch of the entry, or the method it calls, must try AdMob first and then
        /// a MAX ad only after checking MAX is ready. That branch is blanked out of <paramref name="code"/>,
        /// so the rest is checked as the MAX-first path.
        /// </summary>
        static void VendorSwitch(ReportSection sec, Ctx c, string format, MethodInfo entry, Regex maxShow, List<MethodInfo> admobShows,
            Func<string, int> readyIndex, ref string code)
        {
            if (c.s.vendorSwitchFields == null || c.s.vendorSwitchFields.Length == 0) return;
            var switchRx = new Regex(@"\b(" + string.Join("|", c.s.vendorSwitchFields.Where(f => !string.IsNullOrEmpty(f)).Select(Regex.Escape)) + @")\b");
            var owner = c.ad.FirstOrDefault(k => switchRx.IsMatch(k.Body));
            if (owner == null) return;
            var name = switchRx.Match(owner.Body).Value;
            string label = $"{format}: vendor switch ({name})";

            // The switch is read in the entry's condition directly, or through a helper such as IsAdmobTurn(count).
            var turnHelpers = c.adMethods.Where(m => m != entry && switchRx.IsMatch(m.Code)).ToList();
            var wrappers = c.adMethods.Where(m => m != entry && c.model.Reaches(m, maxShow, c.ad, 1, out _)).ToList();
            int AdmobIndex(string s) => admobShows.Select(a => ReferenceRegex(a.name).Match(s)).Where(x => x.Success).Select(x => x.Index).DefaultIfEmpty(-1).Min();

            bool read = false;
            foreach (var (start, end, cond) in IfBlocks(code))
            {
                var usedHelpers = turnHelpers.Where(h => ReferenceRegex(h.name).IsMatch(cond)).ToList();
                if (!switchRx.IsMatch(cond) && usedHelpers.Count == 0) continue;
                // The turn is picked before MAX is tried; an "if (switch …)" after a MAX show is inside the MAX-first path.
                if (FirstIndex(code.Substring(0, start), maxShow, wrappers) >= 0) continue;
                read = true;

                // The branch is the block itself, or the method it calls that holds the AdMob show.
                var block = code.Substring(start, end - start);
                MethodInfo where = null;
                string branch = block;
                if (AdmobIndex(block) < 0)
                {
                    where = c.adMethods.FirstOrDefault(m => m != entry && ReferenceRegex(m.name).IsMatch(block) && AdmobIndex(m.Code) >= 0);
                    if (where == null) continue;
                    branch = where.Code;
                }
                int admobIdx = AdmobIndex(branch);
                int maxIdx = FirstIndex(branch, maxShow, wrappers.Where(w => w != where));
                if (maxIdx >= 0 && maxIdx < admobIdx) continue; // a MAX-first branch (e.g. "if (switch == 0)")

                string file = where?.File.path ?? entry.File.path;
                int line = where?.Line ?? entry.File.LineOf(entry.bodyStart + start);
                string branchName = where?.FullName ?? entry.FullName;

                // Values 1 and 2 must both be handled in the condition or its helpers: "case 1:" or "switch == 1".
                var valuesText = cond + string.Concat(usedHelpers.Select(h => h.Code));
                var sw = Regex.Escape(name);
                var missing = new[] { 1, 2 }.Where(v => !Regex.IsMatch(valuesText,
                    @"\bcase\s+" + v + @"\s*:|\b" + sw + @"\s*==\s*" + v + @"\b|\b" + v + @"\s*==\s*" + sw + @"\b")).ToList();
                sec.Add(label, missing.Count == 0 ? CheckStatus.Pass : CheckStatus.Warn, entry.FullName,
                    missing.Count == 0
                        ? $"Read in {entry.FullName}" + (usedHelpers.Count > 0 ? $" via {usedHelpers[0].FullName}" : "") + $"; AdMob turn in {branchName}"
                        : $"No \"case {missing[0]}:\" or \"{name} == {missing[0]}\" found — {name} = {string.Join(" / ", missing)} may behave like 0 (MAX first every time)",
                    usedHelpers.Count > 0 ? usedHelpers[0].File.path : entry.File.path, usedHelpers.Count > 0 ? usedHelpers[0].Line : entry.Line);

                if (maxIdx < 0)
                    sec.Add($"{format}: AdMob turn — AdMob first, MAX fallback", CheckStatus.Fail, branchName,
                        $"On AdMob's turn MAX is never tried — no {format.ToLower()} when AdMob has none ready", file, line);
                else
                {
                    sec.Add($"{format}: AdMob turn — AdMob first, MAX fallback", CheckStatus.Pass, branchName, "AdMob if ready, else MAX", file, line);
                    int readyIdx = readyIndex(branch);
                    bool guarded = readyIdx >= 0 && readyIdx < maxIdx;
                    sec.Add($"{format}: AdMob turn — MAX fallback only when ready", guarded ? CheckStatus.Pass : CheckStatus.Fail, branchName,
                        guarded ? "Checks MAX is initialized and has an ad ready before showing it"
                            : "Shows MAX without checking it's initialized and ready — the show fails and no ad is shown", file, line);
                }

                code = BlankRange(code, start, end);
                return;
            }

            if (!read)
                sec.Add(label, CheckStatus.Warn, entry.FullName,
                    $"{name} is declared but {entry.FullName} doesn't use it to pick the network before MAX is tried — every value behaves like 0 (MAX first)",
                    entry.File.path, entry.Line);
            else
                sec.Add(label, CheckStatus.Warn, entry.FullName,
                    $"{name} is read, but no branch that tries AdMob first was found (expected \"if (<AdMob turn>) {{ AdMob, else MAX }}\") — check it by hand",
                    entry.File.path, entry.Line);
        }

        /// <summary>Every "if (cond) { … }" or "if (cond) statement;" in the code: start, end (exclusive) and condition.</summary>
        static IEnumerable<(int start, int end, string cond)> IfBlocks(string code)
        {
            foreach (Match im in Regex.Matches(code, @"\bif\s*\("))
            {
                int open = im.Index + im.Length - 1, close = MatchParen(code, open);
                if (close < 0) continue;
                int b = close + 1;
                while (b < code.Length && char.IsWhiteSpace(code[b])) b++;
                int end = b < code.Length && code[b] == '{' ? MatchBrace(code, b) + 1 : code.IndexOf(';', b) + 1;
                if (end <= 0) continue;
                yield return (im.Index, end, code.Substring(open + 1, close - open - 1));
            }
        }

        static int MatchParen(string s, int open)
        {
            int d = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (s[i] == '(') d++;
                else if (s[i] == ')' && --d == 0) return i;
            }
            return -1;
        }

        /// <summary>Blanks a range, keeping length and line breaks so indexes and line numbers stay valid.</summary>
        static string BlankRange(string s, int from, int to)
        {
            var chars = s.ToCharArray();
            for (int i = from; i < to; i++) if (chars[i] != '\n' && chars[i] != '\r') chars[i] = ' ';
            return new string(chars);
        }

        // ================================================================= banner

        static void CheckBanner(ReportSection sec, Ctx c)
        {
            var admobHide = c.adMethods.Where(m => AdmobBannerHideCall.IsMatch(m.Code)).ToList();
            var admobShow = c.adMethods.Where(m => AdmobBannerShowCall.IsMatch(m.Code)).ToList();
            var maxCreate = c.adMethods.FirstOrDefault(m => MaxBannerCreate.IsMatch(m.Code));
            var admobCreate = c.adMethods.FirstOrDefault(m => AdmobBannerNew.IsMatch(m.Code));

            if (maxCreate == null || admobCreate == null)
            {
                sec.Add("Both banners requested", CheckStatus.Fail, "",
                    (maxCreate == null ? "No MaxSdk.CreateBanner. " : "") + (admobCreate == null ? "No AdMob BannerView." : ""));
                return;
            }
            sec.Add("Both banners requested", CheckStatus.Pass, maxCreate.FullName + " + " + admobCreate.FullName, "MAX banner and AdMob banner are both created");

            // MAX banner loaded → hide AdMob banner (+ remember MAX is showing).
            var loaded = Handlers(c, @"MaxSdkCallbacks\s*\.\s*Banner\s*\.\s*OnAdLoadedEvent").ToList();
            var flags = new HashSet<string>();
            if (loaded.Count == 0)
                sec.Add("MAX banner loaded → AdMob banner hidden", CheckStatus.Fail, "", "No MaxSdkCallbacks.Banner.OnAdLoadedEvent handler — the AdMob banner is never hidden when MAX's shows");
            else
            {
                var h = loaded[0];
                bool hides = c.model.Reaches(h, AdmobBannerHideCall, c.ad, 2, out _);
                foreach (Match fm in Regex.Matches(h.Code, @"\b(\w+)\s*=\s*true\b")) flags.Add(fm.Groups[1].Value);
                sec.Add("MAX banner loaded → AdMob banner hidden", hides ? CheckStatus.Pass : CheckStatus.Fail, h.FullName,
                    hides ? "Hides the AdMob banner" + (flags.Count > 0 ? $" and sets {string.Join(", ", flags)}" : "") : "Doesn't hide the AdMob banner — both banners can show",
                    h.File.path, h.Line);
            }

            // MAX banner failed → AdMob banner shown.
            var failed = Handlers(c, @"MaxSdkCallbacks\s*\.\s*Banner\s*\.\s*OnAdLoadFailedEvent").FirstOrDefault();
            if (failed == null)
                sec.Add("MAX banner failed → AdMob banner shown", CheckStatus.Warn, "", "No MaxSdkCallbacks.Banner.OnAdLoadFailedEvent handler — no AdMob fallback when MAX has no banner");
            else
            {
                bool shows = c.model.Reaches(failed, AdmobBannerShowCall, c.ad, 2, out _);
                sec.Add("MAX banner failed → AdMob banner shown", shows ? CheckStatus.Pass : CheckStatus.Warn, failed.FullName,
                    shows ? "Falls back to the AdMob banner" : "Doesn't show the AdMob banner — no banner at all while MAX has none", failed.File.path, failed.Line);
            }

            // AdMob banner loads later than MAX's → it must hide itself (AdMob BannerView shows itself on load).
            var admobLoaded = Handlers(c, @"\.\s*OnBannerAdLoaded\b").FirstOrDefault();
            if (admobLoaded == null)
                sec.Add("AdMob banner loaded while MAX's is showing", CheckStatus.Fail, admobCreate.FullName,
                    "No OnBannerAdLoaded handler. An AdMob BannerView appears by itself when it loads, so if it loads after MAX's banner, both banners show",
                    admobCreate.File.path, admobCreate.Line);
            else
            {
                bool guarded = c.model.Reaches(admobLoaded, AdmobBannerHideCall, c.ad, 2, out _) || flags.Any(f => Regex.IsMatch(admobLoaded.Code, @"\b" + f + @"\b"));
                sec.Add("AdMob banner loaded while MAX's is showing", guarded ? CheckStatus.Pass : CheckStatus.Fail, admobLoaded.FullName,
                    guarded ? "Hides itself when MAX's banner is up" : "Doesn't check MAX's banner — both banners can show", admobLoaded.File.path, admobLoaded.Line);
            }

            // Any explicit AdMob banner show must check the MAX banner flag.
            foreach (var m in admobShow)
            {
                bool guarded = flags.Any(f => Regex.IsMatch(m.Code, @"\b" + f + @"\b"));
                sec.Add($"{m.FullName} checks MAX banner", guarded ? CheckStatus.Pass : CheckStatus.Fail, m.FullName,
                    guarded ? "Hides instead of showing while MAX's banner is up" : "Shows the AdMob banner without checking MAX's — both banners can show",
                    m.File.path, m.Line);
            }
        }

        // ================================================================= reloads

        static void CheckReloads(ReportSection sec, Ctx c)
        {
            MaxFullscreen(sec, c, "MAX Interstitial", "Interstitial", MaxInterLoad, MaxInterShow);
            MaxFullscreen(sec, c, "MAX Rewarded", "Rewarded", MaxRewLoad, MaxRewShow);

            var bannerShow = c.adMethods.FirstOrDefault(m => MaxBannerShow.IsMatch(m.Code));
            sec.Add("MAX Banner", c.adMethods.Any(m => MaxBannerCreate.IsMatch(m.Code)) && bannerShow != null ? CheckStatus.Pass : CheckStatus.Fail,
                "CreateBanner + ShowBanner", "MAX banners refresh by themselves once created");
            var mrecShow = c.adMethods.FirstOrDefault(m => MaxMrecShow.IsMatch(m.Code));
            sec.Add("MAX MREC", c.adMethods.Any(m => MaxMrecCreate.IsMatch(m.Code)) && mrecShow != null ? CheckStatus.Pass : CheckStatus.Fail,
                "CreateMRec + ShowMRec", "MAX MRECs refresh by themselves once created");

            AdmobFullscreen(sec, c, "AdMob Interstitial", "InterstitialAd", AdmobInterLoad);
            AdmobFullscreen(sec, c, "AdMob Rewarded", "RewardedAd", AdmobRewLoad);
            AdmobFullscreen(sec, c, "AdMob App Open", "AppOpenAd", AdmobAppOpenLoad);
            AdmobBanner(sec, c);
            NullSafety(sec, c);
        }

        static void MaxFullscreen(ReportSection sec, Ctx c, string label, string cb, Regex load, Regex show)
        {
            var loader = c.adMethods.FirstOrDefault(m => load.IsMatch(m.Code));
            if (loader == null) { sec.Add(label, CheckStatus.Fail, "", "Never loaded (no MaxSdk load call)"); return; }

            void Need(string evt, string what, CheckStatus missing)
            {
                var h = Handlers(c, @"MaxSdkCallbacks\s*\.\s*" + cb + @"\s*\.\s*" + evt).FirstOrDefault();
                if (h == null) { sec.Add($"{label}: {what}", missing, "", $"No {cb}.{evt} handler"); return; }
                bool ok = c.model.Reaches(h, load, c.ad, 2, out _);
                var via = ok ? null : ReloadViaFlag(c, h, load, loader);
                sec.Add($"{label}: {what}", ok || via != null ? CheckStatus.Pass : missing, h.FullName,
                    ok ? "Loads the next ad" : via != null ? via : $"Doesn't load a new ad — the {cb.ToLower()} stays empty until something else requests one", h.File.path, h.Line);
            }
            Need("OnAdHiddenEvent", "reload after close", CheckStatus.Fail);
            Need("OnAdLoadFailedEvent", "retry after failed load", CheckStatus.Fail);
            Need("OnAdDisplayFailedEvent", "reload after failed show", CheckStatus.Warn);
        }

        /// <summary>
        /// A handler that only sets a flag (e.g. interRetryPending = true, because SDK callbacks can arrive off the main
        /// thread) still reloads when another ad-script method checks that flag and then loads: Update → Invoke(nameof(Request…)).
        /// Returns the explanation, or null.
        /// </summary>
        static string ReloadViaFlag(Ctx c, MethodInfo handler, Regex load, MethodInfo loader)
        {
            foreach (Match fm in Regex.Matches(handler.Code, @"\b(\w+)\s*=\s*true\b"))
            {
                var flag = fm.Groups[1].Value;
                var checkRx = new Regex(@"\bif\s*\(\s*(this\s*\.\s*)?" + Regex.Escape(flag) + @"\s*(==\s*true\s*)?\)");
                foreach (var m in c.adMethods.Where(x => x != handler && checkRx.IsMatch(x.Code)))
                    if (c.model.Reaches(m, load, c.ad, 2, out _) || ReferenceRegex(loader.name).IsMatch(m.CodeWithStrings))
                        return $"Sets {flag}; {m.FullName} then loads the next ad";
            }
            return null;
        }

        static void AdmobFullscreen(ReportSection sec, Ctx c, string label, string type, Regex load)
        {
            var loader = c.adMethods.FirstOrDefault(m => load.IsMatch(m.Code));
            if (loader == null) { sec.Add(label, CheckStatus.Fail, "", $"Never loaded (no {type}.Load)"); return; }

            // Event handlers are registered in the load callback or in a method taking the ad type.
            bool InScope(MethodInfo m) => m == loader || Regex.IsMatch(Header(m), @"\b" + type + @"\b");

            void Need(string evt, string what, CheckStatus missing)
            {
                var h = Handlers(c, @"\.\s*" + evt + @"\b", InScope).FirstOrDefault();
                if (h == null) { sec.Add($"{label}: {what}", missing, loader.FullName, $"No {evt} handler", loader.File.path, loader.Line); return; }
                bool ok = c.model.Reaches(h, load, c.ad, 2, out _);
                var via = ok ? null : ReloadViaFlag(c, h, load, loader);
                sec.Add($"{label}: {what}", ok || via != null ? CheckStatus.Pass : missing, h.FullName,
                    ok ? "Loads the next ad" : via ?? "Doesn't load a new ad", h.File.path, h.Line);
            }
            Need("OnAdFullScreenContentClosed", "reload after close", CheckStatus.Fail);
            Need("OnAdFullScreenContentFailed", "reload after failed show", CheckStatus.Warn);

            // Failed load: the error branch of the Load callback must retry, or nothing ever loads again.
            var code = loader.CodeWithStrings;
            var err = Regex.Match(loader.Code, @"if\s*\([^)]*\berror\s*!=\s*null[^)]*\)");
            if (!err.Success)
                sec.Add($"{label}: retry after failed load", CheckStatus.Warn, loader.FullName, "Couldn't find the load error check", loader.File.path, loader.Line);
            else
            {
                var block = BlockAfter(loader.Code, err.Index + err.Length, out int blockStart);
                var blockText = code.Substring(blockStart, block.Length);
                bool retries = Regex.IsMatch(blockText, @"\b(Invoke|StartCoroutine|\w*[Rr]etry\w*|\w*Pending\w*\s*=\s*true)\b") || ReferenceRegex(loader.name).IsMatch(blockText);
                // Some other public method may reload on demand (e.g. HasRewardedAvailable → Request…).
                var onDemand = c.adMethods.FirstOrDefault(m => m != loader && m.isPublic && !InScope(m) && ReferenceRegex(loader.name).IsMatch(m.CodeWithStrings) && !m.name.StartsWith("Start"));
                var line = loader.File.LineOf(loader.bodyStart + err.Index);
                if (retries)
                    sec.Add($"{label}: retry after failed load", CheckStatus.Pass, loader.FullName, "Retries after a failed load", loader.File.path, line);
                else
                    sec.Add($"{label}: retry after failed load", onDemand != null ? CheckStatus.Warn : CheckStatus.Fail, loader.FullName,
                        onDemand != null ? $"No retry — only reloads when {onDemand.FullName} is called"
                            : "No retry — after one failed load this format never loads again this session",
                        loader.File.path, line);
            }

            // App Open: when the ad is missing/expired at show time, a new one must be loaded.
            if (type == "AppOpenAd")
            {
                var showM = loader.owner.methods.FirstOrDefault(m => Regex.IsMatch(m.Code, @"\.\s*Show\s*\(\s*\)"));
                if (showM != null)
                {
                    bool reloads = ReferenceRegex(loader.name).IsMatch(showM.CodeWithStrings);
                    sec.Add($"{label}: reload when missing/expired", reloads ? CheckStatus.Pass : CheckStatus.Fail, showM.FullName,
                        reloads ? "Loads a new ad when none is usable" : "Just returns when the ad is missing or expired (4 h) — nothing loads a new one, so App Open stops",
                        showM.File.path, showM.Line);
                }
            }
        }

        static void AdmobBanner(ReportSection sec, Ctx c)
        {
            var creator = c.adMethods.FirstOrDefault(m => AdmobBannerNew.IsMatch(m.Code));
            if (creator == null) return;
            var failed = Handlers(c, @"\.\s*OnBannerAdLoadFailed\b").FirstOrDefault();
            bool retries = failed != null && (c.model.Reaches(failed, AdmobBannerNew, c.ad, 2, out _) || Regex.IsMatch(failed.CodeWithStrings, @"\b(Invoke|StartCoroutine)\b"));
            var via = retries || failed == null ? null : ReloadViaFlag(c, failed, AdmobBannerNew, creator); // e.g. bannerRetryPending + Update
            sec.Add("AdMob Banner: retry after failed load", retries || via != null ? CheckStatus.Pass : CheckStatus.Warn, failed?.FullName ?? creator.FullName,
                retries ? "Requests a new banner" : via != null ? via : "No retry — if the first AdMob banner load fails, there's no fallback banner when MAX's banner fails",
                (failed ?? creator).File.path, (failed ?? creator).Line);
        }

        /// <summary>AdMob ad objects used without a null check throw; the empty catch hides it and nothing reloads.</summary>
        static void NullSafety(ReportSection sec, Ctx c)
        {
            int issues = 0;
            foreach (var k in c.ad)
            foreach (Match fm in AdmobAdField.Matches(k.Body))
            {
                var field = fm.Groups[2].Value;
                var use = new Regex(@"\b" + field + @"\s*\.\s*\w+");
                var guard = new Regex(@"\b" + field + @"\s*(!=|==)\s*null|\b" + field + @"\s*\?\.|null\s*(!=|==)\s*" + field + @"\b");
                var assign = new Regex(@"\b" + field + @"\s*=\s*(new\b|\w+\s*;)");
                foreach (var m in k.methods)
                {
                    var um = use.Match(m.Code);
                    if (!um.Success || guard.IsMatch(m.Code)) continue;
                    var am = assign.Match(m.Code);
                    if (am.Success && am.Index < um.Index) continue;
                    var callers = c.model.Referencing(m, k.methods).ToList();
                    if (!m.isPublic && callers.Count > 0 && callers.All(x => assign.IsMatch(x.Code))) continue; // helper run right after creation
                    issues++;
                    sec.Add($"{m.FullName}: null check", CheckStatus.Fail, $"{field}.…",
                        $"'{field}' is used without a null check — if the last load failed it's null, the exception is swallowed and no new ad is requested",
                        m.File.path, m.File.LineOf(m.bodyStart + um.Index));
                }
            }
            if (issues == 0) sec.Add("AdMob ad objects null-checked", CheckStatus.Pass, "", "Every use of an AdMob ad object is guarded");
        }

        // ================================================================= revenue events

        /// <summary>Every format's paid event must report the revenue to each analytics in <see cref="CodeCheckSettings.revenueSinks"/>.</summary>
        static void CheckRevenue(ReportSection sec, Ctx c)
        {
            var sinks = new List<(string name, Regex rx)>();
            foreach (var entry in c.s.revenueSinks ?? new string[0])
            {
                int bar = entry.IndexOf('|');
                if (bar > 0) sinks.Add((entry.Substring(0, bar).Trim(), new Regex(entry.Substring(bar + 1))));
            }
            if (sinks.Count == 0) return;
            foreach (var r in RevenueHandlers(c))
                RevenueResult(sec, c, r.label, r.handler, sinks, r.missing, r.near);
        }

        /// <summary>The paid-event handler of every ad format the game uses (null handler = none subscribed).</summary>
        static List<(string label, MethodInfo handler, string missing, MethodInfo near)> RevenueHandlers(Ctx c)
        {
            var list = new List<(string, MethodInfo, string, MethodInfo)>();

            // MAX: MaxSdkCallbacks.<Format>.OnAdRevenuePaidEvent, for each format the game creates/loads.
            foreach (var (label, cb, used) in new[]
            {
                ("MAX Interstitial", "Interstitial", MaxInterLoad), ("MAX Rewarded", "Rewarded", MaxRewLoad),
                ("MAX Banner", "Banner", MaxBannerCreate), ("MAX MREC", "MRec", MaxMrecCreate)
            })
            {
                if (!c.adMethods.Any(m => used.IsMatch(m.Code))) continue;
                var h = Handlers(c, @"MaxSdkCallbacks\s*\.\s*" + cb + @"\s*\.\s*OnAdRevenuePaidEvent").FirstOrDefault();
                list.Add((label, h, $"No MaxSdkCallbacks.{cb}.OnAdRevenuePaidEvent handler", null));
            }

            // AdMob: ad.OnAdPaid, subscribed where the ad is loaded/created, in a method taking the ad type,
            // or in a helper the loader calls (e.g. RequestBanner → ListenToAdEvents()).
            foreach (var (label, type, load) in new[]
            {
                ("AdMob Interstitial", "InterstitialAd", AdmobInterLoad), ("AdMob Rewarded", "RewardedAd", AdmobRewLoad),
                ("AdMob App Open", "AppOpenAd", AdmobAppOpenLoad), ("AdMob Banner", "BannerView", AdmobBannerNew)
            })
            {
                var loader = c.adMethods.FirstOrDefault(m => load.IsMatch(m.Code));
                if (loader == null) continue;
                var helpers = new HashSet<MethodInfo>(c.model.Callees(loader, new[] { loader.owner }));
                var h = Handlers(c, @"\.\s*OnAdPaid\b", m => m == loader || helpers.Contains(m) || Regex.IsMatch(Header(m), @"\b" + type + @"\b")).FirstOrDefault();
                list.Add((label, h, "No OnAdPaid handler", loader));
            }
            return list;
        }

        static void RevenueResult(ReportSection sec, Ctx c, string label, MethodInfo h, List<(string name, Regex rx)> sinks, string missing, MethodInfo near)
        {
            if (h == null)
            {
                sec.Add($"{label}: revenue reported", CheckStatus.Fail, "", missing + " — this format's ad revenue is never reported", near?.File.path, near?.Line ?? 0);
                return;
            }
            var hit = sinks.Where(s => ReachesText(c, h, s.rx, 2)).Select(s => s.name).ToList();
            var miss = sinks.Select(s => s.name).Where(n => !hit.Contains(n)).ToList();
            var status = miss.Count == 0 ? CheckStatus.Pass : hit.Count == 0 ? CheckStatus.Fail : CheckStatus.Warn;
            sec.Add($"{label}: revenue reported", status, h.FullName,
                hit.Count == 0 ? "The revenue handler reports to none of: " + string.Join(", ", miss)
                    : "Reports to " + string.Join(", ", hit) + (miss.Count == 0 ? "" : " · not to " + string.Join(", ", miss)),
                h.File.path, h.Line);
        }

        /// <summary>Like Reaches, but matches the code with string contents kept (e.g. "ad_impression").</summary>
        static bool ReachesText(Ctx c, MethodInfo m, Regex rx, int depth, HashSet<MethodInfo> seen = null)
        {
            seen = seen ?? new HashSet<MethodInfo>();
            if (!seen.Add(m)) return false;
            if (rx.IsMatch(m.CodeWithStrings)) return true;
            if (depth <= 0) return false;
            return c.model.Callees(m, c.ad).Any(k => ReachesText(c, k, rx, depth - 1, seen));
        }

        // ================================================================= AdsGapTimer resets

        static void CheckTimerResets(ReportSection sec, ReportSection rewardSec, Ctx c)
        {
            var interShowers = c.adMethods.Where(m => m.isPublic &&
                (c.model.Reaches(m, MaxInterShow, c.ad, 2, out _) || c.model.Reaches(m, AdmobInterShow, c.ad, 1, out _))).ToList();
            var rewShowers = c.adMethods.Where(m => m.isPublic &&
                (c.model.Reaches(m, MaxRewShow, c.ad, 2, out _) || c.model.Reaches(m, AdmobRewShow, c.ad, 1, out _))).ToList();

            // Either way is fine: each network resets the timer in its own SDK callback, or every call site resets it.
            // A network that doesn't reset it by itself leaves the job to the call sites, which are then checked.
            var interNets = InterstitialResets(c, interShowers);
            var rewNets = RewardedResets(c);
            bool centralInter = ReportNetworkResets(sec, c, "Interstitial", "interstitial call", interNets);
            bool centralRew = ReportNetworkResets(sec, c, "Rewarded", "rewarded call", rewNets);
            string NotCovered(List<NetworkReset> nets) =>
                nets.Any(n => n.resets) ? $" ({string.Join(", ", nets.Where(n => !n.resets).Select(n => n.network))} doesn't reset it in its own callback either)" : "";

            // ---- Interstitial call sites in game code
            if (!centralInter)
            {
                var names = interShowers.Select(m => m.name).Distinct().ToList();
                var callRx = new Regex(@"\.\s*(" + string.Join("|", names.Select(Regex.Escape)) + @")\s*\(");
                int count = 0;
                foreach (var site in c.gameMethods.Where(m => names.Count > 0 && callRx.IsMatch(m.Code)))
                {
                    count++;
                    var call = callRx.Match(site.Code);
                    var line = site.File.LineOf(site.bodyStart + call.Index);
                    var path = UncoveredPath(c, site, 4, new HashSet<MethodInfo>(), out bool gated, out bool live);
                    if (path == null && !live)
                        sec.Add($"Interstitial · {site.FullName}", CheckStatus.Info, call.Groups[1].Value + "()",
                            "Never called from code, a UI event or Unity — unused, skipped", site.File.path, line);
                    else if (path == null)
                        sec.Add($"Interstitial · {site.FullName}", gated ? CheckStatus.Pass : CheckStatus.Warn, call.Groups[1].Value + "()",
                            "Timer is reset on this path" + (gated ? "" : $" · not gated by {string.Join("/", c.s.gateFields)} — may show more often than the cooldown"),
                            site.File.path, line);
                    else
                        sec.Add($"Interstitial · {site.FullName}", CheckStatus.Fail, call.Groups[1].Value + "()",
                            $"Shows an interstitial without resetting {c.s.gapTimerClass}: {path}{NotCovered(interNets)}", site.File.path, line);
                }
                if (count == 0) sec.Add("Interstitial call sites", CheckStatus.Warn, "", "No game code shows an interstitial");
            }

            // ---- Rewarded call sites: the reward callback must reset the timer.
            var withCallback = rewShowers.Where(m => Regex.IsMatch(Header(m), @"\(\s*\w[\w<>.,\s]*\s+\w+\s*\)")).Select(m => m.name).Distinct().ToList();
            if (!centralRew)
            {
                var callRx = new Regex(@"\.\s*(" + string.Join("|", withCallback.Select(Regex.Escape)) + @")\s*\(");
                foreach (var site in c.gameMethods.Where(m => withCallback.Count > 0 && callRx.IsMatch(m.Code)))
                {
                    var call = callRx.Match(site.Code);
                    var line = site.File.LineOf(site.bodyStart + call.Index);
                    var arg = ArgText(site.CodeWithStrings, call.Index + call.Length - 1);
                    var resets = CallbackResets(c, site, arg, out string cbName);
                    sec.Add($"Rewarded · {site.FullName}", resets ? CheckStatus.Pass : CheckStatus.Fail, $"{call.Groups[1].Value}({Short(arg, 30)})",
                        resets ? $"Reward callback {cbName} resets the timer"
                            : $"Reward callback {cbName} doesn't reset {c.s.gapTimerClass} — an interstitial can follow right after the rewarded ad{NotCovered(rewNets)}",
                        site.File.path, line);
                }
            }

            // AdMob rewarded called directly: it grants through a shared delegate the caller never set.
            // A reward bug, not a timer one, so it has its own section; checked however the timer is reset.
            int direct = 0;
            var delegateCall = new Regex(@"\b(" + string.Join("|", c.delegateFields.DefaultIfEmpty("__none__").Select(Regex.Escape)) + @")\s*\(");
            var directRew = rewShowers.Where(m => !withCallback.Contains(m.name) && Regex.IsMatch(Header(m), @"\(\s*\)") && delegateCall.IsMatch(m.Code)).ToList();
            if (directRew.Count > 0)
            {
                var directRx = new Regex(@"\.\s*(" + string.Join("|", directRew.Select(m => Regex.Escape(m.name))) + @")\s*\(\s*\)");
                foreach (var site in c.gameMethods.Where(m => directRx.IsMatch(m.Code)))
                {
                    if (Regex.IsMatch(site.Code, @"\b\w*[Rr]eward\w*\s*=(?!=)")) continue; // sets the callback itself
                    var call = directRx.Match(site.Code);
                    direct++;
                    rewardSec.Add($"{site.FullName} (direct AdMob call)", CheckStatus.Fail, call.Groups[1].Value + "()",
                        "Calls the AdMob rewarded ad directly without setting the reward callback — the reward goes to the previous ad's callback (or none)"
                        + (centralRew ? "" : ", and the timer isn't reset")
                        + ". Call the waterfall method with a callback (e.g. ShowRewardedAd(callback)) instead, or set the callback before this call",
                        site.File.path, site.File.LineOf(site.bodyStart + call.Index));
                }
            }
            if (direct == 0)
                rewardSec.Add("Every rewarded call sets its callback", CheckStatus.Pass, "",
                    "No game code shows the AdMob rewarded ad directly without its own reward callback");
        }

        class NetworkReset
        {
            public string network;
            public bool resets;
            public MethodInfo where;  // the method that resets, or where the reset was looked for
            public string lookedIn;   // what was searched, for the message when it doesn't reset
        }

        /// <summary>First candidate that resets the timer (directly or one call down in the ad scripts).</summary>
        static NetworkReset FindReset(Ctx c, string network, IEnumerable<MethodInfo> candidates, MethodInfo fallback, string lookedIn)
        {
            foreach (var m in candidates)
                if (c.model.Reaches(m, c.resetRx, c.ad, 1, out var w))
                    return new NetworkReset { network = network, resets = true, where = w ?? m, lookedIn = lookedIn };
            return new NetworkReset { network = network, resets = false, where = fallback, lookedIn = lookedIn };
        }

        // Interstitial: reset when the ad is shown or closed. MAX: OnAdDisplayedEvent / OnAdHiddenEvent handlers;
        // AdMob: OnAdFullScreenContentOpened / Closed handlers on the InterstitialAd. A public show method that
        // resets it itself also covers the network(s) it shows.
        static List<NetworkReset> InterstitialResets(Ctx c, List<MethodInfo> interShowers)
        {
            var list = new List<NetworkReset>();
            if (c.adMethods.Any(m => MaxInterShow.IsMatch(m.Code)))
            {
                var handlers = Handlers(c, @"MaxSdkCallbacks\s*\.\s*Interstitial\s*\.\s*(OnAdDisplayedEvent|OnAdHiddenEvent)").ToList();
                var shows = interShowers.Where(m => c.model.Reaches(m, MaxInterShow, c.ad, 2, out _));
                list.Add(FindReset(c, "MAX", handlers.Concat(shows), handlers.FirstOrDefault(),
                    "MaxSdkCallbacks.Interstitial.OnAdDisplayedEvent / OnAdHiddenEvent"));
            }
            var loader = c.adMethods.FirstOrDefault(m => AdmobInterLoad.IsMatch(m.Code));
            if (loader != null)
            {
                bool InScope(MethodInfo m) => m == loader || Regex.IsMatch(Header(m), @"\bInterstitialAd\b");
                var handlers = Handlers(c, @"\.\s*OnAdFullScreenContent(Opened|Closed)\b", InScope).ToList();
                var shows = interShowers.Where(m => c.model.Reaches(m, AdmobInterShow, c.ad, 1, out _));
                list.Add(FindReset(c, "AdMob", handlers.Concat(shows), handlers.FirstOrDefault() ?? loader,
                    "the InterstitialAd's OnAdFullScreenContentOpened / Closed"));
            }
            return list;
        }

        // Rewarded: reset when the reward is granted. MAX: the OnAdReceivedRewardEvent handler;
        // AdMob: every rewardedAd.Show(reward => …) method (the reset sits in its reward callback).
        static List<NetworkReset> RewardedResets(Ctx c)
        {
            var list = new List<NetworkReset>();
            if (c.adMethods.Any(m => MaxRewShow.IsMatch(m.Code)))
            {
                var handlers = Handlers(c, @"MaxSdkCallbacks\s*\.\s*Rewarded\s*\.\s*OnAdReceivedRewardEvent").ToList();
                list.Add(FindReset(c, "MAX", handlers, handlers.FirstOrDefault(), "MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent"));
            }
            var shows = c.adMethods.Where(m => AdmobRewShow.IsMatch(m.Code) && !MaxRewShow.IsMatch(m.Code)).ToList();
            if (shows.Count > 0)
            {
                var missing = shows.FirstOrDefault(m => !c.model.Reaches(m, c.resetRx, c.ad, 1, out _));
                list.Add(missing == null
                    ? FindReset(c, "AdMob", shows, shows[0], "the rewardedAd.Show reward callback")
                    : new NetworkReset { network = "AdMob", resets = false, where = missing, lookedIn = "the rewardedAd.Show reward callback" });
            }
            return list;
        }

        /// <summary>One row per network; true when every network the game uses resets the timer by itself.</summary>
        static bool ReportNetworkResets(ReportSection sec, Ctx c, string format, string callName, List<NetworkReset> nets)
        {
            bool all = nets.Count > 0 && nets.All(n => n.resets);
            foreach (var n in nets)
            {
                var label = $"{format} · {n.network}: reset in SDK callback";
                string file = n.where?.File.path;
                int line = n.where?.Line ?? 0;
                if (n.resets)
                    sec.Add(label, CheckStatus.Pass, n.where.FullName,
                        all ? $"Resets {c.s.gapTimerClass} itself, so no {callName} needs to reset it" : $"Resets {c.s.gapTimerClass} itself", file, line);
                else
                    sec.Add(label, CheckStatus.Info, n.where?.FullName ?? "",
                        $"No reset in {n.lookedIn}, so every {callName} must reset it (checked below)", file, line);
            }
            return all;
        }

        /// <summary>
        /// Null when every live path into the method resets the timer; otherwise a readable chain of the uncovered path.
        /// A path is live when it starts at a UI/animation event, a Unity message or a reset; methods nothing calls are dead and ignored.
        /// </summary>
        static string UncoveredPath(Ctx c, MethodInfo m, int depth, HashSet<MethodInfo> seen, out bool gated, out bool live)
        {
            gated = c.gateRx.IsMatch(m.Code);
            live = false;
            if (!seen.Add(m)) return null; // cycle: judged on the first visit
            if (c.model.Reaches(m, c.resetRx, c.game, 1, out _)) { live = true; return null; }

            var callers = c.model.Referencing(m, c.gameMethods)
                .Where(k => !AllCallsPassFalse(k, m.name))
                .ToList();
            if (callers.Count == 0)
            {
                bool ui = c.UiMethods.Contains(m.name);
                if (!ui && !UnityMessages.Contains(m.name)) return null; // dead code
                live = true;
                return $"{m.FullName} doesn't reset it" + (ui ? " (called from a UI event in a scene/prefab)" : " (Unity calls it)");
            }
            if (depth == 0) { live = true; return $"{m.FullName} (call chain too deep to follow)"; }

            bool allGated = true, anyLive = false;
            foreach (var k in callers)
            {
                var sub = UncoveredPath(c, k, depth - 1, seen, out bool g, out bool l);
                if (sub != null) { live = true; return $"{m.FullName} ← {sub}"; }
                if (l) { anyLive = true; allGated &= g; }
            }
            live = anyLive;
            gated |= anyLive && allGated;
            return null;
        }

        static bool AllCallsPassFalse(MethodInfo caller, string name)
        {
            var calls = Regex.Matches(caller.Code, @"\b" + Regex.Escape(name) + @"\s*\(([^;]*)\)\s*;");
            return calls.Count > 0 && calls.Cast<Match>().All(m => Regex.IsMatch(m.Groups[1].Value, @"\bfalse\s*$|\bfalse\s*\)\s*$"));
        }

        static bool CallbackResets(Ctx c, MethodInfo site, string arg, out string name)
        {
            arg = arg.Trim();
            if (Regex.IsMatch(arg, @"^\w+$"))
            {
                name = arg;
                var cb = site.owner.methods.FirstOrDefault(m => m.name == arg) ?? c.gameMethods.FirstOrDefault(m => m.name == arg);
                return cb != null && c.model.Reaches(cb, c.resetRx, c.game, 2, out _);
            }
            name = "(lambda)";
            if (c.resetRx.IsMatch(arg)) return true;
            foreach (Match im in Regex.Matches(arg, @"\b(\w+)\s*\("))
            {
                var cb = site.owner.methods.FirstOrDefault(m => m.name == im.Groups[1].Value);
                if (cb != null && c.model.Reaches(cb, c.resetRx, c.game, 2, out _)) return true;
            }
            return false;
        }

        // ================================================================= Remote Config

        // Every key read with GetValue("key") needs a value in the SetDefaultsAsync list: without it, a key missing
        // from the console (or before the first fetch) reads as 0 / false / "" (e.g. adtimer 0 = no cooldown).
        static void CheckRemoteDefaults(ReportSection sec, Ctx c)
        {
            var getRx = new Regex(@"\bGetValue\s*\(\s*""(\w+)""\s*\)");
            var reads = new Dictionary<string, (MethodInfo m, int index)>();
            foreach (var m in c.adMethods.Concat(c.gameMethods))
                foreach (Match g in getRx.Matches(m.CodeWithStrings))
                    if (!reads.ContainsKey(g.Groups[1].Value)) reads[g.Groups[1].Value] = (m, g.Index);
            if (reads.Count == 0) return; // no Remote Config in this game

            var rcClasses = RemoteConfigClasses(c.model, c.s);
            var setDefaults = c.adMethods.Concat(c.gameMethods).FirstOrDefault(m => Regex.IsMatch(m.Code, @"\bSetDefaultsAsync\s*\("));
            var defaultsText = string.Concat(rcClasses.Concat(setDefaults != null ? new[] { setDefaults.owner } : new ClassInfo[0]).Distinct()
                .Select(k => k.file.text.Substring(k.bodyStart, k.bodyEnd - k.bodyStart)));
            var defRx = new Regex(@"[{\[]\s*""(\w+)""\s*[,\]]\s*=?\s*([^,}\]]+)");
            var defaults = new Dictionary<string, string>();
            foreach (Match d in defRx.Matches(defaultsText))
                if (!defaults.ContainsKey(d.Groups[1].Value)) defaults[d.Groups[1].Value] = ResolveDefault(c.model, d.Groups[2].Value);

            if (setDefaults == null)
                sec.Add("Defaults are set", CheckStatus.Warn, "no SetDefaultsAsync",
                    $"Remote Config has no defaults: the {reads.Count} keys the game reads are 0 / false / empty until fetched, and whenever a key is missing from the console");
            else
                sec.Add("Defaults are set", CheckStatus.Pass, setDefaults.FullName, "SetDefaultsAsync is called", setDefaults.File.path, setDefaults.Line);

            foreach (var kv in reads.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var (m, index) = kv.Value;
                bool has = defaults.TryGetValue(kv.Key, out var value);
                sec.Add($"\"{kv.Key}\" has a default", has ? CheckStatus.Pass : CheckStatus.Warn, has ? value : "none",
                    has ? $"Read in {m.FullName}" : $"Read in {m.FullName} with no default: if it's missing from Remote Config it reads as 0 / false / empty. Add it to the SetDefaultsAsync list",
                    m.File.path, m.File.LineOf(m.bodyStart + index));
            }
            foreach (var unused in defaults.Keys.Where(k => !reads.ContainsKey(k)).OrderBy(k => k))
                sec.Add($"\"{unused}\" default", CheckStatus.Info, defaults[unused], "Has a default, but no script reads it (only GetValue(\"…\") with a literal key is seen)");
        }

        static void CheckRemoteConfig(ReportSection sec, Ctx c)
        {
            var timerFields = string.Join("|", c.s.timerFields.Select(Regex.Escape));
            var assignRx = new Regex(@"\b" + Regex.Escape(c.s.gapTimerClass) + @"\b[^;=]*?\.\s*(" + timerFields + @")\s*=(?!=)\s*([^;]+);");
            var rcClasses = RemoteConfigClasses(c.model, c.s);
            if (rcClasses.Count == 0)
            {
                sec.Add("Remote Config sets the cooldown", CheckStatus.Fail, "", "No " + string.Join("/", c.s.remoteConfigClasses) + " class found");
                return;
            }

            var hits = rcClasses.SelectMany(k => k.methods).Select(m => (m, match: assignRx.Match(m.CodeWithStrings))).Where(x => x.match.Success).ToList();
            if (hits.Count == 0)
            {
                sec.Add("Remote Config sets the cooldown", CheckStatus.Fail, "",
                    $"{string.Join("/", c.s.remoteConfigClasses)} never assigns {c.s.gapTimerClass}.{c.s.timerFields[0]} — the cooldown stays at its default forever");
            }

            foreach (var (m, match) in hits)
            {
                var line = m.File.LineOf(m.bodyStart + match.Index);
                var rhs = match.Groups[2].Value.Trim();
                var key = RemoteKey(m.CodeWithStrings.Substring(0, match.Index), rhs);
                sec.Add("Remote Config sets the cooldown", key != null ? CheckStatus.Pass : CheckStatus.Warn, $"{c.s.gapTimerClass}.{match.Groups[1].Value} = {Short(rhs, 40)}",
                    key != null ? $"From Remote Config key \"{key}\" in {m.FullName}" : $"Assigned in {m.FullName}, but the value doesn't come from a Remote Config GetValue",
                    m.File.path, line);

                // Runs after fetch + activate?
                bool afterActivate = Regex.IsMatch(m.Code, @"\b(ActivateAsync|FetchAndActivateAsync)\b")
                    || c.model.Referencing(m, rcClasses.SelectMany(k => k.methods)).Any(k => Regex.IsMatch(k.Code, @"\b(ActivateAsync|FetchAndActivateAsync)\b"));
                sec.Add("Applied after fetch + activate", afterActivate ? CheckStatus.Pass : CheckStatus.Warn, m.FullName,
                    afterActivate ? "Called once the fetched values are activated" : "Couldn't confirm it runs after ActivateAsync — it may read stale/default values",
                    m.File.path, m.Line);

                // Missing key → LongValue 0 → no cooldown at all.
                if (key != null)
                {
                    var allRc = string.Concat(rcClasses.Select(k => k.file.text.Substring(k.bodyStart, k.bodyEnd - k.bodyStart)));
                    bool hasDefault = Regex.IsMatch(allRc, @"[{\[]\s*""" + Regex.Escape(key) + @"""\s*[,\]]");
                    var before = m.Code.Substring(0, match.Index);
                    bool guarded = Regex.IsMatch(before + match.Value, @"if\s*\([^)]*>\s*0[^)]*\)");
                    sec.Add($"Safe if \"{key}\" is missing", hasDefault || guarded ? CheckStatus.Pass : CheckStatus.Warn, key,
                        hasDefault ? "Has a Remote Config default" : guarded ? "Only applied when > 0"
                            : $"No default and no > 0 check — if \"{key}\" is missing from Remote Config, the cooldown becomes 0 and an interstitial shows at every chance",
                        m.File.path, line);

                    // Something that can throw earlier in the same method would skip the update.
                    var parse = Regex.Match(before, @"\b\w+\s*\.\s*Parse\s*\(");
                    if (parse.Success)
                        sec.Add("Nothing before it can throw", CheckStatus.Warn, parse.Value.Trim(),
                            "A Parse call earlier in this method throws on a bad value and skips the cooldown update", m.File.path, m.File.LineOf(m.bodyStart + parse.Index));
                }
            }

            // Other places that change the cooldown (informational).
            foreach (var m in c.gameMethods.Concat(c.adMethods).Where(x => !rcClasses.Contains(x.owner) && x.owner.name != c.s.gapTimerClass))
            {
                var om = assignRx.Match(m.CodeWithStrings);
                if (om.Success)
                    sec.Add("Also changed in", CheckStatus.Info, $"{c.s.gapTimerClass}.{om.Groups[1].Value} = {Short(om.Groups[2].Value.Trim(), 40)}", m.FullName,
                        m.File.path, m.File.LineOf(m.bodyStart + om.Index));
            }
        }

        /// <summary>
        /// The classes holding Remote Config code: the configured ones (remoteConfigClasses) plus any class that reads a
        /// key with GetValue("…"), e.g. a game's RemoteConfigManager, so an old config asset still finds it.
        /// </summary>
        internal static List<ClassInfo> RemoteConfigClasses(GenZSourceModel model, CodeCheckSettings s)
        {
            var list = model.Classes(s.remoteConfigClasses).ToList();
            foreach (var k in model.classes)
                if (!list.Contains(k) && k.methods.Any(m => Regex.IsMatch(m.CodeWithStrings, @"\bGetValue\s*\(\s*""\w+""")))
                    list.Add(k);
            return list;
        }

        /// <summary>A default written as a field name (e.g. { "custom_imp", impressionValue_ForRevenue }): that field's starting value. Otherwise the text itself.</summary>
        internal static string ResolveDefault(GenZSourceModel model, string expr)
        {
            var e = (expr ?? "").Trim();
            var id = Regex.Match(e, @"^(?:\w+\.)?([A-Za-z_]\w*)$");
            if (!id.Success || e == "true" || e == "false") return e;
            foreach (var k in model.classes)
            {
                var d = Regex.Match(k.Body, @"\b(?:int|long|float|double|bool|string)\s+" + Regex.Escape(id.Groups[1].Value) + @"\s*=\s*([^;,]+)[;,]");
                if (d.Success) return k.file.text.Substring(k.bodyStart + d.Groups[1].Index, d.Groups[1].Length).Trim();
            }
            return e;
        }

        static string RemoteKey(string before, string rhs)
        {
            var direct = Regex.Match(rhs, @"GetValue\s*\(\s*""([^""]+)""");
            if (direct.Success) return direct.Groups[1].Value;
            foreach (Match id in Regex.Matches(rhs, @"\b([A-Za-z_]\w*)\b"))
            {
                var src = Regex.Matches(before, @"\b" + Regex.Escape(id.Groups[1].Value) + @"\s*=(?!=)[^;]*GetValue\s*\(\s*""([^""]+)""");
                if (src.Count > 0) return src[src.Count - 1].Groups[1].Value;
            }
            return null;
        }

        // ================================================================= App Open skipped after purchase / review

        static readonly Regex PurchaseCall = new Regex(@"\.\s*(PurchaseProduct|InitiatePurchase)\s*\(");
        static readonly Regex ReviewLaunch = new Regex(@"\bLaunchReviewFlow\s*\(");
        static readonly Regex DelayCall = new Regex(@"\bWaitForSeconds(Realtime)?\b|\bInvoke\s*\(");
        static readonly Regex RealtimeDelay = new Regex(@"\bWaitForSecondsRealtime\b");
        static readonly Regex EarlyExit = new Regex(@"\byield\s+break\s*;|\breturn\s*;");

        // The store dialog and the review card are overlays: coming back from them must not show an App Open ad.
        // The purchase / review sets the skip flag the App Open ad checks (isPausedDuetoAd), and clears it again
        // after its flow ends, a moment later and in real time: the overlay often doesn't send the app to background,
        // so the App Open ad never clears it, and a stuck flag skips the next real App Open.
        // The purchase side and the clears are new in GenZ Ads v1.33, so a v1.32 game gets warnings, not failures.
        static void CheckAppOpenSkip(ReportSection sec, Ctx c)
        {
            var flags = (c.s.appOpenSkipFlags ?? new string[0]).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToArray();
            if (flags.Length == 0) flags = new[] { "isPausedDuetoAd" };
            var alt = string.Join("|", flags.Select(Regex.Escape));
            var setRx = new Regex(@"\b(" + alt + @")\s*=\s*true\b");
            var clearRx = new Regex(@"\b(" + alt + @")\s*=\s*false\b");
            var readRx = new Regex(@"\b(" + alt + @")\b(?!\s*=(?!=))");
            string flagName = flags[0];

            // 1. The App Open ad checks the flag before showing.
            var appOpen = c.ad.FirstOrDefault(k => k.methods.Any(m => AdmobAppOpenLoad.IsMatch(m.Code)));
            if (appOpen == null)
            {
                sec.Add("App Open ad", CheckStatus.Info, "none", "No AppOpenAd.Load in the ad scripts: nothing to check");
                return;
            }
            var showers = appOpen.methods.Where(m => Regex.IsMatch(m.Code, @"\.\s*Show\s*\(")).ToList();
            var unguarded = showers.Where(m => !readRx.IsMatch(m.Code)).ToList();
            if (showers.Count == 0)
                sec.Add("App Open checks the skip flag", CheckStatus.Info, appOpen.name, $"No .Show( call found in {appOpen.name}",
                    appOpen.file.path, appOpen.file.LineOf(appOpen.bodyStart));
            else if (unguarded.Count == 0)
                sec.Add("App Open checks the skip flag", CheckStatus.Pass, flagName,
                    $"{string.Join(", ", showers.Select(m => m.FullName))} checks {flagName} before showing", showers[0].File.path, showers[0].Line);
            else
                foreach (var m in unguarded)
                    sec.Add($"{m.FullName}: checks the skip flag", CheckStatus.Fail, "not checked",
                        $"Shows the App Open ad without checking {flagName}: purchases and the in-app review can't stop it, so it shows when the player returns from them",
                        m.File.path, m.Line);

            // Methods that clear the flag (outside the App Open ad): a clear may go through a helper such as ClearAppOpenSkip().
            var clearers = c.model.Methods.Where(m => clearRx.IsMatch(m.Code) && m.owner != appOpen).ToList();
            Regex ClearOrCall(ClassInfo owner)
            {
                var names = clearers.Where(m => m.owner == owner).Select(m => Regex.Escape(m.name)).Distinct().ToList();
                return new Regex(clearRx + (names.Count > 0 ? @"|\b(" + string.Join("|", names) + @")\s*\(" : ""));
            }

            // 2. Purchases.
            // Callers only: a class that declares the API itself is the SDK's (e.g. a plugin copied under Assets).
            var buyers = c.model.Methods.Where(m => PurchaseCall.IsMatch(m.Code)
                && !m.owner.methods.Any(x => x.name == "PurchaseProduct" || x.name == "InitiatePurchase")).ToList();
            if (buyers.Count == 0)
                sec.Add("Purchase", CheckStatus.Info, "none", "No PurchaseProduct / InitiatePurchase call found: no in-app purchases to check");
            foreach (var b in buyers)
            {
                var call = PurchaseCall.Match(b.Code);
                var set = setRx.Match(b.Code);
                int callLine = b.File.LineOf(b.bodyStart + call.Index);
                if (!set.Success || set.Index > call.Index)
                {
                    sec.Add($"{b.FullName}: sets {flagName} before the purchase", NewIn133(c), set.Success ? "after the call" : "not set",
                        $"Set {flagName} = true before {call.Groups[1].Value}(): otherwise an App Open ad shows when the player returns from the store dialog (GenZ Ads v1.33)",
                        b.File.path, callLine);
                    continue;
                }
                sec.Add($"{b.FullName}: sets {flagName} before the purchase", CheckStatus.Pass, flagName + " = true",
                    "No App Open ad when the player returns from the store dialog", b.File.path, b.File.LineOf(b.bodyStart + set.Index));

                // Cleared again when the purchase ends (success and failure), after a delay in real time.
                var owner = b.owner;
                var scope = new[] { owner };
                foreach (var (label, names) in new[]
                         {
                             ("succeeds", new[] { "OnPurchasePending", "ProcessPurchase", "OnPurchaseConfirmed" }),
                             ("fails", new[] { "OnPurchaseFailed" }),
                         })
                {
                    var handlers = owner.methods.Where(m => names.Contains(m.name)).ToList();
                    if (handlers.Count == 0)
                    {
                        sec.Add($"{owner.name}: {flagName} cleared when a purchase {label}", CheckStatus.Info, "no handler",
                            $"No {string.Join(" / ", names)} method in {owner.name}: not checked", owner.file.path, owner.file.LineOf(owner.bodyStart));
                        continue;
                    }
                    MethodInfo where = null;
                    var h = handlers.FirstOrDefault(m => c.model.Reaches(m, clearRx, scope, 3, out where)) ?? handlers[0];
                    if (where == null)
                    {
                        sec.Add($"{h.FullName}: clears {flagName}", NewIn133(c), "not cleared",
                            $"When a purchase {label}, {flagName} stays true if the store dialog didn't send the game to background (it's an overlay, so it often doesn't): "
                            + $"the next real App Open ad is skipped. Set {flagName} = false a moment after the purchase ends (GenZ Ads v1.33)",
                            h.File.path, h.Line);
                        continue;
                    }
                    // A delay on the way: a method the handler leads to waits (WaitForSeconds… / Invoke) and then clears.
                    var delayers = owner.methods.Where(m => DelayCall.IsMatch(m.Code) && c.model.Reaches(m, clearRx, scope, 1, out _)).ToList();
                    var delay = delayers.FirstOrDefault(d => d == h || c.model.Reaches(h, SourceRef(d.name), scope, 3, out _));
                    if (delay == null)
                        sec.Add($"{h.FullName}: clears {flagName}", CheckStatus.Warn, "at once (" + where.FullName + ")",
                            "Cleared straight away: if the game's return-to-foreground event comes after this callback, the App Open ad still shows. "
                            + "Clear it a moment later (e.g. 2 s with WaitForSecondsRealtime)",
                            where.File.path, where.Line);
                    else if (!RealtimeDelay.IsMatch(delay.Code))
                        sec.Add($"{h.FullName}: clears {flagName}", CheckStatus.Warn, "game time (" + delay.FullName + ")",
                            "Cleared after a delay in game time (WaitForSeconds / Invoke): a shop that pauses the game (timeScale 0) never clears it. Use WaitForSecondsRealtime",
                            delay.File.path, delay.Line);
                    else
                        sec.Add($"{h.FullName}: clears {flagName}", CheckStatus.Pass, delay.FullName,
                            $"Cleared a moment after the purchase {label}, in real time", delay.File.path, delay.Line);
                }
            }

            // 3. In-app review.
            var reviewers = c.model.Methods.Where(m => ReviewLaunch.IsMatch(m.Code)
                && !m.owner.methods.Any(x => x.name == "LaunchReviewFlow")).ToList(); // not the Play Review plugin's own ReviewManager
            if (reviewers.Count == 0)
                sec.Add("In-app review", CheckStatus.Info, "none", "No LaunchReviewFlow call found: no in-app review to check");
            foreach (var r in reviewers)
            {
                var launch = ReviewLaunch.Match(r.Code);
                int launchLine = r.File.LineOf(r.bodyStart + launch.Index);
                var set = setRx.Match(r.Code);
                bool setHere = set.Success && set.Index < launch.Index;
                var setBy = setHere ? r : c.model.Referencing(r).FirstOrDefault(m => setRx.IsMatch(m.Code)); // e.g. Review() before StartCoroutine
                if (setBy == null)
                {
                    sec.Add($"{r.FullName}: sets {flagName} before the review", CheckStatus.Fail, "not set",
                        $"Set {flagName} = true before LaunchReviewFlow(): otherwise an App Open ad shows when the player closes the review card",
                        r.File.path, launchLine);
                    continue;
                }
                sec.Add($"{r.FullName}: sets {flagName} before the review", CheckStatus.Pass, flagName + " = true",
                    $"Set in {setBy.FullName}", setBy.File.path, setHere ? r.File.LineOf(r.bodyStart + set.Index) : setBy.Line);

                var clears = ClearOrCall(r.owner);
                string code = r.Code;
                int from = setHere ? set.Index : 0;

                // After the review flow: cleared, a moment later, in real time.
                var after = code.Substring(launch.Index);
                var clearAfter = clears.Match(after);
                if (!clearAfter.Success)
                    sec.Add($"{r.FullName}: clears {flagName} after the review", NewIn133(c), "not cleared",
                        $"{flagName} stays true after the review: the card is an overlay (and Google's quota often shows none), so the game may not go to background and "
                        + $"the next real App Open ad is skipped. Set {flagName} = false a moment after LaunchReviewFlow ends (GenZ Ads v1.33)",
                        r.File.path, launchLine);
                else
                {
                    var before = after.Substring(0, clearAfter.Index);
                    int clearLine = r.File.LineOf(r.bodyStart + launch.Index + clearAfter.Index);
                    if (!DelayCall.IsMatch(before))
                        sec.Add($"{r.FullName}: clears {flagName} after the review", CheckStatus.Warn, "at once",
                            "Cleared straight after the review flow: if the game's return-to-foreground event comes later, the App Open ad still shows. "
                            + "Wait a moment first (e.g. 2 s with WaitForSecondsRealtime)",
                            r.File.path, clearLine);
                    else if (!RealtimeDelay.IsMatch(before))
                        sec.Add($"{r.FullName}: clears {flagName} after the review", CheckStatus.Warn, "game time",
                            "Waits in game time (WaitForSeconds) before clearing: a paused game (timeScale 0) never clears it. Use WaitForSecondsRealtime",
                            r.File.path, clearLine);
                    else
                        sec.Add($"{r.FullName}: clears {flagName} after the review", CheckStatus.Pass, "after a delay",
                            "Cleared a moment after the review flow ends, in real time", r.File.path, clearLine);
                }

                // Every early exit after the flag is set (a failed review request…) clears it in its own block.
                var stuck = new List<int>();
                foreach (Match x in EarlyExit.Matches(code))
                {
                    if (x.Index < from) continue;
                    int start = Math.Max(code.LastIndexOf('{', x.Index), from);
                    if (!clears.IsMatch(code.Substring(start, x.Index - start))) stuck.Add(r.File.LineOf(r.bodyStart + x.Index));
                }
                if (stuck.Count > 0)
                    sec.Add($"{r.FullName}: clears {flagName} on early exits", NewIn133(c), $"{stuck.Count} exit(s)",
                        $"Exits (yield break / return) at line {string.Join(", ", stuck)} leave {flagName} true, e.g. when the review request fails: "
                        + "the next real App Open ad is skipped. Clear it before exiting",
                        r.File.path, stuck[0]);
                else if (EarlyExit.IsMatch(code.Substring(from)))
                    sec.Add($"{r.FullName}: clears {flagName} on early exits", CheckStatus.Pass, "", "Every early exit clears it", r.File.path, r.Line);
            }
        }

        static Regex SourceRef(string name) => new Regex(@"\b" + Regex.Escape(name) + @"\b");

        /// <summary>Status for a missing feature GenZ Ads added in v1.33: required (Fail) from a v1.33 project, only reported for v1.32.</summary>
        static CheckStatus NewIn133(Ctx c) => c.pkg != null && c.pkg.V133 ? CheckStatus.Fail : CheckStatus.Info;

        // ================================================================= platform-only code

        // Defines of a player build (not the Editor). Version defines (UNITY_2022_3_OR_NEWER…) count as set.
        internal static readonly HashSet<string> AndroidDefines = new HashSet<string> { "UNITY_ANDROID", "PLATFORM_ANDROID" };
        internal static readonly HashSet<string> IosDefines = new HashSet<string> { "UNITY_IOS", "UNITY_IPHONE", "PLATFORM_IOS" };

        // Code that only one platform can compile (an iOS-only package such as com.unity.ads.ios-support, or Play Review on
        // Android) must sit inside #if UNITY_IOS / #if UNITY_ANDROID. Otherwise the other platform's build stops with a
        // compile error, even though the Editor compiles fine. Scripts under an .asmdef are skipped (the asmdef picks platforms).
        static void CheckPlatformCode(ReportSection sec, Ctx c)
        {
            var rules = new List<(string platform, Regex rx)>();
            foreach (var entry in c.s.platformOnlyApis ?? new string[0])
            {
                var bar = entry?.IndexOf('|') ?? -1;
                if (bar <= 0) continue;
                try { rules.Add((entry.Substring(0, bar).Trim(), new Regex(entry.Substring(bar + 1)))); }
                catch (ArgumentException e) { sec.Add("Platform-only API pattern", CheckStatus.Warn, entry, "Not a valid regex: " + e.Message); }
            }
            if (rules.Count == 0) { sec.Add("Platform-only code", CheckStatus.Info, "", "No patterns in codeChecks.platformOnlyApis"); return; }

            var asmdefDirs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            bool UnderAsmdef(string path)
            {
                for (var dir = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/'); !string.IsNullOrEmpty(dir) && dir.StartsWith("Assets", StringComparison.OrdinalIgnoreCase);
                     dir = System.IO.Path.GetDirectoryName(dir)?.Replace('\\', '/'))
                {
                    if (!asmdefDirs.TryGetValue(dir, out var has))
                        asmdefDirs[dir] = has = System.IO.Directory.Exists(dir) && System.IO.Directory.GetFiles(dir, "*.asmdef").Length > 0;
                    if (has) return true;
                }
                return false;
            }

            int problems = 0, scanned = 0;
            foreach (var f in c.model.files)
            {
                if (UnderAsmdef(f.path)) continue;
                scanned++;
                var lines = f.masked.Replace("\r\n", "\n").Split('\n');
                var inAndroid = CompiledLines(lines, AndroidDefines);
                var inIos = CompiledLines(lines, IosDefines);
                foreach (var (platform, rx) in rules)
                {
                    bool iosOnly = platform.Equals("iOS", StringComparison.OrdinalIgnoreCase);
                    var other = iosOnly ? inAndroid : inIos;
                    var hitLines = new List<int>();
                    for (int i = 0; i < lines.Length; i++)
                        if (other[i] && !lines[i].TrimStart().StartsWith("#") && rx.IsMatch(lines[i])) hitLines.Add(i + 1);
                    if (hitLines.Count == 0) continue;
                    problems++;
                    string broken = iosOnly ? "Android" : "iOS", guard = iosOnly ? "#if UNITY_IOS" : "#if UNITY_ANDROID";
                    // Fails for the build target's own build; for the other platform it's a warning (a per-platform project may never build it).
                    bool targetBreaks = c.ios == null || c.ios.Value == !iosOnly;
                    sec.Add($"{System.IO.Path.GetFileName(f.path)}: {platform}-only code", targetBreaks ? CheckStatus.Fail : CheckStatus.Warn, $"line {string.Join(", ", hitLines.Take(6))}",
                        $"{platform}-only code ({Short(rx.Match(lines[hitLines[0] - 1]).Value, 40)}) is compiled in the {broken} build, which doesn't have it: the {broken} build "
                        + $"stops with a compile error (the Editor still compiles). Put it inside {guard} … #endif, including its using line"
                        + (targetBreaks ? "" : $". The build target is {(iosOnly ? "iOS" : "Android")}, so this only matters if this project is also built for {broken}"),
                        f.path, hitLines[0]);
                }
            }
            if (problems == 0)
                sec.Add("Platform-only code guarded", CheckStatus.Pass, $"{scanned} scripts",
                    "iOS-only code is inside #if UNITY_IOS and Android-only code inside #if UNITY_ANDROID, so both builds compile");
        }

        /// <summary>For each line: is it compiled with these defines (player build, not the Editor)?</summary>
        internal static bool[] CompiledLines(string[] lines, HashSet<string> defines)
        {
            var result = new bool[lines.Length];
            var stack = new Stack<(bool parent, bool taken, bool active)>();
            bool active = true;
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].Trim();
                var m = Regex.Match(t, @"^#\s*(if|elif|else|endif)\b(.*)$");
                if (m.Success)
                {
                    var kind = m.Groups[1].Value;
                    var expr = m.Groups[2].Value;
                    if (kind == "if")
                    {
                        bool v = active && EvalDefines(expr, defines);
                        stack.Push((active, v, v));
                        active = v;
                    }
                    else if (stack.Count > 0)
                    {
                        var top = stack.Pop();
                        if (kind == "endif") active = top.parent;
                        else
                        {
                            bool v = top.parent && !top.taken && (kind == "else" || EvalDefines(expr, defines));
                            stack.Push((top.parent, top.taken || v, v));
                            active = v;
                        }
                    }
                    result[i] = false;
                    continue;
                }
                result[i] = active;
            }
            return result;
        }

        // Preprocessor expression: symbols, true/false, !, &&, ||, ==, !=, parentheses. Unknown symbols are false,
        // version defines (UNITY_2022_3_OR_NEWER…) true.
        static bool EvalDefines(string expr, HashSet<string> defines)
        {
            var tokens = Regex.Matches(expr.Split(new[] { "//" }, StringSplitOptions.None)[0], @"\w+|&&|\|\||==|!=|!|\(|\)").Cast<Match>().Select(x => x.Value).ToList();
            int pos = 0;
            bool Or() { bool v = And(); while (pos < tokens.Count && tokens[pos] == "||") { pos++; bool r = And(); v = v || r; } return v; }
            bool And() { bool v = Eq(); while (pos < tokens.Count && tokens[pos] == "&&") { pos++; bool r = Eq(); v = v && r; } return v; }
            bool Eq()
            {
                bool v = Unary();
                while (pos < tokens.Count && (tokens[pos] == "==" || tokens[pos] == "!="))
                {
                    bool eq = tokens[pos++] == "=="; bool r = Unary();
                    v = eq ? v == r : v != r;
                }
                return v;
            }
            bool Unary()
            {
                if (pos >= tokens.Count) return false;
                var tok = tokens[pos++];
                if (tok == "!") return !Unary();
                if (tok == "(") { bool v = Or(); if (pos < tokens.Count && tokens[pos] == ")") pos++; return v; }
                if (tok == "true") return true;
                if (tok == "false") return false;
                return defines.Contains(tok) || Regex.IsMatch(tok, @"^UNITY_\d+(_\d+)*_OR_NEWER$");
            }
            return tokens.Count > 0 && Or();
        }

        // ================================================================= startup order (consent, ATT, Remote Config)

        static readonly Regex FetchWait = new Regex(@"\bisFetchCompleted\b|\bOnRemoteDataFetch\b");
        static readonly Regex FetchDoneTrue = new Regex(@"\bNotifyRemoteDataFetch\s*\(\s*true\b");
        static readonly Regex FetchDoneFalse = new Regex(@"\bNotifyRemoteDataFetch\s*\(\s*false\b");
        static readonly Regex AttPrompt = new Regex(@"\bRequestAuthorizationTracking\s*\(");
        static readonly Regex UmpUpdate = new Regex(@"\bConsentInformation\s*\.\s*Update\s*\(");
        static readonly Regex AttAware = new Regex(@"\bAttPending\b|\bGetAuthorizationTrackingStatus\b|\bATTrackingStatusBinding\b");

        // GenZ Ads v1.33 startup: a first-scene screen (ConsentManager) waits for the Remote Config fetch, so FirebaseManager /
        // RemoteConfigManager must report both outcomes; on iOS, Apple's tracking prompt comes before Google's UMP form.
        // A game without these (v1.32) only gets Info rows.
        static void CheckStartupOrder(ReportSection sec, Ctx c)
        {
            // 1. Something waits for the fetch → it must hear about success and failure.
            var waiters = c.model.Methods.Where(m => FetchWait.IsMatch(m.Code) && !Regex.IsMatch(m.Code, @"\bNotifyRemoteDataFetch\b")
                                                      && !Regex.IsMatch(m.owner.Body, @"\bstatic\s+event\b[^;]*\bOnRemoteDataFetch\b")).ToList();
            // v1.33 requires these (its ConsentManager waits for the fetch); a v1.32 project only gets them when it waits too.
            var missingStatus = c.pkg != null && c.pkg.V133 ? CheckStatus.Fail : CheckStatus.Warn;
            if (waiters.Count == 0)
                sec.Add("Remote Config fetch reported", c.pkg != null && c.pkg.V133 ? CheckStatus.Warn : CheckStatus.Info, "no waiting screen",
                    c.pkg != null && c.pkg.V133
                        ? "GenZ Ads v1.33, but nothing waits for the Remote Config fetch (FirebaseManager.isFetchCompleted / OnRemoteDataFetch): ConsentManager isn't used, so ads may start with default values"
                        : "Nothing waits for the Remote Config fetch (FirebaseManager.isFetchCompleted / OnRemoteDataFetch; new in v1.33): not checked");
            else
            {
                var w = waiters[0];
                var ok = c.model.Methods.FirstOrDefault(m => FetchDoneTrue.IsMatch(m.Code));
                var fail = c.model.Methods.FirstOrDefault(m => FetchDoneFalse.IsMatch(m.Code));
                sec.Add("Remote Config fetch reported (success)", ok != null ? CheckStatus.Pass : missingStatus,
                    ok != null ? ok.FullName : "never",
                    ok != null ? $"{w.FullName} waits for the fetch; {ok.FullName} reports a successful fetch"
                               : $"{w.FullName} waits for the fetch, but nothing calls NotifyRemoteDataFetch(true): the screen always waits the full timeout. "
                                 + "Call it after the fetched values are loaded (e.g. at the end of RemoteConfigManager.LoadValues)",
                    (ok ?? w).File.path, (ok ?? w).Line);
                sec.Add("Remote Config fetch reported (failure)", fail != null ? CheckStatus.Pass : missingStatus,
                    fail != null ? fail.FullName : "never",
                    fail != null ? $"{fail.FullName} reports a failed fetch, so offline players don't wait the full timeout"
                                 : "Nothing calls NotifyRemoteDataFetch(false): when the fetch fails (offline, Firebase unavailable) the screen waits the full timeout",
                    (fail ?? w).File.path, (fail ?? w).Line);
            }

            // 2. iOS: when the game asks for tracking permission, Google's UMP form must wait for that answer.
            var att = c.model.Methods.FirstOrDefault(m => AttPrompt.IsMatch(m.Code));
            var ump = c.model.Methods.Where(m => UmpUpdate.IsMatch(m.Code)).ToList();
            if (att == null)
                sec.Add("iOS: tracking prompt before UMP", CheckStatus.Info, "no tracking prompt",
                    "No code asks for Apple's tracking permission (RequestAuthorizationTracking): not checked");
            else if (ump.Count == 0)
                sec.Add("iOS: tracking prompt before UMP", CheckStatus.Info, "no UMP", $"{att.FullName} shows the tracking prompt; no UMP ConsentInformation.Update call found");
            else
                foreach (var u in ump)
                {
                    bool waits = AttAware.IsMatch(u.owner.Body);
                    sec.Add($"iOS: {u.owner.name} waits for the tracking prompt", waits ? CheckStatus.Pass : missingStatus,
                        waits ? "waits" : "doesn't wait",
                        waits ? $"{u.owner.name} checks the tracking answer ({att.FullName} asks) before Google's consent form"
                              : $"{att.FullName} asks for Apple's tracking permission, but {u.FullName} starts Google's consent form without waiting for the answer: "
                                + "on iOS both can open at once. Wait until the tracking prompt is answered (e.g. ConsentManager.AttPending)",
                        u.File.path, u.Line);
                }
        }

        // ================================================================= other logic

        static void CheckLogic(ReportSection sec, Ctx c)
        {
            // AdMob events off the main thread.
            bool admob = c.adMethods.Any(m => UsesAdmob.IsMatch(m.Code));
            var flagM = c.model.Methods.FirstOrDefault(m => MainThreadFlag.IsMatch(m.Code));
            if (admob)
                sec.Add("AdMob events on Unity's main thread", flagM != null ? CheckStatus.Pass : CheckStatus.Fail,
                    flagM != null ? flagM.FullName : "MobileAds.RaiseAdEventsOnUnityMainThread",
                    flagM != null ? "RaiseAdEventsOnUnityMainThread = true"
                        : "Not set — AdMob callbacks (reward grant, ad closed → reload) run on a background thread; Unity calls there throw, the catch hides it, and the reward/reload is lost. Set MobileAds.RaiseAdEventsOnUnityMainThread = true before MobileAds.Initialize",
                    flagM?.File.path, flagM?.Line ?? 0);

            // Invoke("Name") / StartCoroutine("Name") to a method that doesn't exist.
            int bad = 0;
            foreach (var m in c.adMethods.Concat(c.gameMethods.Where(g => g.owner.file.path.IndexOf("/Scripts", StringComparison.OrdinalIgnoreCase) >= 0)))
            foreach (Match im in InvokeByString.Matches(m.CodeWithStrings))
            {
                var target = im.Groups[2].Value;
                if (m.owner.methods.Any(x => x.name == target) || c.model.classes.Any(k => k.file == m.owner.file && k.methods.Any(x => x.name == target))) continue;
                bad++;
                sec.Add($"{m.FullName}: {im.Groups[1].Value}(\"{target}\")", CheckStatus.Fail, target,
                    "No method with this name in the class — it silently never runs", m.File.path, m.File.LineOf(m.bodyStart + im.Index));
            }
            if (bad == 0) sec.Add("Invoke / StartCoroutine by name", CheckStatus.Pass, "", "Every method named in a string exists");

            // Delegate callbacks invoked without a null check.
            int unsafeCalls = 0;
            foreach (var m in c.adMethods)
            foreach (var f in c.delegateFields)
            {
                var call = Regex.Match(m.Code, @"(?<![\w?])\b" + Regex.Escape(f) + @"\s*\(\s*\)");
                if (!call.Success || Regex.IsMatch(m.Code, @"\b" + Regex.Escape(f) + @"\s*(!=|==)\s*null")) continue;
                unsafeCalls++;
                sec.Add($"{m.FullName}: {f}() null check", CheckStatus.Warn, f + "()",
                    $"Called without a null check — throws if no callback was set (use {f}?.Invoke())", m.File.path, m.File.LineOf(m.bodyStart + call.Index));
            }
            if (unsafeCalls == 0) sec.Add("Callbacks null-checked", CheckStatus.Pass, "", "Every ad callback is invoked safely");

            // Empty catch blocks hide all of the above at runtime.
            int empty = c.adMethods.Sum(m => EmptyCatch.Matches(m.Code).Count);
            sec.Add("Empty catch blocks in ad scripts", empty == 0 ? CheckStatus.Pass : CheckStatus.Info, empty.ToString(),
                empty == 0 ? "None" : "These hide runtime errors in ad code — a failure there is silent. Log the exception at least in debug builds.");
        }

        // ================================================================= helpers

        static HashSet<string> DelegateFields(List<ClassInfo> classes)
        {
            var types = new HashSet<string>(classes.SelectMany(k => Regex.Matches(k.Body, @"\bdelegate\s+\w+\s+(\w+)\s*\(").Cast<Match>().Select(x => x.Groups[1].Value)));
            types.UnionWith(new[] { "Action", "UnityAction" });
            var fieldRx = new Regex(@"\b(" + string.Join("|", types.Select(Regex.Escape)) + @")(<[^>]*>)?\s+(\w+)\s*[;={]");
            return new HashSet<string>(classes.SelectMany(k => fieldRx.Matches(k.Body).Cast<Match>().Select(x => x.Groups[3].Value)));
        }

        /// <summary>Handlers subscribed to an event: named methods (+= Foo) or lambdas (+= (...) => { }), as MethodInfos.</summary>
        static IEnumerable<MethodInfo> Handlers(Ctx c, string eventPattern, Func<MethodInfo, bool> within = null)
        {
            var rx = new Regex(eventPattern + @"\s*\+=\s*");
            foreach (var m in c.adMethods)
            {
                if (within != null && !within(m)) continue;
                foreach (Match sm in rx.Matches(m.Code))
                {
                    int p = m.bodyStart + sm.Index + sm.Length;
                    var text = m.File.masked;
                    var named = Regex.Match(text.Substring(p, Math.Min(200, text.Length - p)), @"^(new\s+\w+\s*\(\s*)?(\w+)\s*\)?\s*;");
                    if (named.Success)
                    {
                        var target = m.owner.methods.FirstOrDefault(x => x.name == named.Groups[2].Value)
                                     ?? c.adMethods.FirstOrDefault(x => x.name == named.Groups[2].Value);
                        if (target != null) yield return target;
                        continue;
                    }
                    int arrow = text.IndexOf("=>", p, StringComparison.Ordinal);
                    int semi = text.IndexOf(';', p);
                    if (arrow < 0 || (semi >= 0 && semi < arrow)) continue;
                    int q = arrow + 2;
                    while (q < text.Length && char.IsWhiteSpace(text[q])) q++;
                    int start, end;
                    if (q < text.Length && text[q] == '{') { start = q + 1; end = MatchBrace(text, q); }
                    else { start = q; end = text.IndexOf(';', q); }
                    if (end < 0) continue;
                    yield return new MethodInfo { name = m.name + "→lambda", owner = m.owner, headerStart = m.bodyStart + sm.Index, bodyStart = start, bodyEnd = end };
                }
            }
        }

        static string Header(MethodInfo m) => m.File.masked.Substring(m.headerStart, m.bodyStart - m.headerStart);

        /// <summary>The statement or { block } that follows a position (e.g. after an if condition).</summary>
        static string BlockAfter(string code, int pos, out int start)
        {
            int i = pos;
            while (i < code.Length && char.IsWhiteSpace(code[i])) i++;
            if (i < code.Length && code[i] == '{')
            {
                int close = MatchBrace(code, i);
                start = i;
                return code.Substring(i, (close < 0 ? code.Length : close + 1) - i);
            }
            int semi = code.IndexOf(';', i);
            start = i;
            return code.Substring(i, (semi < 0 ? code.Length : semi + 1) - i);
        }

        /// <summary>Text of the argument list starting at an opening parenthesis.</summary>
        static string ArgText(string code, int open)
        {
            int d = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '(') d++;
                else if (code[i] == ')' && --d == 0) return code.Substring(open + 1, i - open - 1);
            }
            return "";
        }

        static string Short(string s, int max) => s == null || s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
