using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class BuildingBlockPolygon : MonoBehaviour
{
    [Header("Polygon Points - Local Space")]
    [Tooltip("Add points around the building edge in order. Do not cross the lines.")]
    public List<Vector2> points = new List<Vector2>
    {
        new Vector2(-1, -1),
        new Vector2(1, -1),
        new Vector2(1, 1),
        new Vector2(-1, 1)
    };

    [Header("Building Fill")]
    public Color fillColor = new Color(0.72f, 0.72f, 0.72f, 1f);

    [Header("White Outline")]
    public bool showOutline = true;
    public Color outlineColor = Color.white;
    public float outlineWidth = 0.06f;

    [Header("Sorting")]
    public string sortingLayerName = "Buildings";
    public int fillOrderInLayer = -80;
    public int outlineOrderInLayer = -79;

    [Header("Collider")]
    public bool addPolygonCollider = false;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private LineRenderer outlineRenderer;

    private const string OutlineObjectName = "White_Outline";

    private void OnEnable()
    {
        BuildBlock();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
                BuildBlock();
        };
    }
#endif

    public void BuildBlock()
    {
        if (points == null || points.Count < 3)
            return;

        BuildFillMesh();

        if (showOutline)
            BuildOutline();
        else
            RemoveOutline();

        UpdateCollider();
    }

    private void BuildFillMesh()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        Mesh mesh = new Mesh();
        mesh.name = "Building Polygon Mesh";

        Vector3[] vertices = new Vector3[points.Count];

        for (int i = 0; i < points.Count; i++)
        {
            vertices[i] = new Vector3(points[i].x, points[i].y, 0f);
        }

        List<int> triangles = Triangulate(points);

        if (triangles.Count < 3)
        {
            Debug.LogWarning("[BuildingBlockPolygon] Could not build polygon. Check point order: " + gameObject.name);
            return;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles.ToArray();
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        meshFilter.sharedMesh = mesh;

        if (meshRenderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            meshRenderer.sharedMaterial = new Material(shader);
        }

        meshRenderer.sharedMaterial.color = fillColor;
        meshRenderer.sortingLayerName = sortingLayerName;
        meshRenderer.sortingOrder = fillOrderInLayer;
    }

    private void BuildOutline()
    {
        Transform outlineTransform = transform.Find(OutlineObjectName);

        if (outlineTransform == null)
        {
            GameObject outlineObject = new GameObject(OutlineObjectName);
            outlineObject.transform.SetParent(transform);
            outlineObject.transform.localPosition = Vector3.zero;
            outlineObject.transform.localRotation = Quaternion.identity;
            outlineObject.transform.localScale = Vector3.one;

            outlineRenderer = outlineObject.AddComponent<LineRenderer>();
        }
        else
        {
            outlineRenderer = outlineTransform.GetComponent<LineRenderer>();

            if (outlineRenderer == null)
                outlineRenderer = outlineTransform.gameObject.AddComponent<LineRenderer>();
        }

        outlineRenderer.useWorldSpace = false;
        outlineRenderer.loop = true;
        outlineRenderer.positionCount = points.Count;

        outlineRenderer.startWidth = outlineWidth;
        outlineRenderer.endWidth = outlineWidth;

        outlineRenderer.startColor = outlineColor;
        outlineRenderer.endColor = outlineColor;

        outlineRenderer.numCornerVertices = 0;
        outlineRenderer.numCapVertices = 0;

        outlineRenderer.sortingLayerName = sortingLayerName;
        outlineRenderer.sortingOrder = outlineOrderInLayer;

        if (outlineRenderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            outlineRenderer.sharedMaterial = new Material(shader);
        }

        outlineRenderer.sharedMaterial.color = outlineColor;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 p = points[i];
            outlineRenderer.SetPosition(i, new Vector3(p.x, p.y, -0.01f));
        }
    }

    private void RemoveOutline()
    {
        Transform outlineTransform = transform.Find(OutlineObjectName);

        if (outlineTransform != null)
        {
#if UNITY_EDITOR
            DestroyImmediate(outlineTransform.gameObject);
#else
            Destroy(outlineTransform.gameObject);
#endif
        }
    }

    private void UpdateCollider()
    {
        PolygonCollider2D polygonCollider = GetComponent<PolygonCollider2D>();

        if (addPolygonCollider)
        {
            if (polygonCollider == null)
                polygonCollider = gameObject.AddComponent<PolygonCollider2D>();

            polygonCollider.points = points.ToArray();
        }
        else
        {
            if (polygonCollider != null)
            {
#if UNITY_EDITOR
                DestroyImmediate(polygonCollider);
#else
                Destroy(polygonCollider);
#endif
            }
        }
    }

    private List<int> Triangulate(List<Vector2> polygon)
    {
        List<int> triangles = new List<int>();

        if (polygon.Count < 3)
            return triangles;

        List<int> indices = new List<int>();

        for (int i = 0; i < polygon.Count; i++)
            indices.Add(i);

        bool isClockwise = SignedArea(polygon) < 0f;

        int safety = 0;

        while (indices.Count > 3 && safety < 5000)
        {
            safety++;
            bool earFound = false;

            for (int i = 0; i < indices.Count; i++)
            {
                int previousIndex = indices[(i - 1 + indices.Count) % indices.Count];
                int currentIndex = indices[i];
                int nextIndex = indices[(i + 1) % indices.Count];

                Vector2 previous = polygon[previousIndex];
                Vector2 current = polygon[currentIndex];
                Vector2 next = polygon[nextIndex];

                if (!IsConvex(previous, current, next, isClockwise))
                    continue;

                bool hasPointInside = false;

                for (int j = 0; j < indices.Count; j++)
                {
                    int testIndex = indices[j];

                    if (testIndex == previousIndex || testIndex == currentIndex || testIndex == nextIndex)
                        continue;

                    if (PointInTriangle(polygon[testIndex], previous, current, next))
                    {
                        hasPointInside = true;
                        break;
                    }
                }

                if (hasPointInside)
                    continue;

                if (isClockwise)
                {
                    triangles.Add(previousIndex);
                    triangles.Add(currentIndex);
                    triangles.Add(nextIndex);
                }
                else
                {
                    triangles.Add(previousIndex);
                    triangles.Add(nextIndex);
                    triangles.Add(currentIndex);
                }

                indices.RemoveAt(i);
                earFound = true;
                break;
            }

            if (!earFound)
            {
                Debug.LogWarning("[BuildingBlockPolygon] Polygon may be self-crossing or points are not ordered correctly: " + gameObject.name);
                break;
            }
        }

        if (indices.Count == 3)
        {
            if (isClockwise)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
            }
            else
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[2]);
                triangles.Add(indices[1]);
            }
        }

        return triangles;
    }

    private float SignedArea(List<Vector2> polygon)
    {
        float area = 0f;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 current = polygon[i];
            Vector2 next = polygon[(i + 1) % polygon.Count];

            area += current.x * next.y - next.x * current.y;
        }

        return area * 0.5f;
    }

    private bool IsConvex(Vector2 a, Vector2 b, Vector2 c, bool clockwise)
    {
        float cross = Cross(b - a, c - b);

        if (clockwise)
            return cross < -0.0001f;
        else
            return cross > 0.0001f;
    }

    private float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float area = Mathf.Abs(Cross(b - a, c - a));
        float area1 = Mathf.Abs(Cross(a - p, b - p));
        float area2 = Mathf.Abs(Cross(b - p, c - p));
        float area3 = Mathf.Abs(Cross(c - p, a - p));

        return Mathf.Abs(area - (area1 + area2 + area3)) < 0.001f;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (points == null || points.Count < 2)
            return;

        Gizmos.color = Color.yellow;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 current = transform.TransformPoint(points[i]);
            Vector3 next = transform.TransformPoint(points[(i + 1) % points.Count]);

            Gizmos.DrawSphere(current, 0.08f);
            Gizmos.DrawLine(current, next);
        }
    }
#endif
}