using System.Collections.Generic;
using UnityEngine;

public class WalkNode : MonoBehaviour
{
    [Header("Identity")]
    public string nodeId;

    [Header("Pedestrian Connections")]
    [Tooltip("Add every directly connected pedestrian node here.")]
    public List<WalkNode> connectedNodes = new List<WalkNode>();

    [Header("Scene View")]
    public bool drawConnections = true;
    public float gizmoRadius = 1.2f;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            nodeId = gameObject.name;
    }

    [ContextMenu("Make All Connections Bidirectional")]
    public void MakeAllConnectionsBidirectional()
    {
        foreach (WalkNode neighbour in connectedNodes)
        {
            if (neighbour == null || neighbour == this)
                continue;

            if (!neighbour.connectedNodes.Contains(this))
                neighbour.connectedNodes.Add(this);
        }
    }

    [ContextMenu("Clean Connections")]
    public void CleanConnections()
    {
        List<WalkNode> cleanedConnections = new List<WalkNode>();

        foreach (WalkNode neighbour in connectedNodes)
        {
            if (neighbour == null || neighbour == this)
                continue;

            if (!cleanedConnections.Contains(neighbour))
                cleanedConnections.Add(neighbour);
        }

        connectedNodes = cleanedConnections;
    }

    [ContextMenu("Remove All Connections")]
    public void RemoveAllConnections()
    {
        foreach (WalkNode neighbour in connectedNodes)
        {
            if (neighbour == null)
                continue;

            neighbour.connectedNodes.Remove(this);
        }

        connectedNodes.Clear();
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(
            transform.position,
            Mathf.Max(0.05f, gizmoRadius)
        );

        if (!drawConnections || connectedNodes == null)
            return;

        foreach (WalkNode neighbour in connectedNodes)
        {
            if (neighbour == null)
                continue;

            Gizmos.DrawLine(
                transform.position,
                neighbour.transform.position
            );
        }
    }
}
