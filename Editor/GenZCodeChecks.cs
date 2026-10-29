// GenZ-TestSuit-v1.0
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

        class Ctx
        {
            public GenZSourceModel model;
            public CodeCheckSettings s;
            public List<ClassInfo> ad;     // ad / analytics scripts + the gap timer
            public List<ClassInfo> game;   // everything else that was scanned
            public List<MethodInfo> adMethods, gameMethods;
            public Regex resetRx, gateRx;
            public HashSet<string> delegateFields;   // callback fields declared in the ad scripts (e.g. _rewarded)
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

        public static void Run(TestReport report, CodeCheckSettings settings, GenZSourceModel model = null)
        {
            var ctx = MakeCtx(settings, model ?? GenZSourceModel.Load(settings.excludeFolders));
            CheckScripts(report.Section(SectionScripts, TestStage.Integration), ctx);
            CheckWaterfall(report.Section(SectionWaterfall, TestStage.AdCalling), ctx);
            CheckBanner(report.Section(SectionBanner, TestStage.AdCalling), ctx);
            CheckReloads(report.Section(SectionReload, TestStage.AdCalling), ctx);
            CheckRevenue(report.Section(SectionRevenue, TestStage.AdCalling), ctx);
            CheckTimerResets(report.Section(SectionTimer, TestStage.AdCalling), report.Section(SectionRewardCallback, TestStage.AdCalling), ctx);
            CheckRemoteConfig(report.Section(SectionRemote, TestStage.AdCalling), ctx);
            CheckRemoteDefaults(report.Section(SectionRemoteDefaults, TestStage.AdCalling), ctx);
            CheckLogic(report.Section(SectionLogic, TestStage.AdCalling), ctx);
        }

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
