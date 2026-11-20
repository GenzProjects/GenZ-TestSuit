// GenZ-TestSuit-v1.0.1
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GenZ.TestSuit
{
    public class ToolResult
    {
        public int exitCode;
        public string output = "", error = "";
        public bool Ok => exitCode == 0;
        public string All => (output + "\n" + error).Trim();
    }

    [Serializable]
    public class AdbDevice
    {
        public string serial, state, model, android;
        public string installed;     // the selected game's installed version on this phone, e.g. "2.7 (40)"; "" = not installed
        public bool installChecked;  // installed was read (null strings don't survive serialization)
        public bool Ready => state == "device";
        public string Name => string.IsNullOrEmpty(model) ? serial : model.Replace('_', ' ');
        public string Label => Name + "  (" + serial + ")" + (Ready ? "" : "  — " + state);
    }

    /// <summary>
    /// GenZ-ApkTestSuit: install a release APK on a phone over USB, launch it with Firebase debug logging on,
    /// and check the session from logcat. Nothing keeps running between calls: the phone's log buffer is
    /// enlarged at the start and read on each poll and on Finish, so a script reload can't break a session.
    /// </summary>
    public static class GenZApkTestSuit
    {
        public const string SuiteName = "GenZ-ApkTestSuit";

        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static string LogFolder => Path.Combine(ProjectRoot, "Logs", "GenZ-ApkTestSuit");
        static bool Windows => Application.platform == RuntimePlatform.WindowsEditor;

        // ---------------------------------------------------------------- tools from Unity's Android SDK

        public static string SdkRoot =>
            FirstDir(UnityAndroidSetting("sdkRootPath"), EditorPrefs.GetString("AndroidSdkRoot"),
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/SDK"));

        public static string JdkRoot =>
            FirstDir(UnityAndroidSetting("jdkRootPath"), EditorPrefs.GetString("JdkPath"),
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer/OpenJDK"));

        // UnityEditor.Android.AndroidExternalToolsSettings lives in the Android module, which an asmdef can't
        // reference safely (the module may not be installed), so it's read by reflection.
        static string UnityAndroidSetting(string property)
        {
            try
            {
                var type = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
                return type?.GetProperty(property, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) as string;
            }
            catch { return null; }
        }

        static string FirstDir(params string[] dirs) => dirs.FirstOrDefault(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)) ?? "";

        public static string Adb
        {
            get
            {
                var path = Path.Combine(SdkRoot, "platform-tools", Windows ? "adb.exe" : "adb");
                return File.Exists(path) ? path : "adb"; // fall back to PATH
            }
        }

        /// <summary>A tool from the newest build-tools version, or null.</summary>
        public static string BuildTool(string name)
        {
            var dir = Path.Combine(SdkRoot, "build-tools");
            if (!Directory.Exists(dir)) return null;
            foreach (var v in Directory.GetDirectories(dir).OrderByDescending(d => ParseVersion(Path.GetFileName(d))))
                foreach (var file in Windows ? new[] { name + ".exe", name + ".bat" } : new[] { name })
                    if (File.Exists(Path.Combine(v, file))) return Path.Combine(v, file);
            return null;
        }

        static Version ParseVersion(string s) => Version.TryParse(Regex.Match(s, @"^\d+(\.\d+)*").Value, out var v) ? v : new Version(0, 0);

        /// <param name="env">Extra environment variables (e.g. a password read with keytool's -storepass:env, so it isn't on the command line).</param>
        /// <param name="noInput">Close the tool's stdin, so a password prompt (keytool) gets end-of-input instead of waiting.</param>
        public static ToolResult Run(string exe, string args, int timeoutMs = 60000, string javaHome = null,
            IDictionary<string, string> env = null, bool noInput = false)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = noInput,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                if (!string.IsNullOrEmpty(javaHome))
                {
                    psi.EnvironmentVariables["JAVA_HOME"] = javaHome;
                    psi.EnvironmentVariables["PATH"] = Path.Combine(javaHome, "bin") + Path.PathSeparator + psi.EnvironmentVariables["PATH"];
                }
                if (env != null)
                    foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
                using (var p = Process.Start(psi))
                {
                    if (noInput) p.StandardInput.Close();
                    var stdout = p.StandardOutput.ReadToEndAsync();
                    var stderr = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return new ToolResult { exitCode = -1, error = $"{Path.GetFileName(exe)} timed out after {timeoutMs / 1000} s" };
                    }
                    return new ToolResult { exitCode = p.ExitCode, output = stdout.Result, error = stderr.Result };
                }
            }
            catch (Exception e)
            {
                return new ToolResult { exitCode = -1, error = $"Couldn't run {exe}: {e.Message}" };
            }
        }

        public static ToolResult AdbRun(string serial, string args, int timeoutMs = 60000) =>
            Run(Adb, (string.IsNullOrEmpty(serial) ? "" : $"-s {serial} ") + args, timeoutMs);

        // ---------------------------------------------------------------- devices and the APK

        /// <summary>
        /// Every attached phone, and for the ready ones their Android version and whether <paramref name="package"/>
        /// is installed. Safe to call off the main thread.
        /// </summary>
        public static List<AdbDevice> Devices(string package, out string error)
        {
            var r = Run(Adb, "devices -l", 30000);
            error = r.Ok ? null : r.All;
            var list = new List<AdbDevice>();
            foreach (var line in r.output.Split('\n').Skip(1))
            {
                var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var model = parts.FirstOrDefault(p => p.StartsWith("model:"));
                var d = new AdbDevice { serial = parts[0], state = parts[1], model = model?.Substring(6) };
                if (d.Ready)
                {
                    d.android = Prop(d.serial, "ro.build.version.release");
                    if (!string.IsNullOrEmpty(package))
                    {
                        var app = ApkInfo.FromDumpsys(package, AdbRun(d.serial, "shell dumpsys package " + package, 15000).output);
                        d.installed = app == null ? "" : $"{app.versionName} ({app.versionCode})";
                        d.installChecked = true;
                    }
                }
                list.Add(d);
            }
            return list;
        }

        /// <summary>The newest .apk in the project root, where Unity builds are left.</summary>
        public static string NewestApk() =>
            new DirectoryInfo(ProjectRoot).GetFiles("*.apk").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;

        public static string ProjectPackage => PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);

        /// <summary>The game to test. Its version is only known (Player Settings) when it's this project.</summary>
        public static ExpectedBuild Expected(string package, string gameName, string taichiImpressions)
        {
            bool thisProject = string.IsNullOrEmpty(package) || package == ProjectPackage;
            return new ExpectedBuild
            {
                package = thisProject ? ProjectPackage : package,
                gameName = gameName,
                versionName = thisProject ? PlayerSettings.bundleVersion : null,
                versionCode = thisProject ? PlayerSettings.Android.bundleVersionCode : 0,
                taichiImpressions = taichiImpressions
            };
        }

        // ---------------------------------------------------------------- the studio sheet's games

        /// <summary>Downloads the sheet (blocking, with a progress bar). Null on failure, with the reason in <paramref name="error"/>.</summary>
        public static GenZSheetReader LoadSheet(GenZTestSuitConfig cfg, out string error)
        {
            var reader = new GenZSheetReader();
            error = reader.Download(cfg.EffectiveSheetUrl, cfg.SheetTabsFor(GenZTestSuitConfig.IosTarget)) ? null : reader.error; // the Game list: iOS apps on iOS
            return error == null ? reader : null;
        }

        /// <summary>The sheet's Taichi 2.0 impression count for <paramref name="package"/>, or null.</summary>
        public static string SheetTaichi(GenZSheetReader sheet, GenZTestSuitConfig cfg, string package)
        {
            if (sheet == null || string.IsNullOrEmpty(package) || !sheet.SelectGame(package, cfg.packageColumnNames)) return null;
            var v = sheet.Lookup(cfg.taichi.taichi2SheetColumns, out _);
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        // ---------------------------------------------------------------- session

        /// <summary>
        /// Step 1, then prepares the phone and launches the game: install (when <paramref name="apkPath"/> is given;
        /// null = test the game already installed), bigger log buffer, cleared log, Firebase debug logging on
        /// (the same as GenZ → Firebase → StartADB), cold start.
        /// Returns false when there's nothing to record (unreadable APK, failed install, game not installed).
        /// </summary>
        public static bool Start(GenZTestSuitConfig cfg, ExpectedBuild expected, string apkPath, string serial, bool freshInstall, TestReport report, out ApkInfo apk)
        {
            var s = cfg.apk;
            bool fromApk = !string.IsNullOrEmpty(apkPath);
            try
            {
                var device = new DeviceInfo { serial = serial };
                device.model = Prop(serial, "ro.product.model");
                device.android = Prop(serial, "ro.build.version.release");
                int.TryParse(Prop(serial, "ro.build.version.sdk"), out device.sdk);
                device.abis = Prop(serial, "ro.product.cpu.abilist");

                if (fromApk)
                {
                    Progress("Reading the APK", 0.05f);
                    var aapt = BuildTool("aapt2");
                    var badging = aapt == null ? new ToolResult { exitCode = -1 } : Run(aapt, $"dump badging \"{apkPath}\"");
                    apk = ApkInfo.FromBadging(apkPath, badging.output);

                    Progress("Checking the signature", 0.15f);
                    var apksigner = BuildTool("apksigner");
                    string signer = apksigner == null ? null : Run(apksigner, $"verify --print-certs \"{apkPath}\"", 120000, JdkRoot).All;
                    GenZApkChecks.CheckBuild(report, apk, expected, s, signer);
                    if (string.IsNullOrEmpty(apk.package)) return false;

                    if (freshInstall)
                    {
                        Progress("Uninstalling the old version", 0.25f);
                        AdbRun(serial, "uninstall " + apk.package, 60000);
                    }
                    Progress("Installing " + Path.GetFileName(apkPath), 0.35f);
                    var install = AdbRun(serial, $"install -r -d \"{apkPath}\"", 300000);
                    device.installOutput = install.All;
                    device.installed = install.All.Contains("Success");
                    if (!device.installed)
                    {
                        GenZApkChecks.CheckDevice(report, device, apk);
                        return false;
                    }
                    var installed = ApkInfo.FromDumpsys(apk.package, AdbRun(serial, "shell dumpsys package " + apk.package).output);
                    if (installed != null) device.installedVersionCode = installed.versionCode;
                }
                else
                {
                    Progress("Reading the installed game", 0.1f);
                    apk = ApkInfo.FromDumpsys(expected.package, AdbRun(serial, "shell dumpsys package " + expected.package).output);
                    if (apk == null)
                    {
                        apk = new ApkInfo { package = expected.package, path = "" };
                        var sec = report.Section("Installed game", TestStage.ApkBuild);
                        sec.Add("Installed", CheckStatus.Fail, "not installed",
                            $"{expected.package} isn't installed on {device.model}. Install it, or pick an APK to install.");
                        return false;
                    }
                    // Its APK, copied off the phone, so the signature can be checked too.
                    Progress("Copying the installed APK to check its signature", 0.15f);
                    var pulled = PullApk(serial, apk.package);
                    var signerTool = BuildTool("apksigner");
                    string installedSigner = pulled == null || signerTool == null ? null : Run(signerTool, $"verify --print-certs \"{pulled}\"", 120000, JdkRoot).All;
                    GenZApkChecks.CheckBuild(report, apk, expected, s, installedSigner, fromApk: false);
                    device.installed = true;
                    device.installedVersionCode = apk.versionCode;
                    if (freshInstall)
                    {
                        Progress("Clearing the game's data", 0.25f);
                        AdbRun(serial, "shell pm clear " + apk.package, 60000); // a new player, without reinstalling
                    }
                }
                if (string.IsNullOrEmpty(apk.launchActivity)) apk.launchActivity = LaunchActivity(serial, apk.package);

                Progress("Preparing the log", 0.8f);
                AdbRun(serial, $"logcat -G {Math.Max(1, s.logcatBufferMb)}M");
                AdbRun(serial, "logcat -c");
                AdbRun(serial, "shell setprop debug.firebase.analytics.app " + apk.package);
                AdbRun(serial, "shell setprop log.tag.FA VERBOSE");
                AdbRun(serial, "shell setprop log.tag.FA-SVC VERBOSE");

                Progress("Launching the game", 0.9f);
                AdbRun(serial, "shell am force-stop " + apk.package);
                var launch = string.IsNullOrEmpty(apk.launchActivity)
                    ? AdbRun(serial, $"shell monkey -p {apk.package} -c android.intent.category.LAUNCHER 1")
                    : AdbRun(serial, $"shell am start -W -n {apk.package}/{apk.launchActivity}");
                device.launchOutput = launch.All;
                var total = Regex.Match(launch.output, @"TotalTime:\s*(\d+)");
                if (total.Success) device.launchMs = int.Parse(total.Groups[1].Value);

                GenZApkChecks.CheckDevice(report, device, apk);
                return true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Copies the installed game's base APK into Temp/GenZ-ApkTestSuit (adb pull). Null on failure.</summary>
        static string PullApk(string serial, string package)
        {
            var path = AdbRun(serial, "shell pm path " + package, 15000).output.Split('\n')
                .Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("package:") && l.EndsWith("base.apk"))?.Substring(8);
            if (string.IsNullOrEmpty(path)) return null;
            var dir = Path.Combine(ProjectRoot, "Temp", SuiteName);
            Directory.CreateDirectory(dir);
            var local = Path.Combine(dir, package + ".apk");
            var r = AdbRun(serial, $"pull \"{path}\" \"{local}\"", 120000);
            return r.Ok && File.Exists(local) ? local : null;
        }

        /// <summary>
        /// The release key the APK must be signed with. This project: the keystore in Publishing Settings. Another game:
        /// the keystore the sheet names (KeyStore column), looked for next to this project's keystore.
        /// </summary>
        public static void ExpectedSigning(ExpectedBuild expected, GenZSheetReader sheet, GenZTestSuitConfig cfg)
        {
            var s = cfg.signing ?? new SigningSettings();
            if (!s.enabled) { expected.certNote = "the signing check is off in the config"; return; }
            KeystoreCert cert;
            if (expected.versionName != null) // this project
            {
                cert = GenZSigning.ProjectCertificate(out var why);
                if (cert == null) { expected.certNote = why; return; }
            }
            else
            {
                string value = null;
                if (sheet != null && sheet.SelectGame(expected.package, cfg.packageColumnNames)) value = sheet.Lookup(s.sheetColumns, out _);
                if (string.IsNullOrWhiteSpace(value)) { expected.certNote = "the sheet names no keystore for " + expected.package; return; }
                cert = GenZSigning.FindKeystore(value, s);
                if (cert?.sha256 == null)
                {
                    expected.certNote = $"the sheet's keystore \"{value.Trim()}\" isn't next to this project's keystore ({System.IO.Path.GetDirectoryName(GenZSigning.ProjectKeystorePath)})"
                                        + (cert?.error != null ? ": " + cert.error : "");
                    return;
                }
            }
            expected.certSha256 = cert.sha256;
            expected.certSource = cert.Label;
        }

        static string Prop(string serial, string name) => AdbRun(serial, "shell getprop " + name, 15000).output.Trim();

        /// <summary>The game's launcher activity as the phone resolves it ("pkg/activity" → activity), or null.</summary>
        internal static string LaunchActivity(string serial, string package)
        {
            var output = AdbRun(serial, $"shell cmd package resolve-activity --brief -c android.intent.category.LAUNCHER {package}", 15000).output;
            var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith(package + "/"));
            if (line == null) return null;
            var activity = line.Substring(package.Length + 1);
            return activity.StartsWith(".") ? package + activity : activity;
        }

        static void Progress(string info, float p) => EditorUtility.DisplayProgressBar(SuiteName, info, p);

        /// <summary>The tags the live view needs. Safe to call off the main thread.</summary>
        public static string ReadLiveLog(string serial, ApkSettings s) =>
            AdbRun(serial, $"logcat -d -v threadtime -s Unity FA FA-SVC AndroidRuntime {s.gaTag} {s.adjustTag}", 30000).output;

        /// <summary>Every buffer, for the final check and the saved log.</summary>
        public static string ReadFullLog(string serial) =>
            AdbRun(serial, "logcat -d -v threadtime -b main -b system -b crash -b events", 120000).output;

        /// <summary>The same as GenZ → Firebase → StopADB.</summary>
        public static void StopFirebaseDebug(string serial) =>
            AdbRun(serial, "shell setprop debug.firebase.analytics.app .none.", 15000);

        /// <param name="folder">Null = the APK suite's folder (Logs/GenZ-ApkTestSuit).</param>
        public static string SaveLog(string raw, ApkInfo apk, string folder = null)
        {
            try
            {
                folder = folder ?? LogFolder;
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, $"{apk.package}_{apk.versionName}-{apk.versionCode}_{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.WriteAllText(path, raw ?? "");
                return path;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[{SuiteName}] Couldn't save the log: {e.Message}");
                return null;
            }
        }

        /// <summary>Step 1's sections (kept from the start) plus the session checks on <paramref name="raw"/>.</summary>
        public static TestReport SessionReport(TestReport buildPart, string raw, ApkInfo apk, GenZTestSuitConfig cfg, bool final, string sheetTaichi)
        {
            var report = new TestReport(SuiteName, apk: true);
            if (buildPart != null) report.sections.AddRange(buildPart.sections);
            var log = GenZApkChecks.ParseLogcat((raw ?? "").Split('\n'));
            GenZApkChecks.CheckSession(report, log, apk, cfg.apk, cfg.taichi, final, sheetTaichi);
            return report;
        }
    }
}
