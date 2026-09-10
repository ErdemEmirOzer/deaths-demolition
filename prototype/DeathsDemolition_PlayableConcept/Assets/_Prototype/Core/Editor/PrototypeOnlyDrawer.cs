using DD.Prototype.Core.Config;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Core.EditorTools
{
    /// <summary>
    /// Draws the origin tag under every field marked <see cref="PrototypeOnlyAttribute"/>,
    /// so the Inspector always says which pending planning decision the number occupies.
    /// </summary>
    [CustomPropertyDrawer(typeof(PrototypeOnlyAttribute))]
    public sealed class PrototypeOnlyDrawer : PropertyDrawer
    {
        private const float TagHeight = 14f;
        private const float Spacing = 1f;

        private static GUIStyle s_tagStyle;
        private static GUIStyle s_inventedStyle;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float baseHeight = EditorGUI.GetPropertyHeight(property, label, true);
            var attr = (PrototypeOnlyAttribute)attribute;
            float tag = TagHeight + Spacing;
            if (!string.IsNullOrEmpty(attr.Note))
            {
                tag += TagHeight;
            }

            return baseHeight + tag;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EnsureStyles();

            var attr = (PrototypeOnlyAttribute)attribute;
            float fieldHeight = EditorGUI.GetPropertyHeight(property, label, true);

            var fieldRect = new Rect(position.x, position.y, position.width, fieldHeight);
            EditorGUI.PropertyField(fieldRect, property, label, true);

            float y = position.y + fieldHeight + Spacing;
            var tagRect = new Rect(position.x, y, position.width, TagHeight);

            string prefix = attr.Invented ? "PROTOTYPE-ONLY (invented)" : "PROTOTYPE-ONLY";
            EditorGUI.LabelField(tagRect, $"{prefix} · {attr.PendingDecision}",
                attr.Invented ? s_inventedStyle : s_tagStyle);

            if (!string.IsNullOrEmpty(attr.Note))
            {
                var noteRect = new Rect(position.x, y + TagHeight, position.width, TagHeight);
                EditorGUI.LabelField(noteRect, attr.Note, s_tagStyle);
            }
        }

        private static void EnsureStyles()
        {
            if (s_tagStyle != null)
            {
                return;
            }

            s_tagStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Italic,
                wordWrap = false
            };
            s_tagStyle.normal.textColor = new Color(0.48f, 0.56f, 0.66f);

            s_inventedStyle = new GUIStyle(s_tagStyle);
            s_inventedStyle.normal.textColor = new Color(0.78f, 0.58f, 0.30f);
        }
    }
}
