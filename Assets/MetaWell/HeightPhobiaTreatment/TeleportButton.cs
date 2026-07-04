using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class TeleportButton : MonoBehaviour
{
    public Transform teleportTarget;
    public GameObject xrOrigin;
    public AudioSource bridgeAudio;
    public AudioSource balconyAudio;

    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable interactable;

    void Start()
    {
        interactable = gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        interactable.selectEntered.AddListener(OnButtonPressed);
    }

    void OnButtonPressed(SelectEnterEventArgs args)
    {
        if (teleportTarget == null || xrOrigin == null) return;

        // Teleport player to bridge
        xrOrigin.transform.position = teleportTarget.position;

        // Stop balcony audio, start bridge audio
        if (balconyAudio != null) balconyAudio.Stop();
        if (bridgeAudio != null)
        {
            bridgeAudio.loop = true;
            bridgeAudio.Play();
        }

        Debug.Log("Teleported to bridge!");
    }
}