// GenZ-TestSuit-v1.0
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GenZ.TestSuit
{
    /// <summary>
    /// "Auto Apply IDs": writes the Google Sheet values recorded by the last run (<see cref="TestReport.idFixes"/>)
    /// into the settings assets and the SDK scene components, via SerializedObject (no compile-time references).
    /// Scenes it opens itself are saved and closed; scenes already open are only marked dirty, so the user's own
    /// unsaved edits are never saved behind their back.
    /// </summary>
    public static class GenZIdFixer
    {
        public class Result
        {
            public readonly List<string> applied = new List<string>();
            public readonly List<string> failed = new List<string>();
            public readonly List<string> unsavedScenes = new List<string>();
            public readonly List<string> scripts = new List<string>(); // edited scripts (Unity recompiles)
        }

        /// <summary>
        /// "Fix GA Ad Formats": replaces the quoted string on the exact line the check read (codeFile:codeLine, whose text
        /// must still be codeContext) with "expected". Refused for a line that changed since the check.
        /// </summary>
        public static Result ApplyOnLines(IEnumerable<IdFix> fixes)
        {
            var result = new Result();
            foreach (var group in fixes.Where(f => !string.IsNullOrEmpty(f.codeFile)).GroupBy(f => f.codeFile))
            {
                try
                {
                    var path = System.IO.Path.GetFullPath(group.Key);
                    if (!System.IO.File.Exists(path)) { foreach (var f in group) result.failed.Add($"{f.label}: {group.Key} not found"); continue; }
                    var bytes = System.IO.File.ReadAllBytes(path);
                    bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                    var utf8 = new System.Text.UTF8Encoding(false);
                    var text = utf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
                    var lines = System.Text.RegularExpressions.Regex.Split(text, "(?<=\n)"); // keeps each line's ending

                    bool changed = false;
                    foreach (var f in group)
                    {
                        int i = f.codeLine - 1;
                        string quoted = "\"" + f.codeValue + "\"";
                        if (i < 0 || i >= lines.Length || lines[i].TrimEnd('\r', '\n') != f.codeContext.TrimEnd('\r')
                            || lines[i].IndexOf(quoted, StringComparison.Ordinal) < 0
                            || lines[i].IndexOf(quoted, lines[i].IndexOf(quoted, StringComparison.Ordinal) + 1, StringComparison.Ordinal) >= 0)
                        {
                            result.failed.Add($"{f.label}: {f.codeFile}:{f.codeLine} changed since the check — run the suite again");
                            continue;
                        }
                        lines[i] = lines[i].Replace(quoted, "\"" + f.expected + "\"");
                        changed = true;
                        result.applied.Add($"{f.label}: \"{f.current}\" → \"{f.expected}\"  ({f.Where})");
                    }
                    if (!changed) continue;

                    var body = utf8.GetBytes(string.Concat(lines));
                    System.IO.File.WriteAllBytes(path, bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body);
                    AssetDatabase.ImportAsset(group.Key);
                    result.scripts.Add(group.Key);
                }
                catch (Exception e)
                {
                    foreach (var f in group) result.failed.Add($"{f.label}: couldn't edit {group.Key} — {e.Message}");
                }
            }
            return result;
        }

        public static string Show(IdFix f, string v) => f.secret && v != null && v.Length > 14 ? v.Substring(0, 6) + "…" + v.Substring(v.Length - 6) : v;

        public static Result Apply(IEnumerable<IdFix> fixes)
        {
            var result = new Result();
            var list = fixes.ToList();

            // Settings assets.
            foreach (var group in list.Where(f => !string.IsNullOrEmpty(f.assetPath)).GroupBy(f => f.assetPath))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(group.Key);
                if (asset == null) { foreach (var f in group) result.failed.Add($"{f.label}: asset not found ({group.Key})"); continue; }
                var so = new SerializedObject(asset);
                foreach (var f in group) Set(so, f, result);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();

            // Components in scenes.
            foreach (var group in list.Where(f => string.IsNullOrEmpty(f.assetPath) && !string.IsNullOrEmpty(f.scenePath)).GroupBy(f => f.scenePath))
            {
                var scene = SceneManager.GetSceneByPath(group.Key);
                bool openedHere = !(scene.IsValid() && scene.isLoaded);
                if (openedHere) scene = EditorSceneManager.OpenScene(group.Key, OpenSceneMode.Additive);
                try
                {
                    bool changed = false;
                    foreach (var f in group)
                    {
                        var go = Find(scene, f.objectPath);
                        var comp = go == null ? null : go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == f.componentType);
                        if (comp == null) { result.failed.Add($"{f.label}: {f.componentType} on {f.objectPath} not found in {scene.name}"); continue; }
                        var so = new SerializedObject(comp);
                        if (Set(so, f, result)) changed = true;
                        so.ApplyModifiedProperties();
                        // Keep a prefab instance's value as an override on this scene's object (where the suite reads it).
                        if (PrefabUtility.IsPartOfPrefabInstance(comp)) PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
                    }
                    if (changed)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (openedHere) EditorSceneManager.SaveScene(scene);
                        else result.unsavedScenes.Add(scene.path);
                    }
                }
                finally
                {
                    if (openedHere) EditorSceneManager.CloseScene(scene, true);
                }
            }

            // Numbers written in scripts (a Taichi 2.0 default without an Inspector field).
            foreach (var f in list.Where(f => string.IsNullOrEmpty(f.assetPath) && string.IsNullOrEmpty(f.scenePath) && !string.IsNullOrEmpty(f.codeFile)))
                SetInCode(f, result);
            return result;
        }

        /// <summary>
        /// Replaces the number on the line the check read (<see cref="IdFix.codeContext"/>), only if that line is still in the
        /// file exactly once; keeps the file's encoding and line endings, then reimports it so Unity recompiles.
        /// </summary>
        static void SetInCode(IdFix f, Result result)
        {
            try
            {
                var path = System.IO.Path.GetFullPath(f.codeFile); // Assets/... relative to the project root
                if (!System.IO.File.Exists(path)) { result.failed.Add($"{f.label}: {f.codeFile} not found"); return; }
                var bytes = System.IO.File.ReadAllBytes(path);
                bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var utf8 = new System.Text.UTF8Encoding(false);
                var text = utf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));

                int at = string.IsNullOrEmpty(f.codeContext) ? -1 : text.IndexOf(f.codeContext, StringComparison.Ordinal);
                if (at < 0 || text.IndexOf(f.codeContext, at + 1, StringComparison.Ordinal) >= 0
                    || string.IsNullOrEmpty(f.codeValue) || !f.codeContext.EndsWith(f.codeValue, StringComparison.Ordinal))
                {
                    result.failed.Add($"{f.label}: {f.codeFile}:{f.codeLine} changed since the check — run the suite again");
                    return;
                }
                int valueAt = at + f.codeContext.Length - f.codeValue.Length;
                text = text.Substring(0, valueAt) + f.expected + text.Substring(valueAt + f.codeValue.Length);

                var body = utf8.GetBytes(text);
                var output = bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body;
                System.IO.File.WriteAllBytes(path, output);
                AssetDatabase.ImportAsset(f.codeFile);
                result.applied.Add($"{f.label}: {f.current} → {f.expected}  ({f.Where})");
                if (!result.scripts.Contains(f.codeFile)) result.scripts.Add(f.codeFile);
            }
            catch (Exception e)
            {
                result.failed.Add($"{f.label}: couldn't edit {f.codeFile} — {e.Message}");
            }
        }

        static bool Set(SerializedObject so, IdFix f, Result result)
        {
            var p = so.FindProperty(f.propertyPath);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (p != null && p.propertyType == SerializedPropertyType.String) p.stringValue = f.expected;
            else if (p != null && p.propertyType == SerializedPropertyType.Integer && int.TryParse(f.expected, System.Globalization.NumberStyles.Integer, inv, out var i)) p.intValue = i;
            else if (p != null && p.propertyType == SerializedPropertyType.Float && double.TryParse(f.expected, System.Globalization.NumberStyles.Float, inv, out var d)) p.doubleValue = d;
            else
            {
                result.failed.Add($"{f.label}: field {f.propertyPath} not found");
                return false;
            }
            result.applied.Add($"{f.label}: {Show(f, Display(f.current))} → {Show(f, f.expected)}  ({f.Where})");
            return true;
        }

        public static string Display(string v) => string.IsNullOrEmpty(v) ? "(empty)" : v.Trim().Length != v.Length ? $"\"{v}\"" : v;

        static GameObject Find(Scene scene, string path)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (HierarchyPath(t) == path) return t.gameObject;
            return null;
        }

        static string HierarchyPath(Transform t) => t.parent == null ? t.name : HierarchyPath(t.parent) + "/" + t.name;
    }
}
