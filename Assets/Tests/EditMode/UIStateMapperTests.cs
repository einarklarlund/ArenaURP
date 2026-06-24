using NUnit.Framework;

/// <summary>
/// Edit-mode unit tests for UIStateMapper.
/// Covers each MatchState -> ViewKind mapping and each ViewKind -> menu camera bool.
/// </summary>
[TestFixture]
public class UIStateMapperTests
{
    [Test]
    public void MatchStateToViewKind_Pregame_ReturnsLobby()
    {
        Assert.AreEqual(
            UIStateMapper.ViewKind.Lobby,
            UIStateMapper.MatchStateToViewKind(UIStateMapper.UIMatchState.Pregame));
    }

    [Test]
    public void MatchStateToViewKind_PregameCountdown_ReturnsPregameCountdown()
    {
        Assert.AreEqual(
            UIStateMapper.ViewKind.PregameCountdown,
            UIStateMapper.MatchStateToViewKind(UIStateMapper.UIMatchState.PregameCountdown));
    }

    [Test]
    public void MatchStateToViewKind_PostgameCountdown_ReturnsPostgameCountdown()
    {
        Assert.AreEqual(
            UIStateMapper.ViewKind.PostgameCountdown,
            UIStateMapper.MatchStateToViewKind(UIStateMapper.UIMatchState.PostgameCountdown));
    }

    [Test]
    public void MatchStateToViewKind_During_ReturnsNone()
    {
        // During is driven by local-spawn events, not match-state -> view mapping.
        Assert.AreEqual(
            UIStateMapper.ViewKind.None,
            UIStateMapper.MatchStateToViewKind(UIStateMapper.UIMatchState.During));
    }

    [Test]
    public void MatchStateToViewKind_Postgame_ReturnsNone()
    {
        Assert.AreEqual(
            UIStateMapper.ViewKind.None,
            UIStateMapper.MatchStateToViewKind(UIStateMapper.UIMatchState.Postgame));
    }

    [Test]
    public void ShouldActivateMenuCamera_Main_IsFalse()
    {
        Assert.IsFalse(UIStateMapper.ShouldActivateMenuCamera(UIStateMapper.ViewKind.Main));
    }

    [Test]
    public void ShouldActivateMenuCamera_Lobby_IsTrue()
    {
        Assert.IsTrue(UIStateMapper.ShouldActivateMenuCamera(UIStateMapper.ViewKind.Lobby));
    }

    [Test]
    public void ShouldActivateMenuCamera_PregameCountdown_IsTrue()
    {
        Assert.IsTrue(UIStateMapper.ShouldActivateMenuCamera(UIStateMapper.ViewKind.PregameCountdown));
    }

    [Test]
    public void ShouldActivateMenuCamera_PostgameCountdown_IsTrue()
    {
        Assert.IsTrue(UIStateMapper.ShouldActivateMenuCamera(UIStateMapper.ViewKind.PostgameCountdown));
    }

    [Test]
    public void ShouldActivateMenuCamera_None_IsTrue()
    {
        // None covers menu-y views (Settings, Pause, Loading, etc.) - menu cam should remain on.
        Assert.IsTrue(UIStateMapper.ShouldActivateMenuCamera(UIStateMapper.ViewKind.None));
    }
}
