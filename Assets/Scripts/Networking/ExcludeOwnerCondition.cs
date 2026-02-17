using FishNet.Connection;
using FishNet.Observing;
using UnityEngine;

[CreateAssetMenu(menuName = "FishNet/Observers/Exclude Owner Condition", fileName = "New Exclude Owner Condition")]
public class ExcludeOwnerCondition : ObserverCondition
{
    public override bool ConditionMet(NetworkConnection connection, bool currentlyAdded, out bool notProcessed)
    {
        notProcessed = false;

        // If there is no owner yet, everyone can see it (fallback)
        if (NetworkObject.Owner == null) return false;

        // If this connection IS the owner, return false to hide the object from them.
        // Everyone else returns true to stay as an observer.
        return (connection != NetworkObject.Owner);
    }

    /// <summary>
    /// We use Normal because the owner of a bullet usually doesn't change 
    /// during its short lifetime.
    /// </summary>
    public override ObserverConditionType GetConditionType() => ObserverConditionType.Normal;
}