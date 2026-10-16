using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace BKPureNature
{
    [CustomEditor(typeof(BK_Lava))]
    [CanEditMultipleObjects]
    public sealed class BK_LavaEditor : Editor
    {
        private const string DefaultMaterialName = "Lava_stream";

        private SerializedProperty m_Container;
        private SerializedProperty m_Width;
        private SerializedProperty m_HeightOffset;
        private SerializedProperty m_BorderHeight;
        private SerializedProperty m_LengthSegments;
        private SerializedProperty m_WidthSegments;
        private SerializedProperty m_UVTiling;
        private SerializedProperty m_Curve;
        private SerializedProperty m_Material;

        private void OnEnable()
        {
            m_Container = serializedObject.FindProperty("m_Container");
            m_Width = serializedObject.FindProperty("m_Width");
            m_HeightOffset = serializedObject.FindProperty("m_HeightOffset");
            m_BorderHeight = serializedObject.FindProperty("m_BorderHeight");
            m_LengthSegments = serializedObject.FindProperty("m_LengthSegments");
            m_WidthSegments = serializedObject.FindProperty("m_WidthSegments");
            m_UVTiling = serializedObject.FindProperty("UVTiling");
            m_Curve = serializedObject.FindProperty("curve");
            m_Material = serializedObject.FindProperty("m_Material");

            AssignDefaultMaterialIfNeeded();
            Undo.undoRedoPerformed += RebuildTargets;

            RebuildTargets();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RebuildTargets;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(m_Container, new GUIContent("Spline Container"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Texture", EditorStyles.boldLabel);

            Vector2 tiling = EditorGUILayout.Vector2Field("Tiling", m_UVTiling.vector2Value);
            tiling.x = Mathf.Max(0.01f, tiling.x);
            tiling.y = Mathf.Max(0.01f, tiling.y);
            m_UVTiling.vector2Value = tiling;

            EditorGUILayout.PropertyField(m_Material, new GUIContent("Material"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Shape", EditorStyles.boldLabel);

            m_Width.floatValue = EditorGUILayout.Slider("Width", m_Width.floatValue, 0.1f, 100f);
            m_HeightOffset.floatValue = EditorGUILayout.Slider("Height Offset", m_HeightOffset.floatValue, -10f, 10f);
            m_BorderHeight.floatValue = EditorGUILayout.Slider("Border Height", m_BorderHeight.floatValue, 0f, 40f);
            m_Curve.floatValue = EditorGUILayout.Slider("Curve Strength", m_Curve.floatValue, 0f, 1f);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Segments", EditorStyles.boldLabel);

            m_LengthSegments.intValue = EditorGUILayout.IntSlider("Length", m_LengthSegments.intValue, 1, 7);
            m_WidthSegments.intValue = EditorGUILayout.IntSlider("Width", m_WidthSegments.intValue, 1, 7);

            bool changed = EditorGUI.EndChangeCheck();
            if (serializedObject.ApplyModifiedProperties() || changed)
                RebuildTargets();

            DrawStatus();

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Rebuild Mesh"))
                RebuildTargets();
        }

        private void DrawStatus()
        {
            if (targets.Length != 1)
                return;

            BK_Lava lava = (BK_Lava)target;
            if (lava.m_Container == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a Spline Container, or place this component on/under one.",
                    MessageType.Warning);
                return;
            }

            Spline spline = lava.m_Container.Spline;
            if (spline == null || spline.Count < 2)
            {
                EditorGUILayout.HelpBox(
                    "The spline needs at least two knots.",
                    MessageType.Info);
            }
        }

        private void AssignDefaultMaterialIfNeeded()
        {
            serializedObject.Update();
            if (m_Material.objectReferenceValue != null || m_Material.hasMultipleDifferentValues)
                return;

            Material material = FindMaterialAsset(DefaultMaterialName);
            if (material == null)
                return;

            m_Material.objectReferenceValue = material;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private void RebuildTargets()
        {
            foreach (Object inspectedTarget in targets)
            {
                BK_Lava lava = inspectedTarget as BK_Lava;
                if (lava == null)
                    continue;

                lava.Rebuild();
            }

            SceneView.RepaintAll();
        }

        private static Material FindMaterialAsset(string materialName)
        {
            string[] guids = AssetDatabase.FindAssets($"t:Material {materialName}");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null && material.name == materialName)
                    return material;
            }

            return null;
        }
    }
}