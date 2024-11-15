using System.Collections.Generic;
using System.Linq;
using Elements.Core;
using UnityEngine;
using UnityFrooxEngineRunner;
using Camera = FrooxEngine.Camera;
using Rect = UnityEngine.Rect;
using RenderTextureConnector = Thundagun.NewConnectors.AssetConnectors.RenderTextureConnector;

namespace Thundagun.NewConnectors.ComponentConnectors;

public class CameraConnector : ComponentConnectorSingle<Camera>
{
    public bool PostprocessingSetup;
    public bool ScreenspaceReflections;
    public bool MotionBlur;
    public GameObject CameraGo;
    public CameraRenderEx RenderEx;
    public static int LayerMask;
    public static int PrivateLayerMask;
    public UnityEngine.Camera UnityCamera { get; set; }

    public override IUpdatePacket InitializePacket() => new InitializeCameraConnector(this);

    public override void ApplyChanges() => Thundagun.QueuePacket(new ApplyChangesCameraConnector(this));

    public override void DestroyMethod(bool destroyingWorld)
    {
        RenderEx?.Deinitialize();
        if (!destroyingWorld && CameraGo)
            Object.Destroy(CameraGo);
        base.DestroyMethod(destroyingWorld);
    }
}

public class InitializeCameraConnector : InitializeComponentConnectorSingle<Camera, CameraConnector>
{
    public InitializeCameraConnector(CameraConnector owner) : base(owner, owner.Owner)
    {
        if (CameraConnector.LayerMask == 0) CameraConnector.LayerMask = RenderHelper.PUBLIC_RENDER_MASK;
        if (CameraConnector.PrivateLayerMask == 0) CameraConnector.PrivateLayerMask = RenderHelper.PRIVATE_RENDER_MASK;
    }

    public override void Update()
    {
        base.Update();
        Owner.CameraGo = new GameObject("");
        Owner.CameraGo.transform.SetParent(Owner.AttachedGameObject.transform, false);
        Owner.UnityCamera = Owner.CameraGo.AddComponent<UnityEngine.Camera>();
        Owner.UnityCamera.allowHDR = true;
        Owner.UnityCamera.stereoTargetEye = StereoTargetEyeMask.None;
        Owner.RenderEx = Owner.CameraGo.AddComponent<CameraRenderEx>();
        Owner.RenderEx.Camera = Owner.UnityCamera;
        Owner.RenderEx.Engine = Owner.Engine;
        Owner.RenderEx.Owner = Owner;
        RenderConnector.RegisterUnityCamera(Owner.UnityCamera);
    }
}

public class ApplyChangesCameraConnector : UpdatePacket<CameraConnector>
{
    public bool Orthographic;
    public float FieldOfView;
    public float OrthographicSize;
    public bool UseTransformScale;
    public float NearClip;
    public float FarClip;
    public CameraClearFlags ClearFlags;
    public Color BackgroundColor;
    public Rect Rect;
    public float Depth;
    public RenderingPath RenderingPath;
    public bool RenderShadows;
    public bool PostprocessingSetup;
    public bool ScreenspaceReflections;
    public bool MotionBlur;
    public bool SetupPostProcessing;
    public bool RemovePostProcessing;
    public RenderTextureConnector Texture;
    public bool DoubleBuffer;
    public List<SlotConnector> SelectiveRender;
    public List<SlotConnector> ExcludeRender;
    public int CullingMask;
    public bool Active;

    public ApplyChangesCameraConnector(CameraConnector owner) : base(owner)
    {
        Orthographic = owner.Owner.Projection.Value == CameraProjection.Orthographic;
        FieldOfView = owner.Owner.FieldOfView;
        OrthographicSize = owner.Owner.OrthographicSize;
        UseTransformScale = owner.Owner.UseTransformScale;
        NearClip = owner.Owner.NearClipping;
        FarClip = owner.Owner.FarClipping;
        ClearFlags = owner.Owner.Clear.Value.ToUnity();
        BackgroundColor = owner.Owner.ClearColor.Value.ToUnity(ColorProfile.sRGB);
        Rect = owner.Owner.Viewport.Value.ToUnity();
        Depth = owner.Owner.Depth.Value;
        RenderingPath = owner.Owner.ForwardOnly.Value ? RenderingPath.Forward : RenderingPath.UsePlayerSettings;
        RenderShadows = owner.Owner.RenderShadows.Value;

        PostprocessingSetup = owner.Owner.Postprocessing;

        if (owner.Owner.Postprocessing != owner.PostprocessingSetup ||
            owner.Owner.ScreenSpaceReflections != owner.ScreenspaceReflections ||
            owner.Owner.MotionBlur != owner.MotionBlur)
        {
            ScreenspaceReflections = owner.Owner.ScreenSpaceReflections;
            MotionBlur = owner.Owner.MotionBlur;
            if (owner.Owner.Postprocessing) SetupPostProcessing = true;
            else RemovePostProcessing = true;
        }

        Texture = owner.Owner.RenderTexture?.Asset?.Connector as RenderTextureConnector;
        DoubleBuffer = owner.Owner.DoubleBuffered.Value;

        SelectiveRender = owner.Owner.SelectiveRender.Select(i => i?.Connector as SlotConnector).Where(i => i is not null).ToList();
        ExcludeRender = owner.Owner.ExcludeRender.Select(i => i?.Connector as SlotConnector).Where(i => i is not null).ToList();

        CullingMask = owner.Owner.RenderPrivateUI ? CameraConnector.PrivateLayerMask : CameraConnector.LayerMask;
        Active = owner.Owner.Enabled && owner.Owner.Slot.IsActive;
    }

    public override void Update()
    {
        Owner.UnityCamera.orthographic = Orthographic;
        Owner.UnityCamera.fieldOfView = FieldOfView;
        Owner.UnityCamera.orthographicSize = OrthographicSize;
        Owner.RenderEx.OrthographicSize = OrthographicSize;
        Owner.RenderEx.UseTransformScale = UseTransformScale;
        Owner.RenderEx.NearClip = NearClip;
        Owner.RenderEx.FarClip = FarClip;
        Owner.UnityCamera.clearFlags = ClearFlags;
        Owner.UnityCamera.backgroundColor = BackgroundColor;
        Owner.UnityCamera.rect = Rect;
        Owner.UnityCamera.depth = Depth;
        Owner.UnityCamera.renderingPath = RenderingPath;
        Owner.RenderEx.RenderShadows = RenderShadows;


        if (SetupPostProcessing || RemovePostProcessing)
        {
            Owner.UnityCamera.targetTexture = null;
            Owner.PostprocessingSetup = PostprocessingSetup;
            Owner.ScreenspaceReflections = ScreenspaceReflections;
            Owner.MotionBlur = MotionBlur;
            CameraSettings settings = new CameraSettings();
            settings.MotionBlur = Owner.MotionBlur;
            settings.ScreenSpaceReflection = Owner.ScreenspaceReflections;
            settings.SetupPostProcessing = true;
            if (SetupPostProcessing) CameraInitializer.SetupPostProcessing(Owner.UnityCamera, settings);
            else CameraInitializer.RemovePostProcessing(Owner.UnityCamera);
        }

        Owner.RenderEx.Texture = Texture?.RenderTexture;
        Owner.RenderEx.DoubleBuffer = DoubleBuffer && !PostprocessingSetup;
        Owner.RenderEx.SelectiveRender.Clear();
        Owner.RenderEx.ExcludeRender.Clear();
        Owner.RenderEx.SelectiveRender.AddRange(SelectiveRender.Select(i => i?.ForceGetGameObject()).Where(i => i is not null));
        Owner.RenderEx.ExcludeRender.AddRange(ExcludeRender.Select(i => i?.ForceGetGameObject()).Where(i => i is not null));

        Owner.UnityCamera.cullingMask = Owner.RenderEx.SelectiveRender.Count <= 0
            ? CullingMask
            : 1 << LayerMask.NameToLayer("Temp");
        Owner.UnityCamera.targetTexture = Owner.RenderEx.Texture;
        Owner.UnityCamera.enabled = Owner.UnityCamera.targetTexture != null && Active;
    }
}