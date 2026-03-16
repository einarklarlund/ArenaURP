using UnityEngine;

public class PawnHealthIndicator : MonoBehaviour
{
    [SerializeField] private Light healthIndicator;
    [SerializeField] private Pawn pawn;
    [SerializeField] private Color highHealthColor = Color.green;
    [SerializeField] private Color lowHealthColor = Color.red;

    void Start()
    {
        pawn.Health.OnChange += OnHealthChanged;
        pawn.MaxHealth.OnChange += OnHealthChanged;
    }
    
    private void OnHealthChanged(int prev, int next, bool asServer)
    {
        if(asServer) return;

        healthIndicator.color = Color.Lerp(lowHealthColor, highHealthColor, pawn.MaxHealth.Value);
    }
}
