// GenZ-TestSuit-v1.0
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GenZ.TestSuit
{
    public partial class GenZTestSuitWindow : EditorWindow
    {
        static readonly string[] Tabs = { "GenZ-EditorTestSuit", "GenZ-ApkTestSuit", "GenZ-DeepTestSuit" };
        static readonly Color PassColor = new Color(0.30f, 0.75f, 0.35f);
        static readonly Color FailColor = new Color(0.90f, 0.30f, 0.28f);
        static readonly Color WarnColor = new Color(0.95f, 0.70f, 0.20f);
        static readonly Color InfoColor = new Color(0.55f, 0.65f, 0.80f);

        GenZTestSuitConfig config;
        SerializedObject configSO;
        [System.NonSerialized] TestReport editorReport, apkReport;
        TestReport report => tab == 1 ? apkReport : editorReport; // the one on screen
        [SerializeField] int tab;
        [SerializeField] bool showSettings = true;
        [SerializeField] Vector2 scroll;
        readonly HashSet<string> collapsed = new HashSet<string>();         // "All" tab
        readonly HashSet<string> collapsedFiltered = new HashSet<string>(); // Passed / Failed / Warnings tabs

        // Editing a script reloads the domain and clears everything not serialized. The last run and its view
        // state are copied here after each run and before a reload, and read back in OnEnable, so the results
        // stay until the next run.
        [SerializeField] string savedReport;
        [SerializeField] List<string> savedCollapsed = new List<string>(), savedCollapsedFiltered = new List<string>();
        [SerializeField] int savedStepFilter = -1;
        [SerializeField] string sdkScenePath, adsObjectPath; // GenZEditorTestSuit.Last* are statics, also cleared
        [SerializeField] bool restored;                     // results are from before a reload: say so
        GUIStyle iconStyle, wrapStyle, headerStyle, filterStyle, filterLabelStyle, pillStyle, checkLabelStyle,
            stepNumberStyle, stepLabelStyle, stepCountStyle, accentButtonStyle;

        // Width of the check-name column; drag the divider next to it to resize. Remembered per user.
        const string LabelWidthPref = "GenZ.TestSuit.LabelColumnWidth";
        const float SplitterWidth = 8, OpenButtonWidth = 50;
        float labelColWidth;
        bool draggingSplitter;

        // Result filter tabs. Info checks only show under All.
        enum ResultFilter { All, Passed, Failed, Warnings }
        [SerializeField] ResultFilter filter;
        TestStage? stepFilter; // clicked step in the step bar; null = all steps

        [MenuItem("GenZ/Test Suit", priority = 100)]
        public static void Open()
        {
            var w = GetWindow<GenZTestSuitWindow>(GenZTestSuitInfo.Name);
            w.minSize = new Vector2(560, 420);
        }

        [MenuItem("GenZ/Run Editor Test Suit", priority = 101)]
        static void RunFromMenu()
        {
            Open();
            GetWindow<GenZTestSuitWindow>().RunEditorSuite();
        }

        void OnEnable()
        {
            titleContent = new GUIContent(GenZTestSuitInfo.Name); // also renames a window opened by an older version
            wantsMouseMove = true; // hover feedback on the Auto Apply button
            LoadConfig();
            labelColWidth = EditorPrefs.GetFloat(LabelWidthPref, 230);
            RestoreReport();
            AssemblyReloadEvents.beforeAssemblyReload += SaveReport;
            EditorApplication.update += ApkUpdate;
        }

        void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= SaveReport;
            EditorApplication.update -= ApkUpdate;
            SaveReport();
        }

        void SaveReport()
        {
            savedReport = editorReport == null ? null : JsonUtility.ToJson(editorReport);
            savedApkReport = apkReport == null ? null : JsonUtility.ToJson(apkReport);
            savedApkBuild = apkBuild == null ? null : JsonUtility.ToJson(apkBuild);
            savedCollapsed = collapsed.ToList();
            savedCollapsedFiltered = collapsedFiltered.ToList();
            savedStepFilter = stepFilter == null ? -1 : (int)stepFilter.Value;
        }

        void RestoreReport()
        {
            if (apkReport == null && !string.IsNullOrEmpty(savedApkReport))
            {
                try
                {
                    apkReport = JsonUtility.FromJson<TestReport>(savedApkReport);
                    if (!string.IsNullOrEmpty(savedApkBuild)) apkBuild = JsonUtility.FromJson<TestReport>(savedApkBuild);
                }
                catch (System.Exception e) { Debug.LogWarning($"[{GenZApkTestSuit.SuiteName}] Couldn't restore the last results: {e.Message}"); }
            }

            if (editorReport != null || string.IsNullOrEmpty(savedReport)) return;
            try { editorReport = JsonUtility.FromJson<TestReport>(savedReport); }
            catch (System.Exception e) { Debug.LogWarning($"[{GenZEditorTestSuit.SuiteName}] Couldn't restore the last results: {e.Message}"); }
            if (editorReport == null) { savedReport = null; return; }
            collapsed.Clear(); collapsed.UnionWith(savedCollapsed ?? new List<string>());
            collapsedFiltered.Clear(); collapsedFiltered.UnionWith(savedCollapsedFiltered ?? new List<string>());
            stepFilter = savedStepFilter < 0 ? (TestStage?)null : (TestStage)savedStepFilter;
            restored = true;
        }

        void LoadConfig()
        {
            config = GenZTestSuitConfig.LoadOrCreate();
            configSO = new SerializedObject(config);
        }

        void InitStyles()
        {
            if (iconStyle != null) return;
            iconStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            wrapStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
            headerStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            filterStyle = new GUIStyle(EditorStyles.miniButton) { fixedHeight = 0 };
            filterLabelStyle = new GUIStyle(EditorStyles.label) { richText = true, alignment = TextAnchor.MiddleLeft, fontSize = 12 };
            pillStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11, padding = new RectOffset() };
            checkLabelStyle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
            accentButtonStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, fontSize = 12, normal = { textColor = Color.white } };
            stepNumberStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 13, padding = new RectOffset() };
            stepLabelStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, richText = true, fontSize = 12 };
            stepCountStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, richText = true };
        }

        void OnGUI()
        {
            InitStyles();
            if (config == null || configSO == null || configSO.targetObject == null) LoadConfig();
            HandleSplitterDrag();
            if (Event.current.type == EventType.MouseMove) Repaint(); // button hover
            if (sheetPackages.Count == 0 && !SessionState.GetBool(SheetLoadedKey, false))
            {
                SessionState.SetBool(SheetLoadedKey, true); // the Game list: once per editor session; "Load Games" reloads
                EditorApplication.delayCall += LoadSheetGames;
            }

            DrawHeader();
            DrawTabs();
            EditorGUILayout.Space(6);

            if (tab == 0) DrawEditorSuite();
            else if (tab == 1) DrawApkSuite();
            else DrawDeepSuite();
        }

        // ---------------------------------------------------------------- header and tabs

        static readonly string[] TabHints = { "Project, IDs & ad code", "Release APK on a phone", "Test build & ad panel" };
        static readonly string[][] TabIcons =
        {
            new[] { "d_UnityEditor.InspectorWindow", "UnityEditor.InspectorWindow" },
            new[] { "BuildSettings.Android.Small", "BuildSettings.Android" },
            new[] { "d_DebuggerAttached", "DebuggerAttached", "d_PlayButton", "PlayButton" },
        };
        GUIStyle titleStyle, versionStyle, taglineStyle, tabTitleStyle, tabHintStyle;
        Texture2D[] tabIcons;

        void InitHeaderStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleCenter, padding = new RectOffset() };
            versionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(), normal = { textColor = Color.white } };
            taglineStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
            tabTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.LowerLeft, padding = new RectOffset(), clipping = TextClipping.Clip };
            tabHintStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, alignment = TextAnchor.UpperLeft, padding = new RectOffset(), clipping = TextClipping.Clip };
        }

        // "GenZ-TestSuit  [v1.0]" centred and large, the tagline under it, and a thin accent line.
        void DrawHeader()
        {
            InitHeaderStyles();
            var rect = GUILayoutUtility.GetRect(0, 58, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;

            var title = new GUIContent("GenZ-TestSuit");
            var version = new GUIContent("v" + GenZTestSuitInfo.Version);
            float titleW = titleStyle.CalcSize(title).x, pillW = versionStyle.CalcSize(version).x + 14, gap = 8;
            float x = rect.center.x - (titleW + gap + pillW) / 2;
            titleStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.93f, 0.93f, 0.93f) : new Color(0.12f, 0.12f, 0.12f);
            titleStyle.Draw(new Rect(x, rect.y + 6, titleW, 30), title, false, false, false, false);

            var pill = new Rect(x + titleW + gap, rect.y + 13, pillW, 18);
            GUI.DrawTexture(pill, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, AccentColor, 0, 9);
            versionStyle.Draw(pill, version, false, false, false, false);

            taglineStyle.Draw(new Rect(rect.x, rect.y + 36, rect.width, 14), new GUIContent("GenZ studio pre-release checks"), false, false, false, false);

            // Accent line, fading out to both sides.
            float lineW = Mathf.Min(320, rect.width * 0.6f), cx = rect.center.x, y = rect.yMax - 3;
            const int Segments = 24;
            for (int i = 0; i < Segments; i++)
            {
                float t = (i + 0.5f) / Segments;
                var c = AccentColor; c.a = 0.9f * (1 - Mathf.Abs(t * 2 - 1));
                EditorGUI.DrawRect(new Rect(cx - lineW / 2 + lineW * i / Segments, y, lineW / Segments + 0.5f, 2), c);
            }
        }

        // Three cards: icon, name and what it checks. The selected one is filled with the accent colour.
        void DrawTabs()
        {
            const float Height = 46, Gap = 6;
            var row = GUILayoutUtility.GetRect(0, Height, GUILayout.ExpandWidth(true));
            row.x += 4; row.width -= 8;
            float w = (row.width - Gap * (Tabs.Length - 1)) / Tabs.Length;

            for (int i = 0; i < Tabs.Length; i++)
            {
                var r = new Rect(row.x + i * (w + Gap), row.y, w, Height);
                bool selected = tab == i, hover = r.Contains(Event.current.mousePosition);
                EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                if (GUI.Button(r, new GUIContent("", TabHints[i]), GUIStyle.none) && !selected)
                {
                    tab = i;
                    stepFilter = null; // the other tab has other steps
                    scroll = Vector2.zero;
                    GUI.FocusControl(null);
                }
                if (Event.current.type != EventType.Repaint) continue;

                bool pro = EditorGUIUtility.isProSkin;
                var idle = pro ? new Color(0.24f, 0.24f, 0.24f) : new Color(0.84f, 0.84f, 0.84f);
                var fill = selected ? AccentColor : hover ? Color.Lerp(idle, AccentColor, 0.18f) : idle;
                fill.a = 1;
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, fill, 0, 7);
                var border = selected ? Color.Lerp(AccentColor, Color.white, 0.35f) : pro ? new Color(1, 1, 1, 0.08f) : new Color(0, 0, 0, 0.15f);
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, border, 1, 7);
                if (selected) // a short bar under the selected card
                    GUI.DrawTexture(new Rect(r.center.x - 18, r.yMax - 4, 36, 3), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Color.white, 0, 1.5f);

                float x = r.x + 12;
                if (tabIcons == null) // first match per tab, looked up once (icon names differ between Unity versions)
                    tabIcons = TabIcons.Select(names => names.Select(EditorGUIUtility.FindTexture).FirstOrDefault(t => t != null)).ToArray();
                var icon = tabIcons[i];
                if (icon != null)
                {
                    var prev = GUI.color;
                    GUI.color = selected ? Color.white : new Color(1, 1, 1, pro ? 0.8f : 0.7f);
                    GUI.DrawTexture(new Rect(x, r.center.y - 10, 20, 20), icon, ScaleMode.ScaleToFit);
                    GUI.color = prev;
                    x += 28;
                }
                var text = selected ? Color.white : pro ? new Color(0.88f, 0.88f, 0.88f) : new Color(0.15f, 0.15f, 0.15f);
                tabTitleStyle.normal.textColor = text;
                tabHintStyle.normal.textColor = selected ? new Color(1, 1, 1, 0.8f) : new Color(text.r, text.g, text.b, 0.6f);
                float textW = r.xMax - x - 8;
                tabTitleStyle.Draw(new Rect(x, r.y + 4, textW, 20), new GUIContent(Tabs[i]), false, false, false, false);
                tabHintStyle.Draw(new Rect(x, r.y + 24, textW, 16), new GUIContent(TabHints[i]), false, false, false, false);
            }
        }

        // ---------------------------------------------------------------- Editor suite tab

        void DrawEditorSuite()
        {
            showSettings = EditorGUILayout.Foldout(showSettings, "Settings", true, headerStyle);
            if (showSettings)
            {
                configSO.Update();
                float prevLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Mathf.Max(prevLabelWidth, 185); // room for "Run Scene & Asset Checks"
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.PropertyField(configSO.FindProperty(nameof(GenZTestSuitConfig.sdkScene)),
                        new GUIContent("SDK Scene", "Scene holding the Ads object. Empty = auto-detect (Consent / Splash)."));
                    EditorGUILayout.PropertyField(configSO.FindProperty(nameof(GenZTestSuitConfig.adsObjectName)),
                        new GUIContent("Ads Object Name", "Case-insensitive. If no object has this name, the object carrying the SDK scripts is auto-detected."));
                    EditorGUILayout.PropertyField(configSO.FindProperty(nameof(GenZTestSuitConfig.googleSheetUrl)),
                        new GUIContent("Google Sheet Link", "Sheet with all games' IDs. Must be viewable by anyone with the link. Empty = studio default sheet."));
                    EditorGUILayout.PropertyField(configSO.FindProperty(nameof(GenZTestSuitConfig.sheetTabs)),
                        new GUIContent("Sheet Tabs", "Tabs to read, by name: their tables are combined (e.g. the games tab and the MAX tab). None found = the tab in the link."), true);
                    EditorGUILayout.PropertyField(configSO.FindProperty(nameof(GenZTestSuitConfig.verifyAgainstSheet)),
                        new GUIContent("Verify Against Sheet"));
                    EditorGUILayout.PropertyField(configSO.FindProperty(nameof(GenZTestSuitConfig.runIdChecks)),
                        new GUIContent("Run Scene & Asset Checks", "Steps 1 and 2: the Ads object's scripts and IDs, the SDK scene objects and the settings assets"));
                    EditorGUILayout.PropertyField(configSO.FindProperty(nameof(GenZTestSuitConfig.runIntegrationChecks)),
                        new GUIContent("Run SDK File Checks", "Step 2: google-services.json, SDKs installed and resolved into Gradle, Gradle/manifest setup, Android Player Settings"));
                    EditorGUILayout.PropertyField(configSO.FindProperty(nameof(GenZTestSuitConfig.codeChecks)).FindPropertyRelative(nameof(CodeCheckSettings.enabled)),
                        new GUIContent("Run Code Checks", "Step 4: ad waterfall, show/hide, reloads, revenue events, AdsGapTimer resets, Remote Config"));
                    EditorGUILayout.PropertyField(configSO.FindProperty(nameof(GenZTestSuitConfig.taichi)).FindPropertyRelative(nameof(TaichiSettings.enabled)),
                        new GUIContent("Run Taichi Checks", "Step 3: Taichi 1.0 / 2.0 switched on, events wired to every ad revenue, Remote Config thresholds, sheet value"));
                    DrawGamePicker(apkTab: false);
                }
                EditorGUIUtility.labelWidth = prevLabelWidth;
                if (configSO.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(config);
                    AssetDatabase.SaveAssets();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    // Always available: re-reads the project and the sheet, then offers to apply what differs.
                    GUILayout.Space(15); // line up with the indented settings above
                    if (ApplyAllButton(24)) EditorApplication.delayCall += AutoApplyIdsFresh;
                    if (editorReport != null && editorReport.idFixes.Count > 0)
                    {
                        GUILayout.Space(6);
                        if (CorrectWrongButton(24)) EditorApplication.delayCall += AutoCorrectWrongIds;
                    }
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("Edit Rules…", "Select the config asset to edit script names, field names and sheet columns"), GUILayout.Width(110)))
                        Selection.activeObject = config;
                    if (GUILayout.Button(new GUIContent("Reset Rules", "Restore the default script/field/column rules"), GUILayout.Width(110))
                        && EditorUtility.DisplayDialog("Reset rules", "Replace the current rules with the defaults?", "Reset", "Cancel"))
                    {
                        Undo.RecordObject(config, "Reset GenZ Test Suit rules");
                        config.rules = GenZTestSuitConfig.DefaultRules();
                        EditorUtility.SetDirty(config);
                        AssetDatabase.SaveAssets();
                    }
                }
            }

            EditorGUILayout.Space(6);
            if (GUILayout.Button("▶  Run Editor Test Suit", GUILayout.Height(34)))
                RunEditorSuite();

            if (report == null)
            {
                EditorGUILayout.HelpBox("Step 1 · IDs Check: every ad unit, app ID, SDK key and token — format, spaces and the Google Sheet row for this package.\n"
                    + "Step 2 · SDK Integration: SDKs installed and resolved, google-services.json, settings assets, SDK scene objects and their switches, Gradle setup.\n"
                    + "Step 3 · Taichi: Taichi 1.0 and 2.0 switched on, their Firebase events sent on every ad revenue, Remote Config thresholds read safely, the sheet's impression count.\n"
                    + "Step 4 · Ad Calling Scenarios: ad waterfall, show/hide, reloads, revenue events, AdsGapTimer resets and Remote Config, read from the scripts.", MessageType.Info);
                return;
            }

            if (restored)
                EditorGUILayout.HelpBox($"Results from the last run ({report.time:dd MMM, HH:mm}), kept after the scripts reloaded. Run the suite again to check your changes.", MessageType.None);
            DrawStepper();
            DrawAutoApply();
            DrawGaFormatFix();
            DrawSummary();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawResults();
            EditorGUILayout.EndScrollView();
        }

        static CheckStatus FilterStatus(ResultFilter f) =>
            f == ResultFilter.Passed ? CheckStatus.Pass : f == ResultFilter.Failed ? CheckStatus.Fail : CheckStatus.Warn;

        // Every section with every step's checks; a status tab narrows them (sections then start expanded).
        void DrawResults()
        {
            bool any = false;
            foreach (var section in report.sections)
            {
                var checks = section.checks.FindAll(c => (stepFilter == null || c.stage == stepFilter)
                                                         && (filter == ResultFilter.All || c.status == FilterStatus(filter)));
                if (checks.Count == 0) continue;
                any = true;
                var status = filter == ResultFilter.All ? checks.Max(c => c.status) : FilterStatus(filter);
                DrawSection(section.title, status, checks, filter == ResultFilter.All ? collapsed : collapsedFiltered);
            }
            if (!any)
                EditorGUILayout.HelpBox((filter == ResultFilter.All ? "No checks" : $"No {filter.ToString().ToLowerInvariant()} checks")
                    + (stepFilter != null ? $" in {TestReport.StageName(stepFilter.Value)}." : "."), MessageType.Info);
        }

        void RunEditorSuite()
        {
            if (config == null) LoadConfig();
            tab = 0;
            editorReport = GenZEditorTestSuit.Run(config, GamePackage);
            restored = false;
            sdkScenePath = GenZEditorTestSuit.LastScenePath;
            adsObjectPath = GenZEditorTestSuit.LastAdsObjectPath;
            collapsed.Clear();
            collapsedFiltered.Clear();
            foreach (var s in report.sections)
                if (s.Status == CheckStatus.Pass) collapsed.Add(s.title); // keep the failures in view
            SaveReport();
            Debug.Log($"[{GenZEditorTestSuit.SuiteName}] {report.Count(CheckStatus.Pass)} passed, {report.Count(CheckStatus.Fail)} failed, {report.Count(CheckStatus.Warn)} warnings");
            Repaint();
        }

        void DrawSummary()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                FilterTab(ResultFilter.All, null, "All", report.sections.Sum(s => s.checks.Count), InfoColor);
                FilterTab(ResultFilter.Passed, "✔", "Passed", report.Count(CheckStatus.Pass), PassColor);
                FilterTab(ResultFilter.Failed, "✘", "Failed", report.Count(CheckStatus.Fail), FailColor);
                FilterTab(ResultFilter.Warnings, "!", "Warnings", report.Count(CheckStatus.Warn), WarnColor);
                if (stepFilter != null)
                {
                    // Chip showing the clicked step; ✕ (or clicking the step again) shows every step.
                    GUILayout.Space(6);
                    int n = System.Array.IndexOf(report.StageList, stepFilter.Value) + 1;
                    var chip = new GUIContent($"<color={Hex(AccentColor * 1.4f)}><b>Showing:</b></color> Step {n} · {TestReport.StageName(stepFilter.Value)}   ✕",
                        "Click to show every step again");
                    var r = GUILayoutUtility.GetRect(chip, filterLabelStyle, GUILayout.Height(26));
                    r.width += 16;
                    GUI.DrawTexture(new Rect(r.x, r.y + 3, r.width, r.height - 6), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                        new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.25f), 0, 10);
                    GUI.DrawTexture(new Rect(r.x, r.y + 3, r.width, r.height - 6), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                        new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.8f), 1, 10);
                    GUI.Label(new Rect(r.x + 8, r.y, r.width - 8, r.height), chip, filterLabelStyle);
                    EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { stepFilter = null; scroll = Vector2.zero; }
                    GUILayout.Space(16);
                }
                GUILayout.FlexibleSpace();
                if (tab == 0 && !string.IsNullOrEmpty(sdkScenePath) && GUILayout.Button("Open SDK Scene", GUILayout.Width(115)))
                    OpenSdkScene();
                if (tab == 1 && !string.IsNullOrEmpty(apkLogPath) && System.IO.File.Exists(apkLogPath)
                    && GUILayout.Button(new GUIContent("Open Log", apkLogPath), GUILayout.Width(80)))
                    EditorUtility.OpenWithDefaultApp(apkLogPath);
                if (GUILayout.Button("Copy Report", GUILayout.Width(95)))
                {
                    EditorGUIUtility.systemCopyBuffer = report.ToMarkdown();
                    ShowNotification(new GUIContent("Report copied as Markdown"));
                }
            }
        }

        // A tab: "<icon> Name" on the left, the count in a rounded colored pill on the right.
        void FilterTab(ResultFilter f, string icon, string name, int count, Color color)
        {
            bool selected = filter == f;
            string label = (icon == null ? "" : $"<color={Hex(color)}><b>{icon}</b></color>  ")
                           + (selected ? $"<b>{name}</b>" : name);
            var countText = new GUIContent(count.ToString());
            float pillW = Mathf.Max(24, pillStyle.CalcSize(countText).x + 12);
            float labelW = filterLabelStyle.CalcSize(new GUIContent(label)).x;

            var rect = GUILayoutUtility.GetRect(labelW + pillW + 26, 26, filterStyle, GUILayout.ExpandWidth(false));
            if (GUI.Toggle(rect, selected, GUIContent.none, filterStyle) && !selected)
            {
                filter = f;
                scroll = Vector2.zero;
            }

            GUI.Label(new Rect(rect.x + 9, rect.y, labelW, rect.height), label, filterLabelStyle);

            var pill = new Rect(rect.xMax - pillW - 7, rect.y + 5, pillW, rect.height - 10);
            var fill = count > 0 ? color : new Color(0.5f, 0.5f, 0.5f, 0.45f);
            GUI.DrawTexture(pill, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, fill, 0, pill.height / 2);
            var prev = pillStyle.normal.textColor;
            // dark text on the light amber/blue pills, white on green/red
            pillStyle.normal.textColor = count > 0 && (f == ResultFilter.Warnings || f == ResultFilter.All) ? new Color(0.12f, 0.12f, 0.12f) : Color.white;
            GUI.Label(pill, countText, pillStyle);
            pillStyle.normal.textColor = prev;
        }

        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ---------------------------------------------------------------- Auto Apply IDs

        // Shown when the last run found IDs that are empty, wrong or padded with spaces and the sheet has their value:
        // which ones, and a button that corrects exactly those.
        void DrawAutoApply()
        {
            if (report.idFixes.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                int n = report.idFixes.Count;
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label($"<color={Hex(FailColor)}><b>{n} wrong ID{(n == 1 ? "" : "s")}</b></color>: empty, padded with spaces or different from the Google Sheet.",
                        filterLabelStyle);
                    GUILayout.Label(string.Join("  ·  ", report.idFixes.Take(6).Select(f => f.label)) + (n > 6 ? $"  ·  …{n - 6} more" : ""),
                        EditorStyles.wordWrappedMiniLabel);
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.FlexibleSpace(); // centre the buttons on the two text lines
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // outside OnGUI: both open and save scenes
                        if (ApplyAllButton(30)) EditorApplication.delayCall += AutoApplyIdsFresh;
                        GUILayout.Space(6);
                        if (CorrectWrongButton(30)) EditorApplication.delayCall += AutoCorrectWrongIds;
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        // ---------------------------------------------------------------- Fix GA Ad Formats

        // Shown when GAAdMobIntegration.cs sends a different ad format string than GAMaxIntegration.cs
        // (e.g. AdMob "INTERSTITIAL", MAX "INTER"): GameAnalytics would split that format's revenue in two.
        void DrawGaFormatFix()
        {
            var fixes = report?.gaFormatFixes;
            if (fixes == null || fixes.Count == 0) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Label($"<color={Hex(FailColor)}><b>GameAnalytics gets two different strings</b></color> for the same ad format from MAX and AdMob.",
                        filterLabelStyle);
                    GUILayout.Label(string.Join("  ·  ", fixes.Select(f => $"{f.label.Replace("GameAnalytics AdMob ", "").Replace(" format", "")}: AdMob \"{f.current}\" → MAX \"{f.expected}\"")),
                        EditorStyles.wordWrappedMiniLabel);
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (SolidButton(30, "Fix GA Ad Formats",
                            "Set the ad format strings in GAAdMobIntegration.cs to the ones GAMaxIntegration.cs sends, so GameAnalytics reports AdMob and MAX under one format:\n"
                            + string.Join("\n", fixes.Select(f => $"• {f.label}: \"{f.current}\" → \"{f.expected}\"")) + "\nShows the change and asks first.",
                            CorrectColor, "d_Valid", "Valid"))
                        EditorApplication.delayCall += FixGaAdFormats;
                    GUILayout.FlexibleSpace();
                }
            }
        }

        void FixGaAdFormats()
        {
            if (report == null || report.gaFormatFixes.Count == 0) return;
            const string title = "Fix GA Ad Formats";
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(title, "Stop Play mode first.", "OK");
                return;
            }
            var fixes = report.gaFormatFixes.ToList();
            var msg = "GameAnalytics gets two different strings for these ad formats. AdMob's will be set to MAX's:\n\n"
                      + string.Join("\n", fixes.Select(f => $"• {f.label}\n    \"{f.current}\"  →  \"{f.expected}\"   ({f.Where})"))
                      + "\n\nThe script is edited in place (it's in the GameAnalytics SDK folder: reimporting or updating GameAnalytics brings the old strings back, so run the suite after an update).";
            if (!EditorUtility.DisplayDialog(title, msg, "Fix", "Cancel")) return;

            var result = GenZIdFixer.ApplyOnLines(fixes);
            foreach (var a in result.applied) Debug.Log("[GenZ Test Suit] Fix GA Ad Formats: " + a);
            foreach (var f in result.failed) Debug.LogWarning("[GenZ Test Suit] Fix GA Ad Formats failed: " + f);

            RunEditorSuite(); // verify

            var summary = $"{result.applied.Count} string{(result.applied.Count == 1 ? "" : "s")} fixed.";
            if (result.failed.Count > 0) summary += $"\n\n{result.failed.Count} couldn't be fixed:\n" + string.Join("\n", result.failed);
            summary += $"\n\nRe-checked: {report.gaFormatFixes.Count} ad format{(report.gaFormatFixes.Count == 1 ? "" : "s")} still differ.";
            EditorUtility.DisplayDialog(title, summary, "OK");
        }

        static readonly Color AccentColor = new Color(0.16f, 0.47f, 0.80f);

        static readonly Color CorrectColor = new Color(0.85f, 0.40f, 0.12f); // warm orange: "fix these"

        bool ApplyAllButton(float height) => SolidButton(height, "Auto Apply All IDs from Sheet",
            "Download the Google Sheet again, re-check every ID, and write the sheet's value into each one that differs (Ads object, Adjust, AppMetrica, SDK settings assets). Shows every change and asks first.",
            AccentColor, "d_Refresh", "Refresh");

        bool CorrectWrongButton(float height)
        {
            var fixes = report?.idFixes ?? editorReport?.idFixes;
            int n = fixes?.Count ?? 0;
            return SolidButton(height, $"Auto Correct {n} Wrong ID{(n == 1 ? "" : "s")}",
                "Write the Google Sheet value into only the IDs the last run found wrong:\n" + string.Join("\n", (fixes ?? new List<IdFix>()).Take(12).Select(f => "• " + f.label))
                + (n > 12 ? $"\n…and {n - 12} more" : "") + "\nShows every change and asks first.",
                CorrectColor, "d_Valid", "Valid");
        }

        // Solid rounded button: icon + bold white text; lighter on hover, darker while pressed.
        bool SolidButton(float height, string text, string tooltip, Color color, params string[] iconNames)
        {
            var content = new GUIContent(text, tooltip);
            var icon = iconNames.Select(EditorGUIUtility.FindTexture).FirstOrDefault(t => t != null);
            float iconSize = Mathf.Min(16, height - 6);
            float width = accentButtonStyle.CalcSize(new GUIContent(text)).x + (icon != null ? iconSize + 6 : 0) + 28;

            var rect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            bool hover = rect.Contains(Event.current.mousePosition);
            bool clicked = GUI.Button(rect, content, GUIStyle.none); // invisible: handles click + tooltip
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            if (Event.current.type == EventType.Repaint)
            {
                bool pressed = hover && GUIUtility.hotControl != 0;
                var fill = pressed ? color * 0.8f : hover ? Color.Lerp(color, Color.white, 0.15f) : color;
                fill.a = 1;
                GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, fill, 0, 5);

                float x = rect.x + 14;
                if (icon != null)
                {
                    var prev = GUI.color;
                    GUI.color = Color.white;
                    GUI.DrawTexture(new Rect(x, rect.y + (rect.height - iconSize) / 2, iconSize, iconSize), icon, ScaleMode.ScaleToFit);
                    GUI.color = prev;
                    x += iconSize + 6;
                }
                accentButtonStyle.Draw(new Rect(x, rect.y, rect.xMax - x - 14, rect.height), text, false, false, false, false);
            }
            return clicked;
        }

        // Settings button: run the ID checks against the sheet now (even if they're turned off), then apply.
        void AutoApplyIdsFresh()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Auto Apply IDs", "Stop Play mode first.", "OK");
                return;
            }
            if (config == null) LoadConfig();
            bool sheetWas = config.verifyAgainstSheet, idsWas = config.runIdChecks;
            config.verifyAgainstSheet = true; // in memory only: restored right after, never saved
            config.runIdChecks = true;
            try { RunEditorSuite(); }
            finally { config.verifyAgainstSheet = sheetWas; config.runIdChecks = idsWas; }

            var sheetCheck = report.sections.SelectMany(x => x.checks).FirstOrDefault(c => c.label == "Google Sheet");
            if (sheetCheck != null && sheetCheck.status == CheckStatus.Fail)
            {
                EditorUtility.DisplayDialog("Auto Apply IDs from Sheet", "Couldn't read this game's row from the Google Sheet:\n\n" + sheetCheck.message, "OK");
                return;
            }
            if (report.idFixes.Count == 0)
            {
                EditorUtility.DisplayDialog("Auto Apply IDs from Sheet",
                    $"Every ID already matches the Google Sheet for {GamePackage}. Nothing to apply.", "OK");
                return;
            }
            AutoApplyIds("Auto Apply All IDs from Sheet");
        }

        // The wrong IDs the last run found, without downloading the sheet again.
        void AutoCorrectWrongIds()
        {
            if (editorReport == null || editorReport.idFixes.Count == 0) return;
            tab = 0;
            AutoApplyIds("Auto Correct Wrong IDs");
        }

        void AutoApplyIds(string title)
        {
            if (report == null || report.idFixes.Count == 0) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Auto Apply IDs", "Stop Play mode first.", "OK");
                return;
            }

            var fixes = report.idFixes.ToList();
            const int MaxLines = 18;
            var lines = fixes.Take(MaxLines).Select(f =>
                $"• {f.label}\n    {GenZIdFixer.Show(f, GenZIdFixer.Display(f.current))}  →  {GenZIdFixer.Show(f, f.expected)}");
            var msg = "These IDs will be set to the Google Sheet values for " + GamePackage + ":\n\n"
                      + string.Join("\n", lines)
                      + (fixes.Count > MaxLines ? $"\n…and {fixes.Count - MaxLines} more (see the Console after applying)" : "")
                      + "\n\nSettings assets and SDK scenes that aren't open are saved. A scene you have open is only marked as modified — save it yourself."
                      + (fixes.Any(f => !string.IsNullOrEmpty(f.codeFile))
                          ? "\n\nScripts: " + string.Join(", ", fixes.Where(f => !string.IsNullOrEmpty(f.codeFile)).Select(f => f.Where).Distinct()) + " are edited in place, and Unity recompiles."
                          : "");
            if (!EditorUtility.DisplayDialog(title, msg, "Apply", "Cancel")) return;

            var result = GenZIdFixer.Apply(fixes);
            foreach (var a in result.applied) Debug.Log("[GenZ Test Suit] Auto Apply: " + a);
            foreach (var f in result.failed) Debug.LogWarning("[GenZ Test Suit] Auto Apply failed: " + f);

            RunEditorSuite(); // verify: the applied IDs should now match the sheet

            var summary = $"{result.applied.Count} ID{(result.applied.Count == 1 ? "" : "s")} applied.";
            if (result.failed.Count > 0) summary += $"\n\n{result.failed.Count} couldn't be applied:\n" + string.Join("\n", result.failed.Take(10));
            if (result.unsavedScenes.Count > 0) summary += "\n\nSave these open scenes to keep the change:\n" + string.Join("\n", result.unsavedScenes);
            if (result.scripts.Count > 0) summary += "\n\nScripts edited (Unity recompiles):\n" + string.Join("\n", result.scripts);
            summary += $"\n\nRe-checked: {report.idFixes.Count} ID{(report.idFixes.Count == 1 ? "" : "s")} still differ from the sheet.";
            EditorUtility.DisplayDialog(title, summary, "OK");
        }

        // ---------------------------------------------------------------- step bar

        // 1 ─── 2 ─── 3 ─── 4 : each circle has its step's worst color (green = completed, amber = minor issues,
        // red = errors, grey = didn't run). Click a step to list only its checks; click it again to list every step.
        // The status tabs' counts stay for the whole run.
        void DrawStepper()
        {
            const float Radius = 14;
            var stages = report.StageList;
            var names = stages.Select(TestReport.StageName).ToArray();
            var states = stages.Select(StepState).ToArray();
            // Wide enough for the longest name/status line, so the outer labels aren't cut by the window edge.
            float labelW = 20 + Enumerable.Range(0, stages.Length).Max(i => Mathf.Max(
                stepLabelStyle.CalcSize(new GUIContent($"<b>{names[i]}</b>")).x, stepCountStyle.CalcSize(new GUIContent(states[i])).x));

            var rect = GUILayoutUtility.GetRect(0, 64, GUILayout.ExpandWidth(true));
            float x0 = rect.x + labelW / 2, x1 = rect.xMax - labelW / 2, cy = rect.y + 4 + Radius;
            float Cx(int i) => stages.Length == 1 ? (x0 + x1) / 2 : x0 + (x1 - x0) * i / (stages.Length - 1);
            float hitW = stages.Length == 1 ? labelW : Mathf.Min(labelW, (x1 - x0) / (stages.Length - 1) - 4);
            Rect Hit(int i) => new Rect(Cx(i) - hitW / 2, rect.y - 2, hitW, rect.height + 2);

            for (int i = 0; i < stages.Length; i++)
            {
                var hit = Hit(i);
                EditorGUIUtility.AddCursorRect(hit, MouseCursor.Link);
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hit.Contains(Event.current.mousePosition))
                {
                    stepFilter = stepFilter == stages[i] ? (TestStage?)null : stages[i];
                    scroll = Vector2.zero;
                    Event.current.Use();
                    Repaint();
                }
            }
            if (Event.current.type != EventType.Repaint) return;

            // Completed = ran with no failure and no warning. A connector turns green between two completed steps.
            var done = stages.Select(st => { var w = report.StageStatus(st); return w == CheckStatus.Pass || w == CheckStatus.Info; }).ToArray();
            for (int i = 0; i < stages.Length - 1; i++)
                EditorGUI.DrawRect(new Rect(Cx(i) + Radius + 8, cy - 1, Cx(i + 1) - Cx(i) - 2 * Radius - 16, 2),
                    done[i] && done[i + 1] ? new Color(PassColor.r, PassColor.g, PassColor.b, 0.8f) : new Color(0.5f, 0.5f, 0.5f, 0.5f));

            for (int i = 0; i < stages.Length; i++)
            {
                var worst = report.StageStatus(stages[i]);
                var color = StepColor(worst);
                float cx = Cx(i);
                var circle = new Rect(cx - Radius, cy - Radius, Radius * 2, Radius * 2);
                bool selected = stepFilter == stages[i];
                var hit = Hit(i);
                // Selected: soft tile behind the step + accent ring. Hover: lighter tile. Others dim while a step is selected.
                if (selected || hit.Contains(Event.current.mousePosition))
                    GUI.DrawTexture(hit, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                        selected ? new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.16f) : new Color(1, 1, 1, 0.05f), 0, 8);
                var prevGui = GUI.color;
                if (stepFilter != null && !selected) GUI.color = new Color(1, 1, 1, 0.45f);
                if (selected)
                {
                    var ring = new Rect(circle.x - 8, circle.y - 8, circle.width + 16, circle.height + 16);
                    GUI.DrawTexture(ring, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.45f, 0.70f, 1f), 2f, ring.height / 2);
                }
                if (done[i])
                {
                    // Soft halo + ring around the completed step.
                    var halo = new Rect(circle.x - 5, circle.y - 5, circle.width + 10, circle.height + 10);
                    GUI.DrawTexture(halo, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(color.r, color.g, color.b, 0.18f), 0, halo.height / 2);
                    GUI.DrawTexture(halo, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(color.r, color.g, color.b, 0.55f), 1.5f, halo.height / 2);
                }
                GUI.DrawTexture(circle, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, Radius);
                stepNumberStyle.normal.textColor = worst == CheckStatus.Warn ? new Color(0.12f, 0.12f, 0.12f) : Color.white;
                GUI.Label(circle, (i + 1).ToString(), stepNumberStyle);
                if (done[i]) DrawTickBadge(cx + Radius * 1.0f, cy + Radius * 0.75f);

                GUI.Label(new Rect(cx - labelW / 2, cy + Radius + 6, labelW, 16), $"<b><color={Hex(color)}>{names[i]}</color></b>", stepLabelStyle);
                GUI.Label(new Rect(cx - labelW / 2, cy + Radius + 21, labelW, 14), states[i], stepCountStyle);
                GUI.color = prevGui;
            }
        }

        static readonly Color TickBadgeColor = new Color(0.10f, 0.50f, 0.20f); // deeper than PassColor, so it stands apart from the step circle

        // Small badge on the circle's lower-right edge: a dark green disc with a white outline and a bold white
        // check mark, cut out from the circle by a ring in the window's background colour.
        static void DrawTickBadge(float bx, float by)
        {
            const float R = 8.5f;
            var bg = EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.76f, 0.76f, 0.76f);
            var cut = new Rect(bx - R - 2, by - R - 2, (R + 2) * 2, (R + 2) * 2);
            GUI.DrawTexture(cut, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, bg, 0, R + 2);
            var badge = new Rect(bx - R, by - R, R * 2, R * 2);
            GUI.DrawTexture(badge, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Color.white, 0, R);
            var inner = new Rect(bx - R + 1.5f, by - R + 1.5f, (R - 1.5f) * 2, (R - 1.5f) * 2);
            GUI.DrawTexture(inner, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, TickBadgeColor, 0, R - 1.5f);

            var prev = Handles.color;
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(3.2f, new Vector3(bx - 3.8f, by + 0.1f), new Vector3(bx - 1.1f, by + 2.9f), new Vector3(bx + 4f, by - 2.8f));
            Handles.color = prev;
        }

        static Color StepColor(CheckStatus? worst) =>
            worst == null ? new Color(0.45f, 0.45f, 0.45f) : worst == CheckStatus.Fail ? FailColor : worst == CheckStatus.Warn ? WarnColor : PassColor;

        // "Completed", "Minor issues · 1 warning" or "Errors · 15 failed · 8 warnings".
        string StepState(TestStage st)
        {
            if (!report.Checks(st).Any()) return "Not run";
            int fail = report.Count(st, CheckStatus.Fail), warn = report.Count(st, CheckStatus.Warn);
            var w = $"{warn} warning{(warn == 1 ? "" : "s")}";
            if (fail > 0) return $"<color={Hex(FailColor)}>Errors · {fail} failed</color>" + (warn > 0 ? $" · <color={Hex(WarnColor)}>{w}</color>" : "");
            if (warn > 0) return $"<color={Hex(WarnColor)}>Minor issues · {w}</color>";
            return $"<color={Hex(PassColor)}>✔ Completed</color>";
        }

        void DrawSection(string title, CheckStatus status, List<CheckResult> checks, HashSet<string> collapsedSet)
        {
            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                StatusIcon(status);
                bool open = !collapsedSet.Contains(title);
                bool now = EditorGUILayout.Foldout(open, title, true, headerStyle);
                if (now != open) { if (now) collapsedSet.Remove(title); else collapsedSet.Add(title); }
            }
            if (collapsedSet.Contains(title)) return;

            labelColWidth = ClampLabelWidth(labelColWidth);

            // Rows are laid out with fixed column positions (not auto-layout), so every row's value and
            // message start at the same x: | indent | icon | name | divider | value + message | Open |
            const float Indent = 22, IconW = 18, Gap = 4;
            float rowW = EditorGUIUtility.currentViewWidth - 26; // minus the scrollbar and margins
            float valueX = Indent + IconW + Gap + labelColWidth + SplitterWidth + Gap;
            float valueW = Mathf.Max(80, rowW - valueX - OpenButtonWidth - Gap);
            float line = EditorGUIUtility.singleLineHeight;

            foreach (var c in checks)
            {
                var name = new GUIContent(c.label, c.label); // tooltip shows the full name too
                float nameH = checkLabelStyle.CalcHeight(name, labelColWidth);
                float valueH = string.IsNullOrEmpty(c.value) ? 0 : Mathf.Max(line, wrapStyle.CalcHeight(new GUIContent(c.value), valueW));
                float msgH = string.IsNullOrEmpty(c.message) ? 0 : wrapStyle.CalcHeight(new GUIContent(c.message), valueW);
                float rowH = Mathf.Max(line, nameH, valueH + msgH) + 2;

                var r = GUILayoutUtility.GetRect(rowW, rowH, GUILayout.Width(rowW), GUILayout.Height(rowH));
                StatusIcon(new Rect(r.x + Indent, r.y, IconW, line), c.status);
                GUI.Label(new Rect(r.x + Indent + IconW + Gap, r.y, labelColWidth, nameH), name, checkLabelStyle);
                Splitter(new Rect(r.x + Indent + IconW + Gap + labelColWidth, r.y, SplitterWidth, rowH));

                float vx = r.x + valueX;
                if (valueH > 0)
                    EditorGUI.SelectableLabel(new Rect(vx, r.y, valueW, valueH), c.value, wrapStyle);
                if (msgH > 0)
                    GUI.Label(new Rect(vx, r.y + valueH, valueW, msgH), c.message, wrapStyle);

                if (!string.IsNullOrEmpty(c.file)
                    && GUI.Button(new Rect(r.x + rowW - OpenButtonWidth, r.y, OpenButtonWidth, line), new GUIContent("Open", c.file + ":" + c.line)))
                {
                    // Scripts open at the line; other files (json, xml, gradle) open in their default app.
                    var asset = AssetDatabase.LoadMainAssetAtPath(c.file);
                    if (asset != null) AssetDatabase.OpenAsset(asset, Mathf.Max(1, c.line));
                    else EditorUtility.OpenWithDefaultApp(c.file);
                }
            }
        }

        float ClampLabelWidth(float w) => Mathf.Clamp(w, 120, Mathf.Max(120, position.width - 300));

        // Draggable divider between the name column and the value column (one per row; any of them resizes all).
        void Splitter(Rect r)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(r.center.x, r.y + 2, 1, r.height - 4),
                    draggingSplitter ? new Color(0.35f, 0.6f, 1f, 0.8f) : new Color(0.5f, 0.5f, 0.5f, 0.25f));
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(Event.current.mousePosition))
            {
                draggingSplitter = true;
                GUIUtility.hotControl = id; // keep receiving drag events even when the mouse leaves the divider
                Event.current.Use();
            }
        }

        void HandleSplitterDrag()
        {
            if (!draggingSplitter) return;
            var e = Event.current;
            if (e.type == EventType.MouseDrag)
            {
                labelColWidth = ClampLabelWidth(labelColWidth + e.delta.x);
                e.Use();
                Repaint();
            }
            else if (e.rawType == EventType.MouseUp)
            {
                draggingSplitter = false;
                GUIUtility.hotControl = 0;
                EditorPrefs.SetFloat(LabelWidthPref, labelColWidth);
                Repaint();
            }
        }

        void StatusIcon(CheckStatus s) => StatusIcon(GUILayoutUtility.GetRect(18, 18, iconStyle, GUILayout.Width(18)), s);

        void StatusIcon(Rect r, CheckStatus s)
        {
            string icon; Color color;
            switch (s)
            {
                case CheckStatus.Pass: icon = "✔"; color = PassColor; break;
                case CheckStatus.Fail: icon = "✘"; color = FailColor; break;
                case CheckStatus.Warn: icon = "!"; color = WarnColor; break;
                default: icon = "i"; color = InfoColor; break;
            }
            var prev = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(r, icon, iconStyle);
            GUI.contentColor = prev;
        }

        void OpenSdkScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(sdkScenePath, OpenSceneMode.Single);
            var go = GameObject.Find(adsObjectPath ?? "");
            if (go != null) { Selection.activeGameObject = go; EditorGUIUtility.PingObject(go); }
        }

        // ---------------------------------------------------------------- APK suite tab

        // Session state is serialized, so a script reload in the middle of a session keeps it going.
        [System.NonSerialized] TestReport apkBuild; // step 1, kept from the start and put in front of every live/final report
        [SerializeField] string savedApkReport, savedApkBuild;
        [SerializeField] string apkPath, apkLogPath;
        [SerializeField] bool useApkFile = true; // false = test the game already installed on the phone
        [SerializeField] bool freshInstall, showApkSettings = true;
        [SerializeField] List<AdbDevice> devices;
        [SerializeField] string selectedSerial;
        [SerializeField] string gamePackage; // empty = this project (Player Settings)
        [SerializeField] List<string> sheetPackages = new List<string>(), sheetNames = new List<string>();
        [SerializeField] string sheetError;
        [SerializeField] bool recording;
        [SerializeField] string recSerial, recTaichi;
        [SerializeField] long recStartTicks;
        [SerializeField] ApkInfo recApk;
        double nextPoll, nextRepaint, nextDeviceRefresh;
        volatile bool polling, refreshingDevices;
        volatile string polledLog;                 // set by the background log poll, read on the main thread
        volatile List<AdbDevice> polledDevices;    // set by the background device refresh
        const double PollSeconds = 4, DeviceRefreshSeconds = 5;
        const string SheetLoadedKey = "GenZ.TestSuit.ApkSheetLoaded";
        static readonly string[] GameNameColumns = { "Game Name", "Game", "App Name", "Name" };

        string GamePackage => string.IsNullOrEmpty(gamePackage) ? GenZApkTestSuit.ProjectPackage : gamePackage;
        bool IsThisProject => GamePackage == GenZApkTestSuit.ProjectPackage;
        string GameName { get { int i = sheetPackages.IndexOf(GamePackage); return i >= 0 ? sheetNames[i] : ""; } }
        AdbDevice SelectedDevice => devices?.Find(d => d.serial == selectedSerial);

        void DrawApkSuite()
        {
            if (devices == null) RefreshDevicesNow();
            if (string.IsNullOrEmpty(apkPath)) apkPath = GenZApkTestSuit.NewestApk();

            showApkSettings = EditorGUILayout.Foldout(showApkSettings, "Settings", true, headerStyle);
            if (showApkSettings)
                using (new EditorGUI.DisabledScope(recording))
                {
                    using (new EditorGUI.IndentLevelScope())
                        DrawGamePicker(apkTab: true);
                    EditorGUILayout.Space(4);
                    DrawBuildSource();
                    EditorGUILayout.Space(4);
                    DrawDeviceList();
                }
            EditorGUILayout.Space(2);
            DrawFirebaseAdb(GamePackage, recording); // outside the disabled block: Start ADB also works while recording

            EditorGUILayout.Space(6);
            if (!recording) DrawStartButton();
            else
            {
                var elapsed = System.TimeSpan.FromTicks(System.DateTime.Now.Ticks - recStartTicks);
                EditorGUILayout.HelpBox($"● Recording {recApk?.package} on {SelectedDevice?.Label ?? recSerial} · {elapsed:mm\\:ss}\n"
                    + "Play on the phone now: finish a level, let an interstitial show, watch a rewarded ad, send the game to the background and back (App Open). "
                    + "The results below update every few seconds. Press Finish when you're done.", MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("■  Finish & Check", GUILayout.Height(34)))
                        EditorApplication.delayCall += FinishApkSession;
                    if (GUILayout.Button("Cancel", GUILayout.Height(34), GUILayout.Width(80)))
                        EditorApplication.delayCall += CancelApkSession;
                }
            }

            if (apkReport == null)
            {
                EditorGUILayout.HelpBox("Step 1 · Build & Device: the build is the selected game's release build (package, version, not debuggable, target SDK, 64-bit, signing), installed and launched.\n"
                    + "Step 2 · SDK Init: MAX, AdMob (and its adapters), Firebase, Remote Config, GameAnalytics, Adjust, AppMetrica, UMP, IAP and FCM started without errors.\n"
                    + "Step 3 · Analytics Events: Firebase events logged and uploaded, GameAnalytics events sent with this version, Adjust and AppMetrica events fired and their servers answering.\n"
                    + "Step 4 · Ads: each ad format shown, its revenue reported everywhere, Taichi (against the sheet's count).\n"
                    + "Step 5 · Stability: no crash, ANR or script exception.\n\n"
                    + "Needs the [GenZ] logs from GenZLog.cs in the game's ad scripts, and a release build.", MessageType.Info);
                return;
            }

            DrawStepper();
            DrawSummary();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawResults();
            EditorGUILayout.EndScrollView();
        }

        // Which game: every package in the studio sheet, this project first. One selection for both tabs.
        void DrawGamePicker(bool apkTab)
        {
            {
                if (apkTab)
                {
                    configSO.Update();
                    EditorGUILayout.PropertyField(configSO.FindProperty(nameof(GenZTestSuitConfig.googleSheetUrl)),
                        new GUIContent("Google Sheet Link", "The studio sheet with every game's package name. Empty = studio default sheet."));
                    if (configSO.ApplyModifiedProperties()) { EditorUtility.SetDirty(config); AssetDatabase.SaveAssets(); }
                }

                string project = GenZApkTestSuit.ProjectPackage;
                var packages = new List<string> { project };
                var labels = new List<string> { GameLabel(project, "This project") };
                foreach (var (pkg, i) in sheetPackages.Select((p, i) => (p, i)))
                {
                    if (pkg == project) { labels[0] = GameLabel(pkg, "This project · " + sheetNames[i]); continue; }
                    packages.Add(pkg);
                    labels.Add(GameLabel(pkg, sheetNames[i]));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    // Searchable list (like Add Component): type part of a game name or package.
                    int current = Mathf.Max(0, packages.IndexOf(GamePackage));
                    var rect = EditorGUILayout.GetControlRect();
                    rect = EditorGUI.PrefixLabel(rect, new GUIContent("Game", "The game to test, from the sheet's Package Name columns. Type to search."));
                    if (EditorGUI.DropdownButton(rect, new GUIContent(labels[current]), FocusType.Keyboard))
                        new GameDropdown(packages, labels, pkg => { SelectGame(pkg == GenZApkTestSuit.ProjectPackage ? "" : pkg); Repaint(); }).Show(rect);
                    if (GUILayout.Button(new GUIContent("Load Games", "Download the sheet again"), GUILayout.Width(90)))
                        EditorApplication.delayCall += LoadSheetGames;
                }

                string status = !string.IsNullOrEmpty(sheetError) ? sheetError
                    : sheetPackages.Count == 0 ? "Sheet not loaded yet."
                    : $"{sheetPackages.Count} games in the sheet.";
                string version = !apkTab
                    ? (IsThisProject ? $"This project's IDs are compared with its own sheet row ({GenZApkTestSuit.ProjectPackage}, from Player Settings)."
                                     : $"This project's IDs are compared with {GamePackage}'s sheet row, but Player Settings say {GenZApkTestSuit.ProjectPackage}: the build would ship with that package.")
                    : IsThisProject
                    ? $"This project: version {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode}) from Player Settings is expected."
                    : "Another game than this project: its version is shown, not compared.";
                EditorGUILayout.LabelField(" ", status + "  " + version, EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>The Game list with a search box.</summary>
        class GameDropdown : UnityEditor.IMGUI.Controls.AdvancedDropdown
        {
            readonly List<string> packages, labels;
            readonly System.Action<string> onPick;

            public GameDropdown(List<string> packages, List<string> labels, System.Action<string> onPick)
                : base(new UnityEditor.IMGUI.Controls.AdvancedDropdownState())
            {
                this.packages = packages;
                this.labels = labels;
                this.onPick = onPick;
                minimumSize = new Vector2(420, 360);
            }

            protected override UnityEditor.IMGUI.Controls.AdvancedDropdownItem BuildRoot()
            {
                var root = new UnityEditor.IMGUI.Controls.AdvancedDropdownItem($"Games ({labels.Count})");
                for (int i = 0; i < labels.Count; i++)
                    root.AddChild(new UnityEditor.IMGUI.Controls.AdvancedDropdownItem(labels[i]) { id = i });
                return root;
            }

            protected override void ItemSelected(UnityEditor.IMGUI.Controls.AdvancedDropdownItem item) => onPick(packages[item.id]);
        }

        const string ApkFieldName = "GenZ.ApkPath";

        // A text field with keyboard focus keeps showing its own text, so drop the focus before changing the path.
        void SetApkPath(string path)
        {
            if (GUI.GetNameOfFocusedControl() == ApkFieldName) GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
            apkPath = path.Replace('\\', '/');
            Repaint();
        }

        static string GameLabel(string pkg, string name) =>
            (string.IsNullOrEmpty(name) ? pkg : $"{name}  —  {pkg}").Replace("/", "∕"); // "/" would make a submenu

        void SelectGame(string pkg)
        {
            gamePackage = pkg == GenZApkTestSuit.ProjectPackage ? "" : pkg;
            nextDeviceRefresh = 0; // re-read which phones have it installed
        }

        // APK file to install, or the game already on the phone.
        void DrawBuildSource()
        {
            using (new EditorGUI.IndentLevelScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel(new GUIContent("Build", "Install an APK first, or test the game that's already installed on the phone"));
                    int mode = GUILayout.Toolbar(useApkFile ? 0 : 1, new[] { "Install an APK", "Already installed on the phone" });
                    useApkFile = mode == 0;
                }
                if (useApkFile)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.SetNextControlName(ApkFieldName);
                        apkPath = EditorGUILayout.TextField(new GUIContent("APK", "Release APK to install. Defaults to the newest .apk in the project folder."), apkPath);
                        if (string.IsNullOrEmpty(apkPath) && Event.current.type == EventType.Repaint)
                        {
                            // Placeholder inside the empty field
                            var r = GUILayoutUtility.GetLastRect();
                            var hint = new Rect(r.x + EditorGUIUtility.labelWidth + 2, r.y, r.width - EditorGUIUtility.labelWidth - 2, r.height);
                            GUI.Label(hint, "No .apk in the project folder: Browse… to pick one", EditorStyles.centeredGreyMiniLabel);
                        }
                        if (GUILayout.Button("Browse…", GUILayout.Width(70)))
                        {
                            string folder = !string.IsNullOrEmpty(apkPath) && System.IO.File.Exists(apkPath) ? System.IO.Path.GetDirectoryName(apkPath) : GenZApkTestSuit.ProjectRoot;
                            var picked = EditorUtility.OpenFilePanel("Release APK", folder, "apk");
                            if (!string.IsNullOrEmpty(picked)) SetApkPath(picked);
                            GUIUtility.ExitGUI(); // the file dialog interrupted this OnGUI pass; draw again with the new path
                        }
                        if (GUILayout.Button(new GUIContent("Newest", "The newest .apk in the project folder"), GUILayout.Width(60)))
                        {
                            var newest = GenZApkTestSuit.NewestApk();
                            if (newest != null) SetApkPath(newest);
                            else ShowNotification(new GUIContent("No .apk in the project folder"));
                        }
                    }
                    if (!string.IsNullOrEmpty(apkPath) && System.IO.File.Exists(apkPath))
                        EditorGUILayout.LabelField(" ", $"Built {System.IO.File.GetLastWriteTime(apkPath):dd MMM, HH:mm} · {new System.IO.FileInfo(apkPath).Length / (1024 * 1024)} MB", EditorStyles.miniLabel);
                    freshInstall = EditorGUILayout.Toggle(new GUIContent("Fresh Install",
                        "Uninstall first, so the game starts as a new player: consent form, first-open events. Also needed when the installed app has another signature."), freshInstall);
                }
                else
                    freshInstall = EditorGUILayout.Toggle(new GUIContent("Fresh Start",
                        "Clear the game's data first (pm clear), so it starts as a new player: consent form, first-open events."), freshInstall);
            }
        }

        // Every attached phone with its state; only ready phones can be picked. Refreshed in the background.
        void DrawDeviceList()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    int ready = devices?.Count(d => d.Ready) ?? 0;
                    GUILayout.Label($"<b>Phones</b>  ·  {ready} ready of {devices?.Count ?? 0} attached", filterLabelStyle);
                    GUILayout.FlexibleSpace();
                    if (refreshingDevices) GUILayout.Label("refreshing…", EditorStyles.miniLabel);
                    if (GUILayout.Button("Refresh", GUILayout.Width(70))) RefreshDevicesNow();
                }
                if (devices == null || devices.Count == 0)
                {
                    EditorGUILayout.LabelField("No phone attached. Connect one with a USB cable and turn on USB debugging (Developer options).", EditorStyles.wordWrappedMiniLabel);
                    return;
                }
                foreach (var d in devices)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(!d.Ready))
                        {
                            bool on = GUILayout.Toggle(d.serial == selectedSerial, GUIContent.none, EditorStyles.radioButton, GUILayout.Width(18));
                            if (on && d.serial != selectedSerial) selectedSerial = d.serial;
                        }
                        string state = d.Ready ? $"<color={Hex(PassColor)}>Ready</color>"
                            : d.state == "unauthorized" ? $"<color={Hex(WarnColor)}>Accept the USB debugging prompt on the phone</color>"
                            : $"<color={Hex(FailColor)}>{d.state}: reconnect the cable</color>";
                        string game = !d.Ready || !d.installChecked ? ""
                            : d.installed.Length == 0 ? $"  ·  <color={Hex(WarnColor)}>game not installed</color>"
                            : $"  ·  game {d.installed} installed";
                        GUILayout.Label($"<b>{d.Name}</b>" + (string.IsNullOrEmpty(d.android) ? "" : $"  ·  Android {d.android}")
                            + $"  ·  <size=10>{d.serial}</size>  ·  {state}{game}", filterLabelStyle);
                        GUILayout.FlexibleSpace();
                    }
            }
        }

        void DrawStartButton()
        {
            var device = SelectedDevice;
            string blocker =
                useApkFile && (string.IsNullOrEmpty(apkPath) || !System.IO.File.Exists(apkPath)) ? "Build a release APK first (File › Build Settings › Android), pick one, or choose \"Already installed on the phone\"."
                : device == null ? (devices?.Any(d => d.Ready) == true ? "Pick a phone in the list." : "Connect a phone with a USB cable and USB debugging on.")
                : !device.Ready ? "The selected phone isn't ready."
                : !useApkFile && device.installChecked && device.installed == "" ? $"{GamePackage} isn't installed on {device.Name}. Install it, or choose \"Install an APK\"."
                : null;
            using (new EditorGUI.DisabledScope(blocker != null))
                if (GUILayout.Button(useApkFile ? "▶  Install & Start APK Test" : "▶  Start Test on the Installed Game", GUILayout.Height(34)))
                    EditorApplication.delayCall += StartApkSession; // outside OnGUI: it blocks with a progress bar
            if (blocker != null) EditorGUILayout.HelpBox(blocker, MessageType.Warning);
        }

        void LoadSheetGames()
        {
            if (config == null) LoadConfig();
            var sheet = GenZApkTestSuit.LoadSheet(config, out var error);
            sheetError = error;
            if (sheet != null)
            {
                // The sheet numbers its games ("12 Bubble Tea…"): drop the number, sort by name.
                var games = sheet.ListGames(config.packageColumnNames, GameNameColumns)
                                 .Select(g => new KeyValuePair<string, string>(g.Key, System.Text.RegularExpressions.Regex.Replace(g.Value, @"^\d+[\s.):-]*", "")))
                                 .OrderBy(g => string.IsNullOrEmpty(g.Value) ? g.Key : g.Value, System.StringComparer.OrdinalIgnoreCase).ToList();
                sheetPackages = games.Select(g => g.Key).ToList();
                sheetNames = games.Select(g => g.Value).ToList();
                if (games.Count == 0) sheetError = "No package names found in the sheet.";
            }
            Repaint();
        }

        void RefreshDevicesNow()
        {
            devices = GenZApkTestSuit.Devices(GamePackage, out var error);
            if (error != null) Debug.LogWarning($"[{GenZApkTestSuit.SuiteName}] adb devices failed: {error}");
            AfterDeviceRefresh();
            nextDeviceRefresh = EditorApplication.timeSinceStartup + DeviceRefreshSeconds;
        }

        // Keep the picked phone if it's still ready, else pick the first ready one.
        void AfterDeviceRefresh()
        {
            if (SelectedDevice?.Ready != true)
                selectedSerial = devices.FirstOrDefault(d => d.Ready)?.serial ?? selectedSerial;
        }

        void StartApkSession()
        {
            if (config == null) LoadConfig();
            var device = SelectedDevice;
            if (device == null) return;
            tab = 1;
            stepFilter = null;
            apkLogPath = null;

            var sheet = GenZApkTestSuit.LoadSheet(config, out _); // fresh values for this game (Taichi count)
            recTaichi = GenZApkTestSuit.SheetTaichi(sheet, config, GamePackage);
            var expected = GenZApkTestSuit.Expected(GamePackage, GameName, recTaichi);
            GenZApkTestSuit.ExpectedSigning(expected, sheet, config); // the keystore the APK must be signed with

            apkBuild = new TestReport(GenZApkTestSuit.SuiteName, apk: true);
            bool started = GenZApkTestSuit.Start(config, expected, useApkFile ? apkPath : null, device.serial, freshInstall, apkBuild, out var apk);
            recApk = apk;
            apkReport = apkBuild;
            if (started)
            {
                recording = true;
                recSerial = device.serial;
                recStartTicks = System.DateTime.Now.Ticks;
                nextPoll = EditorApplication.timeSinceStartup + 2;
                apkReport = GenZApkTestSuit.SessionReport(apkBuild, "", recApk, config, final: false, recTaichi);
            }
            SaveReport();
            Repaint();
        }

        // Recording: read the log in the background every few seconds and rebuild the live report.
        // Idle on this tab: refresh the phone list in the background.
        void ApkUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            DeepUpdate(now); // the Deep tab's phone session
            var newDevices = polledDevices;
            if (newDevices != null)
            {
                polledDevices = null;
                devices = newDevices;
                AfterDeviceRefresh();
                Repaint();
            }
            if (!recording)
            {
                if (tab == 0 || refreshingDevices || now < nextDeviceRefresh || devices == null) return; // the APK and Deep tabs list phones
                nextDeviceRefresh = now + DeviceRefreshSeconds;
                refreshingDevices = true;
                string pkg = GamePackage;
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { polledDevices = GenZApkTestSuit.Devices(pkg, out _); }
                    finally { refreshingDevices = false; }
                });
                return;
            }

            var log = polledLog;
            if (log != null)
            {
                polledLog = null;
                apkReport = GenZApkTestSuit.SessionReport(apkBuild, log, recApk, config, final: false, recTaichi);
                Repaint();
            }
            if (now >= nextRepaint) { nextRepaint = now + 1; Repaint(); } // the timer
            if (polling || now < nextPoll || config == null) return;
            nextPoll = now + PollSeconds;
            polling = true;
            string serial = recSerial;
            var settings = config.apk;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { polledLog = GenZApkTestSuit.ReadLiveLog(serial, settings); }
                finally { polling = false; }
            });
        }

        void FinishApkSession()
        {
            if (!recording) return;
            if (config == null) LoadConfig();
            string raw;
            try
            {
                EditorUtility.DisplayProgressBar(GenZApkTestSuit.SuiteName, "Reading the phone's log", 0.5f);
                raw = GenZApkTestSuit.ReadFullLog(recSerial);
                apkLogPath = GenZApkTestSuit.SaveLog(raw, recApk);
                GenZApkTestSuit.StopFirebaseDebug(recSerial);
            }
            finally { EditorUtility.ClearProgressBar(); }
            recording = false;
            apkReport = GenZApkTestSuit.SessionReport(apkBuild, raw, recApk, config, final: true, recTaichi);
            collapsed.Clear();
            collapsedFiltered.Clear();
            foreach (var s in apkReport.sections)
                if (s.Status == CheckStatus.Pass) collapsed.Add(s.title);
            SaveReport();
            Debug.Log($"[{GenZApkTestSuit.SuiteName}] {apkReport.Count(CheckStatus.Pass)} passed, {apkReport.Count(CheckStatus.Fail)} failed, {apkReport.Count(CheckStatus.Warn)} warnings. Log: {apkLogPath}");
            Repaint();
        }

        void CancelApkSession()
        {
            if (!recording) return;
            recording = false;
            GenZApkTestSuit.StopFirebaseDebug(recSerial);
            SaveReport();
            Repaint();
        }
    }
}
