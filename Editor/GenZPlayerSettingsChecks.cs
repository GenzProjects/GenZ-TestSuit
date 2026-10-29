// GenZ-TestSuit-v1.0
using System;
using System.Linq;
using UnityEditor;
using UnityEngine.Rendering;

namespace GenZ.TestSuit
{
    /// <summary>Step 2: the Android Player Settings of a release build (graphics, scripting backend, CPU, API levels).</summary>
    public static class GenZPlayerSettingsChecks
    {
        const string Where = "Player Settings → Android → Other Settings";

        public static void Run(TestReport report, AndroidPlayerRules r)
        {
            var sec = report.Section("Player Settings (Android)", TestStage.Integration);

            // Graphics
            bool auto = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android);
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            sec.Add("Auto Graphics API", auto == r.autoGraphicsApi ? CheckStatus.Pass : CheckStatus.Fail, OnOff(auto),
                auto == r.autoGraphicsApi ? "" : $"Must be {OnOff(r.autoGraphicsApi)}: {Where} → Rendering → Auto Graphics API");
            var forbidden = auto ? new string[0] : apis.Select(a => a.ToString()).Where(a => r.forbiddenGraphicsApis.Contains(a, StringComparer.OrdinalIgnoreCase)).ToArray();
            sec.Add("Graphics APIs", forbidden.Length == 0 && !auto ? CheckStatus.Pass : CheckStatus.Fail, string.Join(", ", apis),
                auto ? "Unity picks the APIs itself while Auto Graphics API is on"
                : forbidden.Length > 0 ? $"Remove {string.Join(", ", forbidden)} from the Graphics APIs list"
                : $"No {string.Join(" / ", r.forbiddenGraphicsApis)}");
            Bool(sec, "Require ES3.1", PlayerSettings.openGLRequireES31, r.requireES31);
            Bool(sec, "Require ES3.1+AEP", PlayerSettings.openGLRequireES31AEP, r.requireES31AEP);
            Bool(sec, "Require ES3.2", PlayerSettings.openGLRequireES32, r.requireES32);

            // Scripting backend and CPU architectures
            var backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android).ToString();
            bool backendOk = string.Equals(backend, r.scriptingBackend, StringComparison.OrdinalIgnoreCase);
            sec.Add("Scripting Backend", backendOk ? CheckStatus.Pass : CheckStatus.Fail, backend,
                backendOk ? "" : $"Must be {r.scriptingBackend}: {Where} → Configuration → Scripting Backend");

            var arch = PlayerSettings.Android.targetArchitectures;
            var ticked = Enum.GetValues(typeof(AndroidArchitecture)).Cast<AndroidArchitecture>()
                             .Where(a => a != AndroidArchitecture.None && a != AndroidArchitecture.All && IsSingleFlag((int)a) && (arch & a) == a)
                             .Select(a => a.ToString()).ToList();
            var missing = r.targetArchitectures.Where(a => !ticked.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
            var extra = ticked.Where(a => !r.targetArchitectures.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
            sec.Add("Target Architectures", missing.Count == 0 && extra.Count == 0 ? CheckStatus.Pass : CheckStatus.Fail, string.Join(", ", ticked),
                missing.Count == 0 && extra.Count == 0 ? $"Only {string.Join(" and ", r.targetArchitectures)}"
                : string.Join("; ", new[] { missing.Count > 0 ? "Tick " + string.Join(", ", missing) : null, extra.Count > 0 ? "Untick " + string.Join(", ", extra) : null }.Where(x => x != null))
                  + $": {Where} → Configuration → Target Architectures");

            // API levels
            int min = (int)PlayerSettings.Android.minSdkVersion;
            sec.Add("Minimum API Level", min == r.minSdk ? CheckStatus.Pass : CheckStatus.Fail, ApiName(min),
                min == r.minSdk ? "" : $"Must be {ApiName(r.minSdk)}: {Where} → Identification → Minimum API Level");
            int target = (int)PlayerSettings.Android.targetSdkVersion;
            sec.Add("Target API Level", target == r.targetSdk ? CheckStatus.Pass : CheckStatus.Fail, target == 0 ? "Automatic (highest installed)" : ApiName(target),
                target == r.targetSdk ? "" : $"Must be {ApiName(r.targetSdk)}: {Where} → Identification → Target API Level");
        }

        static void Bool(ReportSection sec, string label, bool value, bool expected) =>
            sec.Add(label, value == expected ? CheckStatus.Pass : CheckStatus.Fail, OnOff(value),
                value == expected ? "" : $"Must be {OnOff(expected)}: {Where} → Rendering → {label}");

        static string OnOff(bool b) => b ? "On" : "Off";

        static bool IsSingleFlag(int v) => v != 0 && (v & (v - 1)) == 0;

        static string ApiName(int api)
        {
            string version;
            switch (api)
            {
                case 21: version = "5.0"; break;
                case 22: version = "5.1"; break;
                case 23: version = "6.0"; break;
                case 24: version = "7.0"; break;
                case 25: version = "7.1"; break;
                case 26: version = "8.0"; break;
                case 27: version = "8.1"; break;
                case 28: version = "9"; break;
                case 29: version = "10"; break;
                case 30: version = "11"; break;
                case 31: version = "12"; break;
                case 32: version = "12L"; break;
                case 33: version = "13"; break;
                case 34: version = "14"; break;
                case 35: version = "15"; break;
                case 36: version = "16"; break;
                default: return "API " + api;
            }
            return $"Android {version} (API {api})";
        }
    }
}
