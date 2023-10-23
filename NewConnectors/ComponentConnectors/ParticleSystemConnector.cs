using System.Linq;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;
using ParticleSystem = FrooxEngine.ParticleSystem;

namespace Thundagun.NewConnectors.ComponentConnectors;

public class ParticleSystemConnector : 
    UnityComponentConnector<ParticleSystem, ParticleSystemBehavior>
{
    public override IUpdatePacket InitializePacket() => new InitializeParticleSystemConnector(this, Owner);

    public override void ApplyChanges()
    {
        if (Owner.ShouldBeEnabled)
        {
            UnityComponent.enabled = true;
            UnityComponent.UpdateStyle();
        }
        else UnityComponent.enabled = false;
    }

    public override void DestroyMethod(bool destroyingWorld)
    {
        if (!destroyingWorld) UnityComponent.Cleanup();
        base.DestroyMethod(destroyingWorld);
    }
}

public class InitializeParticleSystemConnector : InitializeUnityComponentConnector<ParticleSystem,
    ParticleSystemBehavior, ParticleSystemConnector>
{
    public InitializeParticleSystemConnector(ParticleSystemConnector connector, ParticleSystem component) : base(connector, component)
    {
    }

    public override void Update()
    {
        base.Update();
        Owner.UnityComponent.Init();
    }
}

public class ApplyChangesParticleSystemConnector : UpdatePacket<ParticleSystemConnector>
{
    public bool ShouldBeEnabled;

    //holy fuck
    public int MaxParticles;
    public SlotConnector Space;
    public bool SpaceIsRoot;
    public bool StyleEnabled;
    public MotionVectorGenerationMode MotionVectorGenerationMode;
    public ParticleSystemRenderSpace Alignment;
    public bool AllowRoll;
    public IMaterialConnector Material;
    public IMaterialConnector TrailMaterial;
    public IMeshConnector Mesh;
    public float MinParticleSize;
    public float MaxParticleSize;
    public float GravityModifier;
    public bool UseColorOverLifetime;
    public UnityEngine.ParticleSystem.MinMaxGradient ColorOverLifetimeGradient;
    public ParticleSystemRenderMode RenderMode;
    public float LengthScale;
    public float VelocityScale;
    public bool TextureSheetEnabled;
    public int TextureSheetNumTilesX;
    public int TextureSheetNumTilesY;
    public int TextureSheetCycleCount;
    public ParticleSystemAnimationType TextureSheetAnimation;
    public ParticleSystemAnimationRowMode TextureSheetRowMode;
    public int TextureSheetRowIndex;
    public bool TrailEnabled;
    public ParticleSystemTrailMode TrailMode;
    public float TrailRatio;
    public float TrailMinVertexDistance;
    public bool TrailWorldSpace;
    public bool TrailDieWithParticles;
    public int TrailRibbonCount;
    public bool TrailSizeAffectsWidth;
    public bool TrailSizeAffectsLifetime;
    public bool TrailInheritParticleColor;
    public bool TrailGenerateLightingData;
    public ParticleSystemTrailTextureMode TrailTextureMode;
    public UnityEngine.ParticleSystem.MinMaxCurve TrailLifetime;
    public UnityEngine.ParticleSystem.MinMaxGradient TrailColorOverLifetime;
    public UnityEngine.ParticleSystem.MinMaxCurve TrailWidthOverTrail;
    public bool LightsEnabled;
    public LightConnector LightsLight;
    public float LightsRatio;
    public bool LightsUseRandomDistribution;
    public bool LightsUseParticleColor;
    public bool LightsSizeAffectsRange;
    public bool LightsAlphaAffectsIntensity;
    public float LightsRangeMultiplier;
    public float LightsIntensityMultiplier;
    public int LightsMaxLights;
    private ParticleSystem ParticleSystem => Owner.Owner;
    private ParticleStyle ParticleStyle => ParticleSystem.Style.Target;
    
