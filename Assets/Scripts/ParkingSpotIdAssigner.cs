using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Safe maintenance tool for global ParkingSpot numbers and Spot IDs.
///
/// Normal maintenance:
/// - Existing valid spotNumber values are preserved.
/// - New/unassigned spots (spotNumber <= 0) receive new numbers ABOVE the
///   current maximum.
/// - If a duplicated spot copied an existing number, the first occurrence
///   keeps the old number and later duplicates receive new numbers.
/// - Deleted spots are NOT renumbered. Gaps are intentionally preserved.
///
/// Only use "DANGER - Renumber All Spots 1-N" before real respondent data
/// collection if you deliberately want a complete one-time renumber.
/// </summary>
public class ParkingSpotIdAssigner : MonoBehaviour
{
    [Header("Scope")]
    [Tooltip("Optional. Leave empty to include all ParkingSpot objects in the scene.")]
    public Transform parkingRoot;

    public bool includeInactiveObjects = true;

    [Header("Numbering")]
    public int firstSpotNumber = 1;

    [ContextMenu("SAFE - Assign Numbers To New Or Duplicate Spots")]
    public void AssignNumbersToNewOrDuplicateSpots()
    {
        List<ParkingSpot> spots = GetStableSpotList();

        if (spots.Count == 0)
        {
            Debug.LogWarning("[ParkingSpotIdAssigner] No ParkingSpot objects found.");
            return;
        }

        Dictionary<int, ParkingSpot> firstOwnerByNumber =
            new Dictionary<int, ParkingSpot>();

        List<ParkingSpot> needsNumber = new List<ParkingSpot>();
        int maxExistingNumber = firstSpotNumber - 1;

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            if (spot.spotNumber <= 0)
            {
                needsNumber.Add(spot);
                continue;
            }

            maxExistingNumber = Mathf.Max(maxExistingNumber, spot.spotNumber);

            if (!firstOwnerByNumber.ContainsKey(spot.spotNumber))
            {
                firstOwnerByNumber.Add(spot.spotNumber, spot);
            }
            else
            {
                // Typical case: a ParkingSpot was duplicated and inherited
                // the original object's number. Preserve the first occurrence
                // and give this duplicate a new number.
                needsNumber.Add(spot);
            }
        }

        int nextNumber = Mathf.Max(firstSpotNumber, maxExistingNumber + 1);
        HashSet<int> usedNumbers = new HashSet<int>(firstOwnerByNumber.Keys);

        int changed = 0;

        foreach (ParkingSpot spot in needsNumber)
        {
            while (usedNumbers.Contains(nextNumber))
                nextNumber++;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(spot, "Assign Parking Spot Number");
#endif

            int oldNumber = spot.spotNumber;
            spot.spotNumber = nextNumber;
            usedNumbers.Add(nextNumber);

            spot.RefreshAfterDataChange(true);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(spot);
#endif

            Debug.Log(
                $"[ParkingSpotIdAssigner] {spot.name}: " +
                $"Spot Number {oldNumber} -> {nextNumber}"
            );

            nextNumber++;
            changed++;
        }

        // Refresh all IDs, including existing spots, in case street/side/
        // region/legalness was edited.
        RefreshAllSpotIdsInternal(spots);

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif

        Debug.Log(
            $"[ParkingSpotIdAssigner] SAFE assignment finished. " +
            $"Total spots={spots.Count}, newly numbered/duplicate-fixed={changed}. " +
            "Existing unique Spot Numbers were preserved."
        );

