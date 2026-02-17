using System;
using UnityEngine;

[Serializable]
public abstract class WalkerCondition : ScriptableObject
{
    public abstract bool Check(Walker walker, GenerationContext context);
}