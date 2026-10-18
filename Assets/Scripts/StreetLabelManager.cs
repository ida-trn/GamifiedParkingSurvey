using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Detects which named street the car is currently on by measuring the car's
/// distance to polylines made from the existing driving RoadNodes.
///
/// IMPORTANT:
/// The active label is anchored to the CAR position, not to the camera.
/// Therefore each StreetLabelPath.Moving Label Offset Along Street keeps the
/// same meaning even when the driving camera is shifted forward.
/// </summary>
[DisallowMultipleComponent]
public class StreetLabelManager : MonoBehaviour
{
    [Header("References")]
    public Transform car;
    public Camera drivingCamera;

    [Tooltip("Optional. Assign it if labels should be hidden in assisted mode.")]
    public ScenarioManager scenarioManager;

    [Header("Named streets")]
    [Tooltip("Normally filled automatically from StreetLabelPath components under this object.")]
    public List<StreetLabelPath> streets = new List<StreetLabelPath>();
    public bool collectChildStreetsAutomatically = true;

    [Header("Street detection")]
    [Tooltip("Maximum distance, in Unity world units, between the car and a street path.")]
    public float maximumStreetDistance = 12f;

    [Tooltip("A new street must be this many world units closer than the current street before switching. This prevents flicker at intersections.")]
    public float streetSwitchAdvantage = 2f;

    [Header("Visibility")]
    [Tooltip("OFF: inactive labels remain visible at their configured street starts. ON: only the current street name is visible.")]
    public bool showOnlyActiveStreet = false;

    public bool hideAllLabelsInAssistedMode = true;

    [Header("Debug")]
    public bool logStreetChanges = true;

    public StreetLabelPath ActiveStreet { get; private set; }

    private bool labelsHiddenForAssistedMode;
    private bool labelsHiddenExternally;

    private void Awake()
    {
        CollectStreetsIfNeeded();

        if (drivingCamera == null)
            drivingCamera = Camera.main;
    }

    private void Start()
    {
        ResetStreetLabelsForScenario();
    }

    private void LateUpdate()
    {
        if (car == null)
            return;

        if (labelsHiddenExternally)
        {
            SetAllLabelsVisible(false);
            return;
        }

        bool assistedMode =
            scenarioManager != null &&
            scenarioManager.currentAssistedMode;

        if (hideAllLabelsInAssistedMode && assistedMode)
        {
            if (!labelsHiddenForAssistedMode)
            {
                SetAllLabelsVisible(false);
                labelsHiddenForAssistedMode = true;
            }

            return;
        }

        if (labelsHiddenForAssistedMode)
        {
            labelsHiddenForAssistedMode = false;
            UpdateVisibility();
        }

        SelectActiveStreet();

        // KEY FIX:
        // The active label follows the CAR position projected onto the street,
        // then StreetLabelPath applies that street's existing
        // Moving Label Offset Along Street value.
        //
        // This restores all of the per-street tuning you already made before
        // the camera became forward-biased.
        foreach (StreetLabelPath street in streets)
        {
            if (street == null)
                continue;

            if (street == ActiveStreet)
                street.FollowWorldPosition(car.position);
            else
                street.PlaceLabelAtStreetStart();
        }

        UpdateVisibility();
    }

    public void SetLabelsHidden(bool hidden)
    {
        CollectStreetsIfNeeded();
        labelsHiddenExternally = hidden;

        if (hidden)
        {
            SetAllLabelsVisible(false);
            return;
        }

        bool assistedMode =
            scenarioManager != null &&
            scenarioManager.currentAssistedMode;

        if (hideAllLabelsInAssistedMode && assistedMode)
        {
            labelsHiddenForAssistedMode = true;
            SetAllLabelsVisible(false);
            return;
        }

        labelsHiddenForAssistedMode = false;
        UpdateVisibility();
    }

    [UnityEngine.ContextMenu("Reset Street Labels For Scenario")]
    public void ResetStreetLabelsForScenario()
    {
        CollectStreetsIfNeeded();
        ActiveStreet = null;

        foreach (StreetLabelPath street in streets)
        {
            if (street == null)
                continue;

            street.FindLabelIfMissing();
            street.PlaceLabelAtStreetStart();
        }

        UpdateVisibility();
    }

    [UnityEngine.ContextMenu("Collect Child Street Paths")]
    public void CollectChildStreetPaths()
    {
        streets.Clear();
        streets.AddRange(
            GetComponentsInChildren<StreetLabelPath>(true)
        );
    }

    private void CollectStreetsIfNeeded()
    {
        if (collectChildStreetsAutomatically &&
            (streets == null || streets.Count == 0))
        {
            CollectChildStreetPaths();
        }
    }

    private void SelectActiveStreet()
    {
        Vector2 carPosition = car.position;

        StreetLabelPath nearestStreet = null;
        float nearestDistance = float.PositiveInfinity;

        foreach (StreetLabelPath street in streets)
        {
            if (street == null)
                continue;

            float distance =
                street.DistanceToStreet(carPosition);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestStreet = street;
            }
        }

        StreetLabelPath nextActive =
            ActiveStreet;

        float currentDistance =
            ActiveStreet == null
                ? float.PositiveInfinity
                : ActiveStreet.DistanceToStreet(
                    carPosition
                );

        if (nearestStreet == null ||
            nearestDistance > maximumStreetDistance)
        {
            if (currentDistance > maximumStreetDistance)
                nextActive = null;
        }
        else if (ActiveStreet == null)
        {
            nextActive = nearestStreet;
        }
        else if (nearestStreet == ActiveStreet)
        {
            nextActive = ActiveStreet;
        }
        else if (
            nearestDistance +
            streetSwitchAdvantage <
            currentDistance)
        {
            nextActive = nearestStreet;
        }

        if (nextActive != ActiveStreet)
        {
            ActiveStreet = nextActive;

            if (logStreetChanges)
            {
                Debug.Log(
                    ActiveStreet == null
                        ? "[StreetLabelManager] Car is not on an assigned named street."
                        : "[StreetLabelManager] Active street: " +
                          ActiveStreet.DisplayName +
                          " (" +
                          ActiveStreet.StreetId +
                          ")"
                );
            }
        }
    }

    private void UpdateVisibility()
    {
        if (streets == null)
            return;

        if (labelsHiddenExternally)
        {
            SetAllLabelsVisible(false);
            return;
        }

        foreach (StreetLabelPath street in streets)
        {
            if (street == null)
                continue;

            bool visible =
                !showOnlyActiveStreet ||
                street == ActiveStreet;

            street.SetLabelVisible(visible);
        }
    }

    private void SetAllLabelsVisible(bool visible)
    {
        if (streets == null)
            return;

        foreach (StreetLabelPath street in streets)
        {
            if (street != null)
                street.SetLabelVisible(visible);
        }
    }
}
