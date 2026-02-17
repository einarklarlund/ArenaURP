using FishNet.Object;
using UnityEngine;

public class PawnBodySpawner : NetworkBehaviour
{
    [SerializeField] private PawnManager pawnManager;
    [SerializeField] private Rigidbody bodyPrefab;
    [SerializeField] private float speedOnDeath = 17f;
    [SerializeField] private float rotationalSpeedOnDeath = 5f;

    public override void OnStartServer()
    {
        base.OnStartServer();
        pawnManager.OnPawnKilled += HandlePawnKilled;
    }

    private void HandlePawnKilled(Pawn pawn, DamageInfo damageInfo)
    {
        ObserversInstantiateBody
        (
            pawn.transform.position,
            pawn.transform.rotation,
            pawn.transform.right,
            damageInfo.Direction.normalized
        );
    }

    [ObserversRpc]
    private void ObserversInstantiateBody(Vector3 position, Quaternion rotation, Vector3 torqueAxis, Vector3 velocityDir)
    {
        var rb = Instantiate(bodyPrefab, position, rotation);
        rb.AddForce(speedOnDeath * velocityDir, ForceMode.VelocityChange);
        rb.AddTorque(transform.right, ForceMode.VelocityChange);
    }
}