using UnityEngine;

[RequireComponent(typeof(Light))]
public class LightRangeTween : MonoBehaviour
{
    [SerializeField] private float duration = 1f;
    [SerializeField] private Gradient colorOverLifetime;

    private Light lightSource;
    private float initialRange;
    private float initialIntensity;
    private float enabledTime;

    private void Awake()
    {
        lightSource = GetComponent<Light>();
        initialRange = lightSource.range;
        initialIntensity = lightSource.intensity;
    }

    private void OnEnable()
    {
        enabledTime = Time.time;
        lightSource.range = initialRange;
        lightSource.intensity = initialIntensity;
    }

    private void Update()
    {
        float t = Mathf.Clamp01((Time.time - enabledTime) / duration);
        lightSource.range = Mathf.Lerp(initialRange, 0f, t);
        lightSource.intensity = Mathf.Lerp(initialIntensity, 0f, t);
        if (colorOverLifetime != null)
            lightSource.color = colorOverLifetime.Evaluate(t);
    }
}
