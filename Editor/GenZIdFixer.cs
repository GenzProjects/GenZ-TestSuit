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
            return result;
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
