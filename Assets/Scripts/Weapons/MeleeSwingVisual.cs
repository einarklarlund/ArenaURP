using FishNet;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drives the _SwingProgress property on the melee swing's material instance.
/// SRP Batcher compatible: each renderer gets its own material instance with
/// properties in the UnityPerMaterial CBUFFER — no MaterialPropertyBlock needed.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class MeleeSwingVisual : MonoBehaviour
{
    private static readonly int SwingProgressID = Shader.PropertyToID("_SwingProgress");
    private static readonly int ZTestID = Shader.PropertyToID("_ZTest");

    [SerializeField] private int renderQueue = 3999;
    [SerializeField] private CompareFunction zTest = CompareFunction.Always;

    private Renderer _renderer;
    private Material _material;
    private Bullet _bullet;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _material = _renderer.material;
        _bullet = GetComponentInParent<Bullet>();
    }

    private void Start()
    {
        if (_material != null)
        {
            _material.renderQueue = renderQueue;
            _material.SetInt(ZTestID, (int)zTest);
        }
    }

    private void Update()
    {
        if (_bullet == null || _material == null) return;

        float elapsed = (float)InstanceFinder.TimeManager.TimePassed(_bullet.data.PreciseTick);
        float progress = Mathf.Clamp01(elapsed / Mathf.Max(_bullet.Lifetime, 0.001f));
        _material.SetFloat(SwingProgressID, progress);
    }

    private void OnDestroy()
    {
        if (_material != null)
            Destroy(_material);
    }
}
