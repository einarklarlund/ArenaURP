public interface ISimulationExitCondition
{
    bool IsSimulationComplete(GenerationContext context);
}

public class ExitAtFloorCount : ISimulationExitCondition
{
    private int _target;
    public ExitAtFloorCount(int target) => _target = target;

    public bool IsSimulationComplete(GenerationContext context) 
        => context.Grid.Floors.Count >= _target;
}

public class ExitAtDensity : ISimulationExitCondition
{
    private float _percentage;
    public ExitAtDensity(float p) => _percentage = p;

    public bool IsSimulationComplete(GenerationContext context)
    {
        float totalCells = context.Grid.Width * context.Grid.Height;
        return (context.Grid.Floors.Count / totalCells) >= _percentage;
    }
}

public class ExitAfterSteps : ISimulationExitCondition
{
    private int stopAtStep;

    public ExitAfterSteps(int maxSteps, int currentSteps)
    {
        stopAtStep = currentSteps + maxSteps;
    }

    public bool IsSimulationComplete(GenerationContext context)
    {
        var currentStep = context.Generator.WalkerManager.CurrentSimulationStep;
        
        // Return true if we've hit the limit
        if (currentStep >= stopAtStep)
        {
            return true;
        }

        // Safety check - if all walkers are dead, simulation is complete
        // We can access the ActiveWalkers because we are passing the context 
        // which now includes a reference to the WalkerManager.
        if (context.Generator.WalkerManager.ActiveWalkers.Count == 0)
        {
            return true;
        }

        return false;
    }
}