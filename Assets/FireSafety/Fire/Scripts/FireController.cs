using UnityEngine;

public class FireController : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)]
    private float intensity = 1f;

    public float Intensity => intensity;

    public void SetIntensity(float value)
    {
        intensity = Mathf.Clamp01(value);
    }
}