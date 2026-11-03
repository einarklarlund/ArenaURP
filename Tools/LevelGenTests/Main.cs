/// <summary>NUnitLite's runner: `dotnet test` starts it on the Unity Editor's Mono (see LevelGenTests.csproj).</summary>
public static class Program
{
    public static int Main(string[] args) => new NUnitLite.AutoRun(typeof(Program).Assembly).Execute(args);
}
