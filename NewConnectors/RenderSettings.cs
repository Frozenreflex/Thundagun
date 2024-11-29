#region

using System;
using System.Collections.Generic;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using TextureFormat = Elements.Assets.TextureFormat;

#endregion

namespace Thundagun.NewConnectors;

public class RenderSettings
{
    public CameraClearMode clear;

    public colorX clearColor;

    public Func<Bitmap2D, Bitmap2D> customPostProcess;

    public List<GameObject> excludeObjects;

    public float far;

    public float fov;

    public float near;

    public float ortographicSize;
    public float3 position;

    public bool postProcesing;

    public CameraProjection projection;

    public List<GameObject> renderObjects;

    public bool renderPrivateUI;

    public floatQ rotation;

    public bool screenspaceReflections;

    public int2 size;

    public TextureFormat textureFormat;
}