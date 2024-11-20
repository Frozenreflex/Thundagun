#region

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Elements.Assets;
using Elements.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityFrooxEngineRunner;
using Rect = UnityEngine.Rect;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class ReflectionProbeRenderer : MonoBehaviour
{
    public ReflectionProbe probe;

    public RenderTexture texture;

    public int renderId;

    public ReflectionProbeConnector connector;

    public Dictionary<GameObject, int> previousLayers;

    public TaskCompletionSource<BitmapCube> task;

    private void Update()
    {
        if (!probe.IsFinishedRendering(renderId)) return;

        try
        {
            FinishRender();
        }
        catch (Exception exception)
        {
            task?.TrySetException(exception);
            Cleanup();
        }
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void FinishRender()
    {
        if (connector?.Owner != null) probe.timeSlicingMode = connector.Owner.TimeSlicing.Value.ToUnity();
        var desc = texture.descriptor;
        desc.dimension = TextureDimension.Tex2D;
        var baseSize = desc.width;
        var list = new List<RenderTexture>();
        var list2 = new List<Texture2D>();
        var v = int2.One;
        var miplevels = Bitmap2DBase.MipmapLevelCount(v * desc.width);
        for (var i = 0; i < miplevels; i++)
        {
            list.Add(RenderTexture.GetTemporary(desc));
            list2.Add(new Texture2D(desc.width, desc.height, desc.graphicsFormat, TextureCreationFlags.None));
            desc.useMipMap = false;
            desc.width /= 2;
            desc.height /= 2;
        }

        var faceData = new List<byte[]>();
        for (var j = 0; j < 6; j++)
        {
            Graphics.CopyTexture(texture, j, list[0], 0);
            for (var k = 0; k < miplevels; k++)
            {
                if (k > 0) Graphics.CopyTexture(list[0], 0, k, list[k], 0, 0);
                var texture2D = list2[k];
                var active = RenderTexture.active;
                RenderTexture.active = list[k];
                texture2D.ReadPixels(new Rect(0f, 0f, texture2D.width, texture2D.height), 0, 0, false);
                RenderTexture.active = active;
                faceData.Add(texture2D.GetRawTextureData());
            }
        }

        for (var l = 0; l < miplevels; l++)
        {
            RenderTexture.ReleaseTemporary(list[l]);
            Destroy(list2[l]);
        }

        var _task = task;
        Task.Run(delegate
        {
            try
            {
                var bitmapCube = new BitmapCube(baseSize, baseSize, desc.graphicsFormat.ToEngine(), true,
                    desc.graphicsFormat.ToEngineProfile());
                var num = 0;
                for (var m = 0; m < 6; m++)
                for (var n = 0; n < miplevels; n++)
                {
                    var destinationIndex =
                        bitmapCube.FaceByteOrigin((BitmapCube.Face)m) + bitmapCube.MipmapByteOrigin(n);
                    var array = faceData[num++];
                    Array.Copy(array, 0, bitmapCube.RawData, destinationIndex, array.Length);
                }

                _task.SetResult(bitmapCube);
            }
            catch (Exception exception)
            {
                _task?.TrySetException(exception);
            }
        });
        Cleanup();
        Destroy(this);
    }

    private void Cleanup()
    {
        if (texture != null) RenderTexture.ReleaseTemporary(texture);
        if (previousLayers != null)
        {
            RenderHelper.RestoreLayers(previousLayers);
            previousLayers = null;
        }

        probe = null;
        connector = null;
        task = null;
        texture = null;
    }
}