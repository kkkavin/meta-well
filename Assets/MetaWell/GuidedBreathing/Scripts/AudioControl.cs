using UnityEngine;
using UnityEngine.UI;

public class AudioControl : MonoBehaviour
{
    [Header("Central Audio Component")]
    public AudioSource audioSource;

    private AudioToggleItem currentActiveToggle;
    private ToggleGroup toggleGroup;
    private bool isTrackFinished = false;

    void Awake()
    {
        toggleGroup = GetComponent<ToggleGroup>();
    }

    void Start()
    {
        // Crucial for allowing a toggle to be completely unselected/paused
        if (toggleGroup != null)
        {
            toggleGroup.allowSwitchOff = true;
        }
    }

    void Update()
    {
        // Tracks natural completion of an audio clip track
        if (audioSource != null && !audioSource.isPlaying && !isTrackFinished && audioSource.time == 0)
        {
            if (currentActiveToggle != null)
            {
                isTrackFinished = true;
                ResetAllToggles();
            }
        }
    }

    public void HandleToggleClick(AudioToggleItem clickedToggle, bool isToggled)
    {
        if (audioSource == null) return;

        isTrackFinished = false;

        if (isToggled)
        {
            // Case A: Swapping from an old playing track to a completely different track
            if (currentActiveToggle != null && currentActiveToggle != clickedToggle)
            {
                currentActiveToggle.ResetToInitialState();
            }

            // Load and activate the track into the Unity 6 audio pipeline
            currentActiveToggle = clickedToggle;
            if (clickedToggle.audioTrack != null)
            {
                audioSource.generator = clickedToggle.audioTrack;
            }
            audioSource.Play();
            clickedToggle.UpdateVisuals(true);
        }
        else
        {
            // Case B: User clicked the active track again to intentionally PAUSE it
            if (currentActiveToggle == clickedToggle)
            {
                audioSource.Pause();
                clickedToggle.UpdateVisuals(false);
                currentActiveToggle = null; // Clear active tracking so it can be resumed later
            }
        }
    }

    public void ResetAllToggles()
    {
        if (toggleGroup != null)
        {
            toggleGroup.SetAllTogglesOff();
        }

        if (currentActiveToggle != null)
            currentActiveToggle.ResetToInitialState();

        currentActiveToggle = null;
    }
}
