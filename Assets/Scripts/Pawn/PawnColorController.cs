using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FishNet.Demo.AdditiveScenes;
using FishNet.Object;
using UnityEngine;

public class PawnColorController : NetworkBehaviour
{
    [SerializeField] private MeshRenderer[] meshRenderers;

    [SerializeField] private Pawn pawn;

    private Color[] defaultColors;
    private Color[] defaultEmissionColors;
    private float targetColorChangedTime;
    private float baseColorChangedTime;
    private float baseColorChangeDuration;
    private float targetColorChangeDuration;
    private Color targetColor;
    private Color baseColor;

    private void Start()
    {
        baseColorChangeDuration = -1;
        targetColorChangeDuration = -1;
        targetColorChangedTime = 0;
        baseColorChangedTime = 0;
        defaultColors = new Color[meshRenderers.Count()];
        defaultEmissionColors = new Color[meshRenderers.Count()];
        for(int i = 0; i < meshRenderers.Count(); i++)
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

        pawn.Health.OnChange +=
            (prev, next, asServer) => SetColor(Color.red, 0.3f)
        ;
    }

    public void SetColor(Color color, float forSeconds)
    {
        targetColorChangedTime = Time.time;
        targetColorChangeDuration = forSeconds;
        targetColor = color;
    }

    public void SetBaseColor(Color color, float forSeconds)
    {
        baseColorChangedTime = Time.time;
        baseColorChangeDuration = forSeconds;
        baseColor = color;
    }

    // Update is called once per frame
    void Update()
    {
        float timeSinceTargetColorChanged = Time.time - targetColorChangedTime;
        float timeSinceBaseColorChanged = Time.time - baseColorChangedTime;
        if
        (
            timeSinceTargetColorChanged >= targetColorChangeDuration
            && timeSinceBaseColorChanged >= baseColorChangeDuration
        )
        {
            for(int i = 0; i < meshRenderers.Count(); i++)
            {
                var meshRenderer = meshRenderers[i];
                Color albedoColor = defaultColors[i];
                Color emissionColor = defaultEmissionColors[i];
                meshRenderer.material.SetColor("_BaseColor", albedoColor);

                if (meshRenderer.material.HasColor("_Emission"))
                {
                    meshRenderer.material.SetColor("_Emission", emissionColor);
                }
                else
                {
                    meshRenderer.material.SetColor("_EmissionColor", emissionColor);
                }        
            }
            return;
        }

        for(int i = 0; i < meshRenderers.Count(); i++)
        {
            var meshRenderer = meshRenderers[i];
            Color albedoColor = defaultColors[i];
            Color emissionColor = defaultEmissionColors[i];
            if (timeSinceBaseColorChanged < baseColorChangeDuration)
            {
                albedoColor = Color.Lerp(baseColor, albedoColor, timeSinceBaseColorChanged / baseColorChangeDuration);
                emissionColor = Color.Lerp(baseColor, emissionColor, timeSinceBaseColorChanged / baseColorChangeDuration);
            }
            if (timeSinceTargetColorChanged < targetColorChangeDuration)
            {
                albedoColor = Color.Lerp(targetColor, albedoColor, timeSinceTargetColorChanged / targetColorChangeDuration);
                emissionColor = Color.Lerp(targetColor, emissionColor, timeSinceTargetColorChanged / targetColorChangeDuration);
            }
            meshRenderer.material.SetColor("_BaseColor", albedoColor);

            if (meshRenderer.material.HasColor("_Emission"))
            {
                meshRenderer.material.SetColor("_Emission", emissionColor);
            }
            else
            {
                meshRenderer.material.SetColor("_EmissionColor", emissionColor);
            }        
        }
    }
    
}
