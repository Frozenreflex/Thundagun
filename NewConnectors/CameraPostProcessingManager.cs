#region

using System.Linq;
using AmplifyOcclusion;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityFrooxEngineRunner;
using Camera = UnityEngine.Camera;

#endregion

namespace Thundagun.NewConnectors;

public class CameraPostprocessingManager : MonoBehaviour
{
    private static PostProcessResources _resources;
    private static PostProcessProfile _baseProfile;
    public MotionBlur _motionBlur;
    public Bloom _bloom;
    public AmplifyOcclusionEffect _ao;
    public ScreenSpaceReflections _ssr;
    private CTAA_PC _ctaaPC;
    private CTAAVR_SPS _ctaaVR;
    private PostProcessLayer _postProcessing;

    public Camera Camera { get; private set; }

    public bool IsPrimary { get; private set; }

    public bool IsVR { get; private set; }

    private void OnDestroy()
    {
        Settings.UnregisterValueChanges(new SettingUpdateHandler<PostProcessingSettings>(OnPostProcessingChanged));
        RemovePostProcessing();
    }

    public void Initialize(Camera camera, CameraSettings settings)
    {
        Camera = camera;
        IsPrimary = settings.IsPrimary;
        IsVR = settings.IsVR;
        InitializePostProcessing();
        if (IsPrimary)
        {
            FrooxEngineBootstrap.RunPostInitAction(() =>
                Settings.RegisterValueChanges<PostProcessingSettings>(OnPostProcessingChanged));
        }
        else
        {
            _motionBlur.enabled.value = settings.MotionBlur && !IsVR;
            _ssr.enabled.value = settings.ScreenSpaceReflection;
            UpdateAA(AntiAliasingMethod.FXAA);
        }
    }

    public void UpdatePostProcessing(bool enabled, bool motionBlur, bool screenspaceReflections)
    {
        if (_postProcessing == null) return;

        _postProcessing.enabled = enabled;
        if (_ao != null) _ao.enabled = enabled;
        if (!enabled) return;

        if (_motionBlur != null) _motionBlur.enabled.value = motionBlur;
        if (_ssr == null) return;

        _ssr.enabled.value = screenspaceReflections;
    }

    private void OnPostProcessingChanged(PostProcessingSettings settings)
    {
        Thundagun.QueuePacket(new OnPostProcessingChangedPacket(this, settings));
    }

    private void InitializePostProcessing()
    {
        _postProcessing = gameObject.GetComponent<PostProcessLayer>();
        if (_postProcessing == null) AddPostProcessing();
        if (_ao == null) AddAO();
        _motionBlur = _postProcessing.defaultProfile.GetSetting<MotionBlur>();
        _bloom = _postProcessing.defaultProfile.GetSetting<Bloom>();
        _ssr = _postProcessing.defaultProfile.GetSetting<ScreenSpaceReflections>();
    }

    private void AddPostProcessing()
    {
        if (_resources == null) _resources = Resources.Load<PostProcessResources>("PostProcessResources");
        _postProcessing = gameObject.AddComponent<PostProcessLayer>();
        _postProcessing.Init(_resources);
        if (_baseProfile == null) _baseProfile = Resources.Load<PostProcessProfile>("PostProcessing_V2");
        var postProcessProfile = Instantiate(_baseProfile);
        postProcessProfile.settings.Clear();
        foreach (var setting in _baseProfile.settings.Where(setting => !IsPrimary || setting is not ColorGrading))
            postProcessProfile.settings.Add(Instantiate(setting));
        _postProcessing.defaultProfile = postProcessProfile;
    }

    private void AddAO()
    {
        _ao = gameObject.AddComponent<AmplifyOcclusionEffect>();
        _ao.PerPixelNormals = AmplifyOcclusionBase.PerPixelNormalSource.None;
        _ao.Radius = 4f;
        _ao.PowerExponent = 0.6f;
        _ao.SampleCount = SampleCountLevel.Low;
        if (!IsPrimary)
        {
            _ao.FilterEnabled = false;
            _ao.BlurPasses = 4;
            _ao.BlurRadius = 4;
            _ao.BlurSharpness = 3f;
        }
        else
        {
            _ao.BlurPasses = 2;
            _ao.BlurSharpness = 3f;
            _ao.BlurRadius = 4;
            _ao.FilterResponse = 0.9f;
            _ao.FilterBlending = 0.25f;
        }
    }

    public void UpdateAA(AntiAliasingMethod method)
    {
        switch (method)
        {
            case AntiAliasingMethod.Off:
            case AntiAliasingMethod.CTAA:
                _postProcessing.antialiasingMode = PostProcessLayer.Antialiasing.None;
                _postProcessing.finalBlitToCameraTarget = false;
                break;
            case AntiAliasingMethod.FXAA:
                _postProcessing.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;
                _postProcessing.fastApproximateAntialiasing.keepAlpha = !IsPrimary;
                _postProcessing.finalBlitToCameraTarget = !IsPrimary;
                break;
            case AntiAliasingMethod.SMAA:
                _postProcessing.antialiasingMode = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
                _postProcessing.subpixelMorphologicalAntialiasing.quality =
                    SubpixelMorphologicalAntialiasing.Quality.High;
                _postProcessing.finalBlitToCameraTarget = !IsPrimary;
                break;
            case AntiAliasingMethod.TAA:
                _postProcessing.antialiasingMode = PostProcessLayer.Antialiasing.TemporalAntialiasing;
                _postProcessing.finalBlitToCameraTarget = !IsPrimary;
                break;
        }

        if (method == AntiAliasingMethod.CTAA)
        {
            if (IsVR)
            {
                if (_ctaaVR != null) return;

                _ctaaVR = gameObject.AddComponent<CTAAVR_SPS>();
                _ctaaVR.AdaptiveSharpness = 0.2f;
                _ctaaVR.SharpnessEnabled = false;
                _ctaaVR.TemporalEdgePower = 4f;
            }
            else
            {
                if (_ctaaPC != null) return;

                _ctaaPC = gameObject.AddComponent<CTAA_PC>();
                _ctaaPC.AdaptiveSharpness = 0.2f;
            }
        }
        else
        {
            RemoveCTAA();
        }
    }

    private void RemoveCTAA()
    {
        if (_ctaaPC != null) Destroy(_ctaaPC);
        if (_ctaaVR != null) Destroy(_ctaaVR);
        _ctaaPC = null;
        _ctaaVR = null;
    }

    public void RemovePostProcessing()
    {
        if (_postProcessing != null)
        {
            Destroy(_postProcessing.defaultProfile);
            Destroy(_postProcessing);
            _postProcessing = null;
        }

        RemoveCTAA();
        if (_ao == null) return;

        Destroy(_ao);
        _ao = null;
    }
}

public class OnPostProcessingChangedPacket : UpdatePacket<CameraPostprocessingManager>
{
    private readonly float _ambientOcclusionIntensity;
    private readonly AntiAliasingMethod _antialiasing;
    private readonly float _bloomIntensity;
    private readonly float _motionBlurIntensity;
    private readonly bool _screenSpaceReflections;

    public OnPostProcessingChangedPacket(CameraPostprocessingManager owner, PostProcessingSettings settings) :
        base(owner)
    {
        _motionBlurIntensity = settings.MotionBlurIntensity;
        _bloomIntensity = settings.BloomIntensity;
        _ambientOcclusionIntensity = settings.AmbientOcclusionIntensity;
        _screenSpaceReflections = settings.ScreenSpaceReflections;
        _antialiasing = settings.Antialiasing;
    }

    public override void Update()
    {
        if (Owner == null) return;

        if (Owner._motionBlur != null)
        {
            Owner._motionBlur.enabled.value = !Owner.IsVR && !MathX.Approximately(_motionBlurIntensity, 0.0f);
            Owner._motionBlur.shutterAngle.value = _motionBlurIntensity * 360f;
        }

        if (Owner._bloom != null)
        {
            Owner._bloom.enabled.value = !MathX.Approximately(_bloomIntensity, 0.0f);
            Owner._bloom.intensity.value = _bloomIntensity;
        }

        if (Owner._ao != null)
        {
            Owner._ao.enabled = !MathX.Approximately(_ambientOcclusionIntensity, 0.0f);
            Owner._ao.Intensity = _ambientOcclusionIntensity;
        }

        if (Owner._ssr != null) Owner._ssr.enabled.value = _screenSpaceReflections;
        Owner.UpdateAA(_antialiasing);
    }
}