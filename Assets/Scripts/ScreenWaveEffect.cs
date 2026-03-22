using UnityEngine;

[ExecuteInEditMode]
public class ScreenWaveEffect : MonoBehaviour
{
    public Material material;

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (material != null)
            Graphics.Blit(src, dest, material);
        else
            Graphics.Blit(src, dest);
    }

    public void SetProperties(ScreenEffectSettings settings)
    {
        material.SetFloat("_Intensity", settings.GlitchIntensity);
        material.SetFloat("_ScanlineStrength", settings.ScanlineStrength);
        material.SetFloat("_ColorOffset", settings.ColorOffset);

    }
}