using UnityEditor;
using UnityEngine;

namespace Arena.LevelGen.Editor
{
    /// <summary>The config's fields, with the settings check on top: values the generator will change, and settings
    /// it refuses because no level could satisfy them. Below them, each prop variant's Fit from prefab button and its
    /// collider check.</summary>
    [CustomEditor(typeof(LevelGenConfig))]
    public class LevelGenConfigEditor : UnityEditor.Editor
    {

        public override void OnInspectorGUI()
        {
            var config = (LevelGenConfig)target;
            foreach (var issue in SettingsCheck.Check(config.settings))
                EditorGUILayout.HelpBox(issue.Message, issue.Severity == IssueSeverity.Error ? MessageType.Error : MessageType.Warning);
            DrawDefaultInspector();
            if (config.props != null) DrawCatalogueTools(config);
        }

        private static void DrawCatalogueTools(LevelGenConfig config)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Prop prefabs", EditorStyles.boldLabel);
            foreach (var kind in PropKinds.All)
            {
                var variants = config.props.Variants(kind);
                for (int i = 0; i < variants.Count; i++)
                {
                    var variant = variants[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string name = variant.prefab != null ? variant.prefab.name : "no prefab (placeholder)";
                        EditorGUILayout.LabelField($"{kind} {i}: {name}");
                        using (new EditorGUI.DisabledScope(variant.prefab == null))
                            if (GUILayout.Button(new GUIContent("Fit from prefab", "Read the footprint from the prefab's colliders."),
                                    GUILayout.Width(110)))
                                Fit(config, kind, variant);
                    }
                    if (variant.prefab == null) continue;
                    foreach (var problem in PropFitter.Check(kind, variant.prefab, variant.shape))
                        EditorGUILayout.HelpBox($"{problem} The validator won't see that part.", MessageType.Warning);
                }
            }
        }

        private static void Fit(LevelGenConfig config, PropKind kind, PropVariant variant)
        {
            if (!PropFitter.TryFit(kind, variant.prefab, variant.shape, out var shape, out string error))
            {
                Debug.LogWarning(error, variant.prefab);
                return;
            }
            Undo.RecordObject(config, "Fit prop footprint");
            variant.shape = shape;
            EditorUtility.SetDirty(config);
        }
    }
}
