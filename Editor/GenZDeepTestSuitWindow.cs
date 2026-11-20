// GenZ-TestSuit-v1.0.1
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GenZ.TestSuit
{
    /// <summary>The GenZ-DeepTestSuit tab: Test / Release Environment buttons, every setting's state, Firebase DebugView on the phone.</summary>
    public partial class GenZTestSuitWindow
    {
        [System.NonSerialized] List<GenZDeepTestSuit.Item> deepItems; // read from the project; opening scenes, so not every repaint
        [SerializeField] bool showDeepPanel = true;

        void DrawDeepSuite()
        {
            if (deepItems == null)
            {
                deepItems = new List<GenZDeepTestSuit.Item>(); // read once, outside OnGUI (it opens scenes)
                EditorApplication.delayCall += RefreshDeepState;
            }
            if (devices == null) RefreshDevicesNow();
            if (string.IsNullOrEmpty(apkPath)) apkPath = GenZApkTestSuit.NewestApk();
            var s = config.deepTest;

            DrawEnvironmentBanner(s);
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
                {
                    if (EnvButton("Apply Test Environment", WarnColor, "Turn on the SDK logs, Adjust sandbox, AdMob test ads on your phone and the shake test panel. Shows every change and asks first."))
                        EditorApplication.delayCall += () => ApplyEnvironment(true);
                    if (EnvButton("Apply Release Environment", PassColor, "Put every setting back to its release value and take the test panel out of the build. Shows every change and asks first."))
                        EditorApplication.delayCall += () => ApplyEnvironment(false);
                }
            }
            EditorGUILayout.Space(4);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawDeepSettings(s);
            EditorGUILayout.Space(4);
            DrawDeepPanelSettings();
            EditorGUILayout.Space(4);
            DrawDeepPhone();
            EditorGUILayout.HelpBox("1. Apply Test Environment.  2. Build the APK (File › Build Settings).  3. Install & Start Test (or Start Test on the installed game): "
                + "it installs, turns on Firebase DebugView and opens the game.  4. Shake the phone to open the test panel; each button calls your ad scripts, and the results show here.  "
                + "5. Stop & Save Log.  6. Apply Release Environment before the release build. The Editor and APK suites fail while the Test Environment is on.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        void RefreshDeepState()
        {
            if (config == null) LoadConfig();
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            deepItems = GenZDeepTestSuit.Read(config);
            Repaint();
        }

        void DrawEnvironmentBanner(DeepTestSettings s)
        {
            var state = GenZDeepTestSuit.State(s, deepItems);
            var (text, color, detail) =
                deepItems.Count == 0 ? ("…", InfoColor, "Reading the project.")
                : state == GenZDeepTestSuit.Env.Test ? ("TEST ENVIRONMENT", WarnColor, "Builds made now are test builds: don't upload them.")
                : state == GenZDeepTestSuit.Env.Release ? ("RELEASE ENVIRONMENT", PassColor, "Every setting has its release value.")
                : ("MIXED", FailColor, "Some settings are test, some release: apply one of the environments.");
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label($"<b>Now:</b>  <color={Hex(color)}><b>{text}</b></color>   <size=11>{detail}</size>", filterLabelStyle, GUILayout.Height(26));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Refresh", "Read the settings from the project again"), GUILayout.Width(70), GUILayout.Height(22)))
                    EditorApplication.delayCall += RefreshDeepState;
            }
        }

        bool EnvButton(string text, Color color, string tooltip)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool clicked = GUILayout.Button(new GUIContent(text, tooltip), GUILayout.Height(36));
            GUI.backgroundColor = prev;
            return clicked;
        }

        // One row per setting: its value now, and the Test / Release values.
        void DrawDeepSettings(DeepTestSettings s)
        {
            GUILayout.Label("Settings", EditorStyles.boldLabel);
            bool define = GenZDeepTestSuit.DefineOn(s);
            DeepRow(define ? "T" : "R", "Scripting define " + s.define, define ? "set" : "not set", "set", "not set",
                "Compiles the phone's test panel in (Android)", null);
            bool json = GenZDeepTestSuit.JsonExists;
            DeepRow(json ? "T" : "R", "Test panel settings", json ? "present" : "none", "present", "none",
                GenZDeepTestSuit.RuntimeJson + ": which game ad methods each panel button calls, and the switches", null);
            if (!GenZDeepTestSuit.PanelCurrent)
                EditorGUILayout.HelpBox(GenZDeepTestSuit.PanelInstalled
                    ? $"{GenZDeepTestSuit.PanelScript} is from another release of the suite. Apply Test Environment updates it from the package."
                    : $"{GenZDeepTestSuit.PanelScript} isn't in the project yet. Apply Test Environment copies it from the package.", MessageType.Info);

            foreach (var i in deepItems)
            {
                string tag = !i.enabled ? "–" : i.error != null ? "!" : i.IsTest && i.IsRelease ? "=" : i.IsTest ? "T" : i.IsRelease ? "R" : "?";
                DeepRow(tag, i.label + (i.enabled ? "" : "  (off)"), i.error != null ? "" : i.current, i.test, i.release, i.note, i.error ?? i.where);
            }
            if (deepItems.Count == 0) EditorGUILayout.LabelField("Reading the project…", EditorStyles.miniLabel);
        }

        void DeepRow(string tag, string label, string current, string test, string release, string note, string where)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var color = tag == "T" ? WarnColor : tag == "R" || tag == "=" ? PassColor : tag == "–" ? Color.gray : FailColor;
                var prev = GUI.contentColor;
                GUI.contentColor = color;
                GUILayout.Label(tag, iconStyle, GUILayout.Width(18));
                GUI.contentColor = prev;
                GUILayout.Label(label, checkLabelStyle, GUILayout.Width(Mathf.Min(230, position.width * 0.35f)));
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label($"<b>{current}</b>   <size=10>Test: {test} · Release: {release}</size>", filterLabelStyle);
                    var detail = string.IsNullOrEmpty(where) ? note : note + "  ·  " + where;
                    if (!string.IsNullOrEmpty(detail)) GUILayout.Label(detail, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        void DrawDeepPanelSettings()
        {
            showDeepPanel = EditorGUILayout.Foldout(showDeepPanel, "Phone test panel (shake to open)", true, headerStyle);
            if (!showDeepPanel) return;
            configSO.Update();
            var p = configSO.FindProperty(nameof(GenZTestSuitConfig.deepTest));
            var labels = new[]
            {
                new GUIContent("MAX Verbose Logging", "MaxSdk.SetVerboseLogging(true) before the game starts MAX"),
                new GUIContent("MAX Creative Debugger", "Two-finger double tap on a showing MAX ad tells which network served it"),
                new GUIContent("AdMob Test Ads On The Phone", "The phone becomes an AdMob test device: real ad unit IDs, Google's test ads"),
                new GUIContent("Shake Strength (g)", "How hard to shake the phone to open or close the panel"),
            };
            // Wide enough for the longest label (plus the indent and a gap), so the checkboxes never cover the text.
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Max(prevLabelWidth, labels.Max(l => EditorStyles.label.CalcSize(l).x) + 15 * (EditorGUI.indentLevel + 1) + 16);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(p.FindPropertyRelative(nameof(DeepTestSettings.maxVerboseLogging)), labels[0]);
                EditorGUILayout.PropertyField(p.FindPropertyRelative(nameof(DeepTestSettings.maxCreativeDebugger)), labels[1]);
                EditorGUILayout.PropertyField(p.FindPropertyRelative(nameof(DeepTestSettings.admobTestDevice)), labels[2]);
                EditorGUILayout.PropertyField(p.FindPropertyRelative(nameof(DeepTestSettings.shakeThreshold)), labels[3]);
                var actions = config.deepTest.actions;
                EditorGUILayout.LabelField(" ", $"Panel rows (each calls your ad scripts' own methods): {string.Join(", ", actions.Select(a => a.name))}", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("Edit Settings…", "Select the config asset: Deep Test → settings, panel rows (Class.Method names) and watched fields"), GUILayout.Width(110)))
                        Selection.activeObject = config;
                    if (GUILayout.Button(new GUIContent("Reset", "Restore the default settings and panel rows"), GUILayout.Width(70))
                        && EditorUtility.DisplayDialog("Reset", "Replace the Deep Test settings and panel rows with the defaults?", "Reset", "Cancel"))
                    {
                        Undo.RecordObject(config, "Reset GenZ Deep Test settings");
                        config.deepTest = new DeepTestSettings();
                        EditorUtility.SetDirty(config);
                        AssetDatabase.SaveAssets();
                        EditorApplication.delayCall += RefreshDeepState;
                    }
                }
            }
            EditorGUIUtility.labelWidth = prevLabelWidth;
            if (configSO.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
            if (GenZDeepTestSuit.DefineOn(config.deepTest))
                EditorGUILayout.LabelField(" ", "Changed these? Apply Test Environment again (or just build: the build writes them).", EditorStyles.wordWrappedMiniLabel);
        }

        // ---------------------------------------------------------------- test on a phone

        // Session state is serialized, so a script reload in the middle of a session keeps it going.
        [SerializeField] bool deepRecording;
        [SerializeField] string deepSerial, deepLogPath;
        [SerializeField] long deepStartTicks;
        [SerializeField] ApkInfo deepApk;
        [System.NonSerialized] GenZDeepTestSuit.LiveState deepLive;
        volatile string deepPolled;
        volatile bool deepPolling;
        double deepNextPoll;

        void DrawDeepPhone()
        {
            GUILayout.Label("Test on a phone", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(deepRecording))
            {
                DrawBuildSource();
                EditorGUILayout.Space(2);
                DrawDeviceList();
            }
            EditorGUILayout.Space(4);

            if (!deepRecording) DrawDeepStart();
            else
            {
                var elapsed = System.TimeSpan.FromTicks(System.DateTime.Now.Ticks - deepStartTicks);
                EditorGUILayout.HelpBox($"● Running {deepApk?.package} {deepApk?.versionName} ({deepApk?.versionCode}) on {devices?.Find(d => d.serial == deepSerial)?.Name ?? deepSerial} · {elapsed:mm\\:ss}\n"
                    + "Shake the phone to open the test panel and press its buttons: each one calls your ad scripts. What happened shows below.", MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("■  Stop & Save Log", GUILayout.Height(30)))
                        EditorApplication.delayCall += FinishDeepSession;
                    if (GUILayout.Button("Cancel", GUILayout.Height(30), GUILayout.Width(80)))
                        EditorApplication.delayCall += CancelDeepSession;
                }
            }

            if (!deepRecording && !string.IsNullOrEmpty(deepLogPath) && System.IO.File.Exists(deepLogPath))
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Last log", System.IO.Path.GetFileName(deepLogPath), EditorStyles.miniLabel);
                    if (GUILayout.Button("Open Log", GUILayout.Width(80))) EditorUtility.OpenWithDefaultApp(deepLogPath);
                }
            if (deepRecording || deepLive != null) DrawDeepLive();

            EditorGUILayout.Space(4);
            DrawFirebaseAdb(GenZApkTestSuit.ProjectPackage, deepRecording);
        }

        /// <summary>
        /// Start ADB / Stop ADB, as in GenZ → Firebase: Firebase DebugView for <paramref name="pkg"/> on the selected phone
        /// (Unity's adb, so the right phone even when several are attached). Used by the APK and Deep tabs.
        /// </summary>
        void DrawFirebaseAdb(string pkg, bool running)
        {
            var device = SelectedDevice;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent("Firebase DebugView", $"For {pkg} on {(device != null ? device.Name : "the selected phone")}. The same as GenZ → Firebase → StartADB / StopADB."),
                    EditorStyles.boldLabel, GUILayout.Width(130));
                using (new EditorGUI.DisabledScope(device == null || !device.Ready))
                {
                    if (GUILayout.Button(new GUIContent("▶  Start ADB", $"Events from {pkg} show in Firebase → DebugView right away. Restart the game afterwards. (A test session turns it on by itself.)"), GUILayout.Height(24)))
                        PhoneFirebase(device.serial, pkg, true);
                    // A running session reads Firebase's debug log: stopping it now would empty its Firebase checks.
                    using (new EditorGUI.DisabledScope(running))
                        if (GUILayout.Button(new GUIContent("■  Stop ADB", running ? "Finish the test first: it needs Firebase's debug log" : "Back to Firebase's normal batched uploads"), GUILayout.Height(24)))
                            PhoneFirebase(device.serial, pkg, false);
                }
            }
            if (device == null || !device.Ready)
                EditorGUILayout.LabelField(" ", "Pick a ready phone in the list above.", EditorStyles.miniLabel);
        }

        void DrawDeepStart()
        {
            var device = SelectedDevice;
            string blocker =
                recording ? "An APK test is running in the GenZ-ApkTestSuit tab: finish it first."
                : useApkFile && (string.IsNullOrEmpty(apkPath) || !System.IO.File.Exists(apkPath)) ? "Build the Test Environment APK first (File › Build Settings › Android), pick one, or choose \"Already installed on the phone\"."
                : device == null ? (devices?.Any(d => d.Ready) == true ? "Pick a phone in the list." : "Connect a phone with a USB cable and USB debugging on.")
                : !device.Ready ? "The selected phone isn't ready."
                : !useApkFile && device.installChecked && device.installed == "" ? $"{GenZApkTestSuit.ProjectPackage} isn't installed on {device.Name}. Install it, or choose \"Install an APK\"."
                : null;
            using (new EditorGUI.DisabledScope(blocker != null))
                if (GUILayout.Button(useApkFile ? "▶  Install & Start Test" : "▶  Start Test on the Installed Game", GUILayout.Height(34)))
                    EditorApplication.delayCall += StartDeepSession; // outside OnGUI: it blocks with a progress bar
            if (blocker != null) EditorGUILayout.HelpBox(blocker, MessageType.Warning);
        }

        void StartDeepSession()
        {
            var device = SelectedDevice;
            if (device == null) return;
            deepLive = null;
            deepLogPath = null;
            if (!GenZDeepTestSuit.StartSession(useApkFile ? apkPath : null, GenZApkTestSuit.ProjectPackage, device.serial, freshInstall, out var apk, out var error))
            {
                EditorUtility.DisplayDialog(GenZDeepTestSuit.SuiteName, error, "OK");
                return;
            }
            deepApk = apk;
            deepSerial = device.serial;
            deepStartTicks = System.DateTime.Now.Ticks;
            deepRecording = true;
            deepNextPoll = EditorApplication.timeSinceStartup + 2;
            nextDeviceRefresh = 0; // show the newly installed version
            Repaint();
        }

        void FinishDeepSession()
        {
            if (!deepRecording) return;
            deepRecording = false;
            deepLogPath = GenZDeepTestSuit.FinishSession(deepSerial, deepApk);
            if (deepLogPath != null)
            {
                deepLive = GenZDeepTestSuit.ParseLive(System.IO.File.ReadAllText(deepLogPath));
                Debug.Log($"[{GenZDeepTestSuit.SuiteName}] Log saved: {deepLogPath}");
            }
            Repaint();
        }

        void CancelDeepSession()
        {
            if (!deepRecording) return;
            deepRecording = false;
            GenZDeepTestSuit.FirebaseDebug(deepSerial, deepApk.package, false);
            Repaint();
        }

        // Called from the window's update: reads the phone's log in the background every few seconds.
        void DeepUpdate(double now)
        {
            var polled = deepPolled;
            if (polled != null)
            {
                deepPolled = null;
                if (deepRecording) deepLive = GenZDeepTestSuit.ParseLive(polled);
                Repaint();
            }
            if (!deepRecording) return;
            if (tab == 2 && now >= nextRepaint) { nextRepaint = now + 1; Repaint(); } // the timer
            if (deepPolling || now < deepNextPoll) return;
            deepNextPoll = now + PollSeconds;
            deepPolling = true;
            string serial = deepSerial;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { deepPolled = GenZDeepTestSuit.ReadLiveLog(serial); }
                finally { deepPolling = false; }
            });
        }

        // What the phone did: test build detected, the panel's button results, SDK starts, exceptions.
        void DrawDeepLive()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (deepLive == null) { GUILayout.Label("Reading the phone's log…", EditorStyles.miniLabel); return; }
                bool waited = System.DateTime.Now.Ticks - deepStartTicks > System.TimeSpan.TicksPerSecond * 20;
                GUILayout.Label(deepLive.testBuild
                    ? $"<color={Hex(PassColor)}>✔ Test Environment build</color>  ·  {deepLive.buildDetail}"
                      + (deepLive.admobTestDevice.Length > 0 ? $"  ·  AdMob test ads {deepLive.admobTestDevice}" : "")
                    : deepRecording && !waited ? "Waiting for the game to start…"
                    : $"<color={Hex(FailColor)}>✘ No test panel in this build</color>  ·  Apply Test Environment, rebuild and install it", filterLabelStyle);

                if (deepLive.inits.Count > 0)
                    GUILayout.Label("SDKs started: " + string.Join("   ", deepLive.inits.GroupBy(g => g.sdk).Select(g =>
                    {
                        var last = g.Last();
                        return $"<color={Hex(last.Ok ? PassColor : last.Failed ? FailColor : InfoColor)}>{(last.Ok ? "✔" : last.Failed ? "✘" : "•")} {g.Key}</color>";
                    })), filterLabelStyle);

                GUILayout.Label($"<b>Panel buttons</b>  ({deepLive.calls.Count})", filterLabelStyle);
                if (deepLive.calls.Count == 0) GUILayout.Label("None pressed yet. Shake the phone to open the panel.", EditorStyles.miniLabel);
                foreach (var c in Enumerable.Reverse(deepLive.calls).Take(25))
                    GUILayout.Label($"<color={Hex(c.Failed ? FailColor : c.Ok ? PassColor : InfoColor)}>{(c.Failed ? "✘" : c.Ok ? "✔" : "•")}</color>  {Time(c.time)}  <b>{c.what}</b>  {c.detail}", filterLabelStyle);

                if (deepLive.exceptions.Count > 0)
                {
                    GUILayout.Label($"<color={Hex(FailColor)}><b>Exceptions and crashes ({deepLive.exceptions.Count})</b></color>", filterLabelStyle);
                    foreach (var e in Enumerable.Reverse(deepLive.exceptions).Take(10))
                        GUILayout.Label(e, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        static string Time(string logTime) => logTime != null && logTime.Length > 6 ? logTime.Substring(6) : logTime; // "MM-DD hh:mm:ss.mmm" → time

        void PhoneFirebase(string serial, string pkg, bool on)
        {
            var error = GenZDeepTestSuit.FirebaseDebug(serial, pkg, on);
            if (error != null) Debug.LogWarning($"[{GenZDeepTestSuit.SuiteName}] adb failed: {error}");
            if (error == null) Debug.Log($"[GenZ Test Suit] Firebase DebugView {(on ? "on for " + pkg : "off")} ({serial})");
            ShowNotification(new GUIContent(error != null ? "adb failed (see Console)" : on ? "Start ADB: Firebase DebugView on. Restart the game." : "Stop ADB: Firebase DebugView off"));
        }

        void ApplyEnvironment(bool test)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(GenZDeepTestSuit.SuiteName, "Stop Play mode first.", "OK");
                return;
            }
            if (config == null) LoadConfig();
            var s = config.deepTest;
            deepItems = GenZDeepTestSuit.Read(config);

            var changes = deepItems.Where(i => i.enabled && i.error == null && !(test ? i.IsTest : i.IsRelease))
                                   .Select(i => $"• {i.label}: {i.current} → {(test ? i.test : i.release)}").ToList();
            bool define = GenZDeepTestSuit.DefineOn(s);
            if (test)
            {
                changes.Add($"• Test panel settings: write {GenZDeepTestSuit.RuntimeJson} (which of your ad methods each button calls) and a link.xml");
                if (!define) changes.Add($"• Scripting define {s.define}: add (Android). The scripts recompile.");
            }
            else
            {
                if (GenZDeepTestSuit.JsonExists) changes.Add($"• Test panel settings: delete {GenZDeepTestSuit.RuntimeJson}");
                if (define) changes.Add($"• Scripting define {s.define}: remove. The scripts recompile.");
            }
            var problems = deepItems.Where(i => i.enabled && i.error != null).Select(i => $"• {i.label}: {i.error}").ToList();

            string name = test ? "Apply Test Environment" : "Apply Release Environment";
            if (changes.Count == 0)
            {
                EditorUtility.DisplayDialog(name, "Everything already has its " + (test ? "test" : "release") + " value." + (problems.Count > 0 ? "\n\nNot found:\n" + string.Join("\n", problems) : ""), "OK");
                return;
            }
            var msg = string.Join("\n", changes)
                      + (problems.Count > 0 ? "\n\nNot found, so left as they are:\n" + string.Join("\n", problems) : "")
                      + "\n\nSDK scenes that aren't open are saved. A scene you have open is only marked as modified: save it yourself.";
            if (!EditorUtility.DisplayDialog(name, msg, "Apply", "Cancel")) return;

            var result = GenZDeepTestSuit.Apply(config, test);
            foreach (var a in result.applied) Debug.Log($"[{GenZDeepTestSuit.SuiteName}] {a}");
            foreach (var f in result.failed) Debug.LogWarning($"[{GenZDeepTestSuit.SuiteName}] {f}");
            deepItems = GenZDeepTestSuit.Read(config);

            var summary = $"{result.applied.Count} change{(result.applied.Count == 1 ? "" : "s")} applied.";
            if (result.failed.Count > 0) summary += "\n\nProblems:\n" + string.Join("\n", result.failed.Take(10));
            if (result.unsavedScenes.Count > 0) summary += "\n\nSave these open scenes to keep the change:\n" + string.Join("\n", result.unsavedScenes);
            summary += test ? "\n\nNow build the APK and install it. Shake the phone to open the test panel."
                            : "\n\nRun the Editor Test Suit before the release build.";
            EditorUtility.DisplayDialog(name, summary, "OK");
            Repaint();
        }
    }
}
