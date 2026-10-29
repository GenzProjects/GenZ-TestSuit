// GenZ-TestSuit-v1.0
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

        const string PluginsAndroid = "Assets/Plugins/Android";
        const string FirebaseXml = PluginsAndroid + "/FirebaseApp.androidlib/res/values/google-services.xml";
        const string DesktopJson = "Assets/StreamingAssets/google-services-desktop.json";
        const string MainGradle = PluginsAndroid + "/mainTemplate.gradle";
        const string GradleProperties = PluginsAndroid + "/gradleTemplate.properties";
        const string MainManifest = PluginsAndroid + "/AndroidManifest.xml";

        static readonly Regex SpecRegex = new Regex(@"<androidPackage\s+spec\s*=\s*""([^""]+)""");

        public static void Run(TestReport report, string packageName, IntegrationSettings s)
        {
            var projectSettings = Read("ProjectSettings/ProjectSettings.asset") ?? "";
            CheckFirebase(report.Section(SectionFirebase, TestStage.Integration), packageName, s);
            CheckDependencies(report.Section(SectionDeps, TestStage.Integration), s, projectSettings);
            CheckBuildSetup(report.Section(SectionBuild, TestStage.Integration), projectSettings);
        }

        // ================================================================= Firebase

        static void CheckFirebase(ReportSection sec, string pkg, IntegrationSettings s)
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

        static void CheckDependencies(ReportSection sec, IntegrationSettings s, string projectSettings)
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
                    installed ? (groups.ContainsKey(folder) ? "SDK folder and its Android dependencies file found" : "SDK folder found") : "SDK folder is missing — import the SDK");
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

        // ================================================================= Gradle / manifest

        static void CheckBuildSetup(ReportSection sec, string projectSettings)
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
            if (manifest != null && Regex.IsMatch(projectSettings, @"useCustomMainManifest:\s*1"))
            {
                var dbg = Regex.Match(manifest, @"android:debuggable\s*=\s*""true""");
                sec.Add("Manifest not debuggable", dbg.Success ? CheckStatus.Fail : CheckStatus.Pass, MainManifest,
                    dbg.Success ? "android:debuggable=\"true\" — Google Play rejects debuggable builds" : "No android:debuggable=\"true\"",
                    MainManifest, dbg.Success ? manifest.Substring(0, dbg.Index).Count(ch => ch == '\n') + 1 : 1);
            }
        }

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
