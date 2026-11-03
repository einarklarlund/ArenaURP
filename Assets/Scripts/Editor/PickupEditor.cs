using UnityEditor;

/// <summary>
/// Inspector for <see cref="Pickup"/> that shows only the reward fields of the selected <see cref="Pickup.Kind"/>.
/// The hidden fields keep their values. A multi-selection with mixed kinds shows them all.
/// </summary>
[CustomEditor(typeof(Pickup))]
[CanEditMultipleObjects]
public class PickupEditor : Editor
{
    private SerializedProperty _kind;

    private void OnEnable()
    {
        _kind = serializedObject.FindProperty(nameof(Pickup.kind));
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty property = serializedObject.GetIterator();
        for (bool enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
        {
            if (!IsShown(property.name))
                continue;

            using (new EditorGUI.DisabledScope(property.propertyPath == "m_Script"))
                EditorGUILayout.PropertyField(property, true);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private bool IsShown(string propertyName)
    {
        Pickup.Kind? owner = propertyName switch
        {
            "healthAmount" => Pickup.Kind.Health,
            "ammoRewards" => Pickup.Kind.Ammo,
            "experienceAmount" => Pickup.Kind.Experience,
            _ => null,
        };
        return owner == null || _kind.hasMultipleDifferentValues || (Pickup.Kind)_kind.enumValueIndex == owner;
    }
}
