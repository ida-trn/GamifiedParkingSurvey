using System.Collections.Generic;
using RTLTMPro;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Defines one named street by referencing its existing driving RoadNodes in
/// physical order. While this street is inactive, its label stays at the
/// entrance so it can be read from the intersection. While the car is on this
/// street, the label slides along the same RoadNode path with the camera. In
/// assisted mode, the manager places the label at the street's path midpoint.
/// </summary>
[DisallowMultipleComponent]
public class StreetLabelPath : MonoBehaviour
{
    [Header("Street")]
    [InspectorName("Street ID")]
    [Tooltip("Uses the same StreetCodeOption dropdown as ParkingSpot.streetCodeChoice.")]
    public StreetCodeOption streetId = StreetCodeOption.HDR;

    public string StreetId
    {
        get { return streetId.ToString(); }
    }

    [Header("World-space label")]
    [Tooltip("Assign the RTL Text Mesh Pro 3D child. Enter the Persian street name in that component's RTL Text Input Box.")]
    public RTLTextMeshPro3D label;

    public string DisplayName
    {
        get
        {
            if (label != null && !string.IsNullOrWhiteSpace(label.OriginalText))
                return label.OriginalText;

            return StreetId;
        }
    }

    // Compatibility with the earlier StreetLabelManager version. It reads
    // "displayName" only for Console messages; the actual Persian label text
    // still comes from the RTL Text Input Box.
    public string displayName
    {
        get { return DisplayName; }
    }

    [Tooltip("World Z used by the label. With a 2D camera at Z = -10, -0.1 draws in front of a map at Z = 0.")]
    public float labelDepth = -0.1f;

    [Tooltip("Used only while this is the active street. Positive values move the label toward later Road Nodes; negative values move it toward Element 0.")]
    [FormerlySerializedAs("labelOffsetAlongStreet")]
    public float movingLabelOffsetAlongStreet = 0f;

    [InspectorName("Inactive Start Offset")]
    [Tooltip("Used while the car is on another street. 0 places the label exactly at Road Nodes Element 0. A positive value moves it slightly toward Element 1.")]
    [Min(0f)]
    public float inactiveStartOffset = 0f;

    public bool rotateLabelWithStreet = true;
    public bool keepLabelUpright = true;

    [Header("Existing driving nodes, in street order")]
    [Tooltip("Drag the existing RoadNode components here from one end of this named street to the other.")]
    public List<RoadNode> roadNodes = new List<RoadNode>();

    [Header("Scene view")]
    public bool drawPathGizmos = true;
    public Color pathGizmoColor = new Color(0.15f, 0.85f, 1f, 0.9f);

    private void Reset()
    {
        FindLabelIfMissing();
    }

    private void OnValidate()
    {
        FindLabelIfMissing();
    }

    public void FindLabelIfMissing()
    {
        if (label == null)
            label = GetComponentInChildren<RTLTextMeshPro3D>(true);
    }

    // Compatibility with the earlier StreetLabelManager version. The RTL
    // component owns its displayed text, so this method must not overwrite it
    // from a normal left-to-right string field.
    public void RefreshLabelText()
    {
        FindLabelIfMissing();
    }

    public void SetLabelVisible(bool visible)
    {
        if (label != null && label.gameObject.activeSelf != visible)
            label.gameObject.SetActive(visible);
    }

    /// <summary>
    /// Returns the shortest world-space distance from a point to this street's
    /// RoadNode polyline.
    /// </summary>
    public float DistanceToStreet(Vector2 worldPoint)
    {
        Vector2 closestPoint;
        int segmentIndex;
        float segmentT;

        if (!TryGetClosestPoint(worldPoint, out closestPoint, out segmentIndex, out segmentT))
            return float.PositiveInfinity;

        return Vector2.Distance(worldPoint, closestPoint);
    }

    /// <summary>
    /// Projects the camera position onto this street's RoadNode path, applies
    /// Moving Label Offset Along Street, and clamps the result to this street.
    /// </summary>
    public void FollowCamera(Camera targetCamera)
    {
        FindLabelIfMissing();

        if (label == null || targetCamera == null)
            return;

        FollowWorldPosition(targetCamera.transform.position);
    }

    /// <summary>
    /// Moves the label along this street while keeping it constrained to the
    /// RoadNode polyline.
    /// </summary>
    public void FollowWorldPosition(Vector2 worldPosition)
    {
        FindLabelIfMissing();

        if (label == null)
            return;

        Vector2 closestPoint;
        int segmentIndex;
        float segmentT;

        if (!TryGetClosestPoint(
                worldPosition,
                out closestPoint,
                out segmentIndex,
                out segmentT))
        {
            PlaceLabelAtStreetStart();
            return;
        }

        float distanceAlongPath =
            GetDistanceToSegmentStart(segmentIndex) +
            GetSegmentLength(segmentIndex) * segmentT;

        float targetDistance = Mathf.Clamp(
            distanceAlongPath + movingLabelOffsetAlongStreet,
            0f,
            GetTotalLength()
        );

        Vector2 point;
        Vector2 tangent;
        GetPointAtDistance(targetDistance, out point, out tangent);
        ApplyLabelTransform(point, tangent);
    }

    /// <summary>
    /// Places the label at Road Nodes Element 0, or slightly inside the street
    /// when Inactive Start Offset is greater than zero. Element 1 determines the
    /// label's direction.
    /// </summary>
    [UnityEngine.ContextMenu("Place Label At Street Start")]
    public void PlaceLabelAtStreetStart()
    {
        FindLabelIfMissing();

        if (label == null ||
            roadNodes == null ||
            roadNodes.Count == 0 ||
            roadNodes[0] == null)
        {
            return;
        }

        Vector2 point = roadNodes[0].transform.position;
        Vector2 tangent = Vector2.right;

        if (roadNodes.Count >= 2)
        {
            float targetDistance = Mathf.Clamp(
                inactiveStartOffset,
                0f,
                GetTotalLength()
            );

            GetPointAtDistance(targetDistance, out point, out tangent);
        }

        ApplyLabelTransform(point, tangent);
    }

    /// <summary>
    /// Places the label halfway along the complete street path. This is used in
    /// assisted mode so every street name appears near the middle of its road.
    /// </summary>
    [UnityEngine.ContextMenu("Place Label At Street Middle")]
    public void PlaceLabelAtStreetMiddle()
    {
        FindLabelIfMissing();

        if (label == null ||
            roadNodes == null ||
            roadNodes.Count == 0 ||
            roadNodes[0] == null)
        {
            return;
        }

        if (roadNodes.Count < 2)
        {
            PlaceLabelAtStreetStart();
            return;
        }

        float totalLength = GetTotalLength();

        if (totalLength <= 0.0001f)
        {
            PlaceLabelAtStreetStart();
            return;
        }

        Vector2 point;
        Vector2 tangent;
        GetPointAtDistance(totalLength * 0.5f, out point, out tangent);
        ApplyLabelTransform(point, tangent);
    }

    private bool TryGetClosestPoint(
        Vector2 worldPoint,
        out Vector2 closestPoint,
        out int closestSegmentIndex,
        out float closestSegmentT
    )
    {
        closestPoint = Vector2.zero;
        closestSegmentIndex = -1;
        closestSegmentT = 0f;

        if (roadNodes == null || roadNodes.Count < 2)
            return false;

        float bestSquaredDistance = float.PositiveInfinity;

        for (int i = 0; i < roadNodes.Count - 1; i++)
        {
            if (roadNodes[i] == null || roadNodes[i + 1] == null)
                continue;

            Vector2 a = roadNodes[i].transform.position;
            Vector2 b = roadNodes[i + 1].transform.position;
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;

            if (lengthSquared < 0.0001f)
                continue;

            float t = Mathf.Clamp01(Vector2.Dot(worldPoint - a, ab) / lengthSquared);
            Vector2 candidate = a + ab * t;
            float squaredDistance = (worldPoint - candidate).sqrMagnitude;

            if (squaredDistance < bestSquaredDistance)
            {
                bestSquaredDistance = squaredDistance;
                closestPoint = candidate;
                closestSegmentIndex = i;
                closestSegmentT = t;
            }
        }

        return closestSegmentIndex >= 0;
    }

