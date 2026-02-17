using System;
using System.Collections.Generic;
using UnityEngine;

public class Walker
{
    public Vector3Int GridPosition;
    public Vector3Int Direction;
    public List<WalkerAction> CurrentActionSet;
    public WalkerAction CurrentAction;
    public List<Vector3Int> PathHistory = new List<Vector3Int>(); // The "Breadcrumbs trail"
    
    // A reference back to the blueprint for rules
    public WalkerProfile Profile { get; private set; }

    public Walker(WalkerProfile profile, Vector3Int position, Vector3Int direction)
    {
        Profile = profile;
        GridPosition = position;
        Direction = direction;
        CurrentActionSet = profile.InitialActions;
        if(profile.AvailableActions.Count == 0)
        {
            throw new MissingFieldException("Walker was instantiated with a profile with no AvailableActions.");
        }
    }

    public void Step(GenerationContext context)
    {
        foreach(var action in CurrentActionSet)
        {
            if (action == null)
            {
                throw new Exception($"Tried to run a walker {Profile.name} with a null action");
            }
            CurrentAction = action;
            CurrentAction.Perform(this, context);
        }
        PathHistory.Add(GridPosition);
        CurrentActionSet = Profile.AvailableActions;
    }
}