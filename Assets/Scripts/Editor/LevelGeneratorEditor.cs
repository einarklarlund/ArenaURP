using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LevelGenerator))]
public class LevelGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector(); // Draw all normal variables

        LevelGenerator gen = (LevelGenerator)target;

        if (GUILayout.Button("Generate Level (Editor)"))
        {
            gen.CreateLevelInEditor();
        }

        if (GUILayout.Button("Clear Level"))
        {
            gen.ClearLevel();
        }
    }
}