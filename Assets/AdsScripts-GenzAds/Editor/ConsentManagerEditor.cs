// GenzMonetization-v1.33
using UnityEditor;

/// <summary>ConsentManager Inspector: shows only the field the "Load Target Scene By" dropdown uses (index or name).</summary>
[CustomEditor(typeof(ConsentManager))]
public class ConsentManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var mode = (ConsentManager.SceneLoadMode)serializedObject.FindProperty(nameof(ConsentManager.loadTargetSceneBy)).intValue;

        var it = serializedObject.GetIterator();
        for (bool enter = true; it.NextVisible(enter); enter = false)
        {
            if (it.name == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(it);
                continue;
            }
            if (it.name == nameof(ConsentManager.targetSceneBuildIndex))
            {
                if (mode != ConsentManager.SceneLoadMode.BuildIndex) continue;
                EditorGUILayout.PropertyField(it);
                // Which scene that index is, so a wrong index (e.g. this scene itself) is easy to spot.
                var scenes = EditorBuildSettings.scenes;
                int i = it.intValue;
                if (i < 0 || i >= scenes.Length)
                    EditorGUILayout.HelpBox($"No scene at index {i} in Build Settings ({scenes.Length} scenes).", MessageType.Error);
                else
                    EditorGUILayout.HelpBox("Loads " + scenes[i].path + (scenes[i].enabled ? "" : " (disabled in Build Settings)"),
                        scenes[i].enabled ? MessageType.None : MessageType.Warning);
                continue;
            }
            if (it.name == nameof(ConsentManager.targetSceneName) && mode != ConsentManager.SceneLoadMode.SceneName)
                continue;
            EditorGUILayout.PropertyField(it, true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
