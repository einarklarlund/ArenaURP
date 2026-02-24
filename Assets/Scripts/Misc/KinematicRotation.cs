using UnityEngine;

public class KinematicRotation : MonoBehaviour
{
    [Header("Timing")]
    public float startDelay = 2.0f;
    public float accelerationDuration = 5.0f;

    [Header("Speed")]
    public float maxRotationSpeed = 5.0f; // Degrees per second

    private float _elapsedTime = 0f;
    private Quaternion _initialRotation;

    void OnEnable()
    {
        // Store the starting rotation so we rotate relative to it
        _initialRotation = transform.rotation;
        _elapsedTime = 0;
    }

    void Update()
    {
        _elapsedTime += Time.deltaTime;

        float totalRotationDegrees = CalculateTotalAngle(_elapsedTime);

        transform.rotation = _initialRotation * Quaternion.Euler(0, totalRotationDegrees, 0);
    }

    private float CalculateTotalAngle(float t)
    {
        if (t <= startDelay) return 0f;

        float tMoving = t - startDelay;
        float acceleration = maxRotationSpeed / accelerationDuration;

        if (tMoving <= accelerationDuration)
        {
            // Angle = 0.5 * acceleration * time^2
            return 0.5f * acceleration * Mathf.Pow(tMoving, 2);
        }

        float angleAtEndOfAccel = 0.5f * acceleration * Mathf.Pow(accelerationDuration, 2);
        float timeSpentAtMaxSpeed = tMoving - accelerationDuration;

        return angleAtEndOfAccel + (maxRotationSpeed * timeSpentAtMaxSpeed);
    }
}