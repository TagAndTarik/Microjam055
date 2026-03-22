using UnityEngine;

[ExecuteInEditMode]
public class ScreenWaveEffect : MonoBehaviour
{
    private static readonly int OverlayTexId = Shader.PropertyToID("_OverlayTex");
    private static readonly int OverlayEnabledId = Shader.PropertyToID("_OverlayEnabled");

    public Material material;
    public Texture overlayTexture;

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (material != null)
        {
            if (material.HasProperty(OverlayTexId))
                material.SetTexture(OverlayTexId, overlayTexture);

            if (material.HasProperty(OverlayEnabledId))
                material.SetFloat(OverlayEnabledId, overlayTexture != null ? 1f : 0f);

            Graphics.Blit(src, dest, material);
        }
        else
        {
            Graphics.Blit(src, dest);
        }
    }

    public void SetProperties(ScreenEffectSettings settings)
    {
        material.SetFloat("_Intensity", settings.GlitchIntensity);
        material.SetFloat("_ScanlineStrength", settings.ScanlineStrength);
        material.SetFloat("_ColorOffset", settings.ColorOffset);
    }

    public void SetOverlayTexture(Texture textureToComposite)
    {
        overlayTexture = textureToComposite;
    }
}
