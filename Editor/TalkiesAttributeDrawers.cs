using UnityEditor;
using UnityEngine;


namespace SOSXR.Talkies.Editor
{
    /// <summary>Shared lookup for <see cref="ShowIfAttribute" /> and <see cref="EnableIfAttribute" />.</summary>
    internal static class ConditionUtility
    {
        /// <summary>
        ///     Reads the sibling condition field. Bools use their value, object references are true when assigned,
        ///     enums and ints are true when non-zero, strings when not empty. A missing field counts as true, so the
        ///     property stays visible and editable.
        /// </summary>
        public static bool IsTrue(SerializedProperty property, string conditionField)
        {
            var path = ButtonMethodUtility.GetSiblingPath(property.propertyPath, conditionField);
            var condition = property.serializedObject.FindProperty(path);

            if (condition == null)
            {
                return true;
            }

            return condition.propertyType switch
            {
                SerializedPropertyType.Boolean => condition.boolValue,
                SerializedPropertyType.ObjectReference => condition.objectReferenceValue != null,
                SerializedPropertyType.Enum => condition.enumValueIndex != 0,
                SerializedPropertyType.Integer => condition.intValue != 0,
                SerializedPropertyType.String => !string.IsNullOrEmpty(condition.stringValue),
                _ => true
            };
        }
    }


    /// <summary>Hides the field while the condition field is false.</summary>
    [CustomPropertyDrawer(typeof(ShowIfAttribute))]
    public class ShowIfDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!ConditionUtility.IsTrue(property, ((ShowIfAttribute) attribute).ConditionField))
            {
                // Cancel the spacing Unity adds after each property.
                return -EditorGUIUtility.standardVerticalSpacing;
            }

            return EditorGUI.GetPropertyHeight(property, label, true);
        }


        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (ConditionUtility.IsTrue(property, ((ShowIfAttribute) attribute).ConditionField))
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }
    }


    /// <summary>
    ///     Greys out the field while the condition field is false.
    ///     When the condition is a serialized bool, a checkbox in front of the field toggles it (with Undo and multi-edit).
    ///     Usage: <c>[SerializeField] [HideInInspector] private bool m_useTopic;</c> plus
    ///     <c>[EnableIf(nameof(m_useTopic))] [SerializeField] private string m_topic;</c>
    /// </summary>
    [CustomPropertyDrawer(typeof(EnableIfAttribute))]
    public class EnableIfDrawer : PropertyDrawer
    {
        private const float ToggleWidth = 18f;
        private const float Spacing = 4f;


        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }


        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var conditionField = ((EnableIfAttribute) attribute).ConditionField;
            var path = ButtonMethodUtility.GetSiblingPath(property.propertyPath, conditionField);
            var condition = property.serializedObject.FindProperty(path);

            if (condition is not { propertyType: SerializedPropertyType.Boolean })
            {
                using (new EditorGUI.DisabledScope(!ConditionUtility.IsTrue(property, conditionField)))
                {
                    EditorGUI.PropertyField(position, property, label, true);
                }

                return;
            }

            var toggleRect = new Rect(position.x, position.y, ToggleWidth, EditorGUIUtility.singleLineHeight);
            var fieldRect = new Rect(position.x + ToggleWidth + Spacing, position.y, position.width - ToggleWidth - Spacing, position.height);

            EditorGUI.BeginProperty(toggleRect, GUIContent.none, condition);
            EditorGUI.showMixedValue = condition.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.Toggle(toggleRect, condition.boolValue);

            if (EditorGUI.EndChangeCheck())
            {
                condition.boolValue = value;
            }

            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();

            // Keep labels aligned with the other fields despite the checkbox in front.
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Max(0f, labelWidth - ToggleWidth - Spacing);

            using (new EditorGUI.DisabledScope(!condition.boolValue))
            {
                EditorGUI.PropertyField(fieldRect, property, label, true);
            }

            EditorGUIUtility.labelWidth = labelWidth;
        }
    }


    /// <summary>Shows the field read-only.</summary>
    [CustomPropertyDrawer(typeof(DisableEditingAttribute))]
    public class DisableEditingDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }


        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }
    }


    /// <summary>Draws the field with a unit label (such as "ms") on its right side.</summary>
    [CustomPropertyDrawer(typeof(SuffixAttribute))]
    public class SuffixDrawer : PropertyDrawer
    {
        private const float Gap = 2f;


        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }


        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var suffix = new GUIContent(((SuffixAttribute) attribute).Suffix);
            var width = string.IsNullOrEmpty(suffix.text) ? 0f : EditorStyles.miniLabel.CalcSize(suffix).x;

            var fieldRect = new Rect(position.x, position.y, position.width - width - Gap, position.height);
            EditorGUI.PropertyField(fieldRect, property, label, true);

            if (width > 0f)
            {
                var suffixRect = new Rect(position.xMax - width, position.y, width, EditorGUIUtility.singleLineHeight);
                EditorGUI.LabelField(suffixRect, suffix, EditorStyles.miniLabel);
            }
        }
    }
}
