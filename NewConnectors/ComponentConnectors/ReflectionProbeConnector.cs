#region

using System.Collections.Generic;
using System.Threading.Tasks;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityFrooxEngineRunner;
using ReflectionProbe = FrooxEngine.ReflectionProbe;
using RenderTexture = UnityEngine.RenderTexture;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class ReflectionProbeConnector : ComponentConnector<ReflectionProbe, IReflectionProbeConnector>,
    IReflectionProbeConnector
{
    public UnityEngine.ReflectionProbe probe;

    public GameObject probeGO;

    public override void ApplyChanges()
    {
        Thundagun.QueuePacket(new ApplyChangesReflectionProbeConnector(this));
    }

    public Task<BitmapCube> Render(List<Slot> excludeList)
    {
        var completionSource = new TaskCompletionSource<BitmapCube>();
        ((UnityAssetIntegrator)Engine.AssetManager.Connector).EnqueueTask(delegate
        {
            var reflectionProbeRenderer = probeGO.AddComponent<ReflectionProbeRenderer>();
            reflectionProbeRenderer.probe = probe;
            reflectionProbeRenderer.task = completionSource;
            var value = Owner.Resolution.Value;
            var desc = new RenderTextureDescriptor(value, value,
                Owner.HDR.Value ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R8G8B8A8_UNorm, 24, -1)
            {
                useMipMap = true,
                dimension = TextureDimension.Cube,
                autoGenerateMips = false
            };
            var temporary = RenderTexture.GetTemporary(desc);
            reflectionProbeRenderer.previousLayers = Pool.BorrowDictionary<GameObject, int>();
            RenderHelper.SetHiearchyLayer(excludeList, LayerMask.NameToLayer("Temp"),
                reflectionProbeRenderer.previousLayers);
            reflectionProbeRenderer.texture = temporary;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            reflectionProbeRenderer.renderId = probe.RenderProbe(temporary);
        });
        return completionSource.Task;
    }

    public override IUpdatePacket InitializePacket()
    {
        return new InitializeReflectionProbeConnector(this, Owner);
    }

    public override void DestroyMethod(bool destroyingWorld)
    {
        if (!destroyingWorld && probeGO) Object.Destroy(probeGO);
        probeGO = null;
        base.DestroyMethod(destroyingWorld);
    }
}

public class InitializeReflectionProbeConnector : InitializeComponentConnector<ReflectionProbe,
    IReflectionProbeConnector, ReflectionProbeConnector>
{
    public InitializeReflectionProbeConnector(ReflectionProbeConnector connector, ReflectionProbe component) : base(
        connector, component)
    {
    }

    public override void Update()
    {
        base.Update();
        Owner.probeGO = new GameObject("");
        Owner.probeGO.transform.SetParent(Owner.AttachedGameObject.transform, false);
        Owner.probeGO.layer = Owner.AttachedGameObject.layer;
        Owner.probe = Owner.probeGO.AddComponent<UnityEngine.ReflectionProbe>();
        Owner.probe.cullingMask = RenderHelper.PUBLIC_RENDER_MASK;
    }
}

public class ApplyChangesReflectionProbeConnector : UpdatePacket<ReflectionProbeConnector>
{
    public IUnityTextureProvider BakedTexture;
    public Color ProbeBackgroundColor;
    public float ProbeBlendDistance;
    public bool ProbeBoxProjection;
    public ReflectionProbeClearFlags ProbeClearFlags;
    public float ProbeFarClipPlane;
    public bool ProbeHdr;
    public int ProbeImportance;
    public float ProbeIntensity;
    public ReflectionProbeMode ProbeMode;
    public float ProbeNearClipPlane;
    public int ProbeResolution;
    public float ProbeShadowDistance;
    public Vector3 ProbeSize;
    public ReflectionProbeTimeSlicingMode ProbeTimeSlicingMode;
    public ReflectionProbeRefreshMode RefreshMode;

    public ApplyChangesReflectionProbeConnector(ReflectionProbeConnector owner) : base(owner)
    {
        switch (owner.Owner.ProbeType.Value)
        {
            case ReflectionProbe.Type.Baked:
                ProbeMode = ReflectionProbeMode.Custom;
                BakedTexture = owner.Owner.BakedCubemap?.Asset?.Connector as IUnityTextureProvider;
                break;
            case ReflectionProbe.Type.Realtime:
                ProbeMode = ReflectionProbeMode.Realtime;
                RefreshMode = ReflectionProbeRefreshMode.EveryFrame;
                BakedTexture = null;
                break;
        }

        ProbeImportance = owner.Owner.Importance.Value;
        ProbeIntensity = owner.Owner.Intensity.Value;
        ProbeBlendDistance = owner.Owner.BlendDistance.Value;
        ProbeBoxProjection = owner.Owner.BoxProjection.Value;
        ProbeSize = owner.Owner.BoxSize.Value.ToUnity();
        ProbeTimeSlicingMode = owner.Owner.TimeSlicing.Value.ToUnity();
        ProbeResolution = MathX.Clamp(MathX.NearestPowerOfTwo(owner.Owner.Resolution.Value), 16, 2049);
        ProbeHdr = owner.Owner.HDR.Value;
        ProbeShadowDistance = owner.Owner.ShadowDistance.Value;
        ProbeClearFlags = owner.Owner.ClearFlags.Value switch
        {
            ReflectionProbe.Clear.Skybox => ReflectionProbeClearFlags.Skybox,
            ReflectionProbe.Clear.Color => ReflectionProbeClearFlags.SolidColor,
            _ => ReflectionProbeClearFlags.SolidColor
        };
        ProbeBackgroundColor = owner.Owner.BackgroundColor.Value.ToUnity(ColorProfile.sRGB);
        ProbeNearClipPlane = owner.Owner.NearClip.Value;
        ProbeFarClipPlane = owner.Owner.FarClip.Value;
    }

    public override void Update()
    {
        var probe = Owner.probe;
        probe.mode = ProbeMode;
        if (RefreshMode != default) probe.refreshMode = RefreshMode;
        probe.customBakedTexture = BakedTexture?.UnityTexture;
        probe.importance = ProbeImportance;
        probe.intensity = ProbeIntensity;
        probe.blendDistance = ProbeBlendDistance;
        probe.boxProjection = ProbeBoxProjection;
        probe.size = ProbeSize;
        probe.timeSlicingMode = ProbeTimeSlicingMode;
        probe.resolution = ProbeResolution;
        probe.hdr = ProbeHdr;
        probe.shadowDistance = ProbeShadowDistance;
        probe.clearFlags = ProbeClearFlags;
        probe.backgroundColor = ProbeBackgroundColor;
        probe.nearClipPlane = ProbeNearClipPlane;
        probe.farClipPlane = ProbeFarClipPlane;
    }
}