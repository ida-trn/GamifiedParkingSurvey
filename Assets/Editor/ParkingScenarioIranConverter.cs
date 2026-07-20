#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Optional one-time editor utility.
/// Put this file inside Assets/Editor, then use:
/// Tools > Parking Survey > Convert Scenario Prices To Iran (Toman)
///
/// It preserves the old two-level design by converting:
/// legal fee 5 -> 6,000 and 10 -> 12,000 tomans/hour
/// illegal fine 50 -> 100,000 and 100 -> 200,000 tomans
/// Values that already look like tomans are not converted again.
/// </summary>
public static class ParkingScenarioIranConverter
{
    [MenuItem("Tools/Parking Survey/Convert Scenario Prices To Iran (Toman)")]
    public static void ConvertAllScenarioAssets()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Convert parking scenario prices",
            "This will update every ParkingScenario asset in the project.\n\n" +
            "Legal fees below 1,000 will be multiplied by 1,200.\n" +
            "Illegal fines below 10,000 will be multiplied by 2,000.\n" +
            "Citation probabilities and occupancy values will not change.\n\n" +
            "Continue?",
            "Convert",
            "Cancel"
        );

        if (!confirmed)
            return;

        string[] guids = AssetDatabase.FindAssets("t:ParkingScenario");
        int changedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ParkingScenario scenario = AssetDatabase.LoadAssetAtPath<ParkingScenario>(path);

            if (scenario == null)
                continue;

            bool changed = false;

            if (scenario.legalFeePerHour > 0f && scenario.legalFeePerHour < 1000f)
            {
                scenario.legalFeePerHour *= 1200f;
                changed = true;
            }

            if (scenario.illegalFine > 0f && scenario.illegalFine < 10000f)
            {
                scenario.illegalFine *= 2000f;
                changed = true;
            }

            if (!Mathf.Approximately(scenario.onTimeReward, 100f))
            {
                scenario.onTimeReward = 100f;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(scenario);
                changedCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Conversion complete",
            changedCount + " ParkingScenario asset(s) were updated.",
            "OK"
        );

        Debug.Log("[ParkingScenarioIranConverter] Updated scenario assets: " + changedCount);
    }
}
#endif