    public ApplyChangesParticleSystemConnector(ParticleSystemConnector owner) : base(owner)
    {
        ShouldBeEnabled = ParticleSystem.ShouldBeEnabled;
        if (ShouldBeEnabled)
        {
            Space = ParticleSystem.SimulationSpace.Space.Connector as SlotConnector;
            SpaceIsRoot = Space?.Owner.IsRootSlot ?? false;
            if (ParticleStyle is not null)
            {
                StyleEnabled = true;
                MotionVectorGenerationMode = ParticleStyle.MotionVectorMode.Value.ToUnity();
                Alignment = ParticleStyle.Alignment.Value.ToUnity();
                AllowRoll = ParticleStyle.Alignment.Value != ParticleAlignment.Facing;
                Material = ParticleStyle.Material.Asset.Connector;
                TrailMaterial = ParticleStyle.TrailMaterial.Asset.Connector;
                Mesh = ParticleStyle.Mesh?.Asset?.Connector;
                MinParticleSize = ParticleStyle.MinParticleSize.Value;
                MaxParticleSize = ParticleStyle.MaxParticleSize.Value;
                GravityModifier = ParticleStyle.GravityStrength.Value;
                UseColorOverLifetime = ParticleStyle.UseColorOverLifetime.Value;
                if (UseColorOverLifetime)
                {
                    ColorOverLifetimeGradient = new UnityEngine.ParticleSystem.MinMaxGradient(new Gradient
                    {
                        alphaKeys = ParticleStyle.AlphaOverLifetime.Select(i => new GradientAlphaKey(i.value, i.time)).ToArray(),
                        colorKeys = ParticleStyle.ColorOverLifetime.Select(i => new GradientColorKey(i.value.ToUnity(ColorProfile.sRGB), i.time)).ToArray()
                    });
                }
                if (Mesh is not null)
                {
                    RenderMode = ParticleSystemRenderMode.Mesh;
                }
                else if (MathX.Approximately(ParticleStyle.LengthScale.Value, 1f) &&
                         MathX.Approximately(ParticleStyle.VelocityScale.Value, 0f))
                {
                    RenderMode = ParticleSystemRenderMode.Billboard;
                }
                else
                {
                    RenderMode = ParticleSystemRenderMode.Stretch;
                    LengthScale = ParticleStyle.LengthScale.Value;
                    VelocityScale = ParticleStyle.VelocityScale.Value;
                }
                
                if (MathX.MaxComponent(ParticleStyle.AnimationTiles) > 1)
                {
                    TextureSheetEnabled = true;
                    TextureSheetNumTilesX = ParticleStyle.AnimationTiles.Value.x;
                    TextureSheetNumTilesY = ParticleStyle.AnimationTiles.Value.y;
                    TextureSheetCycleCount = ParticleStyle.AnimationCycles.Value;
                    TextureSheetAnimation = ParticleStyle.AnimationType.Value.ToUnity();
                    TextureSheetRowMode = (ParticleStyle.UseRandomRow ? ParticleSystemAnimationRowMode.Random : ParticleSystemAnimationRowMode.Custom);
                    TextureSheetRowIndex = ParticleStyle.UseRowIndex.Value;
                }

                if (ParticleStyle.ParticleTrails.Value == ParticleTrailMode.None) TrailEnabled = false;
                else
                {
                    TrailEnabled = true;
                    TrailMode = ParticleStyle.ParticleTrails.Value == ParticleTrailMode.PerParticle ? ParticleSystemTrailMode.PerParticle : ParticleSystemTrailMode.Ribbon;
                    TrailRatio = ParticleStyle.TrailRatio.Value;
                    TrailMinVertexDistance = ParticleStyle.TrailMinimumVertexDistance.Value;
                    TrailWorldSpace = ParticleStyle.TrailWorldSpace.Value;
                    TrailDieWithParticles = ParticleStyle.TrailDiesWithParticle.Value;
                    TrailRibbonCount = ParticleStyle.RibbonCount.Value;
                    TrailSizeAffectsWidth = ParticleStyle.ParticleSizeAffectsTrailWidth.Value;
                    TrailSizeAffectsLifetime = ParticleStyle.ParticleSizeAffectsTrailLifetime.Value;
                    TrailInheritParticleColor = ParticleStyle.InheritTrailColorFromParticle.Value;
                    TrailGenerateLightingData = ParticleStyle.GenerateLightingDataForTrails.Value;
                    TrailTextureMode = (ParticleSystemTrailTextureMode)ParticleStyle.TrailTextureMode.Value;
                    TrailLifetime = new UnityEngine.ParticleSystem.MinMaxCurve(ParticleStyle.MinTrailLifetime.Value, ParticleStyle.MaxTrailLifetime.Value);
                    TrailColorOverLifetime = 
                        new UnityEngine.ParticleSystem.MinMaxGradient(
                            ParticleStyle.MinTrailColor.Value.ToUnityAuto(Owner.Engine), 
                            ParticleStyle.MaxTrailColor.Value.ToUnityAuto(Owner.Engine)
                            );
                    TrailWidthOverTrail = new UnityEngine.ParticleSystem.MinMaxCurve(ParticleStyle.MinTrailWidth.Value, ParticleStyle.MaxTrailWidth.Value);
                }
                
                if (ParticleStyle.Light.Target == null)
                {
                    LightsEnabled = false;
                    return;
                }
                
                LightsEnabled = true;
                LightsLight = ParticleStyle.Light.Target.Connector as LightConnector;
                LightsRatio = ParticleStyle.LightsRatio.Value;
                LightsUseRandomDistribution = ParticleStyle.LightRandomDistribution.Value;
                LightsUseParticleColor = ParticleStyle.LightsUseParticleColor.Value;
                LightsSizeAffectsRange = ParticleStyle.SizeAffectsLightRange.Value;
                LightsAlphaAffectsIntensity = ParticleStyle.AlphaAffectsLightIntensity.Value;
                LightsRangeMultiplier = ParticleStyle.LightRangeMultiplier.Value;
                LightsIntensityMultiplier = ParticleStyle.LightIntensityMultiplier.Value;
                LightsMaxLights = ParticleStyle.MaximumLights.Value;
            }
        }
    }

    public override void Update()
    {
        
    }
}