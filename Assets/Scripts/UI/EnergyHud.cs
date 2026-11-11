using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The local pawn's suit power, shown as "Energy": a label over a thin bar. It fades in while the value is
/// changing (sprinting or recharging) and fades out once it has held still for a moment. Lives under MainView,
/// so it shows only while the local player is alive.
/// </summary>
public sealed class EnergyHud : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [Tooltip("A Filled, horizontal Image whose fill amount is the energy left.")]
    [SerializeField] private Image fill;

    [Tooltip("Seconds the display stays up after the energy stops changing.")]
    [SerializeField] private float holdDuration = 0.75f;
    [Tooltip("Seconds to fade from hidden to fully shown.")]
    [SerializeField] private float fadeInDuration = 0.15f;
    [Tooltip("Seconds to fade from fully shown to hidden.")]
    [SerializeField] private float fadeOutDuration = 0.5f;

    private Pawn _pawn;
    private float _lastFraction;
    private float _holdTimer;

    private void OnEnable()
    {
        _pawn = null;
        _holdTimer = 0f;
        group.alpha = 0f;
    }

    private void Update()
    {
        NetworkPlayer player = NetworkPlayer.LocalInstance;
        Pawn pawn = player != null ? player.ControlledPawn.Value : null;

        if (pawn != _pawn)
        {
            // A new life starts with full energy: show nothing for the jump from the old pawn's value.
            _pawn = pawn;
            _lastFraction = pawn != null ? pawn.SuitPowerFraction : 1f;
            _holdTimer = 0f;
        }

        if (pawn != null)
        {
            float fraction = pawn.SuitPowerFraction;
            if (!Mathf.Approximately(fraction, _lastFraction))
                _holdTimer = holdDuration;
            _lastFraction = fraction;
            fill.fillAmount = fraction;
        }

        _holdTimer -= Time.deltaTime;
        bool visible = _holdTimer > 0f;
        float rate = visible ? 1f / fadeInDuration : 1f / fadeOutDuration;
        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, rate * Time.deltaTime);
    }
}
