using FishNet.Object;
using UnityEngine;

/// <summary>
/// Thin glue MonoBehaviour around <see cref="PawnColorProcessor"/>.
/// Builds a per-tick <see cref="PawnColorInput"/> from <see cref="Pawn"/>
/// health changes, advances the state machine each Update, and writes
/// the resolved color to each mesh renderer's albedo + emission slot.
///
/// All non-trivial logic lives in the Core processor and is covered by
/// edit-mode tests; this class only handles Unity-side wiring.
/// </summary>
public class PawnColor : NetworkBehaviour
{
    [SerializeField] private MeshRenderer[] meshRenderers;

    [SerializeField] private Pawn pawn;

    [SerializeField] private PawnColorConfig config = PawnColorConfig.Default;

    private Color[] defaultColors;
    private Color[] defaultEmissionColors;

    private PawnColorState state = PawnColorState.Default;

    // One-shot input flag set by the OnChange callback and consumed by Update.
    private bool hitOccurredThisFrame;

    private void Start()
    {
        state = PawnColorState.Default;

        defaultColors = new Color[meshRenderers.Length];
        defaultEmissionColors = new Color[meshRenderers.Length];
        for (int i = 0; i < meshRenderers.Length; i++)
        {
            defaultColors[i] = meshRenderers[i].material.color;
            if (meshRenderers[i].material.HasColor("_Emission"))
            {
                defaultEmissionColors[i] = meshRenderers[i].material.GetColor("_Emission");
            }
            else
            {
                defaultEmissionColors[i] = meshRenderers[i].material.GetColor("_EmissionColor");
            }
        }

        pawn.Health.OnChange += OnHealthChanged;
    }

    private void OnDestroy()
    {
        if (pawn != null)
        {
            pawn.Health.OnChange -= OnHealthChanged;
        }
    }

    private void OnHealthChanged(int prev, int next, bool asServer)
    {
        hitOccurredThisFrame = true;
    }

    private void Update()
    {
        PawnColorInput input = PawnColorInput.Default;
        if (hitOccurredThisFrame)
        {
            input = PawnColorInput.Hit(config.HitColor, config.HitDurationSeconds);
            hitOccurredThisFrame = false;
        }

        state = PawnColorProcessor.Process(state, in config, in input, Time.deltaTime);

        // Fast path: both timelines expired => write defaults and bail.
        // Matches the original early-out in PawnColor.Update.
        if (!state.IsActive)
        {
            for (int i = 0; i < meshRenderers.Length; i++)
            {
                ApplyColors(meshRenderers[i], defaultColors[i], defaultEmissionColors[i]);
            }
            return;
        }

        for (int i = 0; i < meshRenderers.Length; i++)
        {
            Color albedo = PawnColorProcessor.Resolve(in state, defaultColors[i]);
            Color emission = PawnColorProcessor.Resolve(in state, defaultEmissionColors[i]);
            ApplyColors(meshRenderers[i], albedo, emission);
        }
    }

    private static void ApplyColors(MeshRenderer renderer, Color albedo, Color emission)
    {
        renderer.material.SetColor("_BaseColor", albedo);
        if (renderer.material.HasColor("_Emission"))
        {
            renderer.material.SetColor("_Emission", emission);
        }
        else
        {
            renderer.material.SetColor("_EmissionColor", emission);
        }
    }
}
