#region

using System;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;
using Object = UnityEngine.Object;
using RenderTexture = UnityEngine.RenderTexture;
using TextureWrapMode = FrooxEngine.TextureWrapMode;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public class RenderTextureConnector :
    AssetConnector,
    IRenderTextureConnector,
    IUnityTextureProvider
{
    public RenderTexture RenderTexture { get; private set; }
    public int2 Size { get; private set; }

    public bool HasAlpha { get; }

    public override void Unload()
    {
        var _tex = RenderTexture;
        RenderTexture = null;
        UnityAssetIntegrator.EnqueueProcessing(() =>
        {
            if (!(_tex != null))
                return;
            if (!_tex)
                return;

            Object.Destroy(_tex);
        }, true);
    }

    public void Update(
        int2 size,
        int depth,
        TextureFilterMode filterMode,
        int anisoLevel,
        TextureWrapMode wrapU,
        TextureWrapMode wrapV,
        Action onUpdated)
    {
        UnityAssetIntegrator.EnqueueProcessing(() =>
        {
            size = MathX.Clamp(in size, 4, 8192);
            Size = size;
            Unload();
            RenderTexture = new RenderTexture(Size.x, Size.y, depth, RenderTextureFormat.ARGBHalf);
            RenderTexture.Create();
            if (filterMode == TextureFilterMode.Anisotropic)
            {
                RenderTexture.filterMode = FilterMode.Trilinear;
                RenderTexture.anisoLevel = anisoLevel;
            }
            else
            {
                RenderTexture.filterMode = filterMode.ToUnity();
                RenderTexture.anisoLevel = 0;
            }

            RenderTexture.wrapModeU = wrapU.ToUnity();
            RenderTexture.wrapModeV = wrapV.ToUnity();
            onUpdated();
        }, false);
    }

    public Texture UnityTexture => RenderTexture;
}