using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Assigns intersection-corner parking rules without breaking Spot IDs.
///
/// Recommended use:
/// - Keep Preserve Non-Corner Parking Type ON.
/// - Corner spots are forced to Illegal.
/// - Existing manually assigned non-corner Legal/Illegal values are preserved.
/// - If a corner spot has no illegal reason yet, IntersectionCorner is assigned.
/// - Every changed spot refreshes its color, Persian label, and A1-A6 Spot ID.
/// - spotNumber is NEVER changed by this script.
/// </summary>
public class ParkingRulesAssigner : MonoBehaviour
{
    [Header("Corner Detection")]
    public Transform[] intersections;

    [Tooltip("How close to an intersection counts as a corner spot.")]
    public float cornerRadius = 1.2f;

    [Header("Safe Assignment")]
    [Tooltip("Recommended ON. Non-corner spots keep their current Legal/Illegal status, so manually assigned driveway/sign/bus-stop/etc. rules are not overwritten.")]
    public bool preserveNonCornerParkingType = true;

    [Tooltip("If a detected corner is illegal but has no illegal reason, assign IntersectionCorner.")]
    public bool assignIntersectionCornerReasonIfEmpty = true;

    [Tooltip("Include inactive ParkingSpot objects when assigning rules.")]
    public bool includeInactiveSpots = true;

    [ContextMenu("Assign Parking Types Now")]
    public void AssignParkingTypesNow()
    {
        if (intersections == null || intersections.Length == 0)
        {
            Debug.LogWarning(
                "[ParkingRulesAssigner] No intersections are assigned. " +
                "No corner spots can be detected."
            );
        }

        ParkingSpot[] spots = FindObjectsByType<ParkingSpot>(
            includeInactiveSpots
                ? FindObjectsInactive.Include
                : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        int cornerCount = 0;
        int changedCount = 0;

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(spot, "Assign Parking Rules");
#endif

            ParkingType oldType = spot.parkingType;
            IllegalReason oldReason = spot.illegalReason;

            bool isCorner = IsCornerSpot(spot.transform.position);

            if (isCorner)
            {
                cornerCount++;

                // Corner spots are always illegal.
                spot.parkingType = ParkingType.Illegal;

                // Preserve an existing specific illegal reason. Only fill it
                // automatically when no reason has been assigned yet.
                if (assignIntersectionCornerReasonIfEmpty &&
                    spot.illegalReason == IllegalReason.None)
                {
                    spot.illegalReason = IllegalReason.IntersectionCorner;
                }
            }
            else if (!preserveNonCornerParkingType)
            {
                // Optional legacy behavior: everything not near a corner
                // becomes legal. Keep this OFF if other illegal reasons were
                // assigned manually elsewhere.
                spot.parkingType = ParkingType.Legal;
            }

            // Enforces NoParkingZone -> Illegal, clears incompatible
            // legal/illegal detail fields, refreshes colors/labels, and
            // rebuilds the A1-A6 Spot ID WITHOUT changing spotNumber.
            spot.RefreshAfterDataChange(true);

            if (oldType != spot.parkingType ||
                oldReason != spot.illegalReason)
            {
                changedCount++;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(spot);
#endif
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif

        Debug.Log(
            $"[ParkingRulesAssigner] Finished. Spots checked={spots.Length}, " +
            $"corner spots={cornerCount}, changed spots={changedCount}. " +
            "Spot Numbers were not changed."
        );
    }

    private bool IsCornerSpot(Vector3 pos)
    {
        if (intersections == null)
            return false;

        foreach (Transform inter in intersections)
        {
            if (inter == null)
                continue;

            float dist = Vector2.Distance(pos, inter.position);

            if (dist <= cornerRadius)
                return true;
        }

        return false;
    }
}
