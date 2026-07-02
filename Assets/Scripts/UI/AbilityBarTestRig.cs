#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor/test-only helper for the active-ability HUD PlayMode tests. Builds a real <see cref="AbilityBar"/>
/// from the shipped "Ability Slot" widget prefab, binds it to a pawn through the production
/// <see cref="NetworkUIEvents.OnLocalPawnSpawned"/> path, and reads back the live rendered slot state
/// (the remaining-uses label and the selection highlight) so a test can assert the HUD reflects the synced
/// inventory. Lives in Assembly-CSharp so it can touch <see cref="AbilityBar"/>/<see cref="AbilitySlotWidget"/>
/// directly — the Arena.PlayTests asmdef can't reference Assembly-CSharp, so the tests call these by
/// reflection (one contained seam, per the integration-testing skill). Gated by UNITY_EDITOR so it never ships.
/// </summary>
public static class AbilityBarTestRig
{
    const string SlotPrefabPath = "Assets/Prefabs/UI/Ability Slot.prefab";

    static readonly BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Build an <see cref="AbilityBar"/> wired to the real slot-widget prefab. Created inactive, fields
    /// injected, then activated so Awake (builds the 5 slots) and OnEnable (subscribes to
    /// <see cref="NetworkUIEvents"/>) run with the wiring already in place — exactly as the inspector-authored
    /// bar does on the UIManager prefab.
    /// </summary>
    public static GameObject BuildBar()
    {
        var slotPrefab = AssetDatabase.LoadAssetAtPath<AbilitySlotWidget>(SlotPrefabPath);
        if (slotPrefab == null)
            throw new System.IO.FileNotFoundException($"Ability Slot widget prefab missing at {SlotPrefabPath}");

        var root = new GameObject("TestAbilityBar");
        root.SetActive(false); // configure before Awake/OnEnable run

        var slotsParent = new GameObject("Slots").transform;
        slotsParent.SetParent(root.transform, false);

        var bar = root.AddComponent<AbilityBar>();
        SetField(bar, "slotsParent", slotsParent);
        SetField(bar, "slotPrefab", slotPrefab);

        root.SetActive(true); // Awake builds slots + OnEnable subscribes, now that the fields are set
        return root;
    }

    /// <summary>Bind the bar to a pawn via the same event the local pawn raises through NetworkUIController.</summary>
    public static void Bind(GameObject pawn) => NetworkUIEvents.OnLocalPawnSpawned?.Invoke(pawn.GetComponent<Pawn>());

    /// <summary>Number of slot widgets the bar built (should be <see cref="PawnActiveAbilities.MaxActives"/>).</summary>
    public static int SlotCount(GameObject bar) => Slots(bar).Count;

    /// <summary>True when slot <paramref name="i"/> is showing an ability — its uses label is non-empty.</summary>
    public static bool SlotFilled(GameObject bar, int i) => !string.IsNullOrEmpty(SlotUsesText(bar, i));

    /// <summary>The remaining-uses text the slot widget is currently displaying ("" when the slot is empty).</summary>
    public static string SlotUsesText(GameObject bar, int i)
    {
        var label = (TMP_Text)GetField(Slots(bar)[i], "usesLabel");
        return label != null ? label.text : null;
    }

    /// <summary>Whether slot <paramref name="i"/> is drawing its selection highlight.</summary>
    public static bool SlotSelected(GameObject bar, int i)
    {
        var hl = (GameObject)GetField(Slots(bar)[i], "highlight");
        return hl != null && hl.activeSelf;
    }

    static List<AbilitySlotWidget> Slots(GameObject bar)
        => (List<AbilitySlotWidget>)GetField(bar.GetComponent<AbilityBar>(), "_slots");

    static void SetField(object o, string name, object v) => o.GetType().GetField(name, Priv).SetValue(o, v);
    static object GetField(object o, string name) => o.GetType().GetField(name, Priv).GetValue(o);
}
#endif
