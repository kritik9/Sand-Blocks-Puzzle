using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SandBlocks.Core.Boundary;

namespace SandBlocks.Editor
{
    [CustomEditor(typeof(ContainerBoundary))]
    public class ContainerBoundaryEditor : UnityEditor.Editor
    {
        private SerializedProperty shapeTypeProp;
        private SerializedProperty widthProp;
        private SerializedProperty heightProp;
        private SerializedProperty centerProp;
        private SerializedProperty leftOffsetProp;
        private SerializedProperty rightOffsetProp;
        private SerializedProperty topOffsetProp;
        private SerializedProperty bottomOffsetProp;
        private SerializedProperty customPointsProp;
        private SerializedProperty borderColorProp;
        private SerializedProperty interiorColorProp;
        private SerializedProperty beamThicknessProp;
        private SerializedProperty beamDepthProp;
        private SerializedProperty showBackPlateProp;

        private void OnEnable()
        {
            shapeTypeProp = serializedObject.FindProperty("shapeType");
            widthProp = serializedObject.FindProperty("width");
            heightProp = serializedObject.FindProperty("height");
            centerProp = serializedObject.FindProperty("containerCenter");
            leftOffsetProp = serializedObject.FindProperty("leftBorderOffset");
            rightOffsetProp = serializedObject.FindProperty("rightBorderOffset");
            topOffsetProp = serializedObject.FindProperty("topBorderOffset");
            bottomOffsetProp = serializedObject.FindProperty("bottomBorderOffset");
            customPointsProp = serializedObject.FindProperty("customPolygonPoints");
            borderColorProp = serializedObject.FindProperty("borderColor");
            interiorColorProp = serializedObject.FindProperty("interiorColor");
            beamThicknessProp = serializedObject.FindProperty("borderBeamThickness");
            beamDepthProp = serializedObject.FindProperty("borderBeamDepth");
            showBackPlateProp = serializedObject.FindProperty("showBackPlate");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var boundary = (ContainerBoundary)target;

            EditorGUILayout.LabelField("Container Geometry & Shape", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(shapeTypeProp);

            var shape = (ContainerShapeType)shapeTypeProp.enumValueIndex;

            if (shape == ContainerShapeType.Rectangle)
            {
                EditorGUILayout.PropertyField(widthProp);
                EditorGUILayout.PropertyField(heightProp);
                EditorGUILayout.PropertyField(centerProp);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Independent Border Offsets", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(leftOffsetProp);
                EditorGUILayout.PropertyField(rightOffsetProp);
                EditorGUILayout.PropertyField(topOffsetProp);
                EditorGUILayout.PropertyField(bottomOffsetProp);
            }
            else
            {
                EditorGUILayout.HelpBox("Custom Polygon: You can click and drag vertex handles directly in the Scene view to draw any shape!\nClick the '+' dot on any edge in Scene view to insert a new vertex. Shift-click a vertex to delete it.", MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ Add Point"))
                {
                    Undo.RecordObject(boundary, "Add Polygon Point");
                    boundary.EnsureDefaultPolygonPoints();
                    var pts = boundary.CustomPolygonPoints;
                    Vector2 last = pts[pts.Count - 1];
                    pts.Add(last + new Vector2(0.5f, 0.5f));
                    boundary.RebuildSegments();
                    boundary.BuildVisualContainer();
                    EditorUtility.SetDirty(boundary);
                }
                if (GUILayout.Button("Reset Box"))
                {
                    Undo.RecordObject(boundary, "Reset Polygon to Box");
                    boundary.ResetToDefaultBox();
                    EditorUtility.SetDirty(boundary);
                }
                if (GUILayout.Button("Reset Hexagon"))
                {
                    Undo.RecordObject(boundary, "Reset Polygon to Hexagon");
                    boundary.ResetToHexagon();
                    EditorUtility.SetDirty(boundary);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);
                EditorGUILayout.PropertyField(customPointsProp, true);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Visual Styling", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(borderColorProp);
            EditorGUILayout.PropertyField(interiorColorProp);
            EditorGUILayout.PropertyField(beamThicknessProp);
            EditorGUILayout.PropertyField(beamDepthProp);
            EditorGUILayout.PropertyField(showBackPlateProp);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Rebuild Visual Container", GUILayout.Height(28)))
            {
                boundary.RebuildSegments();
                boundary.BuildVisualContainer();
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                boundary.RebuildSegments();
                boundary.BuildVisualContainer();
            }
        }

        private void OnSceneGUI()
        {
            var boundary = (ContainerBoundary)target;
            if (boundary.ShapeType != ContainerShapeType.CustomPolygon) return;

            var points = boundary.CustomPolygonPoints;
            if (points == null || points.Count < 3) return;

            Vector3 originPos = boundary.transform.position;
            int n = points.Count;

            // 1. Draw polygon edges and '+' midpoint insertion buttons
            Handles.color = new Color(0.96f, 0.25f, 0.55f, 0.95f);
            int insertIndex = -1;
            Vector2 insertPoint = Vector2.zero;

            for (int i = 0; i < n; i++)
            {
                Vector3 pA = originPos + (Vector3)points[i];
                Vector3 pB = originPos + (Vector3)points[(i + 1) % n];

                Handles.DrawLine(pA, pB, 4f);

                // Midpoint button to insert new vertex
                Vector3 mid = (pA + pB) * 0.5f;
                float handleSize = HandleUtility.GetHandleSize(mid) * 0.08f;
                Handles.color = Color.cyan;
                if (Handles.Button(mid, Quaternion.identity, handleSize, handleSize * 1.3f, Handles.DotHandleCap))
                {
                    insertIndex = i + 1;
                    insertPoint = (Vector2)(mid - originPos);
                }
                Handles.color = new Color(0.96f, 0.25f, 0.55f, 0.95f);
            }

            if (insertIndex >= 0)
            {
                Undo.RecordObject(boundary, "Insert Polygon Point");
                points.Insert(insertIndex, insertPoint);
                boundary.RebuildSegments();
                boundary.BuildVisualContainer();
                EditorUtility.SetDirty(boundary);
                GUIUtility.ExitGUI();
                return;
            }

            // 2. Vertex drag handles
            EditorGUI.BeginChangeCheck();
            int deleteIndex = -1;

            for (int i = 0; i < n; i++)
            {
                Vector3 worldPos = originPos + (Vector3)points[i];
                float size = HandleUtility.GetHandleSize(worldPos) * 0.12f;

                // Vertex label (P0, P1, P2...)
                GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.yellow } };
                Handles.Label(worldPos + Vector3.up * size * 1.4f, $"P{i}", labelStyle);

                // Free Move Handle
                Handles.color = Color.yellow;
                Vector3 newPos = Handles.FreeMoveHandle(worldPos, size, Vector3.zero, Handles.SphereHandleCap);

                if (newPos != worldPos)
                {
                    Undo.RecordObject(boundary, "Move Polygon Point");
                    points[i] = new Vector2(newPos.x - originPos.x, newPos.y - originPos.y);
                }

                // Check Shift-Click on vertex to delete
                if (Event.current.type == EventType.MouseDown && Event.current.shift && n > 3)
                {
                    Vector2 guiPoint = HandleUtility.WorldToGUIPoint(worldPos);
                    if (Vector2.Distance(Event.current.mousePosition, guiPoint) < 14f)
                    {
                        deleteIndex = i;
                        Event.current.Use();
                    }
                }
            }

            if (deleteIndex >= 0)
            {
                Undo.RecordObject(boundary, "Delete Polygon Point");
                points.RemoveAt(deleteIndex);
                boundary.RebuildSegments();
                boundary.BuildVisualContainer();
                EditorUtility.SetDirty(boundary);
                GUIUtility.ExitGUI();
                return;
            }

            if (EditorGUI.EndChangeCheck())
            {
                boundary.RebuildSegments();
                boundary.BuildVisualContainer();
                EditorUtility.SetDirty(boundary);
            }
        }
    }
}
