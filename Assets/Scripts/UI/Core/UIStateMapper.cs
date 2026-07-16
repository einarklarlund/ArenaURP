/// <summary>
/// Pure, stateless mapper from match-flow state to the kind of UI view that
/// should be shown, plus whether the menu camera should be active for that view.
///
/// Lives in Arena.UIFlow.Core (no engine references) so it can be tested in
/// edit-mode without instantiating MonoBehaviour Views.
///
/// Concrete View MonoBehaviours live in the engine assembly; UIManager owns
/// the ViewKind -> concrete View translation.
/// </summary>
public static class UIStateMapper
{
    /// <summary>
    /// Mirror of MatchFlowManager.MatchState. Duplicated (rather than
    /// referenced) so this assembly stays free of engine/netcode dependencies.
    /// UIManager translates between the two enums at the call site.
    /// </summary>
    public enum UIMatchState
    {
        Pregame,
        PregameCountdown,
        During,
        PostgameCountdown,
        Postgame,
    }

    /// <summary>
    /// Identifies the kind of view UIManager should display in response to a
    /// match-state change. None = leave the current view alone.
    /// </summary>
    public enum ViewKind
    {
        None,
        Lobby,
        PregameCountdown,
        PostgameCountdown,
        Main,
    }

    /// <summary>
    /// Maps a match state to the view that should be shown when entering it.
    /// Returns ViewKind.None for states that do not drive a view change
    /// (handled by other event sources, e.g. local pawn spawn/death).
    ///
    /// Deliberately a switch expression with NO default arm: adding a
    /// UIMatchState value without handling it here is a compile error
    /// (CS8509, escalated to error in .editorconfig). This is the
    /// exhaustiveness guard that keeps the MatchState mirror honest without
    /// sharing the engine-side enum into this engine-free Core assembly.
    /// </summary>
    public static ViewKind MatchStateToViewKind(UIMatchState state) => state switch
    {
        UIMatchState.Pregame           => ViewKind.Lobby,
        UIMatchState.PregameCountdown  => ViewKind.PregameCountdown,
        UIMatchState.PostgameCountdown => ViewKind.PostgameCountdown,
        UIMatchState.During            => ViewKind.None,
        UIMatchState.Postgame          => ViewKind.None,
    };

    /// <summary>
    /// True if the menu camera should be active while the given view kind is
    /// displayed. Mirrors the rule in UIManager: the menu camera is on for
    /// every view except the in-game Main view.
    /// </summary>
    public static bool ShouldActivateMenuCamera(ViewKind viewKind)
    {
        return viewKind != ViewKind.Main;
    }
}
