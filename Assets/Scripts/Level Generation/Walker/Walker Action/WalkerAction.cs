using System;
using UnityEngine;

[Serializable]
public abstract class WalkerAction : ScriptableObject
{
    public WalkerCondition[] Conditions;

    public abstract bool Perform(Walker walker, GenerationContext context);

    public void TryPerform(Walker walker, GenerationContext context)
    {
        bool CanPerform = true;
        foreach (var condition in Conditions)
        {
            if (!condition.Check(walker, context))
            {
                CanPerform = false;
            }
        }
        if(CanPerform)
        {
            Perform(walker, context);
        }
    }
}
