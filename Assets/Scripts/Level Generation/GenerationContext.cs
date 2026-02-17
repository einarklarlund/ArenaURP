public class GenerationContext
{
    public readonly Grid Grid;
    public readonly LevelGenerator Generator;
    public readonly LevelData Data;

    public GenerationContext(Grid grid, LevelGenerator generator, LevelData data)
    {
        Grid = grid;
        Generator = generator;
        Data = data;
    }
}