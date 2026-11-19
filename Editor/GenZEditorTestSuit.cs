// GenZ-TestSuit-v1.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GenZ.TestSuit
{
    /// <summary>
    /// "GenZ-EditorTestSuit": verifies SDK setup in the SDK scene and the SDK settings assets,
    /// optionally against the studio's Google Sheet. Works without compile-time references to the
    /// SDK/ad scripts: components and assets are matched by class name and fields are read via SerializedObject.
    /// </summary>
    public static class GenZEditorTestSuit
    {
        public const string SuiteName = "GenZ-EditorTestSuit";
        const string GoogleTestPublisher = "ca-app-pub-3940256099942544";
        static readonly Regex AdMobUnitRegex = new Regex(@"^ca-app-pub-\d{16}/\d{10}$");
        static readonly Regex AdMobAppRegex = new Regex(@"^ca-app-pub-\d{16}~\d{10}$");
        static readonly Regex MaxUnitRegex = new Regex(@"^[0-9a-fA-F]{16}$");
        static readonly Regex MaxSdkKeyRegex = new Regex(@"^[A-Za-z0-9_-]{86}$");
        static readonly Regex AdjustTokenRegex = new Regex(@"^[a-z0-9]{12}$");
        static readonly Regex GaGameKeyRegex = new Regex(@"^[0-9a-f]{32}$");
        static readonly Regex GaSecretKeyRegex = new Regex(@"^[0-9a-f]{40}$");
        static readonly Regex AppMetricaKeyRegex = new Regex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");
        static readonly Regex PlayProductRegex = new Regex(@"^[a-z0-9][a-z0-9._]*$");
        static readonly Regex AdsNameRegex = new Regex(@"^(ads?|ad[s]?[ _-]?(manager|object|obj|holder|sdk|controller)s?)$", RegexOptions.IgnoreCase);

        /// <summary>Path of the scene used by the last run (for "Open SDK scene").</summary>
        public static string LastScenePath { get; private set; }
        /// <summary>Hierarchy path of the Ads object found by the last run.</summary>
        public static string LastAdsObjectPath { get; private set; }

        /// <summary>State shared by all checks of one run.</summary>
        class RunContext
        {
            public GenZSheetReader sheet;
            public readonly Dictionary<string, string> idOwners = new Dictionary<string, string>(); // id value -> first "Rule › Field"
            public readonly Dictionary<string, string> values = new Dictionary<string, string>();   // "Rule/Field" -> value (for sameAs)
            public readonly List<IdFix> fixes = new List<IdFix>();                                   // IDs Auto Apply can set from the sheet
        }

        /// <param name="package">The game whose sheet row the project is checked against (picked in the window). Null = this project's package.</param>
        public static TestReport Run(GenZTestSuitConfig cfg, string package = null)
        {
            var report = new TestReport(SuiteName);
            var env = report.Section("Environment", TestStage.Ids);
            var ctx = new RunContext();
            LastScenePath = null;
            LastAdsObjectPath = null;

            var projectPkg = GenZTestSuitConfig.PackageName;
            var pkg = string.IsNullOrEmpty(package) ? projectPkg : package;
            if (pkg == projectPkg)
                env.Add("Package name", string.IsNullOrEmpty(pkg) ? CheckStatus.Fail : CheckStatus.Info, pkg,
                    string.IsNullOrEmpty(pkg) ? "Package name is empty in Player Settings" : "From Build Settings platform: " + EditorUserBuildSettings.activeBuildTarget);
            else
                env.Add("Package name", CheckStatus.Warn, pkg,
                    $"Picked in the Game list. Player Settings say {projectPkg}: the build would ship with that package unless you change it.");

            // 1. Google Sheet (optional) — download first so a bad link is reported even if the scene check fails.
            if (cfg.verifyAgainstSheet)
            {
                var url = cfg.EffectiveSheetUrl;
                var sheet = new GenZSheetReader();
                if (!sheet.Download(url, cfg.sheetTabs) || !sheet.SelectGame(pkg, cfg.packageColumnNames))
                    env.Add("Google Sheet", CheckStatus.Fail, Shorten(url, 60), sheet.error);
                else
                {
                    ctx.sheet = sheet;
                    env.Add("Google Sheet", CheckStatus.Pass, pkg, "Row found"
                        + (sheet.tabsUsed.Count > 0 ? $" · tabs: {string.Join(", ", sheet.tabsUsed)}" : "")
                        + $" · columns: {string.Join(", ", sheet.headers)}");
                }
            }
            else
                env.Add("Google Sheet", CheckStatus.Info, "", "Sheet verification turned off");

            if (EditorUtility.scriptCompilationFailed)
                env.Add("Script compilation", CheckStatus.Fail, "", "The project has compile errors — fix them first (Console). Scene scripts show as Missing until then.")
                    .stage = TestStage.Integration;

            // 2. Scenes + Ads object, then every rule. Scenes opened here stay open until all checks are done.
            var opened = new List<Scene>();
            var taichi = cfg.taichi ?? new TaichiSettings();
            var taichiIn = new TaichiInput();
            if (cfg.runIdChecks || taichi.enabled)
            try
            {
                var scenes = OpenCandidateScenes(cfg, env, opened);
                var ads = FindAdsObject(cfg, env, scenes);
                var sdkScene = ads != null ? ads.scene : scenes.FirstOrDefault();
                if (taichi.enabled) ReadTaichiSwitches(taichi, scenes, ads, taichiIn);

                if (cfg.runIdChecks)
                foreach (var rule in cfg.rules)
                {
                    switch (rule.target)
                    {
                        case RuleTarget.AdsObject: CheckAdsObjectRule(report, rule, ads, ctx); break;
                        case RuleTarget.SceneObject: CheckSceneObjectRule(report, rule, scenes, sdkScene, ctx); break;
                        case RuleTarget.Asset: CheckAssetRule(report, rule, ctx); break;
                    }
                }
            }
            finally
            {
                foreach (var s in opened)
                    if (s.IsValid() && s.isLoaded) EditorSceneManager.CloseScene(s, true);
            }

            // 3. SDK files and Android build setup (no scene needed).
            if (cfg.runIntegrationChecks)
            {
                GenZIntegrationChecks.Run(report, pkg, cfg.integration ?? new IntegrationSettings());
                var player = cfg.playerSettings ?? new AndroidPlayerRules();
                if (player.enabled) GenZPlayerSettingsChecks.Run(report, player);
                GenZDeepTestSuit.CheckRelease(report, cfg);
                GenZSigning.Check(report, ctx.sheet, cfg.signing ?? new SigningSettings());
            }

            report.idFixes.AddRange(ctx.fixes);

            // 4. Taichi and the other code checks read the same scripts: load them once.
            var codeSettings = cfg.codeChecks ?? new CodeCheckSettings();
            if (taichi.enabled || codeSettings.enabled)
            {
                var model = GenZSourceModel.Load(codeSettings.excludeFolders);
                if (taichi.enabled)
                {
                    if (ctx.sheet != null)
                    {
                        taichiIn.sheetRead = true;
                        taichiIn.sheetCount = ctx.sheet.Lookup(taichi.taichi2SheetColumns, out taichiIn.sheetColumn);
                    }
                    GenZCodeChecks.RunTaichi(report, codeSettings, taichi, taichiIn, model);

                    // Auto Apply can write the sheet's Taichi 2.0 count into the Inspector default or, for a game
                    // without one (no RemoteConfigManager), into the number written in code.
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    if (taichiIn.sheetCount != null
                        && int.TryParse(taichiIn.sheetCount.Trim(), System.Globalization.NumberStyles.Integer, inv, out var sheetImp))
                    {
                        var sheetText = sheetImp.ToString(inv);
                        if (taichiIn.taichi2DefaultProperty != null)
                        {
                            if (taichiIn.taichi2Default != sheetText)
                                report.idFixes.Add(new IdFix
                                {
                                    label = "Taichi 2.0 › default impressions",
                                    current = taichiIn.taichi2Default,
                                    expected = sheetText,
                                    propertyPath = taichiIn.taichi2DefaultProperty,
                                    scenePath = taichiIn.defaultFile,
                                    objectPath = taichiIn.defaultObjectPath,
                                    componentType = taichiIn.defaultComponent
                                });
                        }
                        else if (taichiIn.taichi2CodeFile != null)
                        {
                            var suffix = Regex.Match(taichiIn.taichi2CodeValue, @"[lLuUfFdDmM]+$").Value; // keep 50L a long
                            var number = taichiIn.taichi2CodeValue.Substring(0, taichiIn.taichi2CodeValue.Length - suffix.Length);
                            if (!double.TryParse(number, System.Globalization.NumberStyles.Float, inv, out var codeImp) || Math.Abs(codeImp - sheetImp) > 1e-9)
                                report.idFixes.Add(new IdFix
                                {
                                    label = "Taichi 2.0 › default impressions (code)",
                                    current = taichiIn.taichi2CodeValue,
                                    expected = sheetText + suffix,
                                    codeFile = taichiIn.taichi2CodeFile,
                                    codeLine = taichiIn.taichi2CodeLine,
                                    codeContext = taichiIn.taichi2CodeContext,
                                    codeValue = taichiIn.taichi2CodeValue
                                });
                        }
                    }
                }
                if (codeSettings.enabled)
                    GenZCodeChecks.Run(report, codeSettings, model);
            }
            return report;
        }

        /// <summary>Reads the Taichi 1.0 / 2.0 switches from the manager component (Ads object first, then any object in the SDK scenes).</summary>
        static void ReadTaichiSwitches(TaichiSettings t, List<Scene> scenes, GameObject ads, TaichiInput input)
        {
            ReadTaichiDefaults(t, scenes, ads, input);
            Component comp = ads != null ? FindComponent(ads, t.managerClasses) : null;
            if (comp == null)
                foreach (var scene in scenes)
                {
                    var go = AllObjects(scene).FirstOrDefault(o => FindComponent(o, t.managerClasses) != null);
                    if (go != null) { comp = FindComponent(go, t.managerClasses); break; }
                }
            if (comp == null)
            {
                input.note = $"No {string.Join(" / ", t.managerClasses)} in " + (scenes.Count == 0 ? "the SDK scene" : string.Join(", ", scenes.Select(s => s.name)));
                return;
            }

            input.sceneRead = true;
            input.where = comp.gameObject.scene.name + " › " + HierarchyPath(comp.transform) + " (" + comp.GetType().Name + ")";
            input.file = comp.gameObject.scene.path;
            var so = new SerializedObject(comp);
            var p1 = FindProperty(so, t.taichi1Flags);
            var p2 = FindProperty(so, t.taichi2Flags);
            if (p1 != null && p1.propertyType == SerializedPropertyType.Boolean) { input.taichi1 = p1.boolValue; input.taichi1Flag = p1.name; }
            if (p2 != null && p2.propertyType == SerializedPropertyType.Boolean) { input.taichi2 = p2.boolValue; input.taichi2Flag = p2.name; }
        }

        /// <summary>The Taichi defaults set in the Inspector (RemoteConfigManager: Ads object first, then any object in the SDK scenes).</summary>
        static void ReadTaichiDefaults(TaichiSettings t, List<Scene> scenes, GameObject ads, TaichiInput input)
        {
            var classes = t.defaultClasses ?? new string[0];
            Component comp = ads != null ? FindComponent(ads, classes) : null;
            if (comp == null)
                foreach (var scene in scenes)
                {
                    var go = AllObjects(scene).FirstOrDefault(o => FindComponent(o, classes) != null);
                    if (go != null) { comp = FindComponent(go, classes); break; }
                }
            if (comp == null) return;

            input.defaultWhere = comp.gameObject.scene.name + " › " + HierarchyPath(comp.transform) + " (" + comp.GetType().Name + ")";
            input.defaultFile = comp.gameObject.scene.path;
            input.defaultObjectPath = HierarchyPath(comp.transform);
            input.defaultComponent = comp.GetType().Name;
            var so = new SerializedObject(comp);
            var d1 = FindProperty(so, t.taichi1DefaultFields ?? new string[0]);
            var d2 = FindProperty(so, t.taichi2DefaultFields ?? new string[0]);
            if (d1 != null && (d1.propertyType == SerializedPropertyType.Float || d1.propertyType == SerializedPropertyType.Integer))
                input.taichi1Default = DisplayValue(d1);
            if (d2 != null && d2.propertyType == SerializedPropertyType.Integer)
            {
                input.taichi2Default = DisplayValue(d2);
                input.taichi2DefaultProperty = d2.propertyPath;
            }
        }

        // ---------------------------------------------------------------- scene / object discovery

        internal static List<Scene> OpenCandidateScenes(GenZTestSuitConfig cfg, ReportSection env, List<Scene> opened)
        {
            var paths = new List<string>();
            if (cfg.sdkScene != null)
                paths.Add(AssetDatabase.GetAssetPath(cfg.sdkScene));
            else
            {
                // Auto: first enabled build scenes, preferring Consent/Splash-like names.
                var build = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).Take(3).ToList();
                paths.AddRange(build.Where(p => Regex.IsMatch(System.IO.Path.GetFileNameWithoutExtension(p), "consent|splash|loading|init|boot", RegexOptions.IgnoreCase)));
                paths.AddRange(build.Take(2).Where(p => !paths.Contains(p)));
            }

            var scenes = new List<Scene>();
            foreach (var path in paths)
            {
                var scene = SceneManager.GetSceneByPath(path);
                if (!(scene.IsValid() && scene.isLoaded))
                {
                    if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    opened.Add(scene);
                }
                scenes.Add(scene);
            }

            if (scenes.Count == 0)
                env.Add("SDK scene", CheckStatus.Fail, "", "No SDK scene set and no usable scenes in Build Settings");
            return scenes;
        }

        internal static GameObject FindAdsObject(GenZTestSuitConfig cfg, ReportSection env, List<Scene> scenes)
        {
            if (scenes.Count == 0) return null;

            var adsRules = cfg.rules.Where(r => r.target == RuleTarget.AdsObject).ToList();
            GameObject best = null;
            int bestScore = 0;
            string how = null;
            var wantedName = (cfg.adsObjectName ?? "").Trim();

            foreach (var scene in scenes)
            foreach (var go in AllObjects(scene))
            {
                // Configured name (case-insensitive) wins outright; otherwise score by SDK scripts + Ads-like name.
                int scripts = adsRules.Count(r => FindComponent(go, r.scriptNames) != null);
                bool named = wantedName.Length > 0 && string.Equals(go.name.Trim(), wantedName, StringComparison.OrdinalIgnoreCase);
                int score = (named ? 1000 : 0) + scripts * 10 + (AdsNameRegex.IsMatch(go.name.Trim()) ? 5 : 0);

                if (score > bestScore)
                {
                    best = go; bestScore = score;
                    how = named ? $"matched name \"{wantedName}\" ({scripts} of {adsRules.Count} SDK scripts on it)"
                        : $"auto-detected ({scripts} of {adsRules.Count} SDK scripts on it)";
                }
            }

            if (best == null)
            {
                env.Add("SDK scene", CheckStatus.Fail, string.Join(", ", scenes.Select(s => s.name)),
                    "Ads object not found in these scenes. Set the SDK scene and/or Ads object name in Settings.");
                return null;
            }

            LastScenePath = best.scene.path;
            LastAdsObjectPath = HierarchyPath(best.transform);
            bool nameMissed = wantedName.Length > 0 && bestScore < 1000;
            env.Add("SDK scene", CheckStatus.Pass, best.scene.name, best.scene.path);
            env.Add("Ads object", nameMissed ? CheckStatus.Warn : CheckStatus.Pass, LastAdsObjectPath,
                (nameMissed ? $"No object named \"{wantedName}\" — " : "") + how + (best.activeInHierarchy ? "" : " · note: object is inactive"));
            return best;
        }

        internal static IEnumerable<GameObject> AllObjects(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                yield return t.gameObject;
        }

        internal static bool NameMatches(string actual, string[] names) =>
            names != null && names.Any(n => string.Equals(actual, n, StringComparison.OrdinalIgnoreCase));

        internal static Component FindComponent(GameObject go, string[] scriptNames)
        {
            foreach (var c in go.GetComponents<Component>())
                if (c != null && NameMatches(c.GetType().Name, scriptNames))
                    return c;
            return null;
        }

        internal static string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;

        /// <summary>
        /// The component or settings asset a rule points at, found the same way the checks find it (Ads object and its
        /// children; SDK scene first, by name then by script; the asset under the rule's folder first). Null if missing.
        /// </summary>
        internal static Object ResolveRule(ScriptRule rule, List<Scene> scenes, GameObject ads)
        {
            switch (rule.target)
            {
                case RuleTarget.AdsObject:
                    if (ads == null) return null;
                    return FindComponent(ads, rule.scriptNames)
                           ?? ads.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null && NameMatches(c.GetType().Name, rule.scriptNames));
                case RuleTarget.SceneObject:
                {
                    var ordered = scenes.OrderBy(s => ads != null && s == ads.scene ? 0 : 1).ToList();
                    var go = ordered.SelectMany(AllObjects).FirstOrDefault(o => NameMatches(o.name.Trim(), rule.objectNames) && FindComponent(o, rule.scriptNames) != null)
                             ?? ordered.SelectMany(AllObjects).FirstOrDefault(o => FindComponent(o, rule.scriptNames) != null);
                    return go == null ? null : FindComponent(go, rule.scriptNames);
                }
                default:
                {
                    var folder = (rule.assetFolder ?? "").Trim('/');
                    Object fallback = null;
                    foreach (var name in rule.assetNames)
                    foreach (var guid in AssetDatabase.FindAssets(name))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)) continue;
                        var asset = AssetDatabase.LoadMainAssetAtPath(path);
                        if (asset == null || !NameMatches(asset.GetType().Name, rule.scriptNames)) continue;
                        if (folder.Length == 0 || path.Replace('\\', '/').IndexOf("/" + folder + "/", StringComparison.OrdinalIgnoreCase) >= 0) return asset;
                        fallback = fallback ?? asset;
                    }
                    return fallback;
                }
            }
        }

        // ---------------------------------------------------------------- rule targets

        static void CheckAdsObjectRule(TestReport report, ScriptRule rule, GameObject ads, RunContext ctx)
        {
            var section = report.Section(rule.label, TestStage.Integration);
            if (ads == null)
            {
                section.Add("Script attached", CheckStatus.Fail, string.Join(" / ", rule.scriptNames), "Ads object not found (see Environment)");
                return;
            }

            var comp = FindComponent(ads, rule.scriptNames);
            string where = "on " + ads.name;
            if (comp == null)
            {
                // Not on the Ads object itself — look in its children before failing.
                comp = ads.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null && NameMatches(c.GetType().Name, rule.scriptNames));
                if (comp != null) where = "on child " + HierarchyPath(comp.transform);
            }

            if (comp == null)
            {
                var missing = ads.GetComponents<Component>().Any(c => c == null) ? " (the object has a Missing Script — check compile errors)" : "";
                section.Add("Script attached", rule.required ? CheckStatus.Fail : CheckStatus.Warn, string.Join(" / ", rule.scriptNames),
                    "Not found on the Ads object" + missing);
                return;
            }

            section.Add("Script attached", comp.gameObject == ads ? CheckStatus.Pass : CheckStatus.Warn, comp.GetType().Name, where);
            CheckFields(section, rule, new SerializedObject(comp), ctx);
        }

        static void CheckSceneObjectRule(TestReport report, ScriptRule rule, List<Scene> scenes, Scene sdkScene, RunContext ctx)
        {
            var section = report.Section(rule.label, TestStage.Integration);
            var missStatus = rule.required ? CheckStatus.Fail : CheckStatus.Warn;
            if (scenes.Count == 0)
            {
                section.Add("Object in scene", missStatus, string.Join(" / ", rule.objectNames), "No SDK scene available");
                return;
            }

            // SDK scene first, then the other candidate scenes; by name first, then by component.
            var ordered = scenes.OrderBy(s => s == sdkScene ? 0 : 1).ToList();
            GameObject go = null;
            foreach (var s in ordered)
            {
                go = AllObjects(s).FirstOrDefault(o => NameMatches(o.name.Trim(), rule.objectNames));
                if (go != null) break;
            }
            bool byComponent = false;
            if (go == null && rule.scriptNames != null && rule.scriptNames.Length > 0)
                foreach (var s in ordered)
                {
                    go = AllObjects(s).FirstOrDefault(o => FindComponent(o, rule.scriptNames) != null);
                    if (go != null) { byComponent = true; break; }
                }

            if (go == null)
            {
                section.Add("Object in scene", missStatus, string.Join(" / ", rule.objectNames),
                    "Not found in " + string.Join(", ", scenes.Select(s => s.name)));
                return;
            }

            var objStatus = CheckStatus.Pass;
            var notes = new List<string> { "in " + go.scene.name };
            if (sdkScene.IsValid() && go.scene != sdkScene) { objStatus = CheckStatus.Warn; notes.Add("not in the SDK scene " + sdkScene.name); }
            if (byComponent) { objStatus = CheckStatus.Warn; notes.Add("found by its script — expected name " + string.Join(" / ", rule.objectNames)); }
            if (rule.expectPrefab)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(go))
                    notes.Add("prefab instance of " + System.IO.Path.GetFileNameWithoutExtension(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go)));
                else { objStatus = Worse(objStatus, CheckStatus.Warn); notes.Add("not a prefab instance"); }
            }
            section.Add("Object in scene", objStatus, HierarchyPath(go.transform), string.Join(" · ", notes));

            if (rule.mustBeActive)
            {
                var active = go.activeSelf;
                section.Add("Object is active", active && go.activeInHierarchy ? CheckStatus.Pass : CheckStatus.Fail,
                    "SetActive = " + (active ? "true" : "false"),
                    !active ? "Enable the object (SetActive true)" : go.activeInHierarchy ? "OK" : "A parent object is inactive");
            }

            if (rule.scriptNames == null || rule.scriptNames.Length == 0) return;
            var comp = FindComponent(go, rule.scriptNames);
            if (comp == null)
            {
                section.Add("Script attached", missStatus, string.Join(" / ", rule.scriptNames), "Not on " + go.name);
                return;
            }
            section.Add("Script attached", CheckStatus.Pass, comp.GetType().Name, "on " + go.name);
            CheckFields(section, rule, new SerializedObject(comp), ctx);
        }

        static void CheckAssetRule(TestReport report, ScriptRule rule, RunContext ctx)
        {
            var section = report.Section(rule.label, TestStage.Integration);
            var missStatus = rule.required ? CheckStatus.Fail : CheckStatus.Warn;

            // Every .asset with one of the expected file names whose ScriptableObject class matches.
            var found = new List<(string path, Object asset)>();
            foreach (var name in rule.assetNames)
            foreach (var guid in AssetDatabase.FindAssets(name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)) continue;
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null && NameMatches(asset.GetType().Name, rule.scriptNames) && found.All(f => f.path != path))
                    found.Add((path, asset));
            }

            if (found.Count == 0)
            {
                section.Add("Settings asset", missStatus, string.Join(" / ", rule.assetNames),
                    $"No {string.Join(" / ", rule.scriptNames)} asset found" + (string.IsNullOrEmpty(rule.assetFolder) ? "" : $" (expected under {rule.assetFolder})"));
                return;
            }

            // Prefer the one inside the expected folder.
            var folder = (rule.assetFolder ?? "").Trim('/');
            var inFolder = found.Where(f => folder.Length == 0 || f.path.Replace('\\', '/').IndexOf("/" + folder + "/", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var pick = inFolder.Count > 0 ? inFolder[0] : found[0];
            var status = inFolder.Count > 0 ? CheckStatus.Pass : CheckStatus.Fail;
            var note = inFolder.Count > 0 ? "OK" : $"Not under {rule.assetFolder} — the SDK can't load it at runtime";
            if (inFolder.Count > 1) { status = CheckStatus.Warn; note = "Several copies: " + string.Join(", ", inFolder.Select(f => f.path)); }
            section.Add("Settings asset", status, pick.path, note);

            CheckFields(section, rule, new SerializedObject(pick.asset), ctx);
        }

        // ---------------------------------------------------------------- fields

        static void CheckFields(ReportSection section, ScriptRule rule, SerializedObject so, RunContext ctx)
        {
            foreach (var field in rule.fields)
            {
                // ID values go to step 1; switches, expected values and numbers are integration settings (step 2).
                section.stage = IsIdKind(field.kind) ? TestStage.Ids : TestStage.Integration;
                CheckField(section, rule, field, so, ctx);
            }
            section.stage = TestStage.Integration;
        }

        static void CheckField(ReportSection section, ScriptRule rule, FieldRule f, SerializedObject so, RunContext ctx)
        {
            var prop = FindProperty(so, f.fieldNames);
            if (prop == null)
            {
                if (f.required)
                    section.Add(f.label, CheckStatus.Fail, "", "Field not found. Tried: " + string.Join(", ", f.fieldNames) + " — add the real name in the config rules");
                return; // optional fields (e.g. isTestIdOn) are simply skipped when a project doesn't have them
            }

            switch (f.kind)
            {
                case FieldKind.MustBeFalse:
                case FieldKind.MustBeTrue:
                {
                    bool want = f.kind == FieldKind.MustBeTrue;
                    bool isBool = prop.propertyType == SerializedPropertyType.Boolean;
                    bool ok = isBool && prop.boolValue == want;
                    section.Add(f.label, ok ? CheckStatus.Pass : CheckStatus.Fail, prop.name + " = " + (isBool ? (prop.boolValue ? "true" : "false") : prop.propertyType.ToString()),
                        ok ? "OK" : isBool ? $"Must be {(want ? "ON" : "OFF")}" : "Not a bool field");
                    return;
                }
                case FieldKind.Expected:
                {
                    var actual = DisplayValue(prop);
                    bool ok = string.Equals(actual.Trim(), (f.expectedValue ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                    section.Add(f.label, ok ? CheckStatus.Pass : CheckStatus.Fail, prop.name + " = " + actual, ok ? "OK" : "Must be " + f.expectedValue);
                    return;
                }
                case FieldKind.InAppList:
                    CheckInAppList(section, f, prop, ctx.sheet);
                    return;
                case FieldKind.NumberAboveZero:
                {
                    bool isNumber = prop.propertyType == SerializedPropertyType.Integer || prop.propertyType == SerializedPropertyType.Float;
                    double v = prop.propertyType == SerializedPropertyType.Float ? prop.floatValue : prop.intValue;
                    section.Add(f.label, isNumber && v > 0 ? CheckStatus.Pass : CheckStatus.Fail, prop.name + " = " + DisplayValue(prop),
                        !isNumber ? "Not a number field" : v > 0 ? "OK (used until Remote Config overrides it)" : "Must be greater than 0");
                    return;
                }
            }

            // String IDs (optionally picked from a per-platform list).
            string raw;
            string pickNote = null;
            string writePath = prop.propertyType == SerializedPropertyType.String ? prop.propertyPath : null; // where Auto Apply writes
            if (!string.IsNullOrEmpty(f.platformListField))
            {
                raw = PlatformValue(so, prop, f, out pickNote, out writePath);
                if (raw == null)
                {
                    section.Add(f.label, f.required ? CheckStatus.Fail : CheckStatus.Warn, "", pickNote);
                    return;
                }
            }
            else
                raw = prop.propertyType == SerializedPropertyType.String ? prop.stringValue ?? "" : "";

            var value = raw.Trim();
            var status = CheckStatus.Pass;
            var notes = new List<string>();
            if (pickNote != null) notes.Add(pickNote);

            if (value.Length == 0)
            {
                status = f.required ? CheckStatus.Fail : CheckStatus.Warn;
                notes.Add("Empty");
            }
            else
            {
                // Any space, tab or line break breaks an ID at runtime, even at the ends.
                var ws = WhitespaceNote(raw);
                if (ws != null) { status = CheckStatus.Fail; notes.Add(ws); }

                var formatNote = CheckFormat(f.kind, value, out var formatStatus);
                status = Worse(status, formatStatus);
                notes.Add(formatNote);

                if ((f.kind == FieldKind.AdMobUnitId || f.kind == FieldKind.MaxUnitId) && formatStatus != CheckStatus.Fail)
                {
                    if (ctx.idOwners.TryGetValue(value, out var other))
                    {
                        status = Worse(status, CheckStatus.Warn);
                        notes.Add("Same ID as " + other);
                    }
                    else ctx.idOwners[value] = section.title + " › " + f.label;
                }
            }

            // Must equal another field (e.g. AppLovin's AdMob App ID == Google Mobile Ads' App ID).
            if (!string.IsNullOrEmpty(f.sameAs))
            {
                if (!ctx.values.TryGetValue(f.sameAs, out var other))
                {
                    status = Worse(status, CheckStatus.Warn);
                    notes.Add($"Can't compare with {f.sameAs} (not read)");
                }
                else if (other == value) notes.Add("Same as " + f.sameAs);
                else
                {
                    status = CheckStatus.Fail;
                    notes.Add($"Differs from {f.sameAs} ({Shorten(other, 40)})");
                }
            }
            ctx.values[rule.label + "/" + f.label] = value;

            if (ctx.sheet != null && f.sheetColumns != null && f.sheetColumns.Length > 0)
            {
                var expected = ctx.sheet.Lookup(f.sheetColumns, out var col);
                if (expected == null && f.kind == FieldKind.MaxSdkKey)
                {
                    // One SDK key per AppLovin account: the studio sheet keeps it in a single unlabeled cell.
                    expected = ctx.sheet.FindUniqueCell(MaxSdkKeyRegex);
                    col = "the sheet's SDK key cell";
                }
                if (expected == null)
                {
                    status = Worse(status, CheckStatus.Warn);
                    notes.Add("No sheet column (tried " + string.Join(", ", f.sheetColumns) + ")");
                }
                else if (expected.Length == 0)
                {
                    status = Worse(status, CheckStatus.Warn);
                    notes.Add($"Sheet column \"{col}\" is empty");
                }
                else
                {
                    var cmp = f.kind == FieldKind.MaxUnitId || f.kind == FieldKind.AppMetricaApiKey ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    if (string.Equals(expected, value, cmp)) notes.Add("Matches sheet");
                    else
                    {
                        status = CheckStatus.Fail;
                        notes.Add($"Sheet expects {Shorten(expected, 48)}");
                    }
                    // Wrong, empty or padded with whitespace: Auto Apply can write the sheet value.
                    if (!string.Equals(expected, raw, cmp) && writePath != null && AddFix(ctx, so.targetObject, writePath, rule.label + " › " + f.label, raw, expected, f.kind))
                        notes.Add("Auto Apply can set it");
                }
            }

            section.Add(f.label, status, f.kind == FieldKind.MaxSdkKey || f.kind == FieldKind.GameAnalyticsSecretKey ? Shorten(value, 14) : value,
                string.Join(" · ", notes));
        }

        /// <summary>Picks the element of a per-platform list (e.g. GameAnalytics gameKey[i] where Platforms[i] == Android).</summary>
        static string PlatformValue(SerializedObject so, SerializedProperty list, FieldRule f, out string note, out string elementPath)
        {
            note = null;
            elementPath = null;
            var platforms = so.FindProperty(f.platformListField);
            if (platforms == null || !platforms.isArray)
            {
                note = $"Platform list '{f.platformListField}' not found";
                return null;
            }

            int index = -1;
            for (int i = 0; i < platforms.arraySize && index < 0; i++)
                if (string.Equals(DisplayValue(platforms.GetArrayElementAtIndex(i)), f.platformName, StringComparison.OrdinalIgnoreCase))
                    index = i;

            if (index < 0)
            {
                note = $"{f.platformName} platform is not added";
                return null;
            }
            if (!list.isArray || index >= list.arraySize)
            {
                note = $"No {list.name} entry for {f.platformName}";
                return null;
            }
            note = f.platformName + " platform";
            var el = list.GetArrayElementAtIndex(index);
            if (el.propertyType == SerializedPropertyType.String) elementPath = el.propertyPath;
            return el.stringValue ?? "";
        }

        /// <summary>Remembers where a sheet value must be written: an asset, or a component in a scene (by hierarchy path).</summary>
        static bool AddFix(RunContext ctx, Object target, string propertyPath, string label, string current, string expected, FieldKind kind)
        {
            var fix = new IdFix { label = label, propertyPath = propertyPath, current = current, expected = expected,
                secret = kind == FieldKind.MaxSdkKey || kind == FieldKind.GameAnalyticsSecretKey };
            if (target is Component comp)
            {
                if (!comp.gameObject.scene.IsValid() || string.IsNullOrEmpty(comp.gameObject.scene.path)) return false;
                fix.scenePath = comp.gameObject.scene.path;
                fix.objectPath = HierarchyPath(comp.transform);
                fix.componentType = comp.GetType().Name;
            }
            else
            {
                fix.assetPath = AssetDatabase.GetAssetPath(target);
                if (string.IsNullOrEmpty(fix.assetPath)) return false;
            }
            ctx.fixes.Add(fix);
            return true;
        }

        static string WhitespaceNote(string raw)
        {
            if (!raw.Any(char.IsWhiteSpace)) return null;
            if (raw.Length > 0 && (char.IsWhiteSpace(raw[0]) || char.IsWhiteSpace(raw[raw.Length - 1])))
                return "Has a space/tab/line break at the start or end";
            return "Contains a space/tab/line break";
        }

        static string CheckFormat(FieldKind kind, string v, out CheckStatus status)
        {
            status = CheckStatus.Pass;
            switch (kind)
            {
                case FieldKind.AdMobUnitId:
                    if (v.StartsWith(GoogleTestPublisher)) { status = CheckStatus.Fail; return "Google sample/test ID"; }
                    if (v.Contains("~")) { status = CheckStatus.Fail; return "This is an AdMob App ID (~), not an ad unit ID (/)"; }
                    if (!AdMobUnitRegex.IsMatch(v)) { status = CheckStatus.Warn; return "Unexpected AdMob format"; }
                    return "Format OK";
                case FieldKind.AdMobAppId:
                    if (v.StartsWith(GoogleTestPublisher)) { status = CheckStatus.Fail; return "Google sample/test App ID"; }
                    if (v.Contains("/")) { status = CheckStatus.Fail; return "This is an ad unit ID (/), not an App ID (~)"; }
                    if (!AdMobAppRegex.IsMatch(v)) { status = CheckStatus.Warn; return "Unexpected AdMob App ID format"; }
                    return "Format OK";
                case FieldKind.MaxUnitId:
                    if (!MaxUnitRegex.IsMatch(v)) { status = CheckStatus.Warn; return "Unexpected MAX ad unit format (16 hex chars)"; }
                    return "Format OK";
                case FieldKind.MaxSdkKey:
                    if (!MaxSdkKeyRegex.IsMatch(v)) { status = CheckStatus.Warn; return $"Unexpected SDK key format ({v.Length} chars, usually 86)"; }
                    return "Format OK";
                case FieldKind.AdjustToken:
                    if (!AdjustTokenRegex.IsMatch(v)) { status = CheckStatus.Warn; return "Unexpected Adjust token format (12 lowercase letters/digits)"; }
                    return "Format OK";
                case FieldKind.GameAnalyticsGameKey:
                    if (!GaGameKeyRegex.IsMatch(v)) { status = CheckStatus.Warn; return "Unexpected game key format (32 hex chars)"; }
                    return "Format OK";
                case FieldKind.GameAnalyticsSecretKey:
                    if (!GaSecretKeyRegex.IsMatch(v)) { status = CheckStatus.Warn; return "Unexpected secret key format (40 hex chars)"; }
                    return "Format OK";
                case FieldKind.AppMetricaApiKey:
                    if (!AppMetricaKeyRegex.IsMatch(v)) { status = CheckStatus.Warn; return "Unexpected AppMetrica API key format (8-4-4-4-12 hex)"; }
                    return "Format OK";
                default:
                    return "Set";
            }
        }

        static void CheckInAppList(ReportSection section, FieldRule f, SerializedProperty prop, GenZSheetReader sheet)
        {
            var ids = new List<string>();
            var spaced = new List<string>();
            if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var el = prop.GetArrayElementAtIndex(i);
                    string id = el.propertyType == SerializedPropertyType.String ? el.stringValue
                        : (FindRelative(el, "Id", "productId", "ID", "key", "sku")?.stringValue);
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (id.Any(char.IsWhiteSpace)) spaced.Add(id.Trim());
                    ids.Add(id.Trim());
                }
            }

            var status = ids.Count == 0 ? CheckStatus.Fail : CheckStatus.Pass;
            var notes = new List<string> { ids.Count == 0 ? "No in-app ID set" : ids.Count + " product(s)" };

            if (spaced.Count > 0) { status = CheckStatus.Fail; notes.Add("Contain a space/tab/line break: " + string.Join(", ", spaced)); }
            var bad = ids.Where(id => !PlayProductRegex.IsMatch(id)).ToList();
            if (bad.Count > 0) { status = Worse(status, CheckStatus.Warn); notes.Add("Not valid Google Play IDs (lowercase a-z 0-9 . _): " + string.Join(", ", bad)); }
            var dup = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dup.Count > 0) { status = Worse(status, CheckStatus.Warn); notes.Add("Duplicates: " + string.Join(", ", dup)); }

            if (sheet != null && f.sheetColumns != null && f.sheetColumns.Length > 0)
            {
                var expected = sheet.Lookup(f.sheetColumns, out var col);
                if (string.IsNullOrWhiteSpace(expected))
                {
                    status = Worse(status, CheckStatus.Warn);
                    notes.Add(expected == null ? "No sheet column for in-app IDs" : $"Sheet column \"{col}\" is empty");
                }
                else
                {
                    var want = expected.Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                    var missing = want.Where(w => !ids.Contains(w)).ToList();
                    var extra = ids.Where(i => !want.Contains(i)).ToList();
                    if (missing.Count > 0) { status = CheckStatus.Fail; notes.Add("Missing from project: " + string.Join(", ", missing)); }
                    if (extra.Count > 0) { status = Worse(status, CheckStatus.Warn); notes.Add("Not in sheet: " + string.Join(", ", extra)); }
                    if (missing.Count == 0 && extra.Count == 0) notes.Add("Matches sheet");
                }
            }

            section.Add(f.label, status, string.Join(", ", ids), string.Join(" · ", notes));
        }

        // ---------------------------------------------------------------- helpers

        internal static SerializedProperty FindProperty(SerializedObject so, string[] names)
        {
            var wanted = names.Select(GenZSheetReader.Normalize).ToList();
            var it = so.GetIterator();
            // Top-level fields only, including [HideInInspector] ones; first match in the order the names are listed wins.
            var found = new Dictionary<string, SerializedProperty>();
            if (it.Next(true))
                do
                {
                    var key = GenZSheetReader.Normalize(it.name);
                    if (wanted.Contains(key) && !found.ContainsKey(key)) found[key] = it.Copy();
                } while (it.Next(false));

            foreach (var w in wanted)
                if (found.TryGetValue(w, out var p)) return p;
            return null;
        }

        static SerializedProperty FindRelative(SerializedProperty el, params string[] names)
        {
            foreach (var n in names)
            {
                var p = el.FindPropertyRelative(n);
                if (p != null && p.propertyType == SerializedPropertyType.String) return p;
            }
            return null;
        }

        /// <summary>A property's value as text: enum name, true/false, number or string.</summary>
        internal static string DisplayValue(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length ? p.enumNames[p.enumValueIndex] : p.intValue.ToString();
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Integer: return p.intValue.ToString();
                case SerializedPropertyType.Float: return p.doubleValue.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
                case SerializedPropertyType.String: return p.stringValue ?? "";
                default: return p.propertyType.ToString();
            }
        }

        static bool IsIdKind(FieldKind k) =>
            k != FieldKind.MustBeFalse && k != FieldKind.MustBeTrue && k != FieldKind.Expected && k != FieldKind.NumberAboveZero;

        static CheckStatus Worse(CheckStatus a, CheckStatus b) => a > b ? a : b;

        static string Shorten(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max / 2) + "…" + s.Substring(s.Length - max / 2 + 1);
    }
}
