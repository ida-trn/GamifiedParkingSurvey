using UnityEngine;

public class ParticipantSession : MonoBehaviour
{
    public static ParticipantSession Instance { get; private set; }

    [Header("Current Participant")]
    public string participantId = "";

    private void Awake()
    {
        // Prevent more than one ParticipantSession from existing.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Keep this object when Unity changes scenes.
        DontDestroyOnLoad(gameObject);
    }
}