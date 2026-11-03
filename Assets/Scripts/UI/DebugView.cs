using System.Collections.Generic;
using Arena.LevelGen;
using FishNet;
using FishNet.Object.Synchronizing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class DebugView : View
{
    [SerializeField] private Button spawnBotButton;
    [SerializeField] private Button backButton;

    [Header("Level")]
    [Tooltip("Seed, share code, hash and metrics of the level this peer built.")]
    [SerializeField] private TMP_Text levelInfoText;
    [Tooltip("Host only: a new random level with the same config and art (levelgen.regen).")]
    [SerializeField] private Button regenerateButton;
    [SerializeField] private Button copyShareCodeButton;
    [Tooltip("Host only: the level a share code describes (levelgen.load).")]
    [SerializeField] private TMP_InputField loadShareCodeInputField;
    [SerializeField] private Button loadShareCodeButton;

    [Header("Upgrades")]
    [Tooltip("Swaps the Level panel for the Upgrades panel and back.")]
    [SerializeField] private Button upgradesTabButton;
    [SerializeField] private GameObject levelPanel;
    [SerializeField] private GameObject upgradesPanel;
    [Tooltip("One row per catalogue upgrade, built on Show.")]
    [SerializeField] private DebugUpgradeRow upgradeRowPrefab;
    [SerializeField] private Transform upgradeRowContainer;
    [Tooltip("One more level-up pick, offered the normal way.")]
    [SerializeField] private Button grantPickButton;
    [SerializeField] private TMP_Text upgradesStatusText;

    // The metrics take a moment to measure, so they're kept per level.
    private int _measuredMatch = -1;
    private string _metrics = "";
    private string _status = "";

    public override void Show()
    {
        spawnBotButton.gameObject.SetActive(false);
        spawnBotButton.onClick.AddListener(HandleSpawnBotClicked);
        backButton.onClick.AddListener(HandleBackClicked);

        if (levelInfoText != null)
        {
            regenerateButton.onClick.AddListener(HandleRegenerateClicked);
            copyShareCodeButton.onClick.AddListener(HandleCopyShareCodeClicked);
            loadShareCodeButton.onClick.AddListener(HandleLoadShareCodeClicked);
            if (LevelSync.Instance != null) LevelSync.Instance.OnLevelBuilt += HandleLevelBuilt;
            _status = "";
            RefreshLevel();
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        if (upgradeRowPrefab != null)
        {
            upgradesTabButton.onClick.AddListener(HandleUpgradesTabClicked);
            ShowTab(upgrades: false);
            ShowUpgrades();
        }
#endif

        base.Show();
    }

    public override void Hide()
    {
        spawnBotButton.onClick.RemoveListener(HandleSpawnBotClicked);
        backButton.onClick.RemoveListener(HandleBackClicked);

        if (levelInfoText != null)
        {
            regenerateButton.onClick.RemoveListener(HandleRegenerateClicked);
            copyShareCodeButton.onClick.RemoveListener(HandleCopyShareCodeClicked);
            loadShareCodeButton.onClick.RemoveListener(HandleLoadShareCodeClicked);
            if (LevelSync.Instance != null) LevelSync.Instance.OnLevelBuilt -= HandleLevelBuilt;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        if (upgradeRowPrefab != null)
        {
            upgradesTabButton.onClick.RemoveListener(HandleUpgradesTabClicked);
            HideUpgrades();
        }
#endif

        base.Hide();
    }

    private void HandleSpawnBotClicked()
    {
        throw new System.NotImplementedException();
    }

    private void HandleBackClicked()
    {
        LocalUIEvents.OnDebugViewClosed?.Invoke();
    }

    // ── Upgrades ─────────────────────────────────────────────────────────

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    private readonly List<(int id, DebugUpgradeRow row)> _upgradeRows = new();
    private NetworkPlayer _upgradesPlayer;

    private void HandleUpgradesTabClicked() => ShowTab(upgrades: !upgradesPanel.activeSelf);

    private void ShowTab(bool upgrades)
    {
        levelPanel.SetActive(!upgrades);
        upgradesPanel.SetActive(upgrades);
        upgradesTabButton.GetComponentInChildren<TMP_Text>().text = upgrades ? "Level" : "Upgrades";
    }

    private void ShowUpgrades()
    {
        _upgradesPlayer = NetworkPlayer.LocalInstance;
        grantPickButton.interactable = _upgradesPlayer != null;
        if (_upgradesPlayer == null)
        {
            upgradesStatusText.text = "No local player.";
            return;
        }

        grantPickButton.onClick.AddListener(HandleGrantPickClicked);
        _upgradesPlayer.Upgrades.OnChange += HandleUpgradesChanged;
        _upgradesPlayer.UpgradePicksPending.OnChange += HandlePicksPendingChanged;

        UpgradeCatalogue catalogue = _upgradesPlayer.UpgradeCatalogue;
        for (int id = 0; id < catalogue.Count; id++)
        {
            if (catalogue[id] == null) continue;
            DebugUpgradeRow row = Instantiate(upgradeRowPrefab, upgradeRowContainer);
            row.Bind(id, catalogue[id], HandleGrantClicked);
            _upgradeRows.Add((id, row));
        }
        RefreshUpgrades();
    }

    private void HideUpgrades()
    {
        grantPickButton.onClick.RemoveListener(HandleGrantPickClicked);
        if (_upgradesPlayer != null)
        {
            _upgradesPlayer.Upgrades.OnChange -= HandleUpgradesChanged;
            _upgradesPlayer.UpgradePicksPending.OnChange -= HandlePicksPendingChanged;
            _upgradesPlayer = null;
        }

        foreach ((int _, DebugUpgradeRow row) in _upgradeRows)
            Destroy(row.gameObject);
        _upgradeRows.Clear();
    }

    private void HandleGrantClicked(int id)
    {
        if (_upgradesPlayer != null) _upgradesPlayer.DebugGrantUpgrade(id);
    }

    private void HandleGrantPickClicked()
    {
        if (_upgradesPlayer != null) _upgradesPlayer.DebugGrantPick();
    }

    private void HandleUpgradesChanged(SyncListOperation op, int index, byte previous, byte next, bool asServer) => RefreshUpgrades();

    private void HandlePicksPendingChanged(int prev, int next, bool asServer) => RefreshUpgrades();

    private void RefreshUpgrades()
    {
        if (_upgradesPlayer == null) return;

        foreach ((int id, DebugUpgradeRow row) in _upgradeRows)
            row.Refresh(UpgradeCatalogue.StacksOf(id, _upgradesPlayer.Upgrades));
        upgradesStatusText.text = $"{_upgradesPlayer.Upgrades.Count} upgrades this life, " +
            $"{_upgradesPlayer.UpgradePicksPending.Value} picks pending.";
    }
#endif

    // ── Level ────────────────────────────────────────────────────────────

    private void HandleLevelBuilt(LevelDescription level, LevelTicket ticket) => RefreshLevel();

    private void HandleRegenerateClicked()
    {
        if (LevelSync.Instance == null || !InstanceFinder.IsServerStarted) return;
        _status = LevelSync.Instance.ServerNextLevel() ? "" : "No valid level from the next seeds; kept this one.";
        RefreshLevel();
    }

    private void HandleCopyShareCodeClicked()
    {
        string code = CurrentShareCode();
        if (code == null) return;
        GUIUtility.systemCopyBuffer = code;
        _status = "Share code copied.";
        RefreshLevel();
    }

    private void HandleLoadShareCodeClicked()
    {
        if (LevelSync.Instance == null || !InstanceFinder.IsServerStarted) return;
        _status = LevelSync.Instance.ServerLoad(loadShareCodeInputField.text, out string error) ? "" : error;
        RefreshLevel();
    }

    private void RefreshLevel()
    {
        LevelSync sync = LevelSync.Instance;
        bool host = InstanceFinder.IsServerStarted && sync != null;
        regenerateButton.interactable = host;
        loadShareCodeButton.interactable = host;
        loadShareCodeInputField.interactable = host;

        if (sync == null || sync.Level == null)
        {
            copyShareCodeButton.interactable = false;
            levelInfoText.text = "No generated level in this scene." + (_status.Length > 0 ? "\n" + _status : "");
            return;
        }

        copyShareCodeButton.interactable = true;
        LevelTicket ticket = sync.Ticket;
        LevelDescription level = sync.Level;
        if (_measuredMatch != ticket.Match)
        {
            _measuredMatch = ticket.Match;
            _metrics = Measure(level, ticket.Settings);
        }
        levelInfoText.text =
            $"Level {ticket.Match}   seed {ticket.Seed}   generator v{ticket.GeneratorVersion}\n" +
            $"Hash {ticket.Hash:X16}\n" +
            $"{level.Plots.Count} plots, {level.Buildings.Count} buildings, {level.Props.Count} props, " +
            $"{level.SpawnPoints.Count} spawn points\n" +
            _metrics + "\n" +
            $"Share code: {Shorten(CurrentShareCode())} (Copy for all of it)" +
            (_status.Length > 0 ? "\n" + _status : "");
    }

    private static string Shorten(string code) => code.Length <= 40 ? code : code.Substring(0, 36) + "…";

    private static string CurrentShareCode()
    {
        LevelSync sync = LevelSync.Instance;
        if (sync == null || sync.Level == null) return null;
        LevelTicket ticket = sync.Ticket;
        return ShareCode.Encode(ticket.GeneratorVersion, ticket.Seed, ticket.Settings);
    }

    private static string Measure(LevelDescription level, LevelGenSettings settings)
    {
        var options = MeasureOptions.Default;
        options.PathStretchStride = 8;
        GenerationReport report = LevelEvaluator.Evaluate(level, settings, options);
        var stretch = report.PathStretch;
        int failed = report.Constraints.FindAll(c => !c.Passed).Count;
        return $"Path stretch mean {stretch.Mean:F2}, p95 {stretch.P95:F2}, worst {stretch.Worst:F2}   " +
            $"total error {report.TotalError:F2}   constraints {(failed == 0 ? "all pass" : failed + " fail")}";
    }
}
