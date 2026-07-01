using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Variance-aware picker for <c>[SerializeReference, EffectSelector]</c> <see cref="IEffect{TContext}"/> fields
/// and lists. See <see cref="EffectSelectorAttribute"/> for the why.
///
/// The one idea that makes it work: for a field whose element type is <c>IEffect&lt;TAuthored&gt;</c>, an effect
/// type <c>E</c> is offered when <c>E</c> implements some <c>IEffect&lt;TNeed&gt;</c> with
/// <c>TNeed.IsAssignableFrom(TAuthored)</c>. That is a plain interface-implementation test — it does NOT rely on
/// reflection honouring generic contravariance — so an <c>IEffect&lt;IAimContext&gt;</c> is correctly offered in
/// an <c>IEffect&lt;ProjectilePlayerCollision&gt;</c> list because ProjectilePlayerCollision implements
/// IAimContext. The runtime already accepts the stored instance there via the contravariant <c>in TContext</c>.
/// </summary>
[CustomPropertyDrawer(typeof(EffectSelectorAttribute))]
public sealed class EffectSelectorDrawer : PropertyDrawer
{
    // authored context T -> concrete effect types offerable there (built once per T, cheap thereafter)
    static readonly Dictionary<Type, List<Type>> s_candidatesByContext = new();
    static List<Type> s_allEffects;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUI.GetPropertyHeight(property, label, true);

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.ManagedReference)
        {
            EditorGUI.PropertyField(position, property, label, true);
            return;
        }

        // Draw the managed-reference value normally (foldout header + its serialized children).
        EditorGUI.PropertyField(position, property, label, true);

        // Overlay a type-picker button on the header line, to the right of the label.
        var header = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        float labelWidth = EditorGUIUtility.labelWidth + 2f;
        var buttonRect = new Rect(header.x + labelWidth, header.y,
            Mathf.Max(60f, header.width - labelWidth), header.height);

        var content = new GUIContent(DisplayName(property.managedReferenceFullTypename));
        if (EditorGUI.DropdownButton(buttonRect, content, FocusType.Keyboard))
            ShowMenu(buttonRect, property);
    }

    void ShowMenu(Rect rect, SerializedProperty property)
    {
        SerializedProperty target = property.Copy();
        Type context = ResolveContextType();

        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("(None)"), false, () => Assign(target, null));
        foreach (Type type in Candidates(context))
        {
            Type captured = type;
            menu.AddItem(new GUIContent(type.Name), false, () => Assign(target, captured));
        }
        menu.DropDown(rect);
    }

    static void Assign(SerializedProperty property, Type type)
    {
        property.serializedObject.Update();
        property.managedReferenceValue = type == null ? null : Activator.CreateInstance(type);
        property.serializedObject.ApplyModifiedProperties();
    }

    // ---- context resolution ----

    Type ResolveContextType()
    {
        Type element = ElementType(fieldInfo?.FieldType);
        if (element != null && element.IsGenericType && element.GetGenericTypeDefinition() == typeof(IEffect<>))
            return element.GetGenericArguments()[0];
        return typeof(IEffectContext);
    }

    static Type ElementType(Type fieldType)
    {
        if (fieldType == null) return null;
        if (fieldType.IsArray) return fieldType.GetElementType();
        if (fieldType.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(fieldType))
            return fieldType.GetGenericArguments()[0];
        return fieldType;
    }

    // ---- candidate enumeration ----

    static List<Type> Candidates(Type context)
    {
        if (s_candidatesByContext.TryGetValue(context, out List<Type> cached))
            return cached;

        var list = AllEffects()
            .Where(t => ContextArgs(t).Any(need => need.IsAssignableFrom(context)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        s_candidatesByContext[context] = list;
        return list;
    }

    static IEnumerable<Type> ContextArgs(Type effectType)
        => effectType.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEffect<>))
            .Select(i => i.GetGenericArguments()[0]);

    static List<Type> AllEffects()
    {
        if (s_allEffects != null) return s_allEffects;

        s_allEffects = new List<Type>();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

            foreach (Type t in types)
            {
                if (t == null || !t.IsClass || t.IsAbstract || t.IsGenericTypeDefinition) continue;
                if (!t.IsSerializable) continue;                              // [SerializeReference] needs [Serializable]
                if (typeof(UnityEngine.Object).IsAssignableFrom(t)) continue; // not a UnityEngine.Object
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;      // picker Activator-creates it
                if (ContextArgs(t).Any()) s_allEffects.Add(t);
            }
        }
        return s_allEffects;
    }

    static string DisplayName(string managedReferenceFullTypename)
    {
        if (string.IsNullOrEmpty(managedReferenceFullTypename)) return "(None)";
        // format: "<assembly> <namespace.TypeName>"
        int space = managedReferenceFullTypename.IndexOf(' ');
        string full = space >= 0 ? managedReferenceFullTypename.Substring(space + 1) : managedReferenceFullTypename;
        int dot = full.LastIndexOf('.');
        return dot >= 0 ? full.Substring(dot + 1) : full;
    }
}
