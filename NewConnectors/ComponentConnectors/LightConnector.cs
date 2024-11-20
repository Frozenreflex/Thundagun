#region

using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;
using Light = FrooxEngine.Light;
using LightType = UnityEngine.LightType;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class LightConnector : ComponentConnectorSingle<Light>
{
    public GameObject LightGo;

    public UnityEngine.Light UnityLight { get; set; }

    public override IUpdatePacket InitializePacket()
    {
        return new InitializeLightConnector(this, Owner);
    }

    public override void ApplyChanges()
    {
        Thundagun.QueuePacket(new ApplyChangesLightConnector(this));
    }

    public override void DestroyMethod(bool destroyingWorld)
    {
        if (!destroyingWorld && LightGo)
            Object.Destroy(LightGo);
        LightGo = null;
        base.Destroy(destroyingWorld);
    }
}

public class InitializeLightConnector : InitializeComponentConnectorSingle<Light, LightConnector>
{
    public InitializeLightConnector(LightConnector connector, Light component) : base(connector, component)
    {
    }

    public override void Update()
    {
        base.Update();
        Owner.LightGo = new GameObject("");
        Owner.LightGo.transform.SetParent(Owner.AttachedGameObject.transform, false);
        Owner.LightGo.layer = Owner.AttachedGameObject.layer;
        Owner.UnityLight = Owner.LightGo.AddComponent<UnityEngine.Light>();
    }
}

public class ApplyChangesLightConnector : UpdatePacket<LightConnector>
{
    public Color Color;
    public IUnityTextureProvider Cookie;
    public float Intensity;
    public float Range;
    public float ShadowBias;
    public int ShadowCustomResolution;
    public float ShadowNearPlane;
    public float ShadowNormalBias;
    public LightShadows? Shadows;
    public float ShadowStrength;
    public bool ShouldBeEnabled;
    public float SpotAngle;
    public LightType? Type;

    public ApplyChangesLightConnector(LightConnector owner) : base(owner)
    {
        Type = owner.Owner.LightType.Value switch
        {
            FrooxEngine.LightType.Point => LightType.Point,
            FrooxEngine.LightType.Directional => LightType.Directional,
            FrooxEngine.LightType.Spot => LightType.Spot,
            _ => null
        };
        Shadows = owner.Owner.ShadowType.Value switch
        {
            ShadowType.None => LightShadows.None,
            ShadowType.Hard => LightShadows.Hard,
            ShadowType.Soft => LightShadows.Soft,
            _ => null
        };
        ShouldBeEnabled = owner.Owner.ShouldBeEnabled;
        if (!ShouldBeEnabled) return;

        var globalScale = owner.Owner.Slot.GlobalScale;
        Color = MathX.Clamp(MathX.FilterInvalid(owner.Owner.Color.Value), -64f, 64f).ToUnity(ColorProfile.sRGB);
        Intensity = MathX.Clamp(MathX.FilterInvalid(owner.Owner.Intensity.Value), -1024f, 1024f);
        Range = MathX.FilterInvalid(owner.Owner.Range.Value * ((globalScale.x + globalScale.y + globalScale.z) / 3.0f));
        SpotAngle = MathX.Clamp(MathX.FilterInvalid(owner.Owner.SpotAngle.Value), 0.0f, 180f);
        ShadowStrength = MathX.Clamp01(MathX.FilterInvalid(owner.Owner.ShadowStrength.Value));
        ShadowNearPlane = MathX.Max(1f / 1000f, MathX.FilterInvalid((float)owner.Owner.ShadowNearPlane));
        ShadowCustomResolution = owner.Owner.ShadowMapResolution.Value;
        ShadowBias = owner.Owner.ShadowBias.Value;
        ShadowNormalBias = owner.Owner.ShadowNormalBias.Value;
        Cookie = owner.Owner.Cookie.Target != null
            ? owner.Owner.Cookie?.Asset?.Connector as IUnityTextureProvider ??
              owner.Owner.Engine?.AssetManager?.DarkCheckerCubemap?.Connector as IUnityTextureProvider
            : null;
    }

    public override void Update()
    {
        if (Type.HasValue) Owner.UnityLight.type = Type.Value;
        if (Shadows.HasValue) Owner.UnityLight.shadows = Shadows.Value;
        Owner.UnityLight.enabled = ShouldBeEnabled;
        if (!ShouldBeEnabled) return;

        Owner.UnityLight.shadowNearPlane = 0.15f;
        Owner.UnityLight.color = Color;
        Owner.UnityLight.intensity = Intensity;
        Owner.UnityLight.range = Range;
        Owner.UnityLight.spotAngle = SpotAngle;
        Owner.UnityLight.shadowStrength = ShadowStrength;
        Owner.UnityLight.shadowNearPlane = ShadowNearPlane;
        Owner.UnityLight.shadowCustomResolution = ShadowCustomResolution;
        Owner.UnityLight.shadowBias = ShadowBias;
        Owner.UnityLight.shadowNormalBias = ShadowNormalBias;
        Owner.UnityLight.cookie = Cookie?.UnityTexture;
    }
}