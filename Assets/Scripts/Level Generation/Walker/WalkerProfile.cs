using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Walker Profile")]
public class WalkerProfile : ScriptableObject
{
    public List<WalkerAction> InitialActions;
    
    [Header("Behavior Rules")]
    public List<WalkerAction> AvailableActions;
    // You could even add stats here, like 'Energy' or 'Speed'
}