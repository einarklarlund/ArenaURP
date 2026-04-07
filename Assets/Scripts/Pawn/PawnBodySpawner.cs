using FishNet.Object;
using UnityEngine;

public class PawnBodySpawner : NetworkBehaviour
{
    [SerializeField] private PawnManager pawnManager;
    [SerializeField] private Rigidbody bodyPrefab;
    [SerializeField] private float speedOnDeath = 17f;
    [SerializeField] private float rotationalSpeedOnDeath = 5f;

    public override void OnStartClient()
    {
        base.OnStartClient();
        pawnManager.OnPawnKilled += HandlePawnKilled;
    }

    private void HandlePawnKilled(Pawn pawn, DamageInfo damageInfo)
    {
        InstantiateBody
        (
            pawn.transform.position,
            pawn.transform.rotation,
            pawn.transform.right,
            damageInfo.Direction.normalized
        );
    }

    private void InstantiateBody(Vector3 position, Quaternion rotation, Vector3 torqueAxis, Vector3 velocityDir)
    {
        var rb = Instantiate(bodyPrefab, position, rotation);
        rb.AddForce(speedOnDeath * velocityDir, ForceMode.VelocityChange);
        rb.AddTorque(transform.right * rotationalSpeedOnDeath, ForceMode.VelocityChange);
    }
}