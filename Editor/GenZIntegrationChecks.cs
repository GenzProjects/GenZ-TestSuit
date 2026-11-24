// GenZ-TestSuit-v1.0.1
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GenZ.TestSuit
{
    /// <summary>
    /// Step 2 file checks: Firebase config files, SDK Android dependencies resolved into Gradle, and the Gradle /
    /// manifest setup. Reads files only (no UnityEditor), so it also runs from the console harness.
    /// Paths are relative to the project root (the working directory in the Editor).
    /// </summary>
    public static class GenZIntegrationChecks
    {
        const string SectionFirebase = "SDK · Firebase config";
        const string SectionDeps = "SDK · Installed and resolved (Android dependencies)";
        const string SectionBuild = "SDK · Android build setup";
        const string SectionGa = "SDK · GameAnalytics ad revenue formats (MAX = AdMob)";
        const string SectionAdapters = "SDK · AppLovin MAX adapters (mediation networks)";
        const string SectionIos = "SDK · iOS build setup (App Tracking, SKAdNetwork)";
        const string SectionDepsIos = "SDK · Installed (iOS dependencies)";

        const string PluginsAndroid = "Assets/Plugins/Android";
        const string FirebaseXml = PluginsAndroid + "/FirebaseApp.androidlib/res/values/google-services.xml";
        const string DesktopJson = "Assets/StreamingAssets/google-services-desktop.json";
        const string MainGradle = PluginsAndroid + "/mainTemplate.gradle";
        const string GradleProperties = PluginsAndroid + "/gradleTemplate.properties";
        const string MainManifest = PluginsAndroid + "/AndroidManifest.xml";

        static readonly Regex SpecRegex = new Regex(@"<androidPackage\s+spec\s*=\s*""([^""]+)""");

        /// <param name="ios">True when the build target is iOS: the Android-only files (google-services.json, Gradle, manifest)
        /// are skipped and their iOS counterparts checked (GoogleService-Info.plist, CocoaPods adapters, App Tracking text,
        /// SKAdNetwork files).</param>
        public static void Run(TestReport report, string packageName, IntegrationSettings s, bool ios = false)
        {
            var projectSettings = Read("ProjectSettings/ProjectSettings.asset") ?? "";
            CheckFirebase(report.Section(SectionFirebase, TestStage.Integration), packageName, s, ios);
            CheckDependencies(report.Section(ios ? SectionDepsIos : SectionDeps, TestStage.Integration), s, projectSettings, ios);
            CheckMaxAdapters(report.Section(SectionAdapters, TestStage.Integration), s, projectSettings, ios);
            if (ios)
                CheckIosBuild(report.Section(SectionIos, TestStage.Integration), s);
            else
                CheckBuildSetup(report.Section(SectionBuild, TestStage.Integration), projectSettings, s);
            CheckGaAdFormats(report, s, projectSettings);
        }

        // ================================================================= Firebase

        static void CheckFirebase(ReportSection sec, string pkg, IntegrationSettings s, bool ios)
        {
            if (ios) CheckFirebaseIos(sec, pkg);
            else CheckFirebaseAndroid(sec, pkg);
            CheckFirebaseModules(sec, s);
        }

        // iOS: GoogleService-Info.plist must exist and its BUNDLE_ID must be this game's iOS bundle identifier.
        static void CheckFirebaseIos(ReportSection sec, string pkg)
        {
            var plists = Directory.GetFiles("Assets", "GoogleService-Info.plist", SearchOption.AllDirectories).Select(Norm).ToList();
            if (plists.Count == 0)
            {
                sec.Add("GoogleService-Info.plist", CheckStatus.Fail, "",
                    "Missing — download it from the Firebase console (Project settings → Your apps → iOS app) and put it in Assets");
                return;
            }
            foreach (var p in plists)
            {
                var text = Read(p) ?? "";
                var bundle = PlistString(text, "BUNDLE_ID");
                var project = PlistString(text, "PROJECT_ID");
                string label = plists.Count == 1 ? "GoogleService-Info.plist" : "GoogleService-Info.plist · " + p;
                if (bundle.Length == 0)
                    sec.Add(label, CheckStatus.Fail, p, "Not a valid Firebase iOS config (no BUNDLE_ID) — download it again from the Firebase console", p, 1);
                else if (bundle == pkg)
                    sec.Add(label, CheckStatus.Pass, pkg, $"Belongs to this game: Firebase project \"{project}\", iOS app {pkg}", p, LineOf(text, "BUNDLE_ID"));
                else
                    sec.Add(label, CheckStatus.Fail, bundle,
                        $"Belongs to another app: BUNDLE_ID is {bundle}, this game's iOS bundle identifier is {pkg}. Download this game's GoogleService-Info.plist from the Firebase console",
                        p, LineOf(text, "BUNDLE_ID"));
            }
            if (plists.Count > 1)
                sec.Add("Only one GoogleService-Info.plist", CheckStatus.Warn, plists.Count + " copies",
                    "Firebase may read any of them — keep only this game's file: " + string.Join(", ", plists));
        }

        static string PlistString(string plist, string key) =>
            Regex.Match(plist, @"<key>\s*" + Regex.Escape(key) + @"\s*</key>\s*<string>([^<]*)</string>").Groups[1].Value.Trim();

        static void CheckFirebaseAndroid(ReportSection sec, string pkg)
        {
            var jsons = Directory.GetFiles("Assets", "google-services.json", SearchOption.AllDirectories).Select(Norm).ToList();
            if (jsons.Count == 0)
            {
                sec.Add("google-services.json", CheckStatus.Fail, "",
                    "Missing — download it from the Firebase console (Project settings → Your apps → Android app) and put it in Assets");
            }
            else
            {
                // Every copy must belong to this game: its Android app list (client[].…package_name) must contain the
                // Build Settings package. One row per copy, so a wrong second copy can't hide behind a right one.
                string path = null, json = null, project = null;
                foreach (var p in jsons)
                {
                    var text = Read(p) ?? "";
                    var proj = JsonString(text, "project_id");
                    var packages = Regex.Matches(text, @"""package_name""\s*:\s*""([^""]+)""").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
                    bool ours = packages.Contains(pkg);
                    string label = jsons.Count == 1 ? "google-services.json" : "google-services.json · " + p;
                    if (proj.Length == 0 || packages.Count == 0)
                        sec.Add(label, CheckStatus.Fail, p, "Not a valid Firebase Android config (no project_id / package_name) — download it again from the Firebase console", p, 1);
                    else if (ours)
                        sec.Add(label, CheckStatus.Pass, pkg, $"Belongs to this game: Firebase project \"{proj}\" has an Android app with package {pkg}"
                            + (packages.Count > 1 ? $" (also: {string.Join(", ", packages.Where(x => x != pkg))})" : ""), p, LineOf(text, "\"" + pkg + "\""));
                    else
                        sec.Add(label, CheckStatus.Fail, string.Join(", ", packages),
                            $"Belongs to another game: Firebase project \"{proj}\" has no Android app for {pkg}. Download this game's google-services.json (Firebase console → Project settings → Your apps), or add {pkg} as an Android app there",
                            p, LineOf(text, "\"package_name\""));
                    if (ours && path == null) { path = p; json = text; project = proj; }
                }
                if (jsons.Count > 1)
                    sec.Add("Only one google-services.json", CheckStatus.Warn, jsons.Count + " copies",
                        "Firebase may read any of them — keep only this game's file: " + string.Join(", ", jsons));
                if (path == null) { path = jsons[0]; json = Read(path) ?? ""; project = JsonString(json, "project_id"); }

                // The generated Android resources are what the build really uses; a stale copy points at another project.
                var appId = AppIdFor(json, pkg);
                var xml = Read(FirebaseXml);
                if (xml == null)
                    sec.Add("Generated Android config", CheckStatus.Warn, FirebaseXml,
                        "Not generated yet. Firebase creates it from google-services.json when the Editor imports it — reimport google-services.json");
                else
                {
                    var xmlProject = XmlString(xml, "project_id");
                    var xmlAppId = XmlString(xml, "google_app_id");
                    bool sameProject = xmlProject == project;
                    bool sameApp = appId == null || xmlAppId == appId;
                    sec.Add("Generated Android config", sameProject && sameApp ? CheckStatus.Pass : CheckStatus.Fail, FirebaseXml,
                        sameProject && sameApp ? "Matches google-services.json (project and app ID)"
                            : $"Stale: it has project \"{xmlProject}\" / app {xmlAppId}, google-services.json has \"{project}\" / {appId}. Reimport google-services.json so Firebase regenerates it");
                }

                var desktop = Read(DesktopJson);
                if (desktop != null)
                {
                    var dp = JsonString(desktop, "project_id");
                    sec.Add("Editor config (StreamingAssets)", dp == project ? CheckStatus.Pass : CheckStatus.Warn, DesktopJson,
                        dp == project ? "Matches google-services.json (used in Play mode)" : $"Has project \"{dp}\" — Play mode in the Editor uses another Firebase project. Reimport google-services.json");
                }
            }
        }

        static void CheckFirebaseModules(ReportSection sec, IntegrationSettings s)
        {
            // Firebase modules the game code uses.
            var dlls = Directory.GetFiles("Assets", "Firebase.*.dll", SearchOption.AllDirectories)
                .Select(f => Path.GetFileNameWithoutExtension(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var missing = s.firebaseModules.Where(m => !dlls.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
            sec.Add("Firebase SDK modules", missing.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail,
                string.Join(", ", s.firebaseModules),
                missing.Count == 0 ? "All installed" : "Not installed: " + string.Join(", ", missing) + " — import the matching Firebase .unitypackage");
        }

        /// <summary>mobilesdk_app_id of the json client whose package_name is pkg (it comes just before it in client_info).</summary>
        static string AppIdFor(string json, string pkg)
        {
            var pm = Regex.Match(json, @"""package_name""\s*:\s*""" + Regex.Escape(pkg) + @"""");
            if (!pm.Success) return null;
            var ids = Regex.Matches(json.Substring(0, pm.Index), @"""mobilesdk_app_id""\s*:\s*""([^""]+)""");
            return ids.Count == 0 ? null : ids[ids.Count - 1].Groups[1].Value;
        }

        // ================================================================= Android dependencies

        static void CheckDependencies(ReportSection sec, IntegrationSettings s, string projectSettings, bool ios)
        {
            // Every *Dependencies.xml, grouped by SDK folder (MAX adapters each count as their own SDK).
            var groups = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles("Assets", "*Dependencies.xml", SearchOption.AllDirectories).Select(Norm))
            {
                var parts = file.Split('/');
                var key = parts.Length > 3 && parts[1] == "MaxSdk" && parts[2] == "Mediation" ? "MaxSdk/Mediation/" + parts[3] : parts[1];
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<string>();
                list.Add(file);
            }

            // Required SDKs must be installed.
            foreach (var entry in s.requiredSdks)
            {
                var bar = entry.IndexOf('|');
                var label = bar > 0 ? entry.Substring(0, bar) : entry;
                var folder = bar > 0 ? entry.Substring(bar + 1) : entry;
                bool installed = Directory.Exists(Path.Combine("Assets", folder));
                sec.Add($"{label} installed", installed ? CheckStatus.Pass : CheckStatus.Fail, "Assets/" + folder,
                    installed ? (groups.ContainsKey(folder) ? "SDK folder and its dependencies file found" : "SDK folder found") : "SDK folder is missing — import the SDK");
            }

            if (ios)
            {
                sec.Add("Resolved (iOS)", CheckStatus.Info, "CocoaPods",
                    "iOS: the pods in the *Dependencies.xml files are installed when Unity builds the Xcode project (Podfile), so there's nothing resolved to check here");
                return;
            }

            bool customGradle = Regex.IsMatch(projectSettings, @"useCustomMainGradleTemplate:\s*1");
            var gradle = Read(MainGradle);
            if (!customGradle || gradle == null)
            {
                sec.Add("Resolved into Gradle", CheckStatus.Info, MainGradle,
                    "Custom Main Gradle Template is off, so the resolved dependencies can't be checked here");
                return;
            }

            // Each SDK's androidPackage specs must appear in mainTemplate.gradle (written by Android Resolver).
            foreach (var g in groups)
            {
                var notResolved = new List<string>();
                var otherVersion = new List<string>();
                int total = 0;
                foreach (var xmlPath in g.Value)
                {
                    var xml = Read(xmlPath) ?? "";
                    foreach (Match sm in SpecRegex.Matches(xml))
                    {
                        total++;
                        var spec = sm.Groups[1].Value.Trim();
                        var p = spec.Split(':');
                        if (p.Length < 3) continue;
                        var ga = p[0] + ":" + p[1];
                        var line = Regex.Match(gradle, @"['""]" + Regex.Escape(ga) + @":([^'""]+)['""][^\r\n]*");
                        if (!line.Success) { notResolved.Add(ga); continue; }
                        // The resolver may pick a higher version requested by another SDK; only flag lines it attributes to this file.
                        if (line.Groups[1].Value != p[2] && line.Value.Contains(xmlPath))
                            otherVersion.Add($"{ga} ({p[2]} → {line.Groups[1].Value})");
                    }
                }
                if (total == 0) continue;
                var status = notResolved.Count > 0 ? CheckStatus.Fail : otherVersion.Count > 0 ? CheckStatus.Warn : CheckStatus.Pass;
                var msg = notResolved.Count > 0
                    ? "Not in mainTemplate.gradle: " + string.Join(", ", notResolved) + " — run Assets → External Dependency Manager → Android Resolver → Force Resolve"
                    : otherVersion.Count > 0 ? "Resolved at another version than requested: " + string.Join(", ", otherVersion) + " — Force Resolve after updating an SDK"
                    : $"{total} dependenc{(total == 1 ? "y" : "ies")} resolved";
                sec.Add($"{g.Key} resolved", status, string.Join(", ", g.Value.Select(Path.GetFileName).Distinct()), msg, g.Value[0], 1);
            }
        }

        // ================================================================= AppLovin MAX adapters

        static readonly Regex XmlComment = new Regex(@"<!--.*?-->", RegexOptions.Singleline);
        static readonly Regex GradleDep = new Regex(@"^[ \t]*(?:implementation|api|compile)\s*\(?\s*['""]([^'"":\s]+):([^'"":\s]+):([^'""\s]+)['""]", RegexOptions.Multiline);
        static readonly Regex AarName = new Regex(@"^([\w.-]+?)\.([\w-]+)-(\d[\w.\-\[\]]*)\.(aar|jar)$");

        static readonly Regex IosPodTag = new Regex(@"<iosPod\b([^>]*)>", RegexOptions.IgnoreCase);

        class Found { public string key, version, where; public int line; }
        class AdapterEntry { public string name, android, ios; }

        // The studio's MAX adapter set: every listed adapter installed, IronSource gone, and any other adapter reported.
        // Android: Maven packages from every *Dependencies.xml (comments ignored), the resolved mainTemplate.gradle and adapter
        // .aar/.jar files in Plugins/Android. iOS: the <iosPod> names in the same *Dependencies.xml files. Both: MaxSdk/Mediation.
        static void CheckMaxAdapters(ReportSection sec, IntegrationSettings s, string projectSettings, bool ios)
        {
            var required = ParseAdapters(s.maxAdapters);
            var forbidden = ParseAdapters(s.forbiddenAdapters);
            string platform = ios ? "iOS" : "Android";
            string Spec(AdapterEntry e) => ios ? e.ios : e.android;
            string pattern = ios ? s.iosAdapterPattern : s.adapterPattern;
            Regex adapterRx = null;
            try { if (!string.IsNullOrWhiteSpace(pattern)) adapterRx = new Regex(pattern, RegexOptions.IgnoreCase); }
            catch (ArgumentException e) { sec.Add("Adapter pattern", CheckStatus.Warn, pattern, "Not a valid regex, unlisted adapters aren't looked for: " + e.Message); }

            var found = ios ? FindIosPods() : FindAndroidPackages(projectSettings);

            bool maxInstalled = Directory.Exists("Assets/MaxSdk")
                                || found.Any(f => f.key.StartsWith("com.applovin:applovin-sdk", StringComparison.OrdinalIgnoreCase) || f.key.Equals("AppLovinSDK", StringComparison.OrdinalIgnoreCase));
            if (!maxInstalled)
            {
                sec.Add("AppLovin MAX", CheckStatus.Info, "not installed", "No MaxSdk folder or AppLovin SDK dependency: adapters not checked");
                return;
            }

            bool Matches(Found f, string spec) => spec.EndsWith(":")
                ? f.key.StartsWith(spec, StringComparison.OrdinalIgnoreCase)
                : f.key.Equals(spec, StringComparison.OrdinalIgnoreCase);
            string Places(IEnumerable<Found> list) => string.Join(", ", list.Select(f => f.where + (f.line > 0 ? ":" + f.line : "")).Distinct().Take(4));
            string resolveHint = ios ? "then build (pods are installed when Unity builds the Xcode project)" : "then Force Resolve";

            // 1. Every listed adapter is installed (its Dependencies.xml); on Android, one only left in Gradle / Plugins/Android fails too.
            var missing = new List<string>();
            int listed = 0;
            foreach (var e in required)
            {
                var spec = Spec(e);
                if (spec.Length == 0)
                {
                    sec.Add($"{e.name} adapter", CheckStatus.Warn, "not checked",
                        $"No {platform} package for {e.name} in config integration.maxAdapters ('<Network>|<Android package>|<iOS pod>'): add it so the {platform} build is checked");
                    continue;
                }
                listed++;
                var hits = found.Where(f => Matches(f, spec)).ToList();
                var xmlHit = hits.FirstOrDefault(f => f.where.EndsWith(".xml"));
                if (xmlHit != null)
                    sec.Add($"{e.name} adapter", CheckStatus.Pass, xmlHit.version, $"{spec} in {xmlHit.where}", xmlHit.where, xmlHit.line);
                else if (hits.Count > 0)
                {
                    missing.Add(e.name + " (only left in Gradle)");
                    sec.Add($"{e.name} adapter", CheckStatus.Fail, "not installed",
                        $"{spec} is only in {Places(hits)}, left from an earlier resolve: its Dependencies.xml is gone, so the next Force Resolve drops it. Reinstall the adapter",
                        hits[0].where, hits[0].line);
                }
                else
                {
                    missing.Add(e.name);
                    bool custom = !(e.android.StartsWith("com.applovin.mediation:", StringComparison.OrdinalIgnoreCase)
                                    || e.ios.StartsWith("AppLovinMediation", StringComparison.OrdinalIgnoreCase));
                    sec.Add($"{e.name} adapter", CheckStatus.Fail, "missing",
                        $"{spec} isn't installed: MAX can't fill ads from {e.name} on {platform}. "
                        + (custom ? $"It's a custom network: add its *Dependencies.xml with the {platform} {(ios ? "pod" : "package")} (e.g. under Assets/Editor) {resolveHint}"
                                  : $"Install it from AppLovin → Integration Manager, {resolveHint}"));
                }
            }
            sec.Add("All studio adapters installed", missing.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail,
                missing.Count == 0 ? $"{listed} of {listed} ({platform})" : $"{missing.Count} missing ({platform})",
                missing.Count == 0 ? $"Every adapter in the studio list (config integration.maxAdapters) is there for {platform}" : "Missing: " + string.Join(", ", missing));

            // 2. Removed networks (IronSource) must be fully gone: dependency files, Gradle, libraries and the Mediation folder.
            foreach (var e in forbidden)
            {
                var spec = Spec(e);
                var hits = spec.Length == 0 ? new List<Found>() : found.Where(f => Matches(f, spec)).ToList();
                var folderName = Regex.Replace(e.name, @"\W.*$", "");
                // The Integration Manager's folder (MaxSdk/Mediation/IronSource), for MAX adapters only.
                bool maxAdapter = e.android.StartsWith("com.applovin.mediation:", StringComparison.OrdinalIgnoreCase)
                                  || e.ios.StartsWith("AppLovinMediation", StringComparison.OrdinalIgnoreCase);
                var folder = Directory.Exists("Assets/MaxSdk/Mediation") && folderName.Length > 0 && maxAdapter
                    ? Directory.GetDirectories("Assets/MaxSdk/Mediation").Select(Norm).FirstOrDefault(d => string.Equals(Path.GetFileName(d), folderName, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (spec.Length == 0 && folder == null) continue; // nothing to look for on this platform
                if (hits.Count == 0 && folder == null)
                {
                    sec.Add($"{e.name} removed", CheckStatus.Pass, "not included", $"No {spec} in the project");
                    continue;
                }
                var where = Places(hits) + (folder != null ? (hits.Count > 0 ? ", " : "") + folder : "");
                sec.Add($"{e.name} removed", CheckStatus.Fail, "still included",
                    $"{e.name} must not be in the build, but it's in {where}. Remove it with AppLovin → Integration Manager (or delete "
                    + (folder ?? "its Dependencies.xml") + ")" + (ios ? "" : ", then Force Resolve so mainTemplate.gradle drops it"),
                    hits.FirstOrDefault()?.where ?? folder, hits.FirstOrDefault()?.line ?? 0);
            }

            // 3. Any other adapter: not in the studio list.
            int unknown = 0;
            if (adapterRx != null)
                foreach (var g in found.Where(f => adapterRx.IsMatch(f.key)
                                                  && !required.Any(r => Spec(r).Length > 0 && Matches(f, Spec(r)))
                                                  && !forbidden.Any(r => Spec(r).Length > 0 && Matches(f, Spec(r))))
                                       .GroupBy(f => f.key, StringComparer.OrdinalIgnoreCase))
                {
                    unknown++;
                    var first = g.OrderBy(f => f.where.EndsWith(".xml") ? 0 : 1).First();
                    sec.Add($"Unlisted adapter · {g.Key}", CheckStatus.Warn, first.version,
                        $"Not in the studio adapter list (config integration.maxAdapters), found in {Places(g)}. "
                        + "Remove it if the network isn't used, or add it to the list if the studio now uses it",
                        first.where, first.line);
                }
            if (adapterRx != null && unknown == 0)
                sec.Add("No unlisted adapters", CheckStatus.Pass, platform, "Every adapter in the project is in the studio list");
        }

        static List<Found> FindAndroidPackages(string projectSettings)
        {
            var found = new List<Found>();
            foreach (var file in Directory.GetFiles("Assets", "*Dependencies.xml", SearchOption.AllDirectories).Select(Norm))
            {
                var xml = XmlComment.Replace(Read(file) ?? "", m => Regex.Replace(m.Value, "[^\n]", " "));
                foreach (Match sm in SpecRegex.Matches(xml))
                {
                    var p = sm.Groups[1].Value.Trim().Split(':');
                    if (p.Length >= 2) found.Add(new Found { key = p[0] + ":" + p[1], version = p.Length > 2 ? p[2] : "", where = file, line = LineAt(xml, sm.Index) });
                }
            }
            bool customGradle = Regex.IsMatch(projectSettings, @"useCustomMainGradleTemplate:\s*1");
            var gradle = customGradle ? Read(MainGradle) : null;
            if (gradle != null)
                foreach (Match gm in GradleDep.Matches(gradle))
                    found.Add(new Found { key = gm.Groups[1].Value + ":" + gm.Groups[2].Value, version = gm.Groups[3].Value, where = MainGradle, line = LineAt(gradle, gm.Index) });
            if (Directory.Exists(PluginsAndroid))
                foreach (var lib in Directory.GetFiles(PluginsAndroid, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".aar") || f.EndsWith(".jar")).Select(Norm))
                {
                    var am = AarName.Match(Path.GetFileName(lib));
                    if (am.Success) found.Add(new Found { key = am.Groups[1].Value + ":" + am.Groups[2].Value, version = am.Groups[3].Value, where = lib, line = 0 });
                }
            return found;
        }

        static List<Found> FindIosPods()
        {
            var found = new List<Found>();
            foreach (var file in Directory.GetFiles("Assets", "*Dependencies.xml", SearchOption.AllDirectories).Select(Norm))
            {
                var xml = XmlComment.Replace(Read(file) ?? "", m => Regex.Replace(m.Value, "[^\n]", " "));
                foreach (Match pm in IosPodTag.Matches(xml))
                {
                    var name = Regex.Match(pm.Groups[1].Value, @"\bname\s*=\s*""([^""]+)""").Groups[1].Value.Trim();
                    if (name.Length == 0) continue;
                    var version = Regex.Match(pm.Groups[1].Value, @"\bversion\s*=\s*""([^""]*)""").Groups[1].Value.Trim();
                    found.Add(new Found { key = name, version = version, where = file, line = LineAt(xml, pm.Index) });
                }
            }
            return found;
        }

        // '<Network>|<Android group:artifact>|<iOS pod>'; the iOS part is optional (old 2-part entries = Android only).
        static List<AdapterEntry> ParseAdapters(string[] entries) =>
            (entries ?? new string[0]).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e =>
            {
                var p = e.Split('|');
                return new AdapterEntry
                {
                    name = p[0].Trim(),
                    android = p.Length > 1 ? p[1].Trim() : "",
                    ios = p.Length > 2 ? p[2].Trim() : "",
                };
            }).ToList();

        // ================================================================= iOS build setup

        static readonly Regex AttRequest = new Regex(@"\bRequestAuthorizationTracking\s*\(|\brequestTrackingAuthorization\b");

        // iOS: Apple's tracking prompt needs NSUserTrackingUsageDescription in Info.plist (iOS stops the app without it), and the
        // ios-support package copies every SKAdNetworks(.xml/.json) file in the project into Info.plist, sample ones included.
        static void CheckIosBuild(ReportSection sec, IntegrationSettings s)
        {
            CheckSkAdNetworkIds(sec, s);

            // Who shows the tracking prompt: game code, or MAX's own consent flow.
            string promptIn = null;
            foreach (var f in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories).Select(Norm))
            {
                if (f.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 || f.StartsWith("Assets/MaxSdk/", StringComparison.OrdinalIgnoreCase)) continue;
                var code = StripComments(Read(f) ?? "");
                if (AttRequest.IsMatch(code)) { promptIn = f; break; }
            }
            var maxSettings = Read("ProjectSettings/AppLovinInternalSettings.json") ?? "";
            bool maxFlow = Regex.IsMatch(maxSettings, @"""consentFlowEnabled""\s*:\s*true");
            if (maxFlow && promptIn == null) promptIn = "AppLovin MAX consent flow";

            // Where the prompt text can be set; each SDK's iOS build step writes it into Info.plist.
            var texts = new List<string>();
            var gma = Directory.GetFiles("Assets", "GoogleMobileAdsSettings.asset", SearchOption.AllDirectories).Select(Norm).FirstOrDefault();
            if (gma != null && Regex.IsMatch(Read(gma) ?? "", @"^[ \t]*userTrackingUsageDescription:[ \t]*['""]?[^'""\s]", RegexOptions.Multiline))
                texts.Add("Google Mobile Ads settings");
            var adjust = Directory.GetFiles("Assets", "AdjustSettings.asset", SearchOption.AllDirectories).Select(Norm).FirstOrDefault();
            if (adjust != null && Regex.IsMatch(Read(adjust) ?? "", @"^[ \t]*_iOSUserTrackingUsageDescription:[ \t]*['""]?[^'""\s]", RegexOptions.Multiline))
                texts.Add("Adjust settings");
            if (maxFlow && Regex.IsMatch(maxSettings, @"""userTrackingUsageDescriptionEn""\s*:\s*""[^""]+"""))
                texts.Add("AppLovin MAX consent flow");

            if (promptIn == null)
                sec.Add("App Tracking prompt text", CheckStatus.Info, texts.Count > 0 ? string.Join(", ", texts) : "not set",
                    "No code asks for Apple's tracking permission (RequestAuthorizationTracking), so NSUserTrackingUsageDescription isn't required");
            else if (texts.Count > 0)
                sec.Add("App Tracking prompt text", CheckStatus.Pass, string.Join(", ", texts),
                    $"The tracking prompt ({promptIn}) gets its NSUserTrackingUsageDescription text from {string.Join(", ", texts)}");
            else
                sec.Add("App Tracking prompt text", CheckStatus.Fail, "not set",
                    $"{promptIn} asks for Apple's tracking permission, but no NSUserTrackingUsageDescription text is set: iOS stops the app when the prompt "
                    + "is requested, and App Review rejects it. Set it in Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings (User Tracking Usage Description), "
                    + "Adjust settings, or AppLovin's consent flow", promptIn.EndsWith(".cs") ? promptIn : null, 0);

            // SKAdNetwork files the ios-support package's build step reads (by exact name, anywhere in the project).
            var skan = Directory.GetFiles("Assets", "SKAdNetworks*", SearchOption.AllDirectories).Select(Norm)
                .Where(f => { var n = Path.GetFileName(f); return n == "SKAdNetworks" || n == "SKAdNetworks.xml" || n == "SKAdNetworks.json"; }).ToList();
            bool iosSupport = (Read("Packages/manifest.json") ?? "").Contains("com.unity.ads.ios-support");
            if (skan.Count == 0)
                sec.Add("SKAdNetwork files", CheckStatus.Pass, "none", "No SKAdNetworks files in Assets: Info.plist only gets the SKAdNetwork IDs the ad SDKs add (MAX adds its networks')");
            foreach (var f in skan)
            {
                var text = Read(f) ?? "";
                bool sample = f.IndexOf("IOS IDFA Support", StringComparison.OrdinalIgnoreCase) >= 0
                              || Regex.IsMatch(text, @"123456\.skadnetwork|""company_domain""\s*:\s*""company\.com""|\bDSP1\b|\bMMP1\b");
                bool url = !text.TrimStart().StartsWith("<") && !text.TrimStart().StartsWith("[") && !text.TrimStart().StartsWith("{");
                if (!iosSupport)
                    sec.Add("SKAdNetwork file · " + f, CheckStatus.Info, "not read", "The com.unity.ads.ios-support package isn't installed, so this file isn't copied into Info.plist", f, 1);
                else if (sample && !url)
                    sec.Add("SKAdNetwork file · " + f, CheckStatus.Fail, "sample IDs",
                        "A sample/test file from the iOS support package: its build step copies these IDs (e.g. 123456.skadnetwork, made-up DSP1/MMP1) into Info.plist. "
                        + "Delete it (MAX already adds its networks' SKAdNetwork IDs)", f, 1);
                else if (sample)
                    sec.Add("SKAdNetwork file · " + f, CheckStatus.Warn, "downloads a list",
                        "A sample file from the iOS support package: at build time it downloads Unity's SKAdNetwork list and adds it to Info.plist (and the build needs internet). "
                        + "MAX already adds its networks' IDs, so delete it unless you rely on it", f, 1);
                else
                    sec.Add("SKAdNetwork file · " + f, CheckStatus.Warn, url ? "downloads a list" : "IDs added",
                        "Every ID in this file is added to Info.plist on iOS builds" + (url ? " (downloaded from the URL at build time)" : "") + ": make sure they belong to your ad networks", f, 1);
            }
        }

        static readonly Regex PostProcessHook = new Regex(@"\[\s*(UnityEditor\.Callbacks\.)?PostProcessBuild(Attribute)?\b|\bIPostprocessBuildWithReport\b");

        // iOS: every SKAdNetwork ID in config integration.skAdNetworkIds (networks no SDK adds, e.g. the MAX custom network Helix)
        // must reach Info.plist: a build script that writes SKAdNetworkItems, Google's SKAdNetwork XML, or an SKAdNetworks file
        // the ios-support package copies. Read from files only; the script's own #if UNITY_IOS doesn't matter here.
        static void CheckSkAdNetworkIds(ReportSection sec, IntegrationSettings s)
        {
            var entries = (s.skAdNetworkIds ?? new string[0]).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            if (entries.Count == 0) return;

            // Files that put SKAdNetwork IDs into Info.plist: path → text (comments blanked).
            var sources = new List<KeyValuePair<string, string>>();
            foreach (var f in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories).Select(Norm))
            {
                var code = StripComments(Read(f) ?? "");
                if (code.IndexOf("SKAdNetworkItems", StringComparison.Ordinal) >= 0 && PostProcessHook.IsMatch(code))
                    sources.Add(new KeyValuePair<string, string>(f, code));
            }
            foreach (var f in Directory.GetFiles("Assets", "GoogleMobileAdsSKAdNetworkItems.xml", SearchOption.AllDirectories).Select(Norm))
                sources.Add(new KeyValuePair<string, string>(f, XmlComment.Replace(Read(f) ?? "", m => Regex.Replace(m.Value, "[^\n]", " "))));
            if ((Read("Packages/manifest.json") ?? "").Contains("com.unity.ads.ios-support"))
                foreach (var f in Directory.GetFiles("Assets", "SKAdNetworks*", SearchOption.AllDirectories).Select(Norm)
                             .Where(f => { var n = Path.GetFileName(f); return n == "SKAdNetworks" || n == "SKAdNetworks.xml" || n == "SKAdNetworks.json"; }))
                    sources.Add(new KeyValuePair<string, string>(f, Read(f) ?? ""));

            foreach (var entry in entries)
            {
                var bar = entry.IndexOf('|');
                var network = bar > 0 ? entry.Substring(0, bar).Trim() : "SKAdNetwork";
                var ids = (bar > 0 ? entry.Substring(bar + 1) : entry).Split(',').Select(i => i.Trim()).Where(i => i.Length > 0);
                foreach (var id in ids)
                {
                    var rx = new Regex(@"(?<![\w.])" + Regex.Escape(id) + @"(?![\w.])", RegexOptions.IgnoreCase);
                    var hit = sources.Select(src => new { file = src.Key, text = src.Value, m = rx.Match(src.Value) }).FirstOrDefault(h => h.m.Success);
                    if (hit != null)
                        sec.Add($"SKAdNetwork ID · {network}", CheckStatus.Pass, id,
                            $"Added to Info.plist's SKAdNetworkItems on iOS builds by {hit.file}", hit.file, LineAt(hit.text, hit.m.Index));
                    else
                        sec.Add($"SKAdNetwork ID · {network}", CheckStatus.Fail, "missing",
                            $"{id} isn't added to Info.plist on iOS builds, so {network} installs can't be attributed (SKAdNetwork). "
                            + "MAX only adds its own networks' IDs. Add a [PostProcessBuild] script that writes it into SKAdNetworkItems "
                            + "(like AdsScripts-GenzAds/Editor/HelixSKAdNetwork.cs from GenZ Ads v1.33); no Xcode edit needed");
                }
            }
        }

        // ================================================================= Gradle / manifest

        static void CheckBuildSetup(ReportSection sec, string projectSettings, IntegrationSettings s)
        {
            var props = Read(GradleProperties);
            if (props != null && Regex.IsMatch(projectSettings, @"useCustomGradlePropertiesTemplate:\s*1"))
            {
                bool androidX = Regex.IsMatch(props, @"^\s*android\.useAndroidX\s*=\s*true", RegexOptions.Multiline);
                bool jetifier = Regex.IsMatch(props, @"^\s*android\.enableJetifier\s*=\s*true", RegexOptions.Multiline);
                sec.Add("AndroidX + Jetifier", androidX && jetifier ? CheckStatus.Pass : CheckStatus.Fail, GradleProperties,
                    androidX && jetifier ? "android.useAndroidX and android.enableJetifier are on"
                        : "Missing " + string.Join(" and ", new[] { androidX ? null : "android.useAndroidX=true", jetifier ? null : "android.enableJetifier=true" }.Where(x => x != null))
                          + " — the SDKs need AndroidX; Force Resolve adds them", GradleProperties, 1);

                // A hard-coded aapt2 path from another Unity install breaks the Gradle build on machines without it.
                var aapt = Regex.Match(props, @"^\s*android\.aapt2FromMavenOverride\s*=\s*(.+?)\s*$", RegexOptions.Multiline);
                if (aapt.Success)
                {
                    var path = aapt.Groups[1].Value.Trim();
                    var editor = Regex.Match(Read("ProjectSettings/ProjectVersion.txt") ?? "", @"m_EditorVersion:\s*(\S+)").Groups[1].Value;
                    var pathVersion = Regex.Match(path, @"Editor[\\/]+(\d+\.\d+\.\d+\w+)[\\/]").Groups[1].Value;
                    int line = props.Substring(0, aapt.Index).Count(ch => ch == '\n') + 1;
                    if (!File.Exists(path))
                        sec.Add("aapt2 override", CheckStatus.Fail, path, "This file doesn't exist — the Gradle build fails. Remove the line or point it to this Unity's aapt2", GradleProperties, line);
                    else if (pathVersion.Length > 0 && editor.Length > 0 && pathVersion != editor)
                        sec.Add("aapt2 override", CheckStatus.Warn, path,
                            $"Points to Unity {pathVersion}, but the project uses {editor} — breaks on any machine without {pathVersion}. Point it to {editor} or remove it", GradleProperties, line);
                    else
                        sec.Add("aapt2 override", CheckStatus.Pass, path, "Exists and matches this Unity version", GradleProperties, line);
                }
            }

            var manifest = Read(MainManifest);
            bool customManifest = manifest != null && Regex.IsMatch(projectSettings, @"useCustomMainManifest:\s*1");
            if (customManifest)
            {
                var dbg = Regex.Match(manifest, @"android:debuggable\s*=\s*""true""");
                sec.Add("Manifest not debuggable", dbg.Success ? CheckStatus.Fail : CheckStatus.Pass, MainManifest,
                    dbg.Success ? "android:debuggable=\"true\" — Google Play rejects debuggable builds" : "No android:debuggable=\"true\"",
                    MainManifest, dbg.Success ? manifest.Substring(0, dbg.Index).Count(ch => ch == '\n') + 1 : 1);
            }
            CheckOptionalFeatures(sec, s, customManifest ? manifest : null);
        }

        static readonly Regex UsesFeatureTag = new Regex(@"<uses-feature\b([^>]*)>", RegexOptions.IgnoreCase);

        // Every feature in config integration.optionalUsesFeatures must be in the custom manifest with android:required="false".
        // A <uses-feature> without android:required counts as required (Android's default).
        static void CheckOptionalFeatures(ReportSection sec, IntegrationSettings s, string manifest)
        {
            var names = (s.optionalUsesFeatures ?? new string[0]).Select(n => n?.Trim()).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (names.Count == 0) return;
            string Snippet(string n) => $"<uses-feature android:name=\"{n}\" android:required=\"false\" />";
            if (manifest == null)
            {
                sec.Add("Optional hardware features", CheckStatus.Fail, "no custom manifest",
                    $"Custom Main Manifest is off or {MainManifest} is missing, so the build has none of: {string.Join(", ", names)}. "
                    + "Turn on Player Settings → Publishing Settings → Custom Main Manifest and add inside <manifest>: " + string.Join(" ", names.Select(Snippet)));
                return;
            }

            var xml = XmlComment.Replace(manifest, m => Regex.Replace(m.Value, "[^\n]", " "));
            var tags = UsesFeatureTag.Matches(xml).Cast<Match>()
                .Select(m => new
                {
                    name = Regex.Match(m.Groups[1].Value, @"\bandroid:name\s*=\s*""([^""]*)""").Groups[1].Value.Trim(),
                    required = Regex.Match(m.Groups[1].Value, @"\bandroid:required\s*=\s*""([^""]*)""").Groups[1].Value.Trim(),
                    line = LineAt(xml, m.Index),
                }).ToList();
            foreach (var n in names)
            {
                var tag = tags.FirstOrDefault(t => string.Equals(t.name, n, StringComparison.OrdinalIgnoreCase));
                if (tag == null)
                    sec.Add($"Optional feature · {n}", CheckStatus.Fail, "missing",
                        $"Not declared in the manifest: add {Snippet(n)} inside <manifest>, so Google Play doesn't filter out devices without it",
                        MainManifest, 1);
                else if (!string.Equals(tag.required, "false", StringComparison.OrdinalIgnoreCase))
                    sec.Add($"Optional feature · {n}", CheckStatus.Fail, tag.required.Length == 0 ? "required (default)" : "required=" + tag.required,
                        $"Declared as required{(tag.required.Length == 0 ? " (no android:required, which means true)" : "")}: Google Play hides the game from devices without it. "
                        + "Set android:required=\"false\"", MainManifest, tag.line);
                else
                    sec.Add($"Optional feature · {n}", CheckStatus.Pass, "required=false", "Declared optional in the manifest", MainManifest, tag.line);
            }
        }

        // ================================================================= GameAnalytics ILRD formats

        // GameAnalytics groups ad revenue by the adunit_format string each integration sends. GAMaxIntegration.cs and
        // GAAdMobIntegration.cs each hard-code their own; when they differ (AdMob "INTERSTITIAL", MAX "INTER") GA shows
        // one ad format as two. MAX's strings are the reference: each AdMob string must equal MAX's for that format.
        // Every wrong AdMob string is recorded in report.gaFormatFixes for the window's "Fix GA Ad Formats" button.

        // format label, MAX callback group (MaxSdkCallbacks.<group>), AdMob ad class (GoogleMobileAds.Api.<class>)
        static readonly string[][] GaFormats =
        {
            new[] { "Interstitial", "Interstitial", "InterstitialAd" },
            new[] { "Rewarded", "Rewarded", "RewardedAd" },
            new[] { "Banner", "Banner", "BannerView" },
            new[] { "App Open", "AppOpen", "AppOpenAd" },
        };
        static readonly HashSet<string> GaRequired = new HashSet<string> { "Interstitial", "Rewarded" }; // a mismatch fails; others warn

        struct GaString { public string value; public int line; public string lineText; }

        static void CheckGaAdFormats(TestReport report, IntegrationSettings s, string projectSettings)
        {
            var sec = report.Section(SectionGa, TestStage.Integration);
            var maxFile = FindFile("GAMaxIntegration.cs");
            var admobFile = FindFile("GAAdMobIntegration.cs");

            // Without the define, GameAnalytics compiles the integration out and gets no revenue from that network.
            var defines = AndroidDefines(projectSettings);
            foreach (var d in new[] { "gameanalytics_max_enabled", "gameanalytics_admob_enabled" })
            {
                bool on = defines.Contains(d);
                string net = d.Contains("max") ? "MAX" : "AdMob";
                sec.Add("Define " + d, on ? CheckStatus.Pass : CheckStatus.Fail, on ? "set" : "missing",
                    on ? "In the Android Scripting Define Symbols"
                       : $"Not in the Android Scripting Define Symbols: GameAnalytics compiles out its {net} ad revenue code. Turn on {net} in GameAnalytics Settings → Ad Networks, or add the define");
            }

            if (admobFile == null)
            {
                sec.Add("GAAdMobIntegration.cs", CheckStatus.Fail, "not found",
                    "GameAnalytics' AdMob ad revenue script is missing — reimport the GameAnalytics SDK");
                return;
            }

            var fallback = (s.gaAdFormatFallback ?? new string[0]).Select(x => x.Split('|')).Where(p => p.Length == 2)
                .GroupBy(p => p[0].Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);
            var maxStrings = new Dictionary<string, GaString>();
            if (maxFile == null)
                sec.Add("GAMaxIntegration.cs", CheckStatus.Warn, "not found",
                    "GameAnalytics' MAX ad revenue script is missing — comparing AdMob with the usual MAX strings (" + string.Join(", ", fallback.Values) + ")");
            else
                maxStrings = ReadMaxFormats(Read(maxFile) ?? "");
            var admobStrings = ReadAdMobFormats(Read(admobFile) ?? "");

            foreach (var f in GaFormats)
            {
                string label = f[0], lower = label.ToLowerInvariant();
                bool required = GaRequired.Contains(label);
                bool haveMax = maxStrings.TryGetValue(f[1], out var max);
                string expected = haveMax ? max.value : fallback.TryGetValue(label, out var fb) ? fb : null;
                string source = haveMax ? "MAX" : "usual MAX";

                if (!admobStrings.TryGetValue(f[2], out var admob))
                {
                    sec.Add($"GA format · {label}", required ? CheckStatus.Warn : CheckStatus.Info, "not found",
                        $"Couldn't read the adunit_format GAAdMobIntegration.cs sends for {f[2]} — check it by hand" + (expected != null ? $": it must be \"{expected}\"" : ""),
                        admobFile, 1);
                    continue;
                }
                if (expected == null)
                {
                    sec.Add($"GA format · {label}", CheckStatus.Info, $"AdMob \"{admob.value}\"", "No MAX string to compare with", admobFile, admob.line);
                    continue;
                }
                if (admob.value == expected)
                {
                    sec.Add($"GA format · {label}", CheckStatus.Pass, $"\"{expected}\"",
                        $"AdMob and MAX both send \"{expected}\": one {lower} ad format in GameAnalytics", admobFile, admob.line);
                    continue;
                }

                sec.Add($"GA format · {label}", required ? CheckStatus.Fail : CheckStatus.Warn, $"AdMob \"{admob.value}\" ≠ {source} \"{expected}\"",
                    $"GameAnalytics gets two different strings for the {lower} ad format: MAX sends \"{expected}\", AdMob sends \"{admob.value}\", "
                    + $"so GA shows {lower} revenue split into two formats. Click \"Fix GA Ad Formats\" to set AdMob's to \"{expected}\"",
                    admobFile, admob.line);
                report.gaFormatFixes.Add(new IdFix
                {
                    label = $"GameAnalytics AdMob {label} format",
                    current = admob.value,
                    expected = expected,
                    codeFile = admobFile,
                    codeLine = admob.line,
                    codeContext = admob.lineText,
                    codeValue = admob.value,
                });
            }

            CheckGaSubscriptions(sec);
        }

        // The game's ad scripts must hand their ads to GameAnalytics, or the strings above are never sent.
        static void CheckGaSubscriptions(ReportSection sec)
        {
            var adClasses = GaFormats.Select(f => f[2]).Concat(new[] { "RewardedInterstitialAd" }).ToArray();
            var admobCalls = new Dictionary<string, (string file, int line)>(); // ad class ("?" = not resolved) → first call
            (string file, int line)? maxCall = null;
            var callRx = new Regex(@"\bSubscribeAdMobImpressions\s*\(\s*[^,;]+,\s*(\w+)\s*\)");

            foreach (var file in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories).Select(Norm))
            {
                if (file.IndexOf("/GameAnalytics/", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var raw = Read(file) ?? "";
                if (raw.IndexOf("GameAnalyticsILRD", StringComparison.Ordinal) < 0) continue;
                var code = StripComments(raw);

                var m = Regex.Match(code, @"\bSubscribeMaxImpressions\s*\(\s*\)");
                if (m.Success && maxCall == null) maxCall = (file, LineAt(code, m.Index));

                foreach (Match c in callRx.Matches(code))
                {
                    var decl = Regex.Match(code, @"\b(" + string.Join("|", adClasses) + @")\s+" + Regex.Escape(c.Groups[1].Value) + @"\b");
                    var type = decl.Success ? decl.Groups[1].Value : "?";
                    if (!admobCalls.ContainsKey(type)) admobCalls[type] = (file, LineAt(code, c.Index));
                }
            }

            if (maxCall != null)
                sec.Add("GA ← MAX revenue", CheckStatus.Pass, "subscribed", "GameAnalyticsILRD.SubscribeMaxImpressions() is called", maxCall.Value.file, maxCall.Value.line);
            else
                sec.Add("GA ← MAX revenue", CheckStatus.Warn, "not found",
                    "No GameAnalyticsILRD.SubscribeMaxImpressions() call in the ad scripts: GameAnalytics gets no MAX ad revenue");

            bool unresolved = admobCalls.TryGetValue("?", out var someCall);
            foreach (var f in GaFormats.Where(f => GaRequired.Contains(f[0])))
            {
                string label = $"GA ← AdMob {f[0]} revenue";
                if (admobCalls.TryGetValue(f[2], out var at))
                    sec.Add(label, CheckStatus.Pass, "subscribed", $"GameAnalyticsILRD.SubscribeAdMobImpressions(…, {f[2]}) is called", at.file, at.line);
                else if (unresolved) // a call whose ad variable type couldn't be read: don't warn blindly
                    sec.Add(label, CheckStatus.Info, "unclear", $"Couldn't tell whether a SubscribeAdMobImpressions call passes the {f[2]}: check it by hand", someCall.file, someCall.line);
                else
                    sec.Add(label, CheckStatus.Warn, "not found",
                        $"No GameAnalyticsILRD.SubscribeAdMobImpressions(…) for the {f[2]}: GameAnalytics gets no AdMob {f[0].ToLowerInvariant()} revenue");
            }
        }

        // MaxSdkCallbacks.Interstitial.OnAdRevenuePaidEvent += (…) => runCallback("INTER", …);
        static Dictionary<string, GaString> ReadMaxFormats(string text)
        {
            var result = new Dictionary<string, GaString>();
            var lines = SplitLines(text);
            var rx = new Regex(@"MaxSdkCallbacks\.(\w+)\.OnAdRevenuePaidEvent\s*\+=.*?""([^""]*)""");
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsCommentLine(lines[i])) continue;
                var m = rx.Match(lines[i]);
                if (m.Success && !result.ContainsKey(m.Groups[1].Value))
                    result[m.Groups[1].Value] = new GaString { value = m.Groups[2].Value, line = i + 1, lineText = lines[i] };
            }
            return result;
        }

        // ListenForImpressions(string adUnitId, GoogleMobileAds.Api.InterstitialAd ad, …) { … adunit_format = "INTER"; … }
        static Dictionary<string, GaString> ReadAdMobFormats(string text)
        {
            var result = new Dictionary<string, GaString>();
            var lines = SplitLines(text);
            var overload = new Regex(@"ListenForImpressions\s*\(.*?\b(BannerView|InterstitialAd|RewardedAd|RewardedInterstitialAd|AppOpenAd)\s+\w+");
            var format = new Regex(@"\badunit_format\s*=\s*""([^""]*)""");
            string current = null;
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsCommentLine(lines[i])) continue;
                var o = overload.Match(lines[i]);
                if (o.Success) { current = o.Groups[1].Value; continue; }
                var f = format.Match(lines[i]);
                if (f.Success && current != null && !result.ContainsKey(current))
                {
                    result[current] = new GaString { value = f.Groups[1].Value, line = i + 1, lineText = lines[i] };
                    current = null;
                }
            }
            return result;
        }

        static HashSet<string> AndroidDefines(string projectSettings)
        {
            var block = Regex.Match(projectSettings, @"scriptingDefineSymbols:[ \t]*\r?\n((?:[ \t]+\S[^\n]*\n?)*)");
            var android = Regex.Match(block.Groups[1].Value, @"^\s*(?:Android|7):[ \t]*([^\r\n]*)", RegexOptions.Multiline);
            return new HashSet<string>(android.Groups[1].Value.Split(';').Select(d => d.Trim()).Where(d => d.Length > 0));
        }

        static string FindFile(string name) =>
            Directory.GetFiles("Assets", name, SearchOption.AllDirectories).Select(Norm).OrderBy(p => p.Length).FirstOrDefault();

        static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');
        static bool IsCommentLine(string line) { var t = line.TrimStart(); return t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*"); }
        static int LineAt(string text, int index) => text.Substring(0, index).Count(ch => ch == '\n') + 1;

        // Blanks comments but keeps every newline, so offsets still give the right line numbers. Strings are kept.
        static string StripComments(string code) =>
            Regex.Replace(code, @"/\*.*?\*/|//[^\n]*|""(?:\\.|[^""\\\n])*""",
                m => m.Value.StartsWith("\"") ? m.Value : Regex.Replace(m.Value, "[^\n]", " "), RegexOptions.Singleline);

        // ================================================================= helpers

        static int LineOf(string text, string needle)
        {
            int i = text.IndexOf(needle, StringComparison.Ordinal);
            return i < 0 ? 1 : text.Substring(0, i).Count(ch => ch == '\n') + 1;
        }

        static string Read(string path) => File.Exists(path) ? File.ReadAllText(path) : null;
        static string Norm(string p) => p.Replace('\\', '/');
        static string JsonString(string json, string key) => Regex.Match(json, @"""" + Regex.Escape(key) + @"""\s*:\s*""([^""]*)""").Groups[1].Value;
        static string XmlString(string xml, string name) => Regex.Match(xml, @"name=""" + Regex.Escape(name) + @"""[^>]*>([^<]*)<").Groups[1].Value;
    }
}
