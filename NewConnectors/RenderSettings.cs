using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Thundagun.NewConnectors;

public class RenderSettings
{
	public float3 position;

	public floatQ rotation;

	public int2 size;

	public Elements.Assets.TextureFormat textureFormat;

	public CameraProjection projection;

	public float fov;

	public float ortographicSize;

	public CameraClearMode clear;

	public colorX clearColor;

	public float near;

	public float far;

	public List<GameObject> renderObjects;

	public List<GameObject> excludeObjects;

	public bool renderPrivateUI;

	public bool postProcesing;

	public bool screenspaceReflections;

	public Func<Bitmap2D, Bitmap2D> customPostProcess;
}
