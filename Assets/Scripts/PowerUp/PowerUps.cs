using System.Collections;
using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Clients apply powerups to themselves here.
/// </summary>
public class PowerUps : NetworkBehaviour
{
    public enum Type
    {
        Heal,
        MegaMushroom,
        FryingPan
    }

    public FryingPan fryingPanPrefab;

    private static PowerUps Instance;

    private void Awake()
    {
        Instance = this;
    }

    public static void Apply(Pawn pawn, Type type)
    {
        switch (type)
        {
            case Type.Heal:
                Instance.ServerHeal(pawn, 1);
                break;
            case Type.MegaMushroom:
                Vector3 scale = Vector3.one * 3;
                Instance.ScalePawnTo(pawn, scale);
                break;
            case Type.FryingPan:
                Instance.SpawnFryingPan(pawn);
                break;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ServerHeal(Pawn pawn, int amount)
    {
        int maxHealth = pawn.MaxHealth.Value;
        int currentHealth = pawn.CurrentHealth.Value;
        if (currentHealth <= 0) return;

        if (amount > 0)
            pawn.CurrentHealth.Value = Mathf.Max(maxHealth, currentHealth + amount);
    }
    
    private void ScalePawnTo(Pawn pawn, Vector3 scale)
    {
        StartCoroutine(ScaleRoutine(pawn, scale, Time.time, transform.localScale));
    }

    private IEnumerator ScaleRoutine(Pawn pawn, Vector3 scale, float startedAt, Vector3 initialScale)
    {
        float t = 0;
        // scale up
        while (t <= 1)
        {
            t = (Time.time - startedAt) / PowerupConstants.MEGA_MUSHROOM_SCALE_UP_DURATION;
            pawn.transform.localScale = Vector3.Lerp(initialScale, scale, t);
            yield return new WaitForEndOfFrame();
        }
        pawn.transform.localScale = scale;
        // wait to scale down
        startedAt = Time.time;
        t = 0;
        float scaledUpDuration = PowerupConstants.MEGA_MUSHROOM_TOTAL_DURATION
            - PowerupConstants.MEGA_MUSHROOM_SCALE_UP_DURATION
            - PowerupConstants.MEGA_MUSHROOM_SCALE_DOWN_DURATION;
        while (t <= 1)
        {
            t = (Time.time - startedAt) / scaledUpDuration;
            yield return new WaitForEndOfFrame();
        }
        // scale down
        startedAt = Time.time;
        t = 0;
        while (t <= 1)
        {
            t = (Time.time - startedAt) / PowerupConstants.MEGA_MUSHROOM_SCALE_DOWN_DURATION;
            pawn.transform.localScale = Vector3.Lerp(scale, initialScale, t);
            yield return new WaitForEndOfFrame();
        }
        pawn.transform.localScale = initialScale;
    }

    private void SpawnFryingPan(Pawn pawn)
    {
        NetworkObject nob = InstanceFinder.NetworkManager.GetPooledInstantiated(
            fryingPanPrefab, pawn.transform.position, pawn.transform.rotation, asServer: false);
        
        if (nob.TryGetComponent<FryingPan>(out var p))
        {
            p.owningPawn = pawn;
        }

        InstanceFinder.NetworkManager.ServerManager.Spawn(nob, pawn.Owner);
    }
}