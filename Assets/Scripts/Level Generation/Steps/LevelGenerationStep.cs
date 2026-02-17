using System.Collections;
using UnityEngine;

public abstract class LevelGenerationStep : ScriptableObject
{
    public abstract IEnumerator Execute(GenerationContext context);
}