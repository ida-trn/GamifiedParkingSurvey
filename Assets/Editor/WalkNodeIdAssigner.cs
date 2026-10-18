#if UNITY_EDITOR

using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WalkNodeIdAssigner
{
    [MenuItem("Tools/Walking/Assign IDs From WalkNode Names")]
    public static void AssignIdsFromNames()
    {
        WalkNode[] allLoadedNodes =
            Resources.FindObjectsOfTypeAll<WalkNode>();

        List<WalkNode> nodes =
            new List<WalkNode>();

        foreach (WalkNode node in allLoadedNodes)
        {
            if (node == null)
                continue;

            // Ignore prefab assets in the Project window.
            if (EditorUtility.IsPersistent(node))
                continue;

            // Only scene objects.
            if (!node.gameObject.scene.IsValid())
                continue;

            nodes.Add(node);
        }

        if (nodes.Count == 0)
        {
            Debug.LogWarning(
                "[WalkNode ID Assigner] No WalkNodes were found."
            );

            return;
        }

        Undo.RecordObjects(
            nodes.ToArray(),
            "Assign WalkNode IDs From Names"
        );

        int changedCount = 0;
        int failedCount = 0;

        Dictionary<string, List<WalkNode>> resultingIds =
            new Dictionary<string, List<WalkNode>>();

        Debug.Log(
            "========== WALK NODE ID ASSIGNMENT START ==========\n" +
            $"WalkNodes found: {nodes.Count}"
        );

        foreach (WalkNode node in nodes)
        {
            string objectName =
                node.gameObject.name.Trim();

            string newId =
                BuildIdFromGameObjectName(objectName);

            if (string.IsNullOrWhiteSpace(newId))
            {
                failedCount++;

                Debug.LogError(
                    $"[WalkNode ID ERROR] Could not create an ID from:\n" +
                    $"GameObject: {objectName}\n\n" +
                    $"Expected a name such as WalkNode (119), " +
                    $"or a special WN_ name such as WN_GATE2.",
                    node.gameObject
                );

                continue;
            }

            string oldId = node.nodeId;

            node.nodeId = newId;

            EditorUtility.SetDirty(node);

            if (oldId != newId)
                changedCount++;

            if (!resultingIds.ContainsKey(newId))
            {
                resultingIds[newId] =
                    new List<WalkNode>();
            }

            resultingIds[newId].Add(node);

            Debug.Log(
                $"[WalkNode ID] {objectName}\n" +
                $"{oldId}  ->  {newId}",
                node.gameObject
            );
        }

        // ------------------------------------------------------
        // Check whether GameObject names themselves created
        // duplicate IDs.
        // ------------------------------------------------------

        int duplicateIdGroups = 0;

        foreach (
            KeyValuePair<string, List<WalkNode>> pair
            in resultingIds)
        {
            if (pair.Value.Count <= 1)
                continue;

            duplicateIdGroups++;

            foreach (WalkNode node in pair.Value)
            {
                Debug.LogError(
                    $"[WalkNode ID ERROR] Duplicate derived ID: {pair.Key}\n" +
                    $"{pair.Value.Count} WalkNodes produced this same ID.\n\n" +
                    $"This usually means two WalkNode GameObjects have " +
                    $"the same number/name.",
                    node.gameObject
                );
            }
        }

        // Save scene modifications as dirty.
        foreach (WalkNode node in nodes)
        {
            if (node != null &&
                node.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(
                    node.gameObject.scene
                );
            }
        }

        Debug.Log(
            "========== WALK NODE ID ASSIGNMENT FINISHED ==========\n" +
            $"WalkNodes checked: {nodes.Count}\n" +
            $"IDs changed: {changedCount}\n" +
            $"Could not derive ID: {failedCount}\n" +
            $"Duplicate resulting ID groups: {duplicateIdGroups}\n\n" +
            (failedCount == 0 && duplicateIdGroups == 0
                ? "✓ All WalkNodes now have IDs matching their GameObject names."
                : "✗ Check the WalkNode ID ERROR messages.")
        );
    }

    private static string BuildIdFromGameObjectName(
        string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        // ------------------------------------------------------
        // Preserve special explicitly named nodes.
        //
        // Example:
        // WN_GATE2 -> WN_GATE2
        // ------------------------------------------------------

        if (objectName.StartsWith("WN_"))
        {
            return objectName;
        }

        // ------------------------------------------------------
        // Normal Unity duplicate-style names.
        //
        // WalkNode (119) -> WN_119
        // WalkNode (4)   -> WN_4
        // ------------------------------------------------------

        Match match = Regex.Match(
            objectName,
            @"\((\d+)\)\s*$"
        );

        if (match.Success)
        {
            string number =
                match.Groups[1].Value;

            return $"WN_{number}";
        }

        // ------------------------------------------------------
        // Also support names such as:
        //
        // WalkNode 119 -> WN_119
        // ------------------------------------------------------

        match = Regex.Match(
            objectName,
            @"(\d+)\s*$"
        );

        if (match.Success)
        {
            string number =
                match.Groups[1].Value;

            return $"WN_{number}";
        }

        return null;
    }
}

#endif