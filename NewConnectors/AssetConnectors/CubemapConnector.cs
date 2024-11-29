#region

using System;
using System.Collections;
using System.Collections.Generic;
using Elements.Assets;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;
using Cubemap = UnityEngine.Cubemap;
using Object = UnityEngine.Object;
using TextureFormat = Elements.Assets.TextureFormat;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public class CubemapConnector : AssetConnector, IUnityTextureProvider
{
    public Cubemap UnityCubemap { get; private set; }

    public Texture UnityTexture => UnityCubemap;

    public void SetCubemapData(BitmapCube data, AssetIntegrated onSet)
    {
        var colors = new List<Color[]>();
        for (var mip = 0; mip < data.MipMapLevels; ++mip)
        {
            var length = data.MipmapTotalPixels(mip) / 6;
            for (var index1 = 0; index1 < 6; ++index1)
            {
                var num = data.PixelStart(0, 0, (BitmapCube.Face)index1, mip);
                var colorArray = new Color[length];
                for (var index2 = 0; index2 < length; ++index2)
                    colorArray[index2] = data.DecodePixel(num + index2).ToUnity();
                colors.Add(colorArray);
            }
        }

        UnityAssetIntegrator.EnqueueProcessing(UploadTextureData(data, colors, onSet), Asset.HighPriorityIntegration);
    }

    public void SetCubemapData(BitmapCube data, int startMipLevel, AssetIntegrated onSet)
    {
        throw new NotImplementedException();
    }

    public void SetCubemapFormat(
        int size,
        int mipmaps,
        TextureFormat format,
        AssetIntegrated onDone)
    {
        throw new NotImplementedException();
    }

    public override void Unload()
    {
        UnityAssetIntegrator.EnqueueProcessing(Destroy, true);
    }

    private void Destroy()
    {
        if (UnityCubemap)
            Object.Destroy(UnityCubemap);
        UnityCubemap = null;
    }

    private IEnumerator UploadTextureData(
        BitmapCube data,
        List<Color[]> colors,
        AssetIntegrated onDone)
    {
        var cubemapConnector = this;
        var unity = data.Format.ToUnity();
        cubemapConnector.Destroy();
        cubemapConnector.UnityCubemap = new Cubemap(data.Size.x, unity, data.HasMipMaps);
        yield return null;
        var dataIndex = 0;
        for (var mip = 0; mip < data.MipMapLevels; ++mip)
        for (var f = 0; f < 6; ++f)
        {
            var face = (BitmapCube.Face)f;
            cubemapConnector.UnityCubemap.SetPixels(colors[dataIndex++], face.ToUnity(), mip);
            yield return null;
        }

        cubemapConnector.UnityCubemap.Apply(false, true);
        onDone(true);
        cubemapConnector.Engine.TextureUpdated();
    }
}