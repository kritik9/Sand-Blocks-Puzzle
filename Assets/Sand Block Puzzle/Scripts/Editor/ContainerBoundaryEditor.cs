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
        private SerializedProperty beamElevationYProp;
        private SerializedProperty beamHeightProp;
        private SerializedProperty showBackPlateProp;
        private SerializedProperty backPlateElevationYProp;
        private SerializedProperty backPlateThicknessProp;
        private SerializedProperty floorExceptionMarginProp;
        private SerializedProperty autoBuildVisualsProp;

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
            beamElevationYProp = serializedObject.FindProperty("borderBeamElevationY");
            beamHeightProp = serializedObject.FindProperty("borderBeamHeight");
            showBackPlateProp = serializedObject.FindProperty("showBackPlate");
            backPlateElevationYProp = serializedObject.FindProperty("backPlateElevationY");
            backPlateThicknessProp = serializedObject.FindProperty("backPlateThickness");
            floorExceptionMarginProp = serializedObject.FindProperty("floorExceptionMargin");
            autoBuildVisualsProp = serializedObject.FindProperty("autoBuildVisuals");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var boundary = (ContainerBoundary)target;

            EditorGUILayout.LabelField("Container Geometry & Shape", EditorStyles.boldLabel);
            if (shapeTypeProp != null) EditorGUILayout.PropertyField(shapeTypeProp);

            var shape = shapeTypeProp != null ? (ContainerShapeType)shapeTypeProp.enumValueIndex : ContainerShapeType.Rectangle;

            if (shape == ContainerShapeType.Rectangle)
            {
                if (widthProp != null) EditorGUILayout.PropertyField(widthProp);
                if (heightProp != null) EditorGUILayout.PropertyField(heightProp);
                if (centerProp != null) EditorGUILayout.PropertyField(centerProp);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Independent Border Offsets", EditorStyles.miniBoldLabel);
                if (leftOffsetProp != null) EditorGUILayout.PropertyField(leftOffsetProp);
                if (rightOffsetProp != null) EditorGUILayout.PropertyField(rightOffsetProp);
                if (topOffsetProp != null) EditorGUILayout.PropertyField(topOffsetProp);
                if (bottomOffsetProp != null) EditorGUILayout.PropertyField(bottomOffsetProp);
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
                if (customPointsProp != null) EditorGUILayout.PropertyField(customPointsProp, true);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Visual Styling & Elevation", EditorStyles.boldLabel);
            if (borderColorProp != null) EditorGUILayout.PropertyField(borderColorProp);
            if (interiorColorProp != null) EditorGUILayout.PropertyField(interiorColorProp);
            if (beamThicknessProp != null) EditorGUILayout.PropertyField(beamThicknessProp);
            if (beamDepthProp != null) EditorGUILayout.PropertyField(beamDepthProp);
            if (beamElevationYProp != null) EditorGUILayout.PropertyField(beamElevationYProp);
            if (beamHeightProp != null) EditorGUILayout.PropertyField(beamHeightProp);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Back Plate Elevation", EditorStyles.miniBoldLabel);
            if (showBackPlateProp != null)
            {
                EditorGUILayout.PropertyField(showBackPlateProp);
                if (showBackPlateProp.boolValue)
                {
                    if (backPlateElevationYProp != null) EditorGUILayout.PropertyField(backPlateElevationYProp);
                    if (backPlateThicknessProp != null) EditorGUILayout.PropertyField(backPlateThicknessProp);
                    EditorGUILayout.HelpBox($"Crowd characters dynamically stand on backplate surface at Y = {boundary.SurfaceElevationY:F3} (Center: {boundary.BackPlateElevationY:F3} + Half Thickness: {(boundary.BackPlateThickness * 0.5f):F3})", MessageType.None);
                }
            }

            if (floorExceptionMarginProp != null) EditorGUILayout.PropertyField(floorExceptionMarginProp);
            if (autoBuildVisualsProp != null) EditorGUILayout.PropertyField(autoBuildVisualsProp);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Rebuild Visual Container", GUILayout.Height(28)))
            {
                boundary.RebuildSegments();
                boundary.BuildVisualContainer();
                var crowdRenderer = FindAnyObjectByType<SandBlocks.Core.Rendering.InstancedCrowdRenderer>();
                if (crowdRenderer != null)
                {
                    crowdRenderer.SyncLiveCharacterPositions();
                }
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                boundary.RebuildSegments();
                boundary.BuildVisualContainer();
                var crowdRenderer = FindAnyObjectByType<SandBlocks.Core.Rendering.InstancedCrowdRenderer>();
                if (crowdRenderer != null)
                {
                    crowdRenderer.SyncLiveCharacterPositions();
                }
            }
        }

        private void OnSceneGUI()
        {
            var boundary = (ContainerBoundary)target;
            if (boundary.ShapeType != ContainerShapeType.CustomPolygon) return;

            var points = boundary.CustomPolygonPoints;
            if (points == null || points.Count < 3) return;

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            Vector3 originPos = boundary.transform.position;
            int n = points.Count;

            // 1. Draw polygon edges and '+' midpoint insertion buttons
            Handles.color = new Color(0.96f, 0.25f, 0.55f, 0.95f);
            int insertIndex = -1;
            Vector2 insertPoint = Vector2.zero;

            for (int i = 0; i < n; i++)
            {
                Vector3 pA = isHorizontal
                    ? originPos + new Vector3(points[i].x, 0.05f, points[i].y)
                    : originPos + (Vector3)points[i];
                Vector3 pB = isHorizontal
                    ? originPos + new Vector3(points[(i + 1) % n].x, 0.05f, points[(i + 1) % n].y)
                    : originPos + (Vector3)points[(i + 1) % n];

                Handles.DrawLine(pA, pB, 4f);

                // Midpoint button to insert new vertex
                Vector3 mid = (pA + pB) * 0.5f;
                float handleSize = HandleUtility.GetHandleSize(mid) * 0.08f;
                Handles.color = Color.cyan;
                if (Handles.Button(mid, Quaternion.identity, handleSize, handleSize * 1.3f, Handles.DotHandleCap))
                {
                    insertIndex = i + 1;
                    insertPoint = isHorizontal
                        ? new Vector2(mid.x - originPos.x, mid.z - originPos.z)
                        : (Vector2)(mid - originPos);
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
                Vector3 worldPos = isHorizontal
                    ? originPos + new Vector3(points[i].x, 0.05f, points[i].y)
                    : originPos + (Vector3)points[i];
                float size = HandleUtility.GetHandleSize(worldPos) * 0.12f;

                // Vertex label (P0, P1, P2...)
                GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = Color.yellow } };
                Vector3 labelOffset = isHorizontal ? Vector3.forward * size * 1.4f : Vector3.up * size * 1.4f;
                Handles.Label(worldPos + labelOffset, $"P{i}", labelStyle);

                // Free Move Handle
                Handles.color = Color.yellow;
                Vector3 newPos = Handles.FreeMoveHandle(worldPos, size, Vector3.zero, Handles.SphereHandleCap);

                if (newPos != worldPos)
                {
                    Undo.RecordObject(boundary, "Move Polygon Point");
                    points[i] = isHorizontal
                        ? new Vector2(newPos.x - originPos.x, newPos.z - originPos.z)
                        : new Vector2(newPos.x - originPos.x, newPos.y - originPos.y);
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
