// GenZ-TestSuit-v1.0.1
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GenZ.TestSuit
{
    /// <summary>
    /// Step 1, "Store links": the privacy policy and More Games links the game opens must be those of the game's live
    /// store account. The games tab gives the store ("Store Name"; the last such column, U, is the live store), the
    /// stores tab ("Stors") its privacy policy ("Privicy Policy") and developer page ("Link"). Links are found in
    /// buttons / UnityEvents of the open SDK scenes (with Auto Correct), in every other scene and prefab under Assets
    /// (read as text), and as string literals in the game's scripts. A More Games link is only checked when the game
    /// has one; a missing privacy policy link is a warning.
    /// The URL logic has no scene dependency, so the headless harness can run it.
    /// </summary>
    public static class GenZStoreLinkChecks
    {
        public const string SectionTitle = "Store links (privacy policy, More Games)";
        const int StringMode = 5; // PersistentListenerMode.String

        public enum Kind { Privacy, MoreGames, RateUs }

        static string KindName(Kind k) => k == Kind.Privacy ? "Privacy policy" : k == Kind.MoreGames ? "More Games" : "Rate Us";

        /// <summary>A privacy policy or More Games link found in the project.</summary>
        public class Link
        {
            public Kind kind;
            public string label;      // where, for the report row
            public string url;        // as written (may be empty)
            public string file;       // Assets/... (for the row's link)
            public int line;
            // Set for a UnityEvent in an open scene: Auto Correct writes the sheet URL there.
            public string scenePath, objectPath, componentType, propertyPath;
            public bool inBuild = true; // false: not in the build (a scene not in Build Settings, a prefab / asset no build scene uses)
            public bool field;          // an Inspector / script field (not a button's argument): empty = not filled in yet
            public string coveredBy;    // a button in an open scene another UI element covers: the player can't tap it
        }

        /// <summary>The game's store links from the sheet. A null URL = unknown (only the links' format is checked).</summary>
        public class Expected
        {
            public string store, privacy, moreGames;
            public string rateUs; // the game's own store page: Play details?id=<package>, App Store app/id<App ID>

            public bool ios;      // the build target: a store link of the other platform opens nothing
            public string For(Kind k) => k == Kind.Privacy ? privacy : k == Kind.MoreGames ? moreGames : rateUs;
        }

        // ---------------------------------------------------------------- entry point (Editor run)

        /// <summary>
        /// The whole project: the open SDK scenes through SerializedObject (Auto Correct), every other scene, prefab and
        /// ScriptableObject asset under Assets as YAML text, and every script — except <see cref="StoreLinkSettings.excludeFolders"/>.
        /// </summary>
        public static void Run(TestReport report, GenZSheetReader sheet, GenZTestSuitConfig cfg, IList<Scene> openScenes,
                               List<IdFix> fixes, bool ios, string package)
        {
            var s = cfg.storeLinks ?? new StoreLinkSettings();
            var sec = report.Section(SectionTitle, TestStage.Ids);
            var expected = SheetLinks(sec, sheet, s, ios, package);
            var buildScenes = new HashSet<string>(EditorBuildSettings.scenes.Where(b => b.enabled).Select(b => b.path), StringComparer.OrdinalIgnoreCase);
            // What ships: the build scenes and everything they reference, plus Resources folders (loaded by name).
            var shipped = new HashSet<string>(AssetDatabase.GetDependencies(buildScenes.ToArray(), true), StringComparer.OrdinalIgnoreCase);
            bool Ships(string path) => shipped.Contains(path) || path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0;

            var links = new List<Link>();
            var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int openCount = 0;
            foreach (var scene in openScenes.Where(x => x.IsValid() && x.isLoaded))
            {
                if (!string.IsNullOrEmpty(scene.path) && !scanned.Add(scene.path)) continue;
                openCount++;
                links.AddRange(ScanScene(scene, s, string.IsNullOrEmpty(scene.path) || Ships(scene.path)));
            }

            var counts = new Dictionary<string, int> { { ".unity", openCount }, { ".prefab", 0 }, { ".asset", 0 } };
            var binary = new List<string>();
            foreach (var path in AssetFiles(s.excludeFolders ?? new string[0]))
            {
                if (scanned.Contains(path)) continue;
                string text;
                try
                {
                    if (new FileInfo(path).Length > 64L * 1024 * 1024) continue; // baked data, not UI
                    text = File.ReadAllText(path);
                }
                catch (IOException) { continue; }
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (!text.StartsWith("%YAML"))
                {
                    if (ext != ".asset") binary.Add(path); // binary .asset = meshes, lighting… ; a binary scene/prefab can't be read
                    continue;
                }
                counts[ext]++;
                links.AddRange(ScanYaml(path, text, s, Ships(path)));
            }

            var code = GenZSourceModel.Load(s.excludeFolders ?? new string[0]);
            links.AddRange(ScanCode(code, s, ios));

            sec.Add("Project scanned", CheckStatus.Info, $"{counts[".unity"]} scenes · {counts[".prefab"]} prefabs · {counts[".asset"]} assets · {code.files.Count} scripts",
                "Every button / event text, Inspector text field and script string under Assets was read for privacy policy, More Games and Rate Us links"
                + $" (skipped folders: {string.Join(", ", s.excludeFolders ?? new string[0])}, and Editor folders)");
            if (binary.Count > 0)
                sec.Add("Binary scenes / prefabs", CheckStatus.Warn, binary.Count + " not read",
                    "Saved in binary, so their links can't be read: " + string.Join(", ", binary.Take(5).Select(Path.GetFileName))
                    + (binary.Count > 5 ? ", …" : "") + ". Set Project Settings > Editor > Asset Serialization to Force Text");

            Report(sec, links, expected, fixes);
        }

        /// <summary>Reads the game's store and its links from the sheet, with a row saying where they came from.</summary>
        public static Expected SheetLinks(ReportSection sec, GenZSheetReader sheet, StoreLinkSettings s, bool ios, string package = null)
        {
            var exp = new Expected { ios = ios };
            RateUsPage(sec, exp, sheet, s, ios, package);
            const string label = "Store (sheet)";
            if (sheet == null)
            {
                sec.Add(label, CheckStatus.Info, "sheet not read", "The Google Sheet wasn't read (turned off or failed), so only the links' format is checked");
                return exp;
            }
            exp.store = PickStore(sheet.LookupAll(s.storeColumns), s.useLastStoreColumn, out var storeNote, out var storeWarn);
            if (exp.store == null)
            {
                sec.Add(label, CheckStatus.Warn, "no store",
                    $"The game's row has no {string.Join(" / ", s.storeColumns)} value, so its store links can't be looked up. Fill the live store in the sheet");
                return exp;
            }
            var row = FindStoreRow(sheet.grid, exp.store, ios, s, out var note);
            if (row == null)
            {
                sec.Add(label, CheckStatus.Warn, exp.store, (storeNote != null ? storeNote + ". " : "") + note);
                return exp;
            }
            exp.privacy = row.Value(s.privacyColumns, out var pCol);
            exp.moreGames = row.Value(s.moreGamesColumns, out var mCol);
            var parts = new List<string>();
            if (storeNote != null) parts.Add(storeNote);
            parts.Add($"Stores table row \"{row.name}\"");
            parts.Add(exp.privacy != null ? $"privacy policy ({pCol}): {exp.privacy}" : "no privacy policy in the stores table");
            parts.Add(exp.moreGames != null ? $"More Games ({mCol}): {exp.moreGames}" : "no store link in the stores table");
            bool missing = exp.privacy == null || exp.moreGames == null;
            sec.Add(label, storeWarn || missing ? CheckStatus.Warn : CheckStatus.Info, exp.store, string.Join(" · ", parts));
            return exp;
        }

        /// <summary>
        /// The game's own store page, which a Rate Us link must open. Android: Play's details page of the game's package (no sheet
        /// needed). iOS: the App Store page of the sheet's App ID (games tab "App ID", e.g. 6479971966).
        /// </summary>
        static void RateUsPage(ReportSection sec, Expected exp, GenZSheetReader sheet, StoreLinkSettings s, bool ios, string package)
        {
            const string label = "Rate Us page";
            if (!ios)
            {
                if (string.IsNullOrWhiteSpace(package)) return;
                exp.rateUs = "https://play.google.com/store/apps/details?id=" + package.Trim();
                sec.Add(label, CheckStatus.Info, package.Trim(), "A Rate Us link must open this game's Play page: " + exp.rateUs + " (or market://details?id=" + package.Trim() + ")");
                return;
            }
            var raw = sheet?.Lookup(s.appStoreIdColumns, out _);
            var id = Regex.Match(raw ?? "", @"\d{6,}").Value;
            if (id.Length == 0)
            {
                sec.Add(label, sheet == null ? CheckStatus.Info : CheckStatus.Warn, string.IsNullOrWhiteSpace(raw) ? "no App ID" : raw.Trim(),
                    sheet == null ? "The sheet wasn't read: the iOS Rate Us link's App ID can't be compared"
                                  : $"The game's row has no App Store ID ({string.Join(" / ", s.appStoreIdColumns)}): the Rate Us link can't be compared. Fill it in the sheet");
                return;
            }
            exp.rateUs = "https://apps.apple.com/app/id" + id;
            sec.Add(label, CheckStatus.Info, "id" + id, "A Rate Us link must open this App Store ID (sheet): " + exp.rateUs + " (or itms-apps://itunes.apple.com/app/id" + id + ")");
        }

        /// <summary>
        /// The game's store from its "Store Name" values in sheet order. The studio games tab has two such columns: the
        /// last one (U) is the live store. An empty last one falls back to the earlier value, with a warning.
        /// </summary>
        public static string PickStore(List<KeyValuePair<string, string>> values, bool useLast, out string note, out bool warn)
        {
            note = null;
            warn = false;
            var filled = values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToList();
            if (filled.Count == 0) return null;
            if (!useLast || values.Count == 1) return filled[0].Value.Trim();
            var last = values[values.Count - 1];
            if (!string.IsNullOrWhiteSpace(last.Value))
            {
                var lastKey = GenZSheetReader.Normalize(last.Value);
                var other = filled.Where(kv => GenZSheetReader.Normalize(kv.Value) != lastKey).Select(kv => kv.Value.Trim()).Distinct().ToList();
                note = $"From the last {last.Key} column (live store)" + (other.Count > 0 ? $"; the earlier one says {string.Join(", ", other)}" : "");
                return last.Value.Trim();
            }
            warn = true;
            var fallback = filled[filled.Count - 1].Value.Trim();
            note = $"The last {last.Key} column (live store) is empty, so the earlier one's \"{fallback}\" is used: fill the live store in the sheet";
            return fallback;
        }

        // ---------------------------------------------------------------- the sheet's stores table

        public class StoreRow
        {
            public string name;
            public List<string> header, cells;

            /// <summary>The first of these columns the table has with a value; null if none.</summary>
            public string Value(string[] columns, out string column)
            {
                column = null;
                foreach (var c in (columns ?? new string[0]).Select(GenZSheetReader.Normalize))
                {
                    int i = header.FindIndex(h => GenZSheetReader.Normalize(h) == c);
                    if (i < 0) continue;
                    var v = i < cells.Count ? cells[i].Trim() : "";
                    if (v.Length == 0) continue;
                    column = header[i].Trim();
                    return v;
                }
                return null;
            }
        }

        /// <summary>
        /// The store's row in the table whose header row has a store column and a privacy or store-link column (studio
        /// sheet: tab "Stors", "Store Name | Link | Privicy Policy | Privicy Policy New"). Names match ignoring case, spaces
        /// and symbols ("Games To Play Simulation" = "GamesToPlaySimulation"); an iOS build prefers the "IOS - …" row.
        /// </summary>
        public static StoreRow FindStoreRow(List<List<string>> grid, string store, bool ios, StoreLinkSettings s, out string note)
        {
            var storeHeaders = new HashSet<string>((s.storeColumns ?? new string[0]).Select(GenZSheetReader.Normalize));
            var linkHeaders = new HashSet<string>((s.privacyColumns ?? new string[0]).Concat(s.moreGamesColumns ?? new string[0]).Select(GenZSheetReader.Normalize));
            var key = GenZSheetReader.Normalize(store);
            var wanted = ios && !key.StartsWith("ios") ? new[] { "ios" + key, key } : new[] { key };
            bool table = false;

            foreach (var want in wanted)
            for (int h = 0; grid != null && h < grid.Count; h++)
            {
                int sc = grid[h].FindIndex(c => storeHeaders.Contains(GenZSheetReader.Normalize(c)));
                if (sc < 0 || !grid[h].Any(c => linkHeaders.Contains(GenZSheetReader.Normalize(c)))) continue; // e.g. the games tab
                table = true;
                for (int r = h + 1; r < grid.Count; r++)
                {
                    var name = sc < grid[r].Count ? grid[r][sc] : "";
                    var n = GenZSheetReader.Normalize(name);
                    if (storeHeaders.Contains(n)) break; // the next table starts
                    if (n != want) continue;
                    note = null;
                    return new StoreRow { name = name.Trim(), header = grid[h], cells = grid[r] };
                }
            }
            note = !table ? $"The sheet has no stores table (a {string.Join(" / ", s.storeColumns)} column next to {string.Join(" / ", s.privacyColumns)} or {string.Join(" / ", s.moreGamesColumns)}; studio sheet: tab \"Stors\" in Sheet Tabs)"
                          : $"\"{store}\" isn't in the stores table: add it with its links, or fix the game's store name";
            return null;
        }

        // ---------------------------------------------------------------- judging one link

        static readonly Regex GoogleRedirect = new Regex(@"^(https?://)?(www\.)?google\.[a-z.]+/url\?", RegexOptions.IgnoreCase);
        static readonly Regex WebScheme = new Regex(@"^https?://", RegexOptions.IgnoreCase);
        static readonly Regex StoreScheme = new Regex(@"^(https?|market|itms-apps|itms-appss)://", RegexOptions.IgnoreCase);
        static readonly Regex PlayDevId = new Regex(@"(?:play\.google\.com/store/apps/dev(?:eloper)?\?|market://dev\?)(?:[^#]*?&)?id=([^&#]+)", RegexOptions.IgnoreCase);
        static readonly Regex PlayAppId = new Regex(@"(?:play\.google\.com/store/apps/details\?|market://details\?)(?:[^#]*?&)?id=([^&#]+)", RegexOptions.IgnoreCase);
        static readonly Regex AppleDevId = new Regex(@"(?:apps\.apple\.com|itunes\.apple\.com|itms-apps://)[^#?]*?/id(\d+)", RegexOptions.IgnoreCase);

        public static CheckStatus Judge(Kind kind, string url, string expected, out string message)
        {
            var u = (url ?? "").Trim();
            string what = kind == Kind.Privacy ? "privacy policy" : kind == Kind.MoreGames ? "developer page (More Games)" : "own store page (Rate Us)";
            string sheetNote = expected == null ? ""
                : kind == Kind.RateUs ? $". This game's store page is {expected}"
                : $". The sheet's {what} for this store is {expected}";
            if (u.Length == 0)
            {
                message = "Empty: tapping it opens nothing" + sheetNote;
                return CheckStatus.Fail;
            }
            if (GoogleRedirect.IsMatch(u))
            {
                var q = Regex.Match(u, @"[?&]q=([^&]+)");
                var inner = q.Success ? Uri.UnescapeDataString(q.Groups[1].Value) : null;
                message = "A Google redirect link (copied from a Google Doc or Sheet), not the page's own address"
                          + (inner != null ? $": it points to {inner}" : "")
                          + ". Google may show a \"Redirect notice\" page instead, and the link can stop working. Use the page's own URL"
                          + (inner != null && expected != null && !Same(kind, inner, expected) ? " — and that page isn't this store's either" : "")
                          + sheetNote;
                return CheckStatus.Fail;
            }
            if (!(kind == Kind.Privacy ? WebScheme : StoreScheme).IsMatch(u))
            {
                message = (kind == Kind.Privacy ? "Doesn't start with https://" : "Doesn't start with https:// (or market:// / itms-apps://)")
                          + " — Application.OpenURL may open nothing on the phone" + sheetNote;
                return CheckStatus.Fail;
            }
            if (u != (url ?? ""))
            {
                message = "Has a space or line break around the URL: remove it" + sheetNote;
                return CheckStatus.Fail;
            }
            if (expected == null)
            {
                message = $"Format OK (not compared: the sheet's {what} for this store is unknown)";
                return CheckStatus.Pass;
            }
            if (!Same(kind, u, expected))
            {
                message = kind == Kind.RateUs ? $"Not this game's store page (copied from another game?): it must open {expected}"
                                              : $"Not this store's {what}: the sheet says {expected}";
                return CheckStatus.Fail;
            }
            message = kind == Kind.RateUs ? "Opens this game's store page" : "Same as the sheet";
            return CheckStatus.Pass;
        }

        /// <summary>
        /// Same page: case, a trailing '/', http/https and "www." ignored. Store pages compare by developer ID, so
        /// market://dev?id=X = https://play.google.com/store/apps/dev?id=X (extra parameters such as &amp;hl ignored).
        /// </summary>
        public static bool Same(Kind kind, string a, string b)
        {
            if (kind == Kind.RateUs)
            {
                var pa = PlayAppId.Match(a ?? ""); var pb = PlayAppId.Match(b ?? "");
                if (pa.Success && pb.Success) return string.Equals(pa.Groups[1].Value, pb.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
                var aa = AppleDevId.Match(a ?? ""); var ab = AppleDevId.Match(b ?? ""); // …/app/<name>/id123 or itms-apps://…/id123
                if (aa.Success && ab.Success) return aa.Groups[1].Value == ab.Groups[1].Value;
            }
            if (kind == Kind.MoreGames)
            {
                var pa = PlayDevId.Match(a ?? ""); var pb = PlayDevId.Match(b ?? "");
                if (pa.Success && pb.Success)
                    return string.Equals(Uri.UnescapeDataString(pa.Groups[1].Value), Uri.UnescapeDataString(pb.Groups[1].Value), StringComparison.OrdinalIgnoreCase);
                var aa = AppleDevId.Match(a ?? ""); var ab = AppleDevId.Match(b ?? "");
                if (aa.Success && ab.Success) return aa.Groups[1].Value == ab.Groups[1].Value;
            }
            return Canon(a) == Canon(b);
        }

        static string Canon(string url)
        {
            var u = (url ?? "").Trim().ToLowerInvariant();
            u = Regex.Replace(u, @"^https?://", "");
            if (u.StartsWith("www.")) u = u.Substring(4);
            return u.TrimEnd('/');
        }

        static readonly Regex PlayLink = new Regex(@"^\s*(market://|https?://play\.google\.com/)", RegexOptions.IgnoreCase);
        static readonly Regex AppleLink = new Regex(@"^\s*(itms-apps?s?://|https?://(apps|itunes)\.apple\.com/)", RegexOptions.IgnoreCase);

        /// <summary>A Google Play link in the iOS build, or an App Store link in the Android build: it opens nothing / the wrong store.</summary>
        static bool WrongPlatform(string url, bool ios, out string note)
        {
            note = null;
            if (ios && PlayLink.IsMatch(url ?? "")) note = "A Google Play link in the iOS build: on an iPhone it opens nothing (market://) or a web page of the Android app";
            else if (!ios && AppleLink.IsMatch(url ?? "")) note = "An App Store link in the Android build: it opens the iOS app's web page, not Google Play";
            return note != null;
        }

        /// <summary>The start of a link the code completes ("market://dev?id=" + id): can't be compared.</summary>
        static bool IsPrefixOnly(string url) => Regex.IsMatch((url ?? "").Trim(), @"(=|pub:|/id)$");

        // ---------------------------------------------------------------- finding the links

        /// <summary>A button / event's text argument: its kind from the text (a privacy page, a store developer page), else from the called method's name.</summary>
        public static Kind? Classify(string method, string text, StoreLinkSettings s)
        {
            var byText = ClassifyText(text, s);
            if (byText != null) return byText;
            if (!string.IsNullOrEmpty(method))
            {
                if (SafeMatch(method, s.privacyMethodPattern)) return Kind.Privacy;
                if (SafeMatch(method, s.moreGamesMethodPattern)) return Kind.MoreGames;
                if (SafeMatch(method, s.rateUsMethodPattern)) return Kind.RateUs;
            }
            return null;
        }

        /// <summary>
        /// Any other text field (an Inspector string such as privacyPolicyUrl, a prefab override, a ScriptableObject value):
        /// by its text, else by its name when the name says it's a privacy / More Games link (and its text isn't prose).
        /// </summary>
        public static Kind? ClassifyField(string fieldName, string text, StoreLinkSettings s)
        {
            var byText = ClassifyText(text, s);
            if (byText != null) return byText;
            if (string.IsNullOrEmpty(fieldName) || !SafeMatch(fieldName, s.linkFieldPattern)) return null;
            var t = (text ?? "").Trim();
            if (t.Length > 0 && !LooksLikeUrl(t) && !StoreScheme.IsMatch(t) && !GoogleRedirect.IsMatch(t)) return null; // a label, not a link
            if (SafeMatch(fieldName, s.privacyMethodPattern)) return Kind.Privacy;
            if (SafeMatch(fieldName, s.moreGamesMethodPattern)) return Kind.MoreGames;
            if (SafeMatch(fieldName, s.rateUsMethodPattern)) return Kind.RateUs;
            return null;
        }

        static Kind? ClassifyText(string text, StoreLinkSettings s)
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (SafeMatch(text, s.privacyUrlPattern) && LooksLikeUrl(text)) return Kind.Privacy;
            if (SafeMatch(text, s.moreGamesUrlPattern)) return Kind.MoreGames;
            if (SafeMatch(text, s.rateUsUrlPattern)) return Kind.RateUs;
            return null;
        }

        static bool LooksLikeUrl(string text) => Regex.IsMatch(text.Trim(), @"^(https?://|www\.)|^[\w-]+(\.[\w-]+)+/", RegexOptions.IgnoreCase);

        static bool SafeMatch(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            try { return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase); }
            catch (ArgumentException) { return false; }
        }

        const string EventArg = ".m_Arguments.m_StringArgument";

        /// <summary>Every text property of every component in an open scene: button / event arguments and Inspector fields.</summary>
        static List<Link> ScanScene(Scene scene, StoreLinkSettings s, bool inBuild)
        {
            var links = new List<Link>();
            foreach (var go in GenZEditorTestSuit.AllObjects(scene))
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                var so = new SerializedObject(comp);
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.String) continue;
                    var value = it.stringValue ?? "";
                    Kind? kind;
                    string what;
                    if (it.propertyPath.EndsWith(EventArg))
                    {
                        var callPath = it.propertyPath.Substring(0, it.propertyPath.Length - EventArg.Length);
                        var method = so.FindProperty(callPath + ".m_MethodName")?.stringValue ?? "";
                        var mode = so.FindProperty(callPath + ".m_Mode")?.intValue ?? -1;
                        if (method.Length == 0 || mode != StringMode) continue;
                        kind = Classify(method, value, s);
                        var cls = (so.FindProperty(callPath + ".m_TargetAssemblyTypeName")?.stringValue ?? "").Split(',')[0].Trim();
                        what = (cls.Length > 0 ? cls + "." : "") + method;
                    }
                    else
                    {
                        if (it.propertyPath.Contains("m_PersistentCalls")) continue; // method / type names of the calls
                        kind = ClassifyField(it.name, value, s);
                        what = comp.GetType().Name + "." + it.propertyPath;
                    }
                    bool isField = !it.propertyPath.EndsWith(EventArg);
                    if (kind == null) continue;
                    var path = GenZEditorTestSuit.HierarchyPath(go.transform);
                    links.Add(new Link
                    {
                        kind = kind.Value,
                        label = $"{scene.name} › {path}: {what}",
                        url = value,
                        file = scene.path,
                        inBuild = inBuild,
                        field = isField,
                        coveredBy = isField ? null : Cover(go),
                        scenePath = scene.path, objectPath = path, componentType = comp.GetType().Name, propertyPath = it.propertyPath
                    });
                }
            }
            return links;
        }

        static string Cover(GameObject go)
        {
            var c = GenZEditorTestSuit.CoveredBy(go);
            return c != null ? GenZEditorTestSuit.CoverName(c) : null;
        }

        /// <summary>Scenes, prefabs and ScriptableObject assets under Assets, except the excluded folders and Editor folders.</summary>
        static IEnumerable<string> AssetFiles(IEnumerable<string> excludeFolders)
        {
            var excludes = excludeFolders.Select(e => "/" + e.Trim('/').ToLowerInvariant() + "/").ToList();
            foreach (var pattern in new[] { "*.unity", "*.prefab", "*.asset" })
            foreach (var full in Directory.GetFiles("Assets", pattern, SearchOption.AllDirectories))
            {
                var path = full.Replace('\\', '/');
                var lower = "/" + path.ToLowerInvariant();
                if (lower.Contains("/editor/") || excludes.Any(lower.Contains)) continue;
                yield return path;
            }
        }

        static readonly Regex YamlField = new Regex(@"^\s*(?:-\s+)?([A-Za-z_][\w]*):\s+(.*)$");

        /// <summary>
        /// The same, read from a scene / prefab / asset file's YAML (no Auto Correct: the object isn't open): button / event
        /// calls (m_MethodName + m_StringArgument), Inspector text fields, and prefab overrides (propertyPath + value).
        /// </summary>
        public static List<Link> ScanYaml(string path, string text, StoreLinkSettings s, bool inBuild = true)
        {
            var links = new List<Link>();
            if (text.IndexOf("://", StringComparison.Ordinal) < 0 && text.IndexOf("www.", StringComparison.OrdinalIgnoreCase) < 0
                && text.IndexOf("m_MethodName:", StringComparison.Ordinal) < 0 && !Regex.IsMatch(text, s.linkFieldPattern ?? "^$", RegexOptions.IgnoreCase))
                return links;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var eventArgLines = new HashSet<int>();
            string file = Path.GetFileName(path);

            for (int i = 0; i < lines.Length; i++)
            {
                var m = YamlField.Match(lines[i]);
                if (!m.Success) continue;
                var key = m.Groups[1].Value;
                var value = YamlValue(m.Groups[2].Value);

                if (key == "m_MethodName")
                {
                    int mode = -1, argLine = -1;
                    string arg = null;
                    for (int j = i + 1; j < lines.Length && j <= i + 16; j++)
                    {
                        var u = lines[j].TrimStart();
                        if (u.StartsWith("- m_Target:") || u.StartsWith("m_CallState:") || u.StartsWith("m_MethodName:")) break;
                        if (u.StartsWith("m_Mode:")) int.TryParse(u.Substring("m_Mode:".Length).Trim(), out mode);
                        else if (u.StartsWith("m_StringArgument:")) { arg = YamlValue(u.Substring("m_StringArgument:".Length)); argLine = j; }
                    }
                    if (argLine >= 0) eventArgLines.Add(argLine);
                    if (value.Length == 0 || mode != StringMode || arg == null) continue;
                    var kind = Classify(value, arg, s);
                    if (kind != null)
                        links.Add(new Link { kind = kind.Value, label = $"{file}:{argLine + 1}: {value}", url = arg, file = path, line = argLine + 1, inBuild = inBuild });
                    continue;
                }
                if (key == "m_StringArgument" || key == "m_MethodName" || key == "m_TargetAssemblyTypeName" || key == "propertyPath") continue;
                if (eventArgLines.Contains(i)) continue;

                string field = key;
                if (key == "value")
                {
                    // A prefab instance's override: "- target: … / propertyPath: privacyUrl / value: https://…"
                    var prev = i > 0 ? YamlField.Match(lines[i - 1]) : Match.Empty;
                    if (!prev.Success || prev.Groups[1].Value != "propertyPath") continue;
                    var prop = YamlValue(prev.Groups[2].Value);
                    field = prop.EndsWith("m_StringArgument") ? null : prop.Substring(prop.LastIndexOf('.') + 1); // event override: by text only
                }
                var k = field == null ? ClassifyText(value, s) : ClassifyField(field, value, s);
                if (k == null) continue;
                links.Add(new Link { kind = k.Value, label = $"{file}:{i + 1}: {field ?? "button text (override)"}", url = value, file = path, line = i + 1, inBuild = inBuild, field = field != null });
            }
            return links;
        }

        static string YamlValue(string raw)
        {
            var v = raw.Trim();
            if (v.Length >= 2 && v[0] == '\'' && v[v.Length - 1] == '\'') return v.Substring(1, v.Length - 2).Replace("''", "'");
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"')
                return Regex.Replace(v.Substring(1, v.Length - 2), @"\\(.)", m => m.Groups[1].Value == "n" ? "\n" : m.Groups[1].Value);
            return v;
        }

        static readonly Regex StringLiteral = new Regex(@"(?<!@)""((?:[^""\\\r\n]|\\.)*)""");
        static readonly Regex FieldInit = new Regex(@"\b([A-Za-z_]\w*)\s*=\s*$");

        /// <summary>
        /// Links written as strings in the game's scripts, on lines the build target compiles: by the text, or by the name
        /// of the field / variable it's assigned to (string privacyUrl = "…").
        /// </summary>
        public static List<Link> ScanCode(GenZSourceModel model, StoreLinkSettings s, bool ios)
        {
            var links = new List<Link>();
            foreach (var f in model.files)
            {
                bool[] compiled = null;
                foreach (Match m in StringLiteral.Matches(f.text))
                {
                    string value;
                    try { value = Regex.Unescape(m.Groups[1].Value); } catch (ArgumentException) { value = m.Groups[1].Value; }
                    var lineStart = f.text.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                    var assigned = FieldInit.Match(f.text.Substring(lineStart, m.Index - lineStart));
                    var kind = assigned.Success ? ClassifyField(assigned.Groups[1].Value, value, s) : ClassifyText(value, s);
                    if (kind == null) continue;
                    int line = f.LineOf(m.Index);
                    compiled = compiled ?? GenZCodeChecks.CompiledLines(f.masked.Replace("\r\n", "\n").Split('\n'), ios ? GenZCodeChecks.IosDefines : GenZCodeChecks.AndroidDefines);
                    if (line - 1 < compiled.Length && !compiled[line - 1]) continue; // the other platform's link
                    links.Add(new Link { kind = kind.Value, label = $"{Path.GetFileName(f.path)}:{line}" + (assigned.Success ? " " + assigned.Groups[1].Value : ""),
                                         url = value, file = f.path, line = line, field = assigned.Success });
                }
            }
            return links;
        }

        // ---------------------------------------------------------------- report

        public static void Report(ReportSection sec, List<Link> links, Expected expected, List<IdFix> fixes)
        {
            expected = expected ?? new Expected();
            foreach (var kind in new[] { Kind.Privacy, Kind.MoreGames, Kind.RateUs })
            {
                var name = KindName(kind);
                var exp = expected.For(kind);
                var found = links.Where(l => l.kind == kind).ToList();
                if (found.Count == 0)
                {
                    if (kind == Kind.Privacy)
                        sec.Add("Privacy policy link", CheckStatus.Warn, "not found",
                            "Nothing in the project (button / event texts, Inspector fields, scripts) holds a privacy policy link. Google Play and the consent screen need one the player can open"
                            + (exp != null ? ": " + exp : ""));
                    else if (kind == Kind.MoreGames)
                        sec.Add("More Games link", CheckStatus.Info, "not used",
                            "Nothing in the project (button / event texts, Inspector fields, scripts) opens a store developer page (More Games): nothing to check"
                            + (exp != null ? $". If one is added, it must open {exp}" : ""));
                    else
                        sec.Add("Rate Us link", CheckStatus.Info, "not used",
                            "Nothing in the project opens the game's store page (Rate Us): nothing to check (the native review dialog, e.g. InAppReviewManager, needs no link)"
                            + (exp != null ? $". If one is added, it must open {exp}" : ""));
                    continue;
                }
                foreach (var l in found)
                {
                    CheckStatus status;
                    string message;
                    if (kind != Kind.Privacy && WrongPlatform(l.url, expected.ios, out var platformNote))
                    {
                        status = CheckStatus.Fail;
                        message = platformNote + (exp != null ? $". It must open {exp}" : "");
                    }
                    else if (kind != Kind.Privacy && l.propertyPath == null && IsPrefixOnly(l.url) && l.file != null && l.file.EndsWith(".cs"))
                    {
                        status = CheckStatus.Info;
                        message = "The start of a link the code completes: not compared" + (exp != null ? $". It must open {exp}" : "");
                    }
                    else status = Judge(kind, l.url, exp, out message);
                    if (l.coveredBy != null && status == CheckStatus.Fail)
                    {
                        status = CheckStatus.Info; // e.g. an old button left under its replacement
                        message = $"Hidden leftover: {l.coveredBy} is drawn over its whole area, so it can never be tapped (safe to delete). " + message;
                    }
                    else if (l.coveredBy != null && status == CheckStatus.Pass)
                    {
                        status = CheckStatus.Warn;
                        message = $"Right link, but {l.coveredBy} is drawn over its whole area: the player can't tap it";
                    }
                    else if (status == CheckStatus.Fail && l.field && string.IsNullOrWhiteSpace(l.url))
                    {
                        status = CheckStatus.Warn; // a link field not filled in: maybe unused
                        message = "Empty link field: fill it in if the game uses it" + (exp != null ? $" ({exp})" : "");
                    }
                    else if (status == CheckStatus.Fail && !l.inBuild)
                    {
                        status = CheckStatus.Warn;
                        message += (l.file ?? "").EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                            ? ". This scene isn't in Build Settings, so it doesn't ship: fix it or delete the scene"
                            : ". No Build Settings scene uses this file and it isn't in a Resources folder (an old copy?), so it likely doesn't ship"
                              + " (unless it's loaded through Addressables / Asset Bundles): fix it or delete it";
                    }
                    var label = $"{name} · {l.label}";
                    var r = l.line > 0 ? sec.Add(label, status, Short(l.url), message, l.file, l.line)
                                       : sec.Add(label, status, Short(l.url), message);
                    if (l.line <= 0) r.file = l.file;
                    if (status == CheckStatus.Fail && l.coveredBy == null && exp != null && fixes != null && l.propertyPath != null && (l.url ?? "") != exp)
                    {
                        fixes.Add(new IdFix
                        {
                            label = $"{name} link › {l.objectPath}",
                            current = l.url ?? "",
                            expected = exp,
                            propertyPath = l.propertyPath,
                            scenePath = l.scenePath,
                            objectPath = l.objectPath,
                            componentType = l.componentType
                        });
                        r.message += ". Auto Correct can write the sheet's link into this button";
                    }
                }
            }
        }

        static string Short(string s) => string.IsNullOrEmpty(s) ? "(empty)" : s.Length <= 60 ? s : s.Substring(0, 57) + "…";
    }
}
