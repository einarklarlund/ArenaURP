using System;
using FishNet.Managing.Client;
using FishNet.Object;
using UnityEngine;

public class BotSpawner : NetworkBehaviour
{
    public event Action<NetworkObject> OnSpawned;

    private string[] names =
    {
        "A Professional With Standards",
        "AimBot",
        "AmNot",
        "Aperture Science Prototype XR7",
        "Archimedes!",
        "BeepBeepBoop",
        "Big Mean Muther Hubbard",
        "Black Mesa",
        "BoomerBile",
        "Cannon Fodder",
        "CEDA",
        "Chell",
        "Chucklenuts",
        "Companion Cube",
        "Crazed Gunman",
        "CreditToTeam",
        "CRITRAWKETS",
        "Crowbar",
        "CryBaby",
        "CrySomeMore",
        "C++",
        "DeadHead",
        "Delicious Cake",
        "Divide by Zero",
        "Dog",
        "Force of Nature",
        "Freakin' Unbelievable",
        "Gentlemanne of Leisure",
        "GENTLE MANNE of LEISURE",
        "GLaDOS",
        "Glorified Toaster with Legs",
        "Grim Bloody Fable",
        "GutsAndGlory!",
        "Hat-Wearing MAN",
        "Headful of Eyeballs",
        "Herr Doktor",
        "HI THERE",
        "Hostage",
        "Humans Are Weak",
        "H@XX0RZ",
        "I LIVE!",
        "It's Filthy in There!",
        "IvanTheSpaceBiker",
        "Kaboom!",
        "Kill Me",
        "LOS LOS LOS",
        "Maggot",
        "Mann Co.",
        "Me",
        "Mega Baboon",
        "Mentlegen",
        "MindlessElectrons",
        "MoreGun",
        "Nobody",
        "Nom Nom Nom",
        "NotMe",
        "Numnutz",
        "One-Man Cheeseburger Apocalypse",
        "Poopy Joe",
        "Pow!",
        "RageQuit",
        "Ribs Grow Back",
        "Saxton Hale",
        "Screamin' Eagles",
        "SMELLY UNFORTUNATE",
        "SomeDude",
        "Someone Else",
        "Soulless",
        "Still Alive",
        "TAAAAANK!",
        "Target Practice",
        "ThatGuy",
        "The Administrator",
        "The Combine",
        "The Freeman",
        "The G-Man",
        "THEM",
        "Tiny Baby Man",
        "Totally Not A Bot",
        "trigger_hurt",
        "WITCH",
        "ZAWMBEEZ",
        "Ze Ubermensch",
        "Zepheniah Mann",
        "0xDEADBEEF",
        "10001011101"
    };
    
    [SerializeField] private NetworkPlayer botPlayerPrefab;

    // ClientManager this object was initialized under, cached at OnStartServer so we can
    // unsubscribe safely during teardown even after the base reference is cleared.
    private ClientManager _clientManager;

    // Subscribe on the server, which (on a host) starts before its local client authenticates.
    // Use this NetworkBehaviour's OWN ClientManager rather than InstanceFinder: the global
    // accessor logs "Multiple NetworkManagers found" and returns an arbitrary manager whenever
    // more than one exists in the process (e.g. the PlayMode two-peer test fixture).
    public override void OnStartServer()
    {
        base.OnStartServer();
        _clientManager = ClientManager;
        if (_clientManager != null)
            _clientManager.OnAuthenticated += HandleAuthenticated;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        if (_clientManager != null)
        {
            _clientManager.OnAuthenticated -= HandleAuthenticated;
            _clientManager = null;
        }
    }

    private void HandleAuthenticated()
    {
        SpawnBots();
    }

    /// <summary>
    /// Spawns bot players according to Host options.
    /// </summary>
    [Server]
    private void SpawnBots()
    {
        int numBots = HostOptionsManager.Instance != null
            ? HostOptionsManager.Instance.Options.BotCount
            : 0;

        // Spawn bots
        for (int i = 0; i < numBots; i++)
        {
            SpawnBot();
        }
    }

    [Server]
    public void SpawnBot()
    {
        var bot = Instantiate(botPlayerPrefab);
        var name = names[UnityEngine.Random.Range(0, names.Length)];
        bot.name = $"NetworkPlayer Bot {name}";
        bot.Username.Value = $"Bot {name}";
        bot.IsReady.Value = true;
        bot.IsBot = true;
        Spawn(bot, ClientManager.Connection);
        OnSpawned?.Invoke(bot);
    }
}
