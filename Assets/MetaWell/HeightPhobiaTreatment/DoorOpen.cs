using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRSimpleInteractable))]
public class DoorOpen : MonoBehaviour
{
    public Transform doorPivot;      // the door mesh (pivot at hinge)
    public float openAngle = 90f;
    public float openSpeed = 2f;

    public AudioSource audioSource;  // drag your meditation/balcony audio source here
    public AudioClip balconyAudio;

    private bool isOpen = false;
    private Quaternion closedRot;
    private Quaternion openRot;

    void Start()
    {
        closedRot = doorPivot.localRotation;
        openRot = closedRot * Quaternion.Euler(0f, openAngle, 0f);

        GetComponent<XRSimpleInteractable>().selectEntered.AddListener(OnInteract);
    }

    void OnInteract(SelectEnterEventArgs args)
    {
        if (isOpen) return;
        isOpen = true;

        // Play balcony/meditation audio
        audioSource.clip = balconyAudio;
        audioSource.Play();
    }

    void Update()
    {
        if (isOpen)
        {
            doorPivot.localRotation = Quaternion.Slerp(doorPivot.localRotation, openRot, Time.deltaTime * openSpeed);
        }
    }
}