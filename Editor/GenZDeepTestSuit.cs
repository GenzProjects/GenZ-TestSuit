// GenZ-TestSuit-v1.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GenZ.TestSuit
{
    /// <summary>
    /// GenZ-DeepTestSuit: switches the project between the Test Environment (SDK logs on, Adjust sandbox, the phone's
    /// shake panel compiled in) and the Release Environment. The SDK settings are found with the config rules and
    /// written via SerializedObject, like Auto Apply IDs; the ad scripts are never changed. The phone side is
    /// <c>Assets/GenZ-DeepTest/GenZDeepTestPanel.cs</c>, compiled only while the define is set.
    /// </summary>
    public static class GenZDeepTestSuit
    {
        public const string SuiteName = "GenZ-DeepTestSuit";
        public const string RuntimeFolder = "Assets/GenZ-DeepTest";
        /// <summary>Ad unit IDs and switches for the phone's panel, read with Resources.Load("GenZDeepTest").</summary>
        public const string RuntimeJson = RuntimeFolder + "/Resources/GenZDeepTest.json";
        public const string PanelScript = RuntimeFolder + "/GenZDeepTestPanel.cs";

        static readonly NamedBuildTarget Android = NamedBuildTarget.Android;

        /// <summary>One setting's state in the project.</summary>
        public class Item
        {
            public string label, note, where, current, test, release, error;
            public bool enabled;
            public bool IsTest => error == null && Same(current, test);
            public bool IsRelease => error == null && Same(current, release);
        }

        public enum Env { Test, Release, Mixed }

        // Mirrors GenZDeepTestPanel's Settings (JsonUtility field names); the editor assembly can't see the game's.
        [Serializable] class RuntimeSettings
        {
            public bool maxVerbose, creativeDebugger, admobTestDevice;
            public float shakeThreshold;
            public List<DeepAction> actions = new List<DeepAction>();
            public string[] watch = new string[0];
        }

        /// <summary>Keeps the ad classes the panel calls by reflection from being stripped. Test Environment only.</summary>
        public const string LinkXml = RuntimeFolder + "/link.xml";

        static bool Same(string a, string b) => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        // ---------------------------------------------------------------- state

        public static bool DefineOn(DeepTestSettings s) => Defines().Contains(s.define);
        public static bool JsonExists => File.Exists(RuntimeJson);
        public static bool PanelInstalled => File.Exists(PanelScript);

        /// <summary>The panel's source shipped in the package. It uses the ad SDKs' types, so it must compile in the game's
        /// own assembly: Apply Test Environment copies it into <see cref="RuntimeFolder"/>.</summary>
        static string PanelTemplate
        {
            get
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GenZDeepTestSuit).Assembly);
                return package == null ? null : package.resolvedPath + "/Editor/Templates/GenZDeepTestPanel.cs.txt";
            }
        }

        /// <summary>The panel is up to date when its first line (the release comment) matches the package's.</summary>
        public static bool PanelCurrent
        {
            get
            {
                var template = PanelTemplate;
                if (template == null || !File.Exists(template)) return PanelInstalled;
                return PanelInstalled && File.ReadLines(PanelScript).FirstOrDefault() == File.ReadLines(template).FirstOrDefault();
            }
        }

        /// <summary>Copies the package's panel into the game when it's missing or from another release. Returns what it did, or null.</summary>
        static string InstallPanel()
        {
            var template = PanelTemplate;
            if (template == null || !File.Exists(template) || PanelCurrent) return null;
            bool update = PanelInstalled;
            Directory.CreateDirectory(RuntimeFolder);
            File.Copy(template, PanelScript, true);
            AssetDatabase.ImportAsset(PanelScript);
            return (update ? "Updated " : "Installed ") + PanelScript + " from the package";
        }

        static List<string> Defines() =>
            PlayerSettings.GetScriptingDefineSymbols(Android).Split(';').Select(d => d.Trim()).Where(d => d.Length > 0).ToList();

        /// <summary>Every setting's current value. Opens the SDK scenes it needs and closes them again.</summary>
        public static List<Item> Read(GenZTestSuitConfig cfg) => WithTargets(cfg, false, null);

        public static Env State(DeepTestSettings s, List<Item> items)
        {
            var used = items.Where(i => i.enabled && i.error == null).ToList();
            bool define = DefineOn(s);
            if (define && JsonExists && used.All(i => i.IsTest)) return Env.Test;
            if (!define && !JsonExists && used.All(i => i.IsRelease)) return Env.Release;
            return Env.Mixed;
        }

        /// <summary>Result of applying an environment.</summary>
        public class Result
        {
            public readonly List<string> applied = new List<string>(), failed = new List<string>(), unsavedScenes = new List<string>();
        }

        /// <summary>Writes the Test or Release value of every enabled setting, then the runtime file and the define.</summary>
        public static Result Apply(GenZTestSuitConfig cfg, bool test)
        {
            var result = new Result();
            WithTargets(cfg, true, test ? (bool?)true : false, result);
            var s = cfg.deepTest;

            if (test)
            {
                var installed = InstallPanel();
                if (installed != null) result.applied.Add(installed);
                if (!PanelInstalled) result.failed.Add($"Phone panel: {PanelScript} is missing and the package's copy wasn't found. Reinstall the GenZ Test Suit package.");
                var missing = WriteRuntimeJson(cfg);
                result.applied.Add($"Phone panel settings → {RuntimeJson} and {LinkXml}");
                foreach (var m in missing) result.failed.Add(m);
            }
            else if (DeleteRuntimeJson())
                result.applied.Add($"Removed {RuntimeJson} and {LinkXml}");

            // Last: changing the define recompiles the scripts.
            var defines = Defines();
            if (test && !defines.Contains(s.define))
            {
                defines.Add(s.define);
                PlayerSettings.SetScriptingDefineSymbols(Android, string.Join(";", defines));
                result.applied.Add($"Scripting define {s.define} added (Android)");
            }
            else if (!test && defines.Remove(s.define))
            {
                PlayerSettings.SetScriptingDefineSymbols(Android, string.Join(";", defines));
                result.applied.Add($"Scripting define {s.define} removed (Android)");
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        // Reads every setting; with write = true also sets it to the Test (true) or Release (false) value.
        static List<Item> WithTargets(GenZTestSuitConfig cfg, bool write, bool? test, Result result = null)
        {
            var items = new List<Item>();
            var opened = new List<Scene>();
            try
            {
                var scenes = GenZEditorTestSuit.OpenCandidateScenes(cfg, new ReportSection("Environment", TestStage.Ids), opened);
                var ads = GenZEditorTestSuit.FindAdsObject(cfg, new ReportSection("Environment", TestStage.Ids), scenes);
                var dirtyScenes = new HashSet<Scene>();

                foreach (var setting in cfg.deepTest.settings)
                {
                    var item = new Item
                    {
                        label = setting.label, note = setting.note, enabled = setting.enabled,
                        test = setting.testValue, release = setting.releaseValue
                    };
                    items.Add(item);

                    var rule = cfg.rules.FirstOrDefault(r => string.Equals(r.label, setting.rule, StringComparison.OrdinalIgnoreCase));
                    if (rule == null) { item.error = $"No config rule named \"{setting.rule}\""; continue; }
                    var target = GenZEditorTestSuit.ResolveRule(rule, scenes, ads);
                    if (target == null) { item.error = $"{rule.label} not found in the project"; continue; }
                    item.where = Where(target);

                    var so = new SerializedObject(target);
                    var prop = GenZEditorTestSuit.FindProperty(so, setting.fieldNames);
                    if (prop == null) { item.error = "Field not found. Tried: " + string.Join(", ", setting.fieldNames); continue; }
                    item.current = GenZEditorTestSuit.DisplayValue(prop);

                    if (!write || !setting.enabled) continue;
                    var want = test == true ? setting.testValue : setting.releaseValue;
                    if (Same(item.current, want)) continue;
                    if (!SetValue(prop, want, out var why)) { result.failed.Add($"{setting.label}: {why}"); continue; }
                    so.ApplyModifiedProperties();
                    // A prefab instance keeps the value as an override on the scene's object (where the SDK reads it).
                    if (target is Component comp)
                    {
                        if (PrefabUtility.IsPartOfPrefabInstance(comp)) PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
                        dirtyScenes.Add(comp.gameObject.scene);
                    }
                    else EditorUtility.SetDirty(target);
                    result.applied.Add($"{setting.label}: {item.current} → {want}  ({item.where})");
                    item.current = want;
                }

                foreach (var scene in dirtyScenes)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (opened.Contains(scene)) EditorSceneManager.SaveScene(scene);
                    else result.unsavedScenes.Add(scene.path);
                }
            }
            finally
            {
                foreach (var sc in opened)
                    if (sc.IsValid() && sc.isLoaded) EditorSceneManager.CloseScene(sc, true);
            }
            return items;
        }

        static string Where(Object target) => target is Component c
            ? $"{c.gameObject.scene.name} › {GenZEditorTestSuit.HierarchyPath(c.transform)}"
            : AssetDatabase.GetAssetPath(target);

        static bool SetValue(SerializedProperty p, string value, out string why)
        {
            why = null;
            value = (value ?? "").Trim();
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    if (!bool.TryParse(value, out var b)) { why = $"\"{value}\" isn't true/false"; return false; }
                    p.boolValue = b;
                    return true;
                case SerializedPropertyType.Enum:
                    int i = Array.FindIndex(p.enumNames, n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase));
                    if (i < 0) { why = $"\"{value}\" isn't one of {string.Join(", ", p.enumNames)}"; return false; }
                    p.enumValueIndex = i;
                    return true;
                case SerializedPropertyType.Integer:
                    if (!int.TryParse(value, out var n)) { why = $"\"{value}\" isn't a whole number"; return false; }
                    p.intValue = n;
                    return true;
                case SerializedPropertyType.Float:
                    if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) { why = $"\"{value}\" isn't a number"; return false; }
                    p.floatValue = f;
                    return true;
                case SerializedPropertyType.String:
                    p.stringValue = value;
                    return true;
                default:
                    why = $"{p.propertyType} fields can't be set";
                    return false;
            }
        }

        // ---------------------------------------------------------------- the phone panel's file

        /// <summary>
        /// Writes the panel's rows (which game methods each button calls) and switches, and a link.xml keeping those
        /// classes in the build. Returns every 'Class.Member' the game's scripts don't have.
        /// </summary>
        public static List<string> WriteRuntimeJson(GenZTestSuitConfig cfg)
        {
            var s = cfg.deepTest;
            var data = new RuntimeSettings
            {
                maxVerbose = s.maxVerboseLogging,
                creativeDebugger = s.maxCreativeDebugger,
                admobTestDevice = s.admobTestDevice,
                shakeThreshold = s.shakeThreshold,
                actions = s.actions,
                watch = s.watch ?? new string[0]
            };
            Directory.CreateDirectory(Path.GetDirectoryName(RuntimeJson));
            File.WriteAllText(RuntimeJson, JsonUtility.ToJson(data, true));

            var problems = new List<string>();
            var types = new Dictionary<string, Type>();
            foreach (var (where, spec) in References(s))
            {
                var member = spec.Split('=')[0].Trim();
                int dot = member.LastIndexOf('.');
                var type = dot > 0 ? GameType(member.Substring(0, dot)) : null;
                if (type == null) { problems.Add($"{where}: no class \"{(dot > 0 ? member.Substring(0, dot) : member)}\" in the game's scripts"); continue; }
                types[type.FullName] = type;
                var name = member.Substring(dot + 1);
                if (type.GetMember(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Length == 0)
                    problems.Add($"{where}: {type.Name} has no \"{name}\"");
            }

            var linker = new System.Text.StringBuilder("<linker>\n");
            foreach (var asm in types.Values.GroupBy(t => t.Assembly.GetName().Name))
            {
                linker.Append($"  <assembly fullname=\"{asm.Key}\">\n");
                foreach (var t in asm) linker.Append($"    <type fullname=\"{t.FullName}\" preserve=\"all\"/>\n");
                linker.Append("  </assembly>\n");
            }
            File.WriteAllText(LinkXml, linker.Append("</linker>\n").ToString());
            AssetDatabase.ImportAsset(RuntimeJson);
            AssetDatabase.ImportAsset(LinkXml);
            return problems;
        }

        static bool DeleteRuntimeJson()
        {
            bool any = JsonExists && AssetDatabase.DeleteAsset(RuntimeJson);
            return (File.Exists(LinkXml) && AssetDatabase.DeleteAsset(LinkXml)) || any;
        }

        /// <summary>Every 'Class.Member' the panel will call or read, with the row it belongs to.</summary>
        static IEnumerable<(string where, string spec)> References(DeepTestSettings s)
        {
            foreach (var a in s.actions)
                foreach (var spec in new[] { a.load, a.ready, a.show, a.hide, a.rewardField }.Concat(Split(a.before)).Concat(Split(a.showFields)))
                    if (!string.IsNullOrWhiteSpace(spec)) yield return (a.name, spec.Trim());
            foreach (var w in s.watch ?? new string[0])
                if (!string.IsNullOrWhiteSpace(w)) yield return ("Watch", w.Trim());
        }

        static IEnumerable<string> Split(string list) => (list ?? "").Split(';').Where(x => x.Trim().Length > 0);

        /// <summary>A game (non-editor) class by name, as the panel finds it on the phone.</summary>
        static Type GameType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var n = asm.GetName().Name;
                if (n.StartsWith("UnityEditor") || n.EndsWith(".Editor") || n == "Assembly-CSharp-Editor" || n == "Assembly-CSharp-Editor-firstpass") continue;
                Type[] all;
                try { all = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { all = e.Types.Where(t => t != null).ToArray(); }
                var t = all.FirstOrDefault(x => x.Name == name || x.FullName == name);
                if (t != null) return t;
            }
            return null;
        }

        // ---------------------------------------------------------------- release guard (Editor suite, step 2)

        /// <summary>A release must not carry the Test Environment: the define, the panel's file, or a test value.</summary>
        public static void CheckRelease(TestReport report, GenZTestSuitConfig cfg)
        {
            var s = cfg.deepTest ?? new DeepTestSettings();
            var sec = report.Section("Release Environment (GenZ-DeepTestSuit)", TestStage.Integration);
            bool define = DefineOn(s);
            sec.Add("Test define off", define ? CheckStatus.Fail : CheckStatus.Pass, define ? s.define + " is set" : "not set",
                define ? "The build would include the phone's test panel. Test Suit → GenZ-DeepTestSuit → Apply Release Environment." : "OK");
            sec.Add("Test panel settings removed", JsonExists ? CheckStatus.Fail : CheckStatus.Pass, JsonExists ? RuntimeJson : "none",
                JsonExists ? "Left from the Test Environment. Apply Release Environment removes it." : "OK");
        }

        // ---------------------------------------------------------------- phone session

        public static string LogFolder => Path.Combine(GenZApkTestSuit.ProjectRoot, "Logs", SuiteName);

        /// <summary>
        /// Installs the APK (when <paramref name="apkPath"/> is given; null = the game already on the phone), clears
        /// the log, turns on Firebase DebugView and cold-starts the game. No release-build checks: a Test Environment
        /// build isn't a release build. Returns false with the reason in <paramref name="error"/>.
        /// </summary>
        public static bool StartSession(string apkPath, string package, string serial, bool fresh, out ApkInfo apk, out string error)
        {
            apk = null;
            error = null;
            try
            {
                if (!string.IsNullOrEmpty(apkPath))
                {
                    Progress("Reading the APK", 0.05f);
                    var aapt = GenZApkTestSuit.BuildTool("aapt2");
                    apk = ApkInfo.FromBadging(apkPath, aapt == null ? "" : GenZApkTestSuit.Run(aapt, $"dump badging \"{apkPath}\"").output);
                    if (string.IsNullOrEmpty(apk.package)) { error = "aapt2 couldn't read the APK (not an APK, or the Android build-tools aren't installed)."; return false; }
                    if (fresh)
                    {
                        Progress("Uninstalling the old version", 0.2f);
                        GenZApkTestSuit.AdbRun(serial, "uninstall " + apk.package, 60000);
                    }
                    Progress("Installing " + Path.GetFileName(apkPath), 0.35f);
                    var install = GenZApkTestSuit.AdbRun(serial, $"install -r -d \"{apkPath}\"", 300000);
                    if (!install.All.Contains("Success")) { error = "Install failed: " + install.All; return false; }
                }
                else
                {
                    Progress("Reading the installed game", 0.1f);
                    apk = ApkInfo.FromDumpsys(package, GenZApkTestSuit.AdbRun(serial, "shell dumpsys package " + package).output);
                    if (apk == null) { error = $"{package} isn't installed on the phone. Install it, or pick an APK."; return false; }
                    if (fresh)
                    {
                        Progress("Clearing the game's data", 0.25f);
                        GenZApkTestSuit.AdbRun(serial, "shell pm clear " + apk.package, 60000);
                    }
                }
                if (string.IsNullOrEmpty(apk.launchActivity)) apk.launchActivity = GenZApkTestSuit.LaunchActivity(serial, apk.package);

                Progress("Preparing the log", 0.8f);
                GenZApkTestSuit.AdbRun(serial, "logcat -G 16M");
                GenZApkTestSuit.AdbRun(serial, "logcat -c");
                FirebaseDebug(serial, apk.package, true);

                Progress("Launching the game", 0.9f);
                GenZApkTestSuit.AdbRun(serial, "shell am force-stop " + apk.package);
                var launch = string.IsNullOrEmpty(apk.launchActivity)
                    ? GenZApkTestSuit.AdbRun(serial, $"shell monkey -p {apk.package} -c android.intent.category.LAUNCHER 1")
                    : GenZApkTestSuit.AdbRun(serial, $"shell am start -W -n {apk.package}/{apk.launchActivity}");
                if (!launch.Ok) { error = "Couldn't launch the game: " + launch.All; return false; }
                return true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void Progress(string info, float p) => EditorUtility.DisplayProgressBar(SuiteName, info, p);

        /// <summary>The Unity log while the session runs ([GenZ] lines, exceptions). Safe off the main thread.</summary>
        public static string ReadLiveLog(string serial) =>
            GenZApkTestSuit.AdbRun(serial, "logcat -d -v threadtime -s Unity AndroidRuntime", 30000).output;

        /// <summary>What the live view shows, read from the log.</summary>
        public class LiveState
        {
            public bool testBuild;                       // "[GenZ] DeepTest|build" seen
            public string buildDetail = "", admobTestDevice = "";
            public readonly List<GenZLine> calls = new List<GenZLine>();   // the panel's button results, newest last
            public readonly List<GenZLine> inits = new List<GenZLine>();   // "[GenZ] <sdk>|init|…"
            public readonly List<string> exceptions = new List<string>();  // Unity exceptions and crashes
        }

        public static LiveState ParseLive(string raw)
        {
            var log = GenZApkChecks.ParseLogcat((raw ?? "").Split('\n'));
            var state = new LiveState();
            foreach (var g in GenZApkChecks.GenZLines(log))
            {
                if (g.sdk == "DeepTest")
                {
                    if (g.what == "build") { state.testBuild = !g.Failed; state.buildDetail = g.detail; } // FAIL = define without the panel's file
                    else if (g.what == "AdMobTestDevice") state.admobTestDevice = (g.Ok ? "on" : "FAILED") + (g.Ok ? "" : ": " + g.detail);
                    else if (g.what != "panel") state.calls.Add(g);
                }
                else if (g.what == "init") state.inits.Add(g);
            }
            foreach (var l in log)
                if ((l.level == 'E' || l.level == 'F') && (l.msg.Contains("Exception") || l.tag == "AndroidRuntime"))
                    state.exceptions.Add($"{l.time}  {l.msg.Trim()}");
            return state;
        }

        /// <summary>The whole log at the end, saved under Logs/GenZ-DeepTestSuit; Firebase DebugView is turned off.</summary>
        public static string FinishSession(string serial, ApkInfo apk)
        {
            try
            {
                EditorUtility.DisplayProgressBar(SuiteName, "Reading the phone's log", 0.5f);
                var raw = GenZApkTestSuit.ReadFullLog(serial);
                FirebaseDebug(serial, apk.package, false);
                return GenZApkTestSuit.SaveLog(raw, apk, LogFolder);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        /// <summary>Firebase DebugView for the game on the phone (the same as GenZ → Firebase → StartADB).</summary>
        public static string FirebaseDebug(string serial, string package, bool on)
        {
            var r = GenZApkTestSuit.AdbRun(serial, "shell setprop debug.firebase.analytics.app " + (on ? package : ".none."), 15000);
            if (on)
            {
                GenZApkTestSuit.AdbRun(serial, "shell setprop log.tag.FA VERBOSE", 15000);
                GenZApkTestSuit.AdbRun(serial, "shell setprop log.tag.FA-SVC VERBOSE", 15000);
            }
            return r.exitCode == 0 ? null : r.All;
        }
    }

    /// <summary>Warns in the build log when a build is made with the Test Environment, and rewrites the panel's settings from the config.</summary>
    class GenZDeepTestBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            var cfg = GenZTestSuitConfig.LoadOrCreate();
            if (GenZDeepTestSuit.DefineOn(cfg.deepTest))
            {
                foreach (var m in GenZDeepTestSuit.WriteRuntimeJson(cfg)) Debug.LogWarning($"[{GenZDeepTestSuit.SuiteName}] {m}");
                Debug.LogWarning($"[{GenZDeepTestSuit.SuiteName}] TEST build: {cfg.deepTest.define} is set, so this build has the shake test panel, " +
                                 "verbose SDK logs and AdMob test ads. Don't upload it. Apply Release Environment before the release build.");
            }
            else if (GenZDeepTestSuit.JsonExists)
                Debug.LogWarning($"[{GenZDeepTestSuit.SuiteName}] {GenZDeepTestSuit.RuntimeJson} is left from the Test Environment and will be in this build. Apply Release Environment to remove it.");
        }
    }
}
