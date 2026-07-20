using UnityEngine;
using System.Collections.Generic;


public class RoadNode : MonoBehaviour
{
    [Header("Node Identity")]
    public bool autoGenerateNodeId = true;
    public string nodeId;

    [Header("One-way outgoing connections")]
    [Tooltip("Only nodes the car is allowed to move to from this node.")]
    public List<RoadNode> connectedNodes = new List<RoadNode>();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (autoGenerateNodeId)
        {
            nodeId = GenerateNodeIdFromObjectName();
        }
    }
#endif

    private void Reset()
    {
        if (autoGenerateNodeId)
        {
            nodeId = GenerateNodeIdFromObjectName();
        }
    }

    private void Awake()
    {
        if (autoGenerateNodeId)
        {
            nodeId = GenerateNodeIdFromObjectName();
        }
        else if (string.IsNullOrWhiteSpace(nodeId))
        {
            nodeId = gameObject.name.Replace(" ", "_");
        }
    }

    private string GenerateNodeIdFromObjectName()
    {
        string objectName = gameObject.name;

        // Example:
        // "Node (37)" becomes "N37"
        // "Node 37" becomes "N37"
        // "Node_37" becomes "N37"

        string digits = "";

        foreach (char c in objectName)
        {
            if (char.IsDigit(c))
            {
                digits += c;
            }
        }

        if (!string.IsNullOrEmpty(digits))
        {
            return "N" + digits;
        }

        return objectName.Replace(" ", "_");
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(transform.position, 0.3f);

        foreach (var node in connectedNodes)
        {
            if (node != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, node.transform.position);
            }
        }
    }
}