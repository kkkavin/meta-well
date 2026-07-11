using UnityEngine;
using UnityEngine.Video;

[RequireComponent(typeof(VideoPlayer))]
public class SkyboxVideoBuffer : MonoBehaviour
{
    [Header("Skybox Materials")]
    [Tooltip("The static image material shown during loading.")]
    public Material staticSkyboxMaterial;

    [Tooltip("The video render texture material.")]
    public Material videoSkyboxMaterial;

    private VideoPlayer videoPlayer;

    void Awake()
    {
        videoPlayer = GetComponent<VideoPlayer>();

        // Force the static fallback material at the absolute start
        RenderSettings.skybox = staticSkyboxMaterial;

        // Subscribe to Unity's video engine events
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.started += OnVideoStarted;
    }

    void Start()
    {
        // Start buffering the video frames into memory
        videoPlayer.Prepare();
    }

    // Called when video engine has buffered enough data to play without lagging
    void OnVideoPrepared(VideoPlayer vp)
    {
        vp.Play();
    }

    // Called the exact frame the first video texture pixel renders
    void OnVideoStarted(VideoPlayer vp)
    {
        RenderSettings.skybox = videoSkyboxMaterial;
        DynamicGI.UpdateEnvironment(); // Refreshes ambient lighting immediately
    }

    void OnDestroy()
    {
        // Unsubscribe to prevent memory leaks
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.started -= OnVideoStarted;
        }
    }
}
