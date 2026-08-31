using FishNet;
using FishNet.Managing.Timing;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// All data and logic for an individual weapon.
/// </summary>
public class PawnWeapon : MonoBehaviour
{
    private static readonly int FiredHash = Animator.StringToHash("Fired");

    [Header("Weapon Stats")]

    [Tooltip("Firing rate in projectiles per second.")]
    public float fireRate = 5;

    public float spreadAngle = 5f;

    [Header("Projectile Spawning")]

    [Tooltip("Transform that keeps track of where projectiles are spawned.")]
    public Transform spawnPoint;

    [Tooltip("Transform that keeps track of where projectiles appear visually when spawned.")]
    public Transform muzzlePoint;

    [Tooltip("The projectile prefab to spawn.")]
    public Projectile projectile;

    [Header("Visuals")]
    public Animator animator;

    public GameObject smokeEffects;

    public ParticleSystem muzzleFlash;

    private float _lastFiredAt = 0;

    public void ProcessFireInput(Pawn pawnOwner, PreciseTick tick)
    {
        float minDurationBetweenShots = 1 / fireRate;
        if (_lastFiredAt + minDurationBetweenShots > Time.time)
            return;

        Fire(pawnOwner, tick);
    }

    private void Fire(Pawn pawnOwner, PreciseTick tick)
    {
        var fireRot = CalculateSpread();
        var firePos = muzzlePoint.position;

        NetworkObject nob = InstanceFinder.NetworkManager.GetPooledInstantiated(
            projectile, firePos, fireRot, asServer: false);
        if (nob.TryGetComponent<Projectile>(out var p))
        {
            p.owningPawn = pawnOwner;
            p.spawnTick = tick;
            // Send the muzzle point to the *client only* so that projectiles
            // always appear at the right spot during client-side predicted spawn.
            p.visualPredictionPoint = muzzlePoint;
            // TODO: spawn position should be set here and be calculated based on where
            // the pawn was at spawnTick.
        }
        InstanceFinder.NetworkManager.ServerManager.Spawn(nob, pawnOwner.Owner);

        PlayVfx();

        _lastFiredAt = Time.time;
    }

    private Quaternion CalculateSpread()
    {
        // Random spread within a cone
        float maxPitch = Mathf.Min(2, spreadAngle);
        float randomYaw = Random.Range(-spreadAngle / 2, spreadAngle / 2);
        float randomPitch = Random.Range(-maxPitch / 2, maxPitch / 2);
        return muzzlePoint.rotation * Quaternion.Euler(randomPitch, randomYaw, 0);
    }

    private void PlayVfx()
    {
        Instantiate(smokeEffects, muzzlePoint.position, muzzlePoint.rotation);
        animator.SetTrigger(FiredHash);
        if(muzzleFlash.isPlaying)
            muzzleFlash.Stop();
        muzzleFlash.Play();
    }
}
