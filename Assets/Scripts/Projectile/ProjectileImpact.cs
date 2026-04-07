using FishNet;
using FishNet.Object;
using UnityEngine;

public abstract class ProjectileImpact : NetworkBehaviour
{
    protected ProjectileData data;

    protected virtual void Awake()
    {
        data = GetComponent<ProjectileData>();
    }

    private void OnEnable()
    {
        ShowVisuals();
    }

    protected void SpawnPrefab(NetworkObject prefab)
    {
        NetworkObject nob = InstanceFinder.NetworkManager.GetPooledInstantiated
        (
            prefab,
            transform.position,
            transform.rotation,
            false
        );

        InstanceFinder.ServerManager.Spawn(nob, Owner);
    }

    protected void ActivateVfx()
    {
        foreach (var vfx in data.impactVfx)
        {
            Instantiate(vfx, transform.position, transform.rotation);
        }
    }

    protected void HideVisuals()
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>())
            renderer.enabled = false;
        foreach (var light in GetComponentsInChildren<Light>())
            light.enabled = false;
    }

    protected void ShowVisuals()
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>())
            renderer.enabled = true;
        foreach (var light in GetComponentsInChildren<Light>())
            light.enabled = true;
    }
}
