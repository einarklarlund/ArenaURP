public sealed class DebugMatchMediator : MatchMediator
{
    bool isInitialized = false;

    private void Update()
    {
        // Wait until managers are ready
        if
        (
            !isInitialized
            && matchFlowManager.IsServerInitialized
            && deathmatchManager.IsServerInitialized
            && networkPlayerManager.IsServerInitialized
            && pawnManager.IsServerInitialized
        )
        {
            InitializeDebugMatch();
        }
    }

    private void InitializeDebugMatch()
    {
        matchFlowManager.StartDebugMatch();
        deathmatchManager.BeginGame();
        isInitialized = true;
    }
}
