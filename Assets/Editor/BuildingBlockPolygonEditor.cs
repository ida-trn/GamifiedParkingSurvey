using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BuildingBlockPolygon))]
public class BuildingBlockPolygonEditor : Editor
{
    private BuildingBlockPolygon block;

    private void OnEnable()
    {
        block = (BuildingBlockPolygon)target;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(10);

        if (GUILayout.Button("Add Point"))
        {
            Undo.RecordObject(block, "Add Polygon Point");

            if (block.points == null)
                block.points = new System.Collections.Generic.List<Vector2>();

            if (block.points.Count == 0)
                block.points.Add(Vector2.zero);
            else
                block.points.Add(block.points[block.points.Count - 1] + new Vector2(0.5f, 0.5f));

            block.BuildBlock();
            EditorUtility.SetDirty(block);
        }

        if (GUILayout.Button("Remove Last Point"))
        {
            if (block.points != null && block.points.Count > 3)
            {
                Undo.RecordObject(block, "Remove Polygon Point");

                block.points.RemoveAt(block.points.Count - 1);

                block.BuildBlock();
                EditorUtility.SetDirty(block);
            }
        }

        if (GUILayout.Button("Rebuild Polygon"))
        {
            block.BuildBlock();
            EditorUtility.SetDirty(block);
        }
    }

    private void OnSceneGUI()
    {
        if (block == null || block.points == null)
            return;

        Transform t = block.transform;

        Handles.color = Color.yellow;

        for (int i = 0; i < block.points.Count; i++)
        {
            Vector3 localPoint = new Vector3(block.points[i].x, block.points[i].y, 0f);
            Vector3 worldPoint = t.TransformPoint(localPoint);

            float handleSize = HandleUtility.GetHandleSize(worldPoint) * 0.12f;

            Handles.Label(worldPoint + Vector3.up * handleSize, "P" + i);

            EditorGUI.BeginChangeCheck();

            Vector3 newWorldPoint = Handles.FreeMoveHandle(
                worldPoint,
                handleSize,
                Vector3.zero,
                Handles.DotHandleCap
            );

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(block, "Move Polygon Point");

                Vector3 newLocalPoint = t.InverseTransformPoint(newWorldPoint);

                block.points[i] = new Vector2(newLocalPoint.x, newLocalPoint.y);

                block.BuildBlock();
                EditorUtility.SetDirty(block);
            }
        }

        Handles.color = Color.cyan;

        for (int i = 0; i < block.points.Count; i++)
        {
            Vector3 current = t.TransformPoint(block.points[i]);
            Vector3 next = t.TransformPoint(block.points[(i + 1) % block.points.Count]);

            Handles.DrawLine(current, next);
        }
    }
}