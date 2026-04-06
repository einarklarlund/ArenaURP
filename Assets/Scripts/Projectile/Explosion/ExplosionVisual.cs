using FishNet;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class ExplosionVisual : MonoBehaviour
{
    private static readonly int ProgressID = Shader.PropertyToID("_Progress");

    private Renderer _renderer;
    private Material _material;
    private ProjectileData _projectileData;
    private ProjectileState _projectileState;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _material = _renderer.material;
        _projectileData = GetComponentInParent<ProjectileData>();
        _projectileState = GetComponentInParent<ProjectileState>();
    }

    private void Update()
    {
        if (_projectileState == null || _material == null) return;

        float elapsed = (float)InstanceFinder.TimeManager.TimePassed(_projectileState.PreciseTick);
        float progress = Mathf.Clamp01(elapsed / Mathf.Max(_projectileData.Lifetime, 0.001f));
        _material.SetFloat(ProgressID, progress);
    }

    private void OnDestroy()
    {
        if (_material != null)
            Destroy(_material);
    }
}
