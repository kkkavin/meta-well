using UnityEngine;

public class WaterFlow : MonoBehaviour
{
    public float scrollSpeed = 0.09f;
    private Renderer rend;

    void Start()
    {
        rend = GetComponent<Renderer>();
    }

    void Update()
    {
        float offset = Time.time * scrollSpeed;
        rend.material.SetTextureOffset("_BaseMap",
            new Vector2(offset, offset * 0.3f));
    }
}