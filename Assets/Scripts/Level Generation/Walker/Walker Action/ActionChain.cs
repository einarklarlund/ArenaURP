using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Walker/Actions/Action Chain")]
public class ActionGroup : WalkerAction
{
    public List<WalkerAction> ActionsToExecute;

    public override bool Perform(Walker walker, GenerationContext context)
    {
        if (ActionsToExecute == null) return true;

        bool anyFailed = false;
        foreach (var action in ActionsToExecute)
        {
            // We call Perform directly because the Group's own conditions 
            // were already checked by the Walker before calling this.
            // anyFailed will be true if the action fails to perform, or
            // any previous action failed to perform.
            anyFailed = anyFailed || !action.Perform(walker, context);
        }
        return anyFailed;
    }
}