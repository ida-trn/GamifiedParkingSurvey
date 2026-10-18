#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ScenarioFinePolicyMigrator
{
    [MenuItem("Tools/Scenarios/Migrate Legacy Fine Levels")]
    public static void MigrateLegacyFineLevels()
    {
        string[] guids = AssetDatabase.FindAssets("t:ParkingScenario");

        int mainScenarioCount = 0;
        int currentCount = 0;
        int doubledCount = 0;
        int practiceCount = 0;
        int warningCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ParkingScenario scenario =
                AssetDatabase.LoadAssetAtPath<ParkingScenario>(path);

            if (scenario == null)
                continue;

            Undo.RecordObject(
                scenario,
                "Migrate Scenario Fine Policy"
            );

            if (string.Equals(
                    scenario.scenarioId,
                    "Practice01",
                    StringComparison.OrdinalIgnoreCase))
            {
                scenario.illegalFinePolicy =
                    IllegalFinePolicyLevel.Current;

                practiceCount++;
                EditorUtility.SetDirty(scenario);
                continue;
            }

            bool isMainScenario =
                !string.IsNullOrWhiteSpace(scenario.scenarioId) &&
                scenario.scenarioId.StartsWith(
                    "S",
                    StringComparison.OrdinalIgnoreCase
                );

            if (!isMainScenario)
                continue;

            mainScenarioCount++;

            if (Mathf.Approximately(
                    scenario.illegalFine,
                    60000f))
            {
                scenario.illegalFinePolicy =
                    IllegalFinePolicyLevel.Current;

                currentCount++;
            }
            else if (Mathf.Approximately(
                         scenario.illegalFine,
                         120000f))
            {
                scenario.illegalFinePolicy =
                    IllegalFinePolicyLevel.Doubled;

                doubledCount++;
            }
            else
            {
                warningCount++;

                Debug.LogWarning(
                    $"[Fine Policy Migration] {scenario.scenarioId} " +
                    $"has legacy illegalFine={scenario.illegalFine:0}. " +
                    "Expected 60000 or 120000, so its Fine Policy was not changed.",
                    scenario
                );
            }

            EditorUtility.SetDirty(scenario);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "========== FINE POLICY MIGRATION FINISHED ==========\n" +
            $"Main scenarios found: {mainScenarioCount}\n" +
            $"Mapped to Current (1x): {currentCount}\n" +
            $"Mapped to Doubled (2x): {doubledCount}\n" +
            $"Practice scenarios set to Current: {practiceCount}\n" +
            $"Warnings: {warningCount}\n\n" +
            (mainScenarioCount == 12 &&
             currentCount == 6 &&
             doubledCount == 6 &&
             warningCount == 0
                ? "✓ S01-S12 preserved the original balanced low/high factor as Current/Doubled."
                : "Check the counts before collecting pilot data.")
        );
    }

    [MenuItem("Tools/Scenarios/Validate Fine Policy Balance")]
    public static void ValidateFinePolicyBalance()
    {
        string[] guids = AssetDatabase.FindAssets("t:ParkingScenario");

        List<ParkingScenario> mainScenarios =
            new List<ParkingScenario>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ParkingScenario scenario =
                AssetDatabase.LoadAssetAtPath<ParkingScenario>(path);

            if (scenario == null ||
                string.IsNullOrWhiteSpace(scenario.scenarioId))
            {
                continue;
            }

            if (scenario.scenarioId.StartsWith(
                    "S",
                    StringComparison.OrdinalIgnoreCase))
            {
                mainScenarios.Add(scenario);
            }
        }

        mainScenarios.Sort(
            (a, b) => string.Compare(
                a.scenarioId,
                b.scenarioId,
                StringComparison.OrdinalIgnoreCase
            )
        );

        int currentCount = 0;
        int doubledCount = 0;

        foreach (ParkingScenario scenario in mainScenarios)
        {
            if (scenario.illegalFinePolicy ==
                IllegalFinePolicyLevel.Doubled)
            {
                doubledCount++;
            }
            else
            {
                currentCount++;
            }

            Debug.Log(
                $"[Fine Policy] {scenario.scenarioId} | " +
                $"Policy={scenario.illegalFinePolicy} | " +
                $"Multiplier={scenario.GetIllegalFineMultiplier():0.#} | " +
                $"NoParking={ParkingPolicy1405.GetNoParkingFineToman(scenario.GetIllegalFineMultiplier())} | " +
                $"NoStopping={ParkingPolicy1405.GetNoStoppingFineToman(scenario.GetIllegalFineMultiplier())}",
                scenario
            );
        }

        Debug.Log(
            "========== FINE POLICY BALANCE ==========\n" +
            $"Main scenarios: {mainScenarios.Count}\n" +
            $"Current (1x): {currentCount}\n" +
            $"Doubled (2x): {doubledCount}\n\n" +
            (mainScenarios.Count == 12 &&
             currentCount == 6 &&
             doubledCount == 6
                ? "✓ Fine-policy factor is balanced 6 / 6."
                : "✗ Fine-policy balance is not the expected 6 / 6.")
        );
    }
}

#endif
