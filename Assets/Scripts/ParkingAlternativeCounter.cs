using UnityEngine;

public class ParkingAlternativeCounter : MonoBehaviour
{
    [Header("Counter Settings")]
    [Tooltip("Include inactive ParkingSpot objects in the count.")]
    public bool includeInactiveSpots = true;


    [Header("Diagnostic Group Colors")]
    [Tooltip("Temporary visualization color for A1 - Inner Legal")]
    public Color a1Color = new Color(0f, 1f, 0f, 1f);       // bright green

    [Tooltip("Temporary visualization color for A2 - Inner Illegal")]
    public Color a2Color = new Color(1f, 0f, 0f, 1f);       // bright red

    [Tooltip("Temporary visualization color for A3 - Outer Legal")]
    public Color a3Color = new Color(0f, 1f, 1f, 1f);       // cyan

    [Tooltip("Temporary visualization color for A4 - Outer Illegal")]
    public Color a4Color = new Color(1f, 0f, 1f, 1f);       // magenta

    [Tooltip("Temporary visualization color for A5 - Far Legal")]
    public Color a5Color = new Color(0.2f, 0.4f, 1f, 1f);   // blue

    [Tooltip("Temporary visualization color for A6 - Far Illegal")]
    public Color a6Color = new Color(1f, 0.5f, 0f, 1f);     // orange


    [Header("Single Group Highlight")]
    [Tooltip("Color used for the selected group when showing only one alternative.")]
    public Color singleHighlightColor = Color.yellow;

    [Tooltip("How dark the other groups become when highlighting one group.")]
    [Range(0f, 1f)]
    public float otherGroupsBrightness = 0.12f;


    // =========================================================
    // COUNTING
    // =========================================================

    [ContextMenu("Count Parking Spots A1-A6")]
    public void CountParkingSpots()
    {
        ParkingSpot[] spots = GetSpots();

        int a1 = 0, a2 = 0, a3 = 0, a4 = 0, a5 = 0, a6 = 0;
        int unknown = 0;
        int excludedBlocked = 0;

        int a2NoFine = 0, a2Fined = 0, a2Other = 0;
        int a4NoFine = 0, a4Fined = 0, a4Other = 0;
        int a6NoFine = 0, a6Fined = 0, a6Other = 0;

        int intersectionCorner = 0;
        int pedestrianCrossing = 0;
        int drivewayEntrance = 0;
        int noParkingSign = 0;
        int noStoppingSign = 0;
        int busStop = 0;
        int otherReason = 0;
        int noReason = 0;

        int modeledTotal = 0;

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            // Match ParkingOccupancyManager: NoParkingZone objects are static
            // blocked areas, not part of the scenario occupancy supply.
            if (spot.IsPhysicallyBlockedParking())
            {
                excludedBlocked++;
                continue;
            }

            if (!spot.IsModeledAlternative())
            {
                unknown++;
                continue;
            }

            modeledTotal++;
            string alternative = spot.GetAlternativeId6();

            switch (alternative)
            {
                case "A1": a1++; break;
                case "A2":
                    a2++;
                    CountIllegalSubgroup(spot, ref a2NoFine, ref a2Fined, ref a2Other);
                    break;
                case "A3": a3++; break;
                case "A4":
                    a4++;
                    CountIllegalSubgroup(spot, ref a4NoFine, ref a4Fined, ref a4Other);
                    break;
                case "A5": a5++; break;
                case "A6":
                    a6++;
                    CountIllegalSubgroup(spot, ref a6NoFine, ref a6Fined, ref a6Other);
                    break;
                default:
                    unknown++;
                    Debug.LogWarning($"[ParkingAlternativeCounter] Unknown alternative on spot: {spot.name}", spot);
                    break;
            }

            if (spot.parkingType == ParkingType.Illegal)
            {
                switch (spot.illegalReason)
                {
                    case IllegalReason.IntersectionCorner: intersectionCorner++; break;
                    case IllegalReason.PedestrianCrossing: pedestrianCrossing++; break;
                    case IllegalReason.DrivewayEntrance: drivewayEntrance++; break;
                    case IllegalReason.NoParkingSign: noParkingSign++; break;
                    case IllegalReason.NoStoppingSign: noStoppingSign++; break;
                    case IllegalReason.BusStop: busStop++; break;
                    case IllegalReason.Other: otherReason++; break;
                    case IllegalReason.None:
                    default: noReason++; break;
                }
            }
        }

        int legalTotal = a1 + a3 + a5;
        int illegalTotal = a2 + a4 + a6;
        int noFineTotal = a2NoFine + a4NoFine + a6NoFine;
        int finedTotal = a2Fined + a4Fined + a6Fined;
        int otherIllegalTotal = a2Other + a4Other + a6Other;

        Debug.Log("========== MODELED PARKING A1-A6 COUNTS ==========");
        LogCount("A1 - Inner Legal", a1, modeledTotal);
        LogCount("A2 - Inner Illegal", a2, modeledTotal);
        LogCount("A3 - Outer Legal", a3, modeledTotal);
        LogCount("A4 - Outer Illegal", a4, modeledTotal);
        LogCount("A5 - Far Legal", a5, modeledTotal);
        LogCount("A6 - Far Illegal", a6, modeledTotal);

        Debug.Log("--------------------------------------------------");
        Debug.Log("ILLEGAL SPLIT BY DISTANCE + FINE STATUS");
        LogIllegalBreakdown("A2 - Inner Illegal", a2, a2NoFine, a2Fined, a2Other);
        LogIllegalBreakdown("A4 - Outer Illegal", a4, a4NoFine, a4Fined, a4Other);
        LogIllegalBreakdown("A6 - Far Illegal", a6, a6NoFine, a6Fined, a6Other);

        Debug.Log("--------------------------------------------------");
        Debug.Log($"NO-FINE illegal total: {noFineTotal}/{illegalTotal} ({Percent(noFineTotal, illegalTotal):F1}% of illegal)");
        Debug.Log($"FINED illegal total:   {finedTotal}/{illegalTotal} ({Percent(finedTotal, illegalTotal):F1}% of illegal)");

        if (otherIllegalTotal > 0)
            Debug.LogWarning($"OTHER/UNCLASSIFIED illegal total: {otherIllegalTotal}/{illegalTotal}. These are IllegalReason.Other or IllegalReason.None.");

        Debug.Log("--------------------------------------------------");
        Debug.Log("ILLEGAL REASONS");
        Debug.Log($"No fine | Intersection Corner:  {intersectionCorner}");
        Debug.Log($"No fine | Pedestrian Crossing: {pedestrianCrossing}");
        Debug.Log($"Fined   | Driveway Entrance:   {drivewayEntrance}");
        Debug.Log($"Fined   | No Parking Sign:     {noParkingSign}");
        Debug.Log($"Fined   | No Stopping Sign:    {noStoppingSign}");
        Debug.Log($"Fined   | Bus Stop:            {busStop}");

        if (otherReason > 0) Debug.LogWarning($"IllegalReason.Other: {otherReason}");
        if (noReason > 0) Debug.LogWarning($"IllegalReason.None on illegal spots: {noReason}");

        Debug.Log("--------------------------------------------------");
        Debug.Log($"Inner total: {a1 + a2}");
        Debug.Log($"Outer total: {a3 + a4}");
        Debug.Log($"Far total:   {a5 + a6}");
        Debug.Log("--------------------------------------------------");
        Debug.Log($"Legal total:   {legalTotal}");
        Debug.Log($"Illegal total: {illegalTotal}");
        Debug.Log("--------------------------------------------------");
        Debug.Log($"Excluded physically blocked NoParkingZone spots: {excludedBlocked}");
        Debug.Log($"Unknown / non-modeled: {unknown}");
        Debug.Log($"TOTAL MODELED PARKING SPOTS: {modeledTotal}");
        Debug.Log($"TOTAL ParkingSpot OBJECTS FOUND: {spots.Length}");
        Debug.Log("==================================================");
    }


    // =========================================================
    // SHOW ALL SIX GROUPS USING DIFFERENT COLORS
    // =========================================================

    [ContextMenu("VISUALIZE - Show All A1-A6 With Colors")]
    public void ShowAllGroupsWithColors()
    {
        ParkingSpot[] spots = GetSpots();

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            SpriteRenderer sr = spot.GetComponent<SpriteRenderer>();

            if (sr == null)
                continue;

            switch (spot.GetAlternativeId6())
            {
                case "A1":
                    sr.color = a1Color;
                    break;

                case "A2":
                    sr.color = a2Color;
                    break;

                case "A3":
                    sr.color = a3Color;
                    break;

                case "A4":
                    sr.color = a4Color;
                    break;

                case "A5":
                    sr.color = a5Color;
                    break;

                case "A6":
                    sr.color = a6Color;
                    break;

                default:
                    sr.color = Color.black;
                    break;
            }
        }

        Debug.Log(
            "[ParkingAlternativeCounter] Showing all A1-A6 groups using temporary diagnostic colors."
        );
    }


    // =========================================================
    // SHOW ONE GROUP AT A TIME
    // =========================================================

    [ContextMenu("VISUALIZE - Highlight A1 Only")]
    public void HighlightA1()
    {
        HighlightSingleGroup("A1");
    }

    [ContextMenu("VISUALIZE - Highlight A2 Only")]
    public void HighlightA2()
    {
        HighlightSingleGroup("A2");
    }

    [ContextMenu("VISUALIZE - Highlight A3 Only")]
    public void HighlightA3()
    {
        HighlightSingleGroup("A3");
    }

    [ContextMenu("VISUALIZE - Highlight A4 Only")]
    public void HighlightA4()
    {
        HighlightSingleGroup("A4");
    }

    [ContextMenu("VISUALIZE - Highlight A5 Only")]
    public void HighlightA5()
    {
        HighlightSingleGroup("A5");
    }

    [ContextMenu("VISUALIZE - Highlight A6 Only")]
    public void HighlightA6()
    {
        HighlightSingleGroup("A6");
    }


    private void HighlightSingleGroup(string targetAlternative)
    {
        ParkingSpot[] spots = GetSpots();

        int highlightedCount = 0;

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            SpriteRenderer sr = spot.GetComponent<SpriteRenderer>();

            if (sr == null)
                continue;

            string alternative = spot.GetAlternativeId6();

            if (alternative == targetAlternative)
            {
                // Very bright selected group
                sr.color = singleHighlightColor;
                highlightedCount++;
            }
            else
            {
                // Darken all other parking spots so the selected
                // alternative becomes easy to see on the map.
                Color original = sr.color;

                sr.color = new Color(
                    otherGroupsBrightness,
                    otherGroupsBrightness,
                    otherGroupsBrightness,
                    Mathf.Max(0.25f, original.a)
                );
            }
        }

        Debug.Log(
            $"[ParkingAlternativeCounter] Highlighting {targetAlternative}: " +
            $"{highlightedCount} spots."
        );
    }


    // =========================================================
    // RESTORE NORMAL GAME COLORS
    // =========================================================

    [ContextMenu("VISUALIZE - Restore Normal Parking Colors")]
    public void RestoreNormalColors()
    {
        ParkingSpot[] spots = GetSpots();

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            // ParkingSpot already knows whether the spot should be:
            // legal green,
            // illegal red,
            // disabled blue,
            // occupied grey,
            // etc.
            spot.UpdateVisual();
        }

        Debug.Log(
            "[ParkingAlternativeCounter] Normal ParkingSpot colors restored."
        );
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private ParkingSpot[] GetSpots()
    {
        return FindObjectsByType<ParkingSpot>(
            includeInactiveSpots
                ? FindObjectsInactive.Include
                : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );
    }


    private void CountIllegalSubgroup(ParkingSpot spot, ref int noFine, ref int fined, ref int other)
    {
        if (spot == null || spot.parkingType != ParkingType.Illegal) return;

        if (IsNoFineIllegalReason(spot.illegalReason)) noFine++;
        else if (IsFinedIllegalReason(spot.illegalReason)) fined++;
        else other++;
    }

    private bool IsNoFineIllegalReason(IllegalReason reason)
    {
        return reason == IllegalReason.IntersectionCorner ||
               reason == IllegalReason.PedestrianCrossing;
    }

    private bool IsFinedIllegalReason(IllegalReason reason)
    {
        return reason == IllegalReason.DrivewayEntrance ||
               reason == IllegalReason.NoParkingSign ||
               reason == IllegalReason.NoStoppingSign ||
               reason == IllegalReason.BusStop;
    }

    private void LogIllegalBreakdown(string label, int total, int noFine, int fined, int other)
    {
        Debug.Log(
            $"{label}: total={total} | " +
            $"NO-FINE={noFine} ({Percent(noFine, total):F1}%) | " +
            $"FINED={fined} ({Percent(fined, total):F1}%) | " +
            $"other={other}"
        );
    }

    private float Percent(int part, int total)
    {
        return total > 0 ? (part / (float)total) * 100f : 0f;
    }


    private void LogCount(string label, int count, int total)
    {
        float percentage =
            total > 0
                ? (count / (float)total) * 100f
                : 0f;

        Debug.Log(
            $"{label}: {count} spots ({percentage:F1}%)"
        );
    }
}