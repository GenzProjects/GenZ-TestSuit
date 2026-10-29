// GenZ-TestSuit-v1.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GenZ.TestSuit
{
    /// <summary>A key's certificate, read from a keystore with keytool.</summary>
    public class KeystoreCert
    {
        public string file, alias, sha256, owner, error; // sha256: lower-case hex, no colons
        public string Label => $"{Path.GetFileName(file)} ({alias})";
    }

    /// <summary>
    /// Android signing: the release keystore is set, it's the one the studio sheet names for this game, and (APK suite)
    /// the APK is signed with that keystore's certificate. The certificate is read with keytool from Unity's JDK. A JKS
    /// keystore lists its certificates without a password; for PKCS12 the password from Publishing Settings is used.
    /// </summary>
    public static class GenZSigning
    {
        const string PasswordVar = "GENZ_KEYSTORE_PASS";

        static string Keytool
        {
            get
            {
                var path = Path.Combine(GenZApkTestSuit.JdkRoot, "bin", Application.platform == RuntimePlatform.WindowsEditor ? "keytool.exe" : "keytool");
                return File.Exists(path) ? path : "keytool";
            }
        }

        /// <summary>The keystore in Publishing Settings, as a full path (Unity stores it relative to the project or absolute).</summary>
        public static string ProjectKeystorePath
        {
            get
            {
                var name = PlayerSettings.Android.keystoreName;
                if (string.IsNullOrWhiteSpace(name)) return "";
                return Path.IsPathRooted(name) ? name : Path.GetFullPath(Path.Combine(GenZApkTestSuit.ProjectRoot, name));
            }
        }

        /// <summary>The certificate of <paramref name="alias"/> in <paramref name="keystore"/>; <see cref="KeystoreCert.error"/> says why not.</summary>
        public static KeystoreCert ReadCertificate(string keystore, string alias, string password)
        {
            var cert = new KeystoreCert { file = keystore, alias = alias };
            if (!File.Exists(keystore)) { cert.error = "Keystore file not found"; return cert; }
            var args = $"-list -v -keystore \"{keystore}\"" + (string.IsNullOrEmpty(alias) ? "" : $" -alias \"{alias}\"");
            Dictionary<string, string> env = null;
            if (!string.IsNullOrEmpty(password))
            {
                args += $" -storepass:env {PasswordVar}";
                env = new Dictionary<string, string> { { PasswordVar, password } };
            }
            var r = GenZApkTestSuit.Run(Keytool, args, 30000, GenZApkTestSuit.JdkRoot, env, noInput: true);
            ParseKeytool(r.All, cert);
            if (cert.sha256 == null)
                cert.error = FirstError(r.All) ?? "keytool listed no certificate";
            return cert;
        }

        /// <summary>Reads the first entry's SHA-256 fingerprint, owner and alias from "keytool -list -v" output.</summary>
        public static void ParseKeytool(string output, KeystoreCert cert)
        {
            var sha = Regex.Match(output ?? "", @"SHA-?256:\s*([0-9A-Fa-f:]{64,95})");
            if (sha.Success) cert.sha256 = NormalizeSha(sha.Groups[1].Value);
            var owner = Regex.Match(output ?? "", @"Owner:\s*(.+)");
            if (owner.Success) cert.owner = owner.Groups[1].Value.Trim();
            var alias = Regex.Match(output ?? "", @"Alias name:\s*(.+)");
            if (alias.Success && string.IsNullOrEmpty(cert.alias)) cert.alias = alias.Groups[1].Value.Trim();
        }

        public static string NormalizeSha(string s) => Regex.Replace(s ?? "", "[^0-9A-Fa-f]", "").ToLowerInvariant();

        static string FirstError(string output)
        {
            var line = (output ?? "").Split('\n').Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("keytool error", StringComparison.OrdinalIgnoreCase) || l.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0);
            return line == null ? null : Regex.Replace(line, @"^keytool error:\s*", "", RegexOptions.IgnoreCase);
        }

        /// <summary>The project's release key (Publishing Settings), or null with the reason when there isn't one.</summary>
        public static KeystoreCert ProjectCertificate(out string why)
        {
            why = null;
            if (!PlayerSettings.Android.useCustomKeystore) { why = "Custom Keystore is off in Publishing Settings (debug key)"; return null; }
            var cert = ReadCertificate(ProjectKeystorePath, PlayerSettings.Android.keyaliasName, PlayerSettings.Android.keystorePass);
            if (cert.sha256 == null) { why = $"Couldn't read {Path.GetFileName(cert.file)}: {cert.error}"; return null; }
            return cert;
        }

        /// <summary>Sheet value ("gamestoplay") vs a keystore: equal to its file name (no extension) or its key alias, ignoring case and symbols.</summary>
        public static bool Matches(string sheetValue, string keystorePath, string alias, out string how)
        {
            var want = GenZSheetReader.Normalize(sheetValue);
            how = null;
            if (want.Length == 0) return false;
            if (GenZSheetReader.Normalize(Path.GetFileNameWithoutExtension(keystorePath ?? "")) == want) { how = "keystore file " + Path.GetFileName(keystorePath); return true; }
            if (GenZSheetReader.Normalize(alias) == want) { how = "key alias " + alias; return true; }
            return false;
        }

        /// <summary>
        /// The keystore the sheet names for another game: a file next to this project's keystore whose name (or only key
        /// alias) matches the sheet value. Its first key is used. Null when none is found.
        /// </summary>
        public static KeystoreCert FindKeystore(string sheetValue, SigningSettings s)
        {
            var folder = Path.GetDirectoryName(ProjectKeystorePath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder) || GenZSheetReader.Normalize(sheetValue).Length == 0) return null;
            var files = Directory.GetFiles(folder).Where(f => s.keystoreExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))).ToList();
            var byName = files.FirstOrDefault(f => Matches(sheetValue, f, null, out _));
            if (byName != null)
            {
                bool same = string.Equals(Path.GetFullPath(byName), Path.GetFullPath(ProjectKeystorePath), StringComparison.OrdinalIgnoreCase);
                return ReadCertificate(byName, same ? PlayerSettings.Android.keyaliasName : null, same ? PlayerSettings.Android.keystorePass : null);
            }
            foreach (var f in files) // a keystore whose key alias is the sheet value
            {
                var cert = ReadCertificate(f, null, null);
                if (cert.sha256 != null && Matches(sheetValue, null, cert.alias, out _)) return cert;
            }
            return null;
        }

        // ---------------------------------------------------------------- Editor suite

        /// <summary>Step 2: release keystore set and readable. Step 1: it's the one the sheet names. Android build target only.</summary>
        public static void Check(TestReport report, GenZSheetReader sheet, SigningSettings s)
        {
            if (s == null || !s.enabled) return;
            var sec = report.Section("Android signing (keystore)", TestStage.Integration);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                sec.Add("Build target", CheckStatus.Info, EditorUserBuildSettings.activeBuildTarget.ToString(), "Not Android: the keystore isn't checked");
                return;
            }

            bool custom = PlayerSettings.Android.useCustomKeystore;
            var path = ProjectKeystorePath;
            var alias = PlayerSettings.Android.keyaliasName ?? "";
            sec.Add("Custom keystore", custom ? CheckStatus.Pass : CheckStatus.Fail, custom ? "on" : "off",
                custom ? "Release builds are signed with your keystore"
                       : "Builds would be signed with Unity's debug key, which Google Play rejects. Player Settings → Publishing Settings → Custom Keystore");
            if (!custom) return;

            bool exists = File.Exists(path);
            sec.Add("Keystore file", exists ? CheckStatus.Pass : CheckStatus.Fail, PlayerSettings.Android.keystoreName,
                exists ? "Found" : "Not found on this computer: the build fails. Select the keystore in Publishing Settings");
            sec.Add("Key alias", alias.Trim().Length > 0 ? CheckStatus.Pass : CheckStatus.Fail, alias,
                alias.Trim().Length > 0 ? "" : "No key alias selected in Publishing Settings");

            // Step 1: the keystore the studio sheet names for this game.
            sec.stage = TestStage.Ids;
            string column = null;
            var want = sheet?.Lookup(s.sheetColumns, out column);
            if (sheet == null)
                sec.Add("Keystore = sheet", CheckStatus.Info, "", "Sheet verification is off or the sheet couldn't be read");
            else if (want == null)
                sec.Add("Keystore = sheet", CheckStatus.Info, "", "The sheet has no keystore column (tried " + string.Join(", ", s.sheetColumns) + ")");
            else if (want.Trim().Length == 0)
                sec.Add("Keystore = sheet", CheckStatus.Warn, Path.GetFileName(path), $"The sheet's \"{column}\" cell is empty for this game, so the keystore can't be confirmed");
            else if (Matches(want, path, alias, out var how))
                sec.Add("Keystore = sheet", CheckStatus.Pass, want.Trim(), $"Matches the {how}");
            else
                sec.Add("Keystore = sheet", CheckStatus.Fail, $"{Path.GetFileName(path)} ({alias})",
                    $"The sheet's \"{column}\" says {want.Trim()}: neither the keystore file name nor the key alias matches. Wrong keystore for this game?");
            sec.stage = TestStage.Integration;

            // The certificate, for reference (the APK suite compares the APK's signer with it).
            if (!exists) return;
            var cert = ReadCertificate(path, alias, PlayerSettings.Android.keystorePass);
            if (cert.sha256 != null)
                sec.Add("Certificate", CheckStatus.Info, "SHA-256 " + Spaced(cert.sha256), cert.owner ?? "");
            else
                sec.Add("Certificate", CheckStatus.Warn, "", $"Couldn't read the certificate of \"{alias}\": {cert.error}. "
                    + "A PKCS12 keystore needs its password: enter it in Publishing Settings and run again");
        }

        static string Spaced(string sha) => string.Join(":", Enumerable.Range(0, sha.Length / 2).Select(i => sha.Substring(i * 2, 2))).ToUpperInvariant();
    }
}