    private void ApplyLabelTransform(Vector2 point, Vector2 tangent)
    {
        label.transform.position = new Vector3(point.x, point.y, labelDepth);

        if (!rotateLabelWithStreet || tangent.sqrMagnitude <= 0.0001f)
            return;

        float angle = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;

        if (keepLabelUpright)
        {
            if (angle > 90f)
                angle -= 180f;
            else if (angle < -90f)
                angle += 180f;
        }

        label.transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void GetPointAtDistance(float targetDistance, out Vector2 point, out Vector2 tangent)
    {
        point = transform.position;
        tangent = Vector2.right;

        if (roadNodes == null || roadNodes.Count < 2)
            return;

        float travelled = 0f;

        for (int i = 0; i < roadNodes.Count - 1; i++)
        {
            if (roadNodes[i] == null || roadNodes[i + 1] == null)
                continue;

            Vector2 a = roadNodes[i].transform.position;
            Vector2 b = roadNodes[i + 1].transform.position;
            Vector2 direction = b - a;
            float segmentLength = direction.magnitude;

            if (segmentLength < 0.0001f)
                continue;

            if (travelled + segmentLength >= targetDistance)
            {
                float t = Mathf.Clamp01((targetDistance - travelled) / segmentLength);
                point = Vector2.Lerp(a, b, t);
                tangent = direction / segmentLength;
                return;
            }

            travelled += segmentLength;
            point = b;
            tangent = direction / segmentLength;
        }
    }

    private float GetTotalLength()
    {
        if (roadNodes == null || roadNodes.Count < 2)
            return 0f;

        float totalLength = 0f;

        for (int i = 0; i < roadNodes.Count - 1; i++)
        {
            if (roadNodes[i] == null || roadNodes[i + 1] == null)
                continue;

            totalLength += Vector2.Distance(
                roadNodes[i].transform.position,
                roadNodes[i + 1].transform.position
            );
        }

        return totalLength;
    }

    private float GetDistanceToSegmentStart(int segmentIndex)
    {
        if (roadNodes == null || roadNodes.Count < 2)
            return 0f;

        float distance = 0f;
        int lastSegment = Mathf.Clamp(segmentIndex, 0, roadNodes.Count - 2);

        for (int i = 0; i < lastSegment; i++)
            distance += GetSegmentLength(i);

        return distance;
    }

    private float GetSegmentLength(int segmentIndex)
    {
        if (roadNodes == null ||
            segmentIndex < 0 ||
            segmentIndex >= roadNodes.Count - 1 ||
            roadNodes[segmentIndex] == null ||
            roadNodes[segmentIndex + 1] == null)
        {
            return 0f;
        }

        return Vector2.Distance(
            roadNodes[segmentIndex].transform.position,
            roadNodes[segmentIndex + 1].transform.position
        );
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawPathGizmos || roadNodes == null || roadNodes.Count == 0)
            return;

        Gizmos.color = pathGizmoColor;

        for (int i = 0; i < roadNodes.Count; i++)
        {
            if (roadNodes[i] == null)
                continue;

            Vector3 position = roadNodes[i].transform.position;
            Gizmos.DrawSphere(position, 0.75f);

            if (i < roadNodes.Count - 1 && roadNodes[i + 1] != null)
                Gizmos.DrawLine(position, roadNodes[i + 1].transform.position);
        }
    }
}