using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
[RequireComponent(typeof(BuildingBlockPolygon))]
public class BuildingBlockDetails : MonoBehaviour
{
    [Header("Interior Style")]
    public bool showInnerBorder = true;
    public bool showRoofDetails = true;

    [Range(0.02f, 0.35f)]
    public float innerInset = 0.10f;

    [Header("Roof Detail Lines")]
    public int roofLineCount = 4;
    public float roofLineLength = 0.35f;
    public float roofLineWidth = 0.035f;

    [Header("Colors")]
    public Color innerLineColor = Color.white;
    public Color roofDetailColor = new Color(0.9f, 0.9f, 0.9f, 1f);

    [Header("Sorting")]
    public string sortingLayerName = "Buildings";
    public int orderInLayer = -78;

    private BuildingBlockPolygon block;
    private const string DetailRootName = "Building_Details";

    private void OnEnable()
    {
        RebuildDetails();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
                RebuildDetails();
        };
    }
#endif

    public void RebuildDetails()
    {
        block = GetComponent<BuildingBlockPolygon>();

        ClearOldDetails();

        if (block == null || block.points == null || block.points.Count < 3)
            return;

        GameObject root = new GameObject(DetailRootName);
        root.transform.SetParent(transform);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        List<Vector2> innerPoints = CreateInsetPoints(block.points, innerInset);

        if (showInnerBorder)
        {
            CreateLine(root.transform, "Inner_White_Border", innerPoints, true, innerLineColor, roofLineWidth);
        }

        if (showRoofDetails)
        {
            CreateRoofLines(root.transform, innerPoints);
        }
    }

    private List<Vector2> CreateInsetPoints(List<Vector2> originalPoints, float inset)
    {
        List<Vector2> insetPoints = new List<Vector2>();

        Vector2 center = Vector2.zero;

        foreach (Vector2 p in originalPoints)
            center += p;

        center /= originalPoints.Count;

        foreach (Vector2 p in originalPoints)
        {
            insetPoints.Add(Vector2.Lerp(p, center, inset));
        }

        return insetPoints;
    }

    private void CreateRoofLines(Transform root, List<Vector2> polygon)
    {
        if (roofLineCount <= 0)
            return;

        Bounds bounds = GetLocalBounds(polygon);

        int created = 0;
        int attempts = 0;

        while (created < roofLineCount && attempts < 200)
        {
            attempts++;

            float x = Random.Range(bounds.min.x, bounds.max.x);
            float y = Random.Range(bounds.min.y, bounds.max.y);

            Vector2 center = new Vector2(x, y);

            if (!PointInsidePolygon(center, polygon))
                continue;

            Vector2 a = center + Vector2.left * roofLineLength * 0.5f;
            Vector2 b = center + Vector2.right * roofLineLength * 0.5f;

            if (!PointInsidePolygon(a, polygon) || !PointInsidePolygon(b, polygon))
                continue;

            CreateLine(
                root,
                "Roof_Detail_" + created,
                new List<Vector2> { a, b },
                false,
                roofDetailColor,
                roofLineWidth
            );

            created++;
        }
    }

    private Bounds GetLocalBounds(List<Vector2> polygon)
    {
        Bounds bounds = new Bounds(polygon[0], Vector3.zero);

        foreach (Vector2 p in polygon)
        {
            bounds.Encapsulate(new Vector3(p.x, p.y, 0f));
        }

        return bounds;
    }

    private void CreateLine(
        Transform parent,
        string lineName,
        List<Vector2> points,
        bool loop,
        Color color,
        float width
    )
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

        line.startWidth = width;
        line.endWidth = width;

        line.startColor = color;
        line.endColor = color;

        line.sortingLayerName = sortingLayerName;
        line.sortingOrder = orderInLayer;

        line.numCornerVertices = 0;
        line.numCapVertices = 0;

        Shader shader = Shader.Find("Sprites/Default");
        line.sharedMaterial = new Material(shader);
        line.sharedMaterial.color = color;

        for (int i = 0; i < points.Count; i++)
        {
            line.SetPosition(i, new Vector3(points[i].x, points[i].y, -0.02f));
        }
    }

    private bool PointInsidePolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;

        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            bool intersect =
                ((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                (point.x < (polygon[j].x - polygon[i].x) *
                (point.y - polygon[i].y) /
                (polygon[j].y - polygon[i].y) + polygon[i].x);

            if (intersect)
                inside = !inside;
        }

        return inside;
    }

    private void ClearOldDetails()
    {
        Transform oldRoot = transform.Find(DetailRootName);

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