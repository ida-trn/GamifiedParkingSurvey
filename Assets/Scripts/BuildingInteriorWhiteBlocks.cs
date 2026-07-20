using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
[RequireComponent(typeof(BuildingBlockPolygon))]
public class BuildingInteriorWhiteBlocks : MonoBehaviour
{
    [Header("Interior White Block Effect")]
    public bool showInteriorBlocks = true;

    [Tooltip("How far the white inner line is from the edge. 0.08 to 0.15 is usually good.")]
    [Range(0.01f, 0.40f)]
    public float insetPercent = 0.08f;

    [Tooltip("For 4-point polygons only. 1 means no vertical division.")]
    public int columns = 1;

    [Tooltip("For 4-point polygons only. 1 means no horizontal division.")]
    public int rows = 1;

    [Header("Line Style")]
    public Color lineColor = Color.white;
    public float lineWidth = 0.06f;

    [Header("Sorting")]
    public string sortingLayerName = "Buildings";
    public int orderInLayer = -78;

    private BuildingBlockPolygon block;

    private const string RootName = "Interior_White_Blocks";

    private void OnEnable()
    {
        Rebuild();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
                Rebuild();
        };
    }
#endif

    public void Rebuild()
    {
        block = GetComponent<BuildingBlockPolygon>();

        ClearOldLines();

        if (!showInteriorBlocks)
            return;

        if (block == null || block.points == null || block.points.Count < 3)
            return;

        GameObject root = new GameObject(RootName);
        root.transform.SetParent(transform);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        List<Vector2> innerPoints = CreateInsetPoints(block.points, insetPercent);

        // Draw inner white outline
        CreateLine(root.transform, "Inner_White_Outline", innerPoints, true);

        // If the polygon has exactly 4 points, we can also divide it into inner blocks
        if (innerPoints.Count == 4)
        {
            CreateGridLines(root.transform, innerPoints);
        }
    }

    private List<Vector2> CreateInsetPoints(List<Vector2> originalPoints, float inset)
    {
        List<Vector2> insetPoints = new List<Vector2>();

        Vector2 center = Vector2.zero;

        for (int i = 0; i < originalPoints.Count; i++)
            center += originalPoints[i];

        center /= originalPoints.Count;

        for (int i = 0; i < originalPoints.Count; i++)
        {
            Vector2 p = originalPoints[i];

            // Move each point slightly toward the center
            Vector2 insetPoint = Vector2.Lerp(p, center, inset);

            insetPoints.Add(insetPoint);
        }

        return insetPoints;
    }

    private void CreateGridLines(Transform root, List<Vector2> p)
    {
        columns = Mathf.Max(1, columns);
        rows = Mathf.Max(1, rows);

        // Expected order:
        // p[0] bottom-left
        // p[1] bottom-right
        // p[2] top-right
        // p[3] top-left

        for (int i = 1; i < columns; i++)
        {
            float t = i / (float)columns;

            Vector2 bottom = Vector2.Lerp(p[0], p[1], t);
            Vector2 top = Vector2.Lerp(p[3], p[2], t);

            CreateLine(root, "Vertical_Division_" + i, new List<Vector2> { bottom, top }, false);
        }

        for (int i = 1; i < rows; i++)
        {
            float t = i / (float)rows;

            Vector2 left = Vector2.Lerp(p[0], p[3], t);
            Vector2 right = Vector2.Lerp(p[1], p[2], t);

            CreateLine(root, "Horizontal_Division_" + i, new List<Vector2> { left, right }, false);
        }
    }

    private void CreateLine(Transform parent, string lineName, List<Vector2> points, bool loop)
    {
        GameObject lineObject = new GameObject(lineName);
        lineObject.transform.SetParent(parent);
        lineObject.transform.localPosition = Vector3.zero;
        lineObject.transform.localRotation = Quaternion.identity;
        lineObject.transform.localScale = Vector3.one;

        LineRenderer line = lineObject.AddComponent<LineRenderer>();

        line.useWorldSpace = false;
        line.loop = loop;
        line.positionCount = points.Count;

        line.startWidth = lineWidth;
        line.endWidth = lineWidth;

        line.startColor = lineColor;
        line.endColor = lineColor;

        line.numCornerVertices = 0;
        line.numCapVertices = 0;

        line.sortingLayerName = sortingLayerName;
        line.sortingOrder = orderInLayer;

        Shader shader = Shader.Find("Sprites/Default");
        line.sharedMaterial = new Material(shader);
        line.sharedMaterial.color = lineColor;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 p = points[i];
            line.SetPosition(i, new Vector3(p.x, p.y, -0.02f));
        }
    }

    private void ClearOldLines()
    {
        Transform oldRoot = transform.Find(RootName);

        if (oldRoot != null)
        {
#if UNITY_EDITOR
            DestroyImmediate(oldRoot.gameObject);
#else
            Destroy(oldRoot.gameObject);
#endif
        }
    }
}