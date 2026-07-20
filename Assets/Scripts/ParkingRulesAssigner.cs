using UnityEngine;

public class ParkingRulesAssigner : MonoBehaviour
{
    [Header("Corner detection")]
    public Transform[] intersections; // assign in Inspector
    public float cornerRadius = 1.2f; // how close to an intersection counts as corner

    [ContextMenu("Assign Parking Types Now")]
    public void AssignParkingTypesNow()
    {
        if (intersections == null || intersections.Length == 0)
        {
            Debug.LogWarning("ParkingRulesAssigner: No intersections set.");
        }

        ParkingSpot[] spots = FindObjectsOfType<ParkingSpot>();

        foreach (var spot in spots)
        {
            bool isCorner = IsCornerSpot(spot.transform.position);

            if (isCorner)
            {
                // corners are always illegal
                spot.parkingType = ParkingType.Illegal;
            }
            else
            {
                // everything else is legal by default
                spot.parkingType = ParkingType.Legal;
            }

            // update its color (illegal = red if free)
            spot.UpdateVisual();
        }

        Debug.Log("ParkingRulesAssigner: Parking types assigned.");
    }

    private bool IsCornerSpot(Vector3 pos)
    {
        foreach (var inter in intersections)
        {
            if (inter == null) continue;

            float dist = Vector2.Distance(pos, inter.position);
            if (dist <= cornerRadius)
                return true;
        }
        return false;
    }
}
