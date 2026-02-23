using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class PixelateEffect : MonoBehaviour
{
    [Range(2, 512)]
    public int pixelSize = 64;

    private Material material;

    void Awake()
    {
        Shader shader = Shader.Find("Hidden/Pixelate");
        if (shader == null)
        {
            Debug.LogError("Pixelate shader not found. Make sure Pixelate.shader is in the project.");
            enabled = false;
            return;
        }
        material = new Material(shader);
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (material == null)
        {
            Graphics.Blit(src, dest);
            return;
        }

        material.SetFloat("_PixelSize", pixelSize);
        Graphics.Blit(src, dest, material);
    }
}