using UnityEngine;

public class MovementArrow : MonoBehaviour
{
    private CarNodeFollower car;
    private RoadNode targetNode;

    public void Setup(CarNodeFollower carFollower, RoadNode nextNode)
    {
        car = carFollower;
        targetNode = nextNode;
    }

    private void OnMouseDown()
    {
        if (car != null && targetNode != null)
        {
            car.ChooseNextNodeFromArrow(targetNode);
        }
    }
}