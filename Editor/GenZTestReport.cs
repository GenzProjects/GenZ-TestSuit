// GenZ-TestSuit-v1.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GenZ.TestSuit
{
    // Order matters: higher = worse, used to roll up a section's status.
    public enum CheckStatus { Pass, Info, Warn, Fail }

    /// <summary>The window's steps. Every check belongs to one; a section can hold checks of several steps.</summary>
    public enum TestStage
    {
        Ids,         // 1. IDs Check: every ad unit / app ID / SDK key / token, its format and the sheet
        Integration, // 2. SDK Integration: SDK files, settings assets, scene objects and switches, Android build setup
        Taichi,      // 3. Taichi: Taichi 1.0 / 2.0 revenue events switched on and wired correctly
        AdCalling,   // 4. Ad Calling Scenarios: the code checks (waterfall, show/hide, reloads, timer, revenue)

        // GenZ-ApkTestSuit (a release APK on a phone)
        ApkBuild,    // 1. Build & Device: the APK file, install and launch
        SdkInit,     // 2. SDK Init: every SDK started without errors ([GenZ] lines)
        Events,      // 3. Analytics Events: Firebase, GameAnalytics, Adjust, AppMetrica fired and sent
        Ads,         // 4. Ads: impressions per format, revenue reported, Taichi
        Stability    // 5. Stability: crashes, ANRs, script exceptions
    }

    [Serializable]
    public class CheckResult
    {
        public string label;
        public CheckStatus status;
        public TestStage stage;
        public string value;   // what the project has (shortened for display)
        public string message; // why it passed / failed
        public string file;    // optional: script to open (Assets/...)
        public int line;       // optional: line in that script

        public CheckResult() { } // for deserialization

        public CheckResult(string label, CheckStatus status, string value, string message)
        {
            this.label = label;
            this.status = status;
            this.value = value;
            this.message = message;
        }
    }

    /// <summary>An ID that differs from the Google Sheet, and where to write the sheet value (Auto Apply).</summary>
    [Serializable]
    public class IdFix
    {
        public string label;          // "Rule › Field"
        public string current;        // value in the project (raw)
        public string expected;       // value from the sheet
        public bool secret;           // shorten in dialogs and logs
        public string propertyPath;   // serialized property to set
        public string assetPath;      // settings asset, or…
        public string scenePath, objectPath, componentType; // …a component in a scene, or…
        public string codeFile;       // …a number written in a script (Assets/...): the Taichi 2.0 default without an Inspector field
        public int codeLine;
        public string codeContext;    // its line up to the end of the number, exactly as the check read it
        public string codeValue;      // the number as written, replaced by expected

        public string Where => !string.IsNullOrEmpty(assetPath) ? assetPath
                             : !string.IsNullOrEmpty(codeFile) ? $"{codeFile}:{codeLine}"
                             : $"{scenePath} › {objectPath} ({componentType})";
    }

    [Serializable]
    public class ReportSection
    {
        public string title;
        /// <summary>Step given to checks added from now on. Change it before adding a check that belongs to another step.</summary>
        public TestStage stage;
        public List<CheckResult> checks = new List<CheckResult>();

        public ReportSection() { } // for deserialization
        public ReportSection(string title, TestStage stage) { this.title = title; this.stage = stage; }

        public CheckStatus Status => checks.Count == 0 ? CheckStatus.Info : checks.Max(c => c.status);

        public CheckResult Add(string label, CheckStatus status, string value, string message)
        {
            var r = new CheckResult(label, status, value, message) { stage = stage };
            checks.Add(r);
            return r;
        }

        public CheckResult Add(string label, CheckStatus status, string value, string message, string file, int line)
        {
            var r = Add(label, status, value, message);
            r.file = file;
            r.line = line;
            return r;
        }
    }

    // Serializable (JsonUtility) so the window keeps the last run across script reloads.
    // After a round trip, null strings come back as "": test them with string.IsNullOrEmpty.
    [Serializable]
    public class TestReport
    {
        public string suiteName;
        public long timeTicks = DateTime.Now.Ticks; // DateTime itself isn't serialized
        public DateTime time => new DateTime(timeTicks);
        public List<ReportSection> sections = new List<ReportSection>();
        /// <summary>IDs the sheet says are wrong or empty, with where to write the sheet value.</summary>
        public List<IdFix> idFixes = new List<IdFix>();
        /// <summary>GAAdMobIntegration.cs ad format strings that differ from GAMaxIntegration.cs ("Fix GA Ad Formats").</summary>
        public List<IdFix> gaFormatFixes = new List<IdFix>();

        /// <summary>True for a GenZ-ApkTestSuit report: it has the APK steps instead of the Editor ones.</summary>
        public bool apk;

        public TestReport() { } // for deserialization
        public TestReport(string suiteName, bool apk = false) { this.suiteName = suiteName; this.apk = apk; }

        public ReportSection Section(string title, TestStage stage)
        {
            var s = new ReportSection(title, stage);
            sections.Add(s);
            return s;
        }

        public static readonly TestStage[] Stages = { TestStage.Ids, TestStage.Integration, TestStage.Taichi, TestStage.AdCalling };
        public static readonly TestStage[] ApkStages = { TestStage.ApkBuild, TestStage.SdkInit, TestStage.Events, TestStage.Ads, TestStage.Stability };

        /// <summary>This report's steps, in order.</summary>
        public TestStage[] StageList => apk ? ApkStages : Stages;

        public static string StageName(TestStage s)
        {
            switch (s)
            {
                case TestStage.Ids: return "IDs Check";
                case TestStage.Integration: return "SDK Integration";
                case TestStage.Taichi: return "Taichi";
                case TestStage.AdCalling: return "Ad Calling Scenarios";
                case TestStage.ApkBuild: return "Build & Device";
                case TestStage.SdkInit: return "SDK Init";
                case TestStage.Events: return "Analytics Events";
                case TestStage.Ads: return "Ads";
                default: return "Stability";
            }
        }

        public IEnumerable<CheckResult> Checks(TestStage stage) => sections.SelectMany(s => s.checks).Where(c => c.stage == stage);

        public int Count(CheckStatus status) => sections.Sum(s => s.checks.Count(c => c.status == status));
        public int Count(TestStage stage, CheckStatus status) => Checks(stage).Count(c => c.status == status);

        /// <summary>Worst status of the step's checks; null when the step has none (not run).</summary>
        public CheckStatus? StageStatus(TestStage stage)
        {
            var list = Checks(stage).ToList();
            return list.Count == 0 ? (CheckStatus?)null : list.Max(c => c.status);
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {suiteName} report");
            sb.AppendLine();
            sb.AppendLine($"_{GenZTestSuitInfo.Name}_");
            sb.AppendLine();
            sb.AppendLine($"{time:yyyy-MM-dd HH:mm} · ✅ {Count(CheckStatus.Pass)} passed · ❌ {Count(CheckStatus.Fail)} failed · ⚠️ {Count(CheckStatus.Warn)} warnings");
            var stages = StageList;
            for (int i = 0; i < stages.Length; i++)
            {
                var stage = stages[i];
                if (!Checks(stage).Any()) continue;
                sb.AppendLine();
                sb.AppendLine($"# Step {i + 1} · {StageName(stage)} — ✅ {Count(stage, CheckStatus.Pass)} · ❌ {Count(stage, CheckStatus.Fail)} · ⚠️ {Count(stage, CheckStatus.Warn)}");
                foreach (var s in sections)
                {
                    var checks = s.checks.Where(c => c.stage == stage).ToList();
                    if (checks.Count == 0) continue;
                    sb.AppendLine();
                    sb.AppendLine($"## {Icon(checks.Max(c => c.status))} {s.title}");
                    sb.AppendLine();
                    sb.AppendLine("| | Check | Value | Result |");
                    sb.AppendLine("|---|---|---|---|");
                    foreach (var c in checks)
                        sb.AppendLine($"| {Icon(c.status)} | {Esc(c.label)} | {Esc(c.value)} | {Esc(c.message)}{(string.IsNullOrEmpty(c.file) ? "" : $" (`{c.file}:{c.line}`)")} |");
                }
            }
            return sb.ToString();
        }

        static string Icon(CheckStatus s) =>
            s == CheckStatus.Pass ? "✅" : s == CheckStatus.Fail ? "❌" : s == CheckStatus.Warn ? "⚠️" : "ℹ️";

        static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("|", "\\|").Replace("\n", " ");
    }
}
