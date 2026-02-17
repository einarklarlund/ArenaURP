using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WalkerManager : MonoBehaviour
{
	public GridNetworkPlayer NetworkPlayer;

    public int CurrentSimulationStep { get; private set; } = 0;
    public List<Walker> ActiveWalkers { get; private set; } = new List<Walker>();

    private List<Walker> toAdd = new List<Walker>();
    private List<Walker> toRemove = new List<Walker>();
	private bool skipDebugDrawing = false;

    public void RegisterWalker(Walker walker) => toAdd.Add(walker);
    public void UnregisterWalker(Walker walker) => toRemove.Add(walker);

	public Walker SpawnWalker(WalkerProfile profile, Vector3Int position, Vector3Int direction)
	{
		Walker walker = new Walker(profile, position, direction);
		ActiveWalkers.Add(walker);

		return walker;
	}

	public IEnumerator ExecuteSimulation(ISimulationExitCondition exitCondition, GenerationContext context, bool isDebugEnabled)
	{
		if (NetworkPlayer != null)
			NetworkPlayer.Setup(context);

		while(!exitCondition.IsSimulationComplete(context))
		{
			ExecuteSimulationStep(context);

			if(isDebugEnabled && !skipDebugDrawing)
			{
				yield return WaitForNextButtonPress();
			}
		}
		
		ActiveWalkers.Clear();

		yield break;
	}

    private void ExecuteSimulationStep(GenerationContext context)
    {
        foreach (var walker in ActiveWalkers)
        {
            walker.Step(context);
        }

        // Process spawning/deletion after the loop to avoid "Collection Modified" errors
        UpdateWalkerList();
        CurrentSimulationStep++;
    }

    private void UpdateWalkerList()
    {
        foreach (var w in toAdd) ActiveWalkers.Add(w);
        foreach (var w in toRemove) ActiveWalkers.Remove(w);
        toAdd.Clear();
        toRemove.Clear();
    }

	void Update()
    {
        if(Input.GetKeyDown(KeyCode.S))
        {
            skipDebugDrawing = true;
        }
    }

	private IEnumerator WaitForNextButtonPress()
	{
		// wait for N key to be pressed if skip button hasn't been pressed
		while(!skipDebugDrawing && !Input.GetKey(KeyCode.N))
		{
			yield return null;
		}
		yield return new WaitForSeconds(0.07f);	
	}
}