#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class StudySetupValidator
{
    private static int errors;
    private static int warnings;

    [MenuItem("Tools/Scenarios/Validate Study Setup")]
    public static void ValidateStudySetup()
    {
        errors = 0;
        warnings = 0;

        ScenarioManager[] managers = FindSceneObjects<ScenarioManager>();

        if (managers.Length == 0)
        {
            Error("No ScenarioManager exists in the loaded scene.");
            Finish();
            return;
        }

        if (managers.Length > 1)
        {
            Error($"Multiple ScenarioManager components found: {managers.Length}. Keep only the intended study manager.");
        }

        ScenarioManager manager = managers[0];

        Debug.Log(
            "========== STUDY SETUP VALIDATION START ==========\n" +
            $"ScenarioManager: {manager.gameObject.name}",
            manager.gameObject
        );

        ValidateVersions(manager);
        ValidateRoundDesign(manager);
        ValidateScenarioAssets(manager);
        ValidateStartNodes(manager);
        ValidateResearchReferences(manager);
        ValidateScoring(manager);
        ValidateWalkingSetup(manager);
        ValidateLogger(manager);
        ValidateTutorial(manager);
        ValidateImportantUI(manager);

        Finish();
    }

    // ------------------------------------------------------------
    // Versions / build identity
    // ------------------------------------------------------------

    private static void ValidateVersions(ScenarioManager manager)
    {
        if (string.IsNullOrWhiteSpace(manager.studyVersion))
        {
            Error("Study Version is empty.", manager.gameObject);
        }
        else if (!string.Equals(
                     manager.studyVersion.Trim(),
                     "1.4-pilot-fine-policy",
                     StringComparison.Ordinal))
        {
            Warn(
                $"Study Version is '{manager.studyVersion}'. " +
                "Current fine-policy pilot version is expected to be '1.4-pilot-fine-policy'. " +
                "If you intentionally bumped the study version, this warning can be ignored.",
                manager.gameObject
            );
        }

        if (string.IsNullOrWhiteSpace(manager.buildVersion))
        {
            Error("Build Version is empty.", manager.gameObject);
        }
        else if (!string.Equals(
                     manager.buildVersion.Trim(),
                     "1.4.0",
                     StringComparison.Ordinal))
        {
            Warn(
                $"Build Version is '{manager.buildVersion}'. " +
                "Current fine-policy build is expected to be '1.4.0'. " +
                "If this is a deliberate newer build, this warning can be ignored.",
                manager.gameObject
            );
        }

        if (string.IsNullOrWhiteSpace(manager.scenarioOrderLabel))
        {
            Error("Scenario Order Label is empty.", manager.gameObject);
        }
        else if (!string.Equals(
                     manager.scenarioOrderLabel.Trim(),
                     "Fixed01",
                     StringComparison.Ordinal))
        {
            Warn(
                $"Scenario Order Label is '{manager.scenarioOrderLabel}', not 'Fixed01'.",
                manager.gameObject
            );
        }
    }

    // ------------------------------------------------------------
    // Overall experimental round design
    // ------------------------------------------------------------

    private static void ValidateRoundDesign(ScenarioManager manager)
    {
        if (!manager.runConventionalAndAssistedForEachScenario)
        {
            Error(
                "Run Conventional And Assisted For Each Scenario is OFF. " +
                "The study requires each of the 12 scenarios in both modes.",
                manager.gameObject
            );
        }

        if (!manager.conventionalFirst)
        {
            Error(
                "Conventional First is OFF. Current Fixed01 study setup expects Conventional first, then Assisted.",
                manager.gameObject
            );
        }

        if (!manager.reuseExactOccupancyForModePair)
        {
            Error(
                "Reuse Exact Occupancy For Mode Pair is OFF. " +
                "Conventional and Assisted must reuse the exact same physical occupancy pattern.",
                manager.gameObject
            );
        }

        if (manager.maxRoundsForTesting != 0)
        {
            Error(
                $"Max Rounds For Testing = {manager.maxRoundsForTesting}. " +
                "Set it to 0 before the WebGL/pilot build.",
                manager.gameObject
            );
        }

        if (manager.scenarios != null &&
            manager.scenarios.Count == 12 &&
            manager.runConventionalAndAssistedForEachScenario &&
            manager.maxRoundsForTesting == 0)
        {
            int totalRounds = manager.GetTotalRoundCount();

            if (totalRounds != 24)
            {
                Error(
                    $"Expected 24 study rounds, but ScenarioManager reports {totalRounds}.",
                    manager.gameObject
                );
            }
        }
    }

    // ------------------------------------------------------------
    // S01-S12
    // ------------------------------------------------------------

    private static void ValidateScenarioAssets(ScenarioManager manager)
    {
        if (manager.scenarios == null)
        {
            Error("Scenario list is null.", manager.gameObject);
            return;
        }

        if (manager.scenarios.Count != 12)
        {
            Error(
                $"Scenario list contains {manager.scenarios.Count} assets. Expected exactly 12 (S01-S12).",
                manager.gameObject
            );
        }

        HashSet<ParkingScenario> uniqueAssets =
            new HashSet<ParkingScenario>();

        HashSet<string> uniqueIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        HashSet<string> experimentalSignatures =
            new HashSet<string>();

        int dwell120 = 0;
        int dwell360 = 0;

        int limit3 = 0;
        int limit6 = 0;

        int citation5 = 0;
        int citation15 = 0;

        int occupancy70 = 0;
        int occupancy90 = 0;

        int fineCurrent = 0;
        int fineDoubled = 0;

        for (int i = 0; i < manager.scenarios.Count; i++)
        {
            ParkingScenario scenario = manager.scenarios[i];

            if (scenario == null)
            {
                Error(
                    $"Scenario list Element {i} is null.",
                    manager.gameObject
                );
                continue;
            }

            if (!uniqueAssets.Add(scenario))
            {
                Error(
                    $"Scenario asset '{scenario.name}' appears more than once in the ScenarioManager list.",
                    scenario
                );
            }

            string expectedId = $"S{i + 1:00}";

            if (!string.Equals(
                    scenario.scenarioId,
                    expectedId,
                    StringComparison.OrdinalIgnoreCase))
            {
                Error(
                    $"Scenario list Element {i} should be {expectedId}, but its Scenario ID is '{scenario.scenarioId}'.",
                    scenario
                );
            }

            if (string.IsNullOrWhiteSpace(scenario.scenarioId))
            {
                Error("Scenario ID is empty.", scenario);
            }
            else if (!uniqueIds.Add(scenario.scenarioId.Trim()))
            {
                Error(
                    $"Duplicate Scenario ID '{scenario.scenarioId}'.",
                    scenario
                );
            }

            string assetPath = AssetDatabase.GetAssetPath(scenario);
            string fileName = string.IsNullOrWhiteSpace(assetPath)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(assetPath);

            if (!string.IsNullOrWhiteSpace(fileName) &&
                !string.Equals(fileName, expectedId, StringComparison.OrdinalIgnoreCase))
            {
                Warn(
                    $"{expectedId} uses asset file '{fileName}.asset'. " +
                    "This does not break runtime behavior, but matching filenames are easier to audit.",
                    scenario
                );
            }

            if (scenario.startPointIndex != i)
            {
                Error(
                    $"{scenario.scenarioId} has Start Point Index {scenario.startPointIndex}. Expected {i}.",
                    scenario
                );
            }

            if (scenario.dwellTimeMinutes <= 0f)
            {
                Error($"{scenario.scenarioId}: Dwell Time must be greater than 0.", scenario);
            }

            if (scenario.timeLimitMinutes <= 0f)
            {
                Error($"{scenario.scenarioId}: Time Limit must be greater than 0.", scenario);
            }

            if (Approximately(scenario.dwellTimeMinutes, 120f))
                dwell120++;
            else if (Approximately(scenario.dwellTimeMinutes, 360f))
                dwell360++;
            else
                Error(
                    $"{scenario.scenarioId}: unexpected Dwell Time {scenario.dwellTimeMinutes}. Expected 120 or 360 minutes.",
                    scenario
                );

            if (Approximately(scenario.timeLimitMinutes, 3f))
                limit3++;
            else if (Approximately(scenario.timeLimitMinutes, 6f))
                limit6++;
            else
                Error(
                    $"{scenario.scenarioId}: unexpected Time Limit {scenario.timeLimitMinutes}. Expected 3 or 6 minutes.",
                    scenario
                );

            if (Approximately(scenario.citationProbability, 0.05f))
                citation5++;
            else if (Approximately(scenario.citationProbability, 0.15f))
                citation15++;
            else
                Error(
                    $"{scenario.scenarioId}: unexpected Citation Probability {scenario.citationProbability:0.###}. Expected 0.05 or 0.15.",
                    scenario
                );

            if (Approximately(scenario.legalOccupancyRate, 0.70f))
                occupancy70++;
            else if (Approximately(scenario.legalOccupancyRate, 0.90f))
                occupancy90++;
            else
                Error(
                    $"{scenario.scenarioId}: unexpected Legal Occupancy {scenario.legalOccupancyRate:0.###}. Expected 0.70 or 0.90.",
                    scenario
                );

            if (!Approximately(scenario.illegalOccupancyRate, 0.10f))
            {
                Error(
                    $"{scenario.scenarioId}: Illegal Occupancy is {scenario.illegalOccupancyRate:0.###}. Expected constant 0.10.",
                    scenario
                );
            }

            if (scenario.illegalFinePolicy == IllegalFinePolicyLevel.Doubled)
                fineDoubled++;
            else
                fineCurrent++;

            string signature =
                $"{scenario.dwellTimeMinutes:0.###}|" +
                $"{scenario.timeLimitMinutes:0.###}|" +
                $"{scenario.citationProbability:0.###}|" +
                $"{scenario.legalOccupancyRate:0.###}|" +
                $"{scenario.illegalFinePolicy}";

            if (!experimentalSignatures.Add(signature))
            {
                Error(
                    $"{scenario.scenarioId}: duplicate experimental combination found. " +
                    "Two scenarios share the same dwell/time/citation/occupancy/fine-policy combination.",
                    scenario
                );
            }
        }

        CheckSixSix("Dwell Time 120/360", dwell120, dwell360, manager.gameObject);
        CheckSixSix("Time Limit 3/6", limit3, limit6, manager.gameObject);
        CheckSixSix("Citation Probability 5%/15%", citation5, citation15, manager.gameObject);
        CheckSixSix("Legal Occupancy 70%/90%", occupancy70, occupancy90, manager.gameObject);
        CheckSixSix("Fine Policy Current/Doubled", fineCurrent, fineDoubled, manager.gameObject);

        Debug.Log(
            "[STUDY SETUP] Scenario factor counts\n" +
            $"Dwell 120/360: {dwell120}/{dwell360}\n" +
            $"Time limit 3/6: {limit3}/{limit6}\n" +
            $"Citation 5%/15%: {citation5}/{citation15}\n" +
            $"Legal occupancy 70%/90%: {occupancy70}/{occupancy90}\n" +
            $"Fine Current/Doubled: {fineCurrent}/{fineDoubled}",
            manager.gameObject
        );
    }

    private static void CheckSixSix(
        string label,
        int first,
        int second,
        UnityEngine.Object context)
    {
        if (first != 6 || second != 6)
        {
            Error(
                $"{label} is not balanced 6/6. Current counts: {first}/{second}.",
                context
            );
        }
    }

    // ------------------------------------------------------------
    // Start points
    // ------------------------------------------------------------

    private static void ValidateStartNodes(ScenarioManager manager)
    {
        if (manager.scenarioStartNodes == null)
        {
            Error("Scenario Start Nodes array is null.", manager.gameObject);
            return;
        }

        if (manager.scenarioStartNodes.Length < 13)
        {
            Error(
                $"Scenario Start Nodes has {manager.scenarioStartNodes.Length} entries. " +
                "Need at least 13: indices 0-11 for S01-S12 and index 12 for Practice01.",
                manager.gameObject
            );
        }

        HashSet<RoadNode> usedNodes = new HashSet<RoadNode>();

        int requiredCount = Mathf.Min(13, manager.scenarioStartNodes.Length);

        for (int i = 0; i < requiredCount; i++)
        {
            RoadNode node = manager.scenarioStartNodes[i];

            if (node == null)
            {
                Error(
                    $"Scenario Start Nodes Element {i} is null.",
                    manager.gameObject
                );
                continue;
            }

            if (!usedNodes.Add(node))
            {
                Warn(
                    $"Scenario Start Node '{node.name}' is reused at more than one required index.",
                    node.gameObject
                );
            }
        }
    }

    // ------------------------------------------------------------
    // Required research/gameplay references
    // ------------------------------------------------------------

    private static void ValidateResearchReferences(ScenarioManager manager)
    {
        CheckComponentReference(
            "ParkingOccupancyManager",
            manager.occupancyManager,
            FindSceneObjects<ParkingOccupancyManager>(),
            manager.gameObject
        );

        CheckComponentReference(
            "CarParkingInteraction",
            manager.carParking,
            FindSceneObjects<CarParkingInteraction>(),
            manager.gameObject
        );

        CheckComponentReference(
            "ParkingViewTracker",
            manager.parkingViewTracker,
            FindSceneObjects<ParkingViewTracker>(),
            manager.gameObject
        );

        CheckComponentReference(
            "WalkingNetworkCalculator",
            manager.walkingNetworkCalculator,
            FindSceneObjects<WalkingNetworkCalculator>(),
            manager.gameObject
        );

        CheckComponentReference(
            "FinalQuestionnaire",
            manager.finalQuestionnaire,
            FindSceneObjects<FinalQuestionnaire>(),
            manager.gameObject
        );

        CheckComponentReference(
            "TutorialManager",
            manager.tutorialManager,
            FindSceneObjects<TutorialManager>(),
            manager.gameObject
        );

        if (manager.destinationGate == null)
            Error("Destination Gate is not assigned.", manager.gameObject);

        if (manager.assistedMapCamera == null)
            Error("Assisted Map Camera is not assigned.", manager.gameObject);

        if (manager.carTransform == null && manager.carParking == null)
        {
            Error(
                "Neither Car Transform nor CarParkingInteraction is available to resolve the car.",
                manager.gameObject
            );
        }
    }

    private static void CheckComponentReference<T>(
        string label,
        T assignedReference,
        T[] sceneObjects,
        UnityEngine.Object context)
        where T : UnityEngine.Object
    {
        if (assignedReference != null)
            return;

        if (sceneObjects != null && sceneObjects.Length > 0)
        {
            Warn(
                $"{label} field is not assigned on ScenarioManager, although a scene component exists. " +
                "Runtime auto-find may work, but assigning it explicitly is safer for the final build.",
                context
            );
        }
        else
        {
            Error(
                $"{label} is missing from both ScenarioManager and the loaded scene.",
                context
            );
        }
    }

    // ------------------------------------------------------------
    // Score/timer
    // ------------------------------------------------------------

    private static void ValidateScoring(ScenarioManager manager)
    {
        if (!Approximately(manager.startingScore, 100f))
        {
            Error(
                $"Starting Score = {manager.startingScore}. Expected 100.",
                manager.gameObject
            );
        }

        if (!Approximately(manager.tomanPerPoint, 1000f))
        {
            Error(
                $"Toman Per Point = {manager.tomanPerPoint}. Expected 1000.",
                manager.gameObject
            );
        }

        if (!Approximately(manager.overtimeParkingMinutes, 3f))
        {
            Error(
                $"Overtime Parking Minutes = {manager.overtimeParkingMinutes}. Expected 3.",
                manager.gameObject
            );
        }

        if (!Approximately(manager.star1TargetRatio, 0.50f) ||
            !Approximately(manager.star2TargetRatio, 0.70f) ||
            !Approximately(manager.star3TargetRatio, 0.85f))
        {
            Warn(
                $"Star ratios are {manager.star1TargetRatio:0.##}/" +
                $"{manager.star2TargetRatio:0.##}/" +
                $"{manager.star3TargetRatio:0.##}. " +
                "Expected pilot values are 0.50 / 0.70 / 0.85.",
                manager.gameObject
            );
        }
    }

    // ------------------------------------------------------------
    // Walking setup
    // ------------------------------------------------------------

    private static void ValidateWalkingSetup(ScenarioManager manager)
    {
        WalkingNetworkCalculator calculator = manager.walkingNetworkCalculator;

        if (calculator == null)
        {
            WalkingNetworkCalculator[] found =
                FindSceneObjects<WalkingNetworkCalculator>();

            if (found.Length > 0)
                calculator = found[0];
        }

        if (calculator != null)
        {
            if (calculator.destinationNode == null)
            {
                Error(
                    "WalkingNetworkCalculator Destination Node is not assigned.",
                    calculator.gameObject
                );
            }
            else if (!string.Equals(
                         calculator.destinationNode.nodeId,
                         "WN_GATE2",
                         StringComparison.OrdinalIgnoreCase))
            {
                Warn(
                    $"Walking destination Node ID is '{calculator.destinationNode.nodeId}', not 'WN_GATE2'.",
                    calculator.destinationNode.gameObject
                );
            }
        }

        if (manager.recalculateWalkingDistancesBeforeEachRound)
        {
            Warn(
                "Recalculate Walking Distances Before Each Round is ON. " +
                "Your walking network and stored distances are already validated/static. " +
                "For the final WebGL study build, OFF is recommended to avoid unnecessary repeated recalculation.",
                manager.gameObject
            );
        }
    }

    // ------------------------------------------------------------
    // Logger
    // ------------------------------------------------------------

    private static void ValidateLogger(ScenarioManager manager)
    {
        ParkingLogger[] loggers =
            FindSceneObjects<ParkingLogger>();

        if (loggers.Length == 0)
        {
            Error(
                "No ParkingLogger exists in the loaded scene.",
                manager.gameObject
            );
            return;
        }

        if (loggers.Length > 1)
        {
            Error(
                $"Multiple ParkingLogger components found: {loggers.Length}.",
                manager.gameObject
            );
        }

        ParkingLogger logger = loggers[0];

        if (!logger.writeAlternativeFile)
        {
            Error(
                "ParkingLogger Write Alternative File is OFF. " +
                "The study expects both A1-A6 and collapsed A1-A5 alternative files.",
                logger.gameObject
            );
        }

        if (string.IsNullOrWhiteSpace(logger.outputSubfolder))
        {
            Warn(
                "ParkingLogger Output Subfolder is empty.",
                logger.gameObject
            );
        }
    }

    // ------------------------------------------------------------
    // Tutorial / practice
    // ------------------------------------------------------------

    private static void ValidateTutorial(ScenarioManager manager)
    {
        if (!manager.waitForTutorial)
        {
            Error(
                "Wait For Tutorial is OFF. The intended participant flow includes the tutorial/practice before the 24 main rounds.",
                manager.gameObject
            );
        }

        TutorialManager tutorial = manager.tutorialManager;

        if (tutorial == null)
        {
            TutorialManager[] found =
                FindSceneObjects<TutorialManager>();

            if (found.Length > 0)
                tutorial = found[0];
        }

        if (tutorial == null)
            return;

        if (tutorial.scenarioManager != manager)
        {
            Warn(
                "TutorialManager Scenario Manager reference is not this ScenarioManager.",
                tutorial.gameObject
            );
        }

        if (tutorial.practiceScenario == null)
        {
            Error(
                "TutorialManager Practice Scenario is not assigned.",
                tutorial.gameObject
            );
            return;
        }

        if (!string.Equals(
                tutorial.practiceScenario.scenarioId,
                "Practice01",
                StringComparison.OrdinalIgnoreCase))
        {
            Warn(
                $"Practice scenario ID is '{tutorial.practiceScenario.scenarioId}', expected 'Practice01'.",
                tutorial.practiceScenario
            );
        }

        int index = tutorial.practiceScenario.startPointIndex;

        if (index != 12)
        {
            Error(
                $"Practice01 Start Point Index = {index}. Expected 12.",
                tutorial.practiceScenario
            );
        }

        if (manager.scenarioStartNodes != null &&
            index >= 0 &&
            index < manager.scenarioStartNodes.Length &&
            manager.scenarioStartNodes[index] == null)
        {
            Error(
                "Practice01 start RoadNode at index 12 is null.",
                manager.gameObject
            );
        }
    }

    // ------------------------------------------------------------
    // Important UI references
    // ------------------------------------------------------------

    private static void ValidateImportantUI(ScenarioManager manager)
    {
        CheckUI("Scenario Intro Panel", manager.scenarioIntroPanel, manager);
        CheckUI("Timer Label", manager.timerLabel, manager);
        CheckUI("Score Label", manager.scoreLabel, manager);
        CheckUI("Result Panel", manager.resultPanel, manager);
        CheckUI("Result Title Label", manager.resultTitleLabel, manager);
        CheckUI("Result Detail Label", manager.resultDetailLabel, manager);
        CheckUI("Result Score Label", manager.resultScoreLabel, manager);

        if (manager.destinationArrowRoot == null)
        {
            Warn(
                "Destination Arrow Root is not assigned. Auto-binding may find it by name, but explicit assignment is safer.",
                manager.gameObject
            );
        }
    }

    private static void CheckUI(
        string label,
        UnityEngine.Object reference,
        ScenarioManager manager)
    {
        if (reference != null)
            return;

        if (manager.autoFindUIReferences)
        {
            Warn(
                $"{label} is not explicitly assigned. Auto Find UI References is ON, so runtime name-based binding may resolve it.",
                manager.gameObject
            );
        }
        else
        {
            Error(
                $"{label} is missing and Auto Find UI References is OFF.",
                manager.gameObject
            );
        }
    }

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------

    private static T[] FindSceneObjects<T>() where T : UnityEngine.Object
    {
        T[] all = Resources.FindObjectsOfTypeAll<T>();
        List<T> sceneObjects = new List<T>();

        foreach (T item in all)
        {
            if (item == null)
                continue;

            if (EditorUtility.IsPersistent(item))
                continue;

            Component component = item as Component;

            if (component != null)
            {
                if (!component.gameObject.scene.IsValid())
                    continue;
            }

            sceneObjects.Add(item);
        }

        return sceneObjects.ToArray();
    }

    private static bool Approximately(float a, float b)
    {
        return Mathf.Abs(a - b) < 0.0001f;
    }

    private static void Error(
        string message,
        UnityEngine.Object context = null)
    {
        errors++;

        if (context != null)
            Debug.LogError("[STUDY SETUP ERROR] " + message, context);
        else
            Debug.LogError("[STUDY SETUP ERROR] " + message);
    }

    private static void Warn(
        string message,
        UnityEngine.Object context = null)
    {
        warnings++;

        if (context != null)
            Debug.LogWarning("[STUDY SETUP WARNING] " + message, context);
        else
            Debug.LogWarning("[STUDY SETUP WARNING] " + message);
    }

    private static void Finish()
    {
        Debug.Log(
            "========== STUDY SETUP VALIDATION FINISHED ==========\n" +
            $"Errors: {errors}\n" +
            $"Warnings: {warnings}\n\n" +
            (errors == 0
                ? "✓ No critical study-setup errors found."
                : "✗ Fix the STUDY SETUP ERROR entries before the final WebGL build.")
        );
    }
}

#endif
