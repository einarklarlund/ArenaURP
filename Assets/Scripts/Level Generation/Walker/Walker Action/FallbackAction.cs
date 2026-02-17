using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Composite Fallback")]
public class FallbackAction : WalkerAction
{
    public WalkerAction PrimaryAction;
    public WalkerAction SecondaryAction;

    public override bool Perform(Walker walker, GenerationContext context)
    {
        // This requires changing WalkerAction.Perform to return a bool (success/fail)
        // or checking the state of the grid before and after.
        if(PrimaryAction.Perform(walker, context))
            return true;
        else
            return SecondaryAction.Perform(walker, context);
    }
}