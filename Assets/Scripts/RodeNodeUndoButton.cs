using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[RequireComponent(typeof(Button))]
public class RoadNodeUndoButton : MonoBehaviour
{
    [Header("References")]
    public CarNodeFollower carFollower;

    [Header("Nodes Where Undo Is Allowed")]
    public List<RoadNode> allowedUndoNodes = new List<RoadNode>();

    [Header("Testing")]
    public bool showOnAllNodes = false;

    private Button button;
    private CanvasGroup canvasGroup;

    private void Awake()
    {
        button = GetComponent<Button>();

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        if (carFollower == null)
            carFollower = FindFirstObjectByType<CarNodeFollower>();

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(ClickUndo);

        Hide();
    }

    private void Start()
    {
        if (carFollower == null)
            carFollower = FindFirstObjectByType<CarNodeFollower>();

        if (carFollower != null)
            carFollower.RefreshUndoButtons();
    }

    private void ClickUndo()
    {
        if (carFollower == null)
            carFollower = FindFirstObjectByType<CarNodeFollower>();

        if (carFollower != null)
            carFollower.UI_UndoMoveButton();
    }

    public void Refresh(RoadNode currentNode, bool canUndo)
    {
        bool nodeIsAllowed = showOnAllNodes || IsNodeAllowed(currentNode);

        bool shouldShow =
            currentNode != null &&
            canUndo &&
            nodeIsAllowed;

        if (shouldShow)
            Show();
        else
            Hide();
    }

    private bool IsNodeAllowed(RoadNode currentNode)
    {
        if (currentNode == null)
            return false;

        foreach (RoadNode node in allowedUndoNodes)
        {
            if (node == currentNode)
                return true;
        }

        return false;
    }

    private void Show()
    {
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    private void Hide()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }
}