        CheckSpotNumbersAndIds();
    }

    [ContextMenu("SAFE - Refresh All Spot IDs")]
    public void RefreshAllSpotIds()
    {
        List<ParkingSpot> spots = GetStableSpotList();
        RefreshAllSpotIdsInternal(spots);

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif

        Debug.Log(
            $"[ParkingSpotIdAssigner] Refreshed {spots.Count} Spot IDs. " +
            "No Spot Numbers were changed."
        );
    }

    [ContextMenu("CHECK - Spot Numbers And IDs")]
    public void CheckSpotNumbersAndIds()
    {
        List<ParkingSpot> spots = GetStableSpotList();

        Dictionary<int, List<ParkingSpot>> byNumber =
            new Dictionary<int, List<ParkingSpot>>();

        Dictionary<string, List<ParkingSpot>> byId =
            new Dictionary<string, List<ParkingSpot>>();

        int unassigned = 0;
        int minNumber = int.MaxValue;
        int maxNumber = int.MinValue;

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

            if (spot.spotNumber <= 0)
            {
                unassigned++;
            }
            else
            {
                minNumber = Mathf.Min(minNumber, spot.spotNumber);
                maxNumber = Mathf.Max(maxNumber, spot.spotNumber);

                if (!byNumber.TryGetValue(spot.spotNumber, out List<ParkingSpot> numberList))
                {
                    numberList = new List<ParkingSpot>();
                    byNumber.Add(spot.spotNumber, numberList);
                }

                numberList.Add(spot);
            }

            string id = spot.spotId ?? string.Empty;

            if (!byId.TryGetValue(id, out List<ParkingSpot> idList))
            {
                idList = new List<ParkingSpot>();
                byId.Add(id, idList);
            }

            idList.Add(spot);
        }

        var duplicateNumbers = byNumber
            .Where(pair => pair.Value.Count > 1)
            .ToList();

        var duplicateIds = byId
            .Where(pair =>
                !string.IsNullOrWhiteSpace(pair.Key) &&
                pair.Value.Count > 1)
            .ToList();

        foreach (var duplicate in duplicateNumbers)
        {
            Debug.LogWarning(
                $"[ParkingSpotIdAssigner] Duplicate Spot Number {duplicate.Key}: " +
                string.Join(", ", duplicate.Value.Select(s => s.name))
            );
        }

        foreach (var duplicate in duplicateIds)
        {
            Debug.LogWarning(
                $"[ParkingSpotIdAssigner] Duplicate Spot ID {duplicate.Key}: " +
                string.Join(", ", duplicate.Value.Select(s => s.name))
            );
        }

        string range = minNumber == int.MaxValue
            ? "none"
            : $"{minNumber}-{maxNumber}";

        Debug.Log(
            $"[ParkingSpotIdAssigner] CHECK complete | " +
            $"total={spots.Count}, numberRange={range}, " +
            $"unassigned={unassigned}, " +
            $"duplicateNumbers={duplicateNumbers.Count}, " +
            $"duplicateIds={duplicateIds.Count}"
        );
    }

    [ContextMenu("DANGER - Renumber All Spots 1-N")]
    public void RenumberAllSpotsOneToN()
    {
        List<ParkingSpot> spots = GetStableSpotList();

        int nextNumber = Mathf.Max(1, firstSpotNumber);

        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(spot, "Renumber All Parking Spots");
#endif

            spot.spotNumber = nextNumber;
            spot.RefreshAfterDataChange(true);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(spot);
#endif

            nextNumber++;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif

        Debug.LogWarning(
            $"[ParkingSpotIdAssigner] ALL {spots.Count} spots were renumbered. " +
            "Do not use this after real respondent data collection has started."
        );

        CheckSpotNumbersAndIds();
    }

    private void RefreshAllSpotIdsInternal(List<ParkingSpot> spots)
    {
        foreach (ParkingSpot spot in spots)
        {
            if (spot == null)
                continue;

#if UNITY_EDITOR
            if (!Application.isPlaying)
                Undo.RecordObject(spot, "Refresh Parking Spot ID");
#endif

            spot.RefreshAfterDataChange(true);

#if UNITY_EDITOR
            if (!Application.isPlaying)
                EditorUtility.SetDirty(spot);
#endif
        }
    }

    private List<ParkingSpot> GetStableSpotList()
    {
        ParkingSpot[] spots;

        if (parkingRoot != null)
        {
            spots = parkingRoot.GetComponentsInChildren<ParkingSpot>(
                includeInactiveObjects
            );
        }
        else
        {
            spots = FindObjectsByType<ParkingSpot>(
                includeInactiveObjects
                    ? FindObjectsInactive.Include
                    : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );
        }

        return spots
            .Where(s => s != null)
            .OrderBy(s => GetHierarchySortKey(s.transform))
            .ToList();
    }

    private static string GetHierarchySortKey(Transform target)
    {
        if (target == null)
            return string.Empty;

        List<int> siblingIndices = new List<int>();
        Transform current = target;

        while (current != null)
        {
            siblingIndices.Add(current.GetSiblingIndex());
            current = current.parent;
        }

        siblingIndices.Reverse();

        return string.Join(
            ".",
            siblingIndices.Select(i => i.ToString("D6"))
        );
    }
}
