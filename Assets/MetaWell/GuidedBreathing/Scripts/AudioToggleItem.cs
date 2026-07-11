using UnityEngine;
using UnityEngine.UI;

public class AudioToggleItem : MonoBehaviour
{
    [Header("Track Configuration")]
    public AudioClip audioTrack;

    [Header("UI Visuals")]
    public Sprite playSprite;
    public Sprite pauseSprite;
    public GameObject checkBox;

    [HideInInspector] public Image uiImage;
    private Toggle toggleComponent;
    private AudioControl manager;

    void Awake()
    {
        if (checkBox != null) uiImage = checkBox.GetComponent<Image>();
        toggleComponent = GetComponent<Toggle>();
        manager = GetComponentInParent<AudioControl>();

        // Disable standard Unity UI graphic overrides to prioritize script control
        if (toggleComponent != null && uiImage != null)
        {
            toggleComponent.targetGraphic = uiImage;
            toggleComponent.graphic = null;
        }
    }

    void Start()
    {
        // FORCE DEFAULT STATE: Start unchecked and showing the play sprite
        if (toggleComponent != null)
        {
            toggleComponent.SetIsOnWithoutNotify(false);
            toggleComponent.onValueChanged.AddListener(OnStateChanged);
        }
        UpdateVisuals(false);
    }

    public void OnStateChanged(bool isToggled)
    {
        // Let the manager look after specific play/pause routing 
        if (manager != null)
        {
            manager.HandleToggleClick(this, isToggled);
        }
    }

    public void UpdateVisuals(bool isPlaying)
    {
        if (uiImage == null) return;
        // True (Checked) = Shows Pause Button | False (Unchecked) = Shows Play Button
        uiImage.sprite = isPlaying ? pauseSprite : playSprite;
    }

    public void ResetToInitialState()
    {
        if (toggleComponent != null)
        {
            toggleComponent.SetIsOnWithoutNotify(false);
        }
        UpdateVisuals(false); // Instantly switches graphic back to standard play icon
    }

    void OnDestroy()
    {
        if (toggleComponent != null)
        {
            toggleComponent.onValueChanged.RemoveListener(OnStateChanged);
        }
    }
}
