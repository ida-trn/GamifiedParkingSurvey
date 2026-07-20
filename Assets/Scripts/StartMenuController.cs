using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

public class StartMenuController : MonoBehaviour
{
    public TMP_InputField participantInput;
    public string mainSceneName = "ParkingGame"; // <-- use your game scene name

    public void OnClickStart()
    {
        string id = participantInput != null ? participantInput.text.Trim() : "";

        if (string.IsNullOrEmpty(id))
            id = "P001"; // fallback default

        if (ParticipantSession.Instance != null)
        {
            ParticipantSession.Instance.participantId = id;
            Debug.Log("[StartMenu] Participant ID set to: " + id);
        }
        else
        {
            Debug.LogWarning("[StartMenu] No ParticipantSession found.");
        }

        SceneManager.LoadScene(mainSceneName);
    }
}
