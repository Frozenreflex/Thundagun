#region

using System.Linq;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityFrooxEngineRunner;
using MaterialConnector = Thundagun.NewConnectors.AssetConnectors.MaterialConnector;
using MeshConnector = Thundagun.NewConnectors.AssetConnectors.MeshConnector;
using ParticleSystem = FrooxEngine.LegacyParticleSystem;
using ParticleStyle = FrooxEngine.LegacyParticleStyle;
using ParticleAlignment = FrooxEngine.PhotonDust.LegacyParticleAlignment;
using ParticleTrailMode = FrooxEngine.PhotonDust.LegacyParticleTrailMode;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class ParticleSystemConnector :
    UnityComponentConnector<ParticleSystem, ParticleSystemBehavior>
{
    public override IUpdatePacket InitializePacket()
    {
        return new InitializeParticleSystemConnector(this, Owner);
    }

    public override void ApplyChanges()
    {
        Thundagun.QueuePacket(new ApplyChangesParticleSystemConnector(this));
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
    public InitializeParticleSystemConnector(ParticleSystemConnector connector, ParticleSystem component) : base(
        connector, component)
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
    public ParticleSystemRenderSpace Alignment;
    public bool AllowRoll;
    public UnityEngine.ParticleSystem.MinMaxGradient ColorOverLifetimeGradient;
    public float GravityModifier;
    public float LengthScale;
    public bool LightsAlphaAffectsIntensity;
    public bool LightsEnabled;
    public float LightsIntensityMultiplier;
    public LightConnector LightsLight;
    public int LightsMaxLights;
    public float LightsRangeMultiplier;
    public float LightsRatio;
    public bool LightsSizeAffectsRange;
    public bool LightsUseParticleColor;
    public bool LightsUseRandomDistribution;
    public MaterialConnector Material;

    public int MaxParticles;
    public float MaxParticleSize;
    public MeshConnector Mesh;
    public float MinParticleSize;
    public MotionVectorGenerationMode MotionVectorGenerationMode;
    public ParticleSystemRenderMode RenderMode;
    public bool ShouldBeEnabled;
    public SlotConnector Space;
    public bool SpaceIsRoot;
    public bool StyleEnabled;
    public ParticleSystemAnimationType TextureSheetAnimation;
    public int TextureSheetCycleCount;
    public bool TextureSheetEnabled;
    public int TextureSheetNumTilesX;
    public int TextureSheetNumTilesY;
    public int TextureSheetRowIndex;
    public ParticleSystemAnimationRowMode TextureSheetRowMode;
    public UnityEngine.ParticleSystem.MinMaxGradient TrailColorOverLifetime;
    public bool TrailDieWithParticles;
    public bool TrailEnabled;
    public bool TrailGenerateLightingData;
    public bool TrailInheritParticleColor;
    public UnityEngine.ParticleSystem.MinMaxCurve TrailLifetime;
    public MaterialConnector TrailMaterial;
    public float TrailMinVertexDistance;
    public ParticleSystemTrailMode TrailMode;
    public float TrailRatio;
    public int TrailRibbonCount;
    public bool TrailSizeAffectsLifetime;
    public bool TrailSizeAffectsWidth;
    public ParticleSystemTrailTextureMode TrailTextureMode;
    public UnityEngine.ParticleSystem.MinMaxCurve TrailWidthOverTrail;
    public bool TrailWorldSpace;
    public bool UseColorOverLifetime;
    public float VelocityScale;

    public ApplyChangesParticleSystemConnector(ParticleSystemConnector owner) : base(owner)
    {
        ShouldBeEnabled = ParticleSystem.ShouldBeEnabled;
        if (ShouldBeEnabled)
        {
            MaxParticles = ParticleSystem.MaxParticles;
            Space = ParticleSystem.SimulationSpace.Space.Connector as SlotConnector;
            SpaceIsRoot = Space?.Owner.IsRootSlot ?? false;
            if (ParticleStyle is not null)
            {
                StyleEnabled = true;
                MotionVectorGenerationMode = ParticleStyle.MotionVectorMode.Value.ToUnity();
                Alignment = ParticleStyle.Alignment.Value.ToUnity();
                AllowRoll = ParticleStyle.Alignment.Value != ParticleAlignment.Facing;
                Material = ParticleStyle.Material?.Asset?.Connector as MaterialConnector;
                TrailMaterial = ParticleStyle.TrailMaterial?.Asset?.Connector as MaterialConnector;
                Mesh = ParticleStyle.Mesh?.Asset?.Connector as MeshConnector;
                MinParticleSize = ParticleStyle.MinParticleSize.Value;
                MaxParticleSize = ParticleStyle.MaxParticleSize.Value;
                GravityModifier = ParticleStyle.GravityStrength.Value;
                UseColorOverLifetime = ParticleStyle.UseColorOverLifetime.Value;
                if (UseColorOverLifetime)
                    ColorOverLifetimeGradient = new UnityEngine.ParticleSystem.MinMaxGradient(new Gradient
                    {
                        alphaKeys = ParticleStyle.AlphaOverLifetime.Select(i => new GradientAlphaKey(i.value, i.time))
                            .ToArray(),
                        colorKeys = ParticleStyle.ColorOverLifetime
                            .Select(i => new GradientColorKey(i.value.ToUnity(ColorProfile.sRGB), i.time)).ToArray()
                    });

                if (owner.UnityComponent?.pRenderer?.mesh is not null)
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
                    TextureSheetCycleCount = ParticleStyle.AnimationCycles;
                    TextureSheetAnimation = ParticleStyle.AnimationType.Value.ToUnity();
                    TextureSheetRowMode = ParticleStyle.UseRandomRow
                        ? ParticleSystemAnimationRowMode.Random
                        : ParticleSystemAnimationRowMode.Custom;
                    TextureSheetRowIndex = ParticleStyle.UseRowIndex;
                }

                if (ParticleStyle.ParticleTrails.Value == ParticleTrailMode.None)
                {
                    TrailEnabled = false;
                }
                else
                {
                    TrailEnabled = true;
                    TrailMode = ParticleStyle.ParticleTrails.Value == ParticleTrailMode.PerParticle
                        ? ParticleSystemTrailMode.PerParticle
                        : ParticleSystemTrailMode.Ribbon;
                    TrailRatio = ParticleStyle.TrailRatio;
                    TrailMinVertexDistance = ParticleStyle.TrailMinimumVertexDistance;
                    TrailWorldSpace = ParticleStyle.TrailWorldSpace;
                    TrailDieWithParticles = ParticleStyle.TrailDiesWithParticle;
                    TrailRibbonCount = ParticleStyle.RibbonCount;
                    TrailSizeAffectsWidth = ParticleStyle.ParticleSizeAffectsTrailWidth;
                    TrailSizeAffectsLifetime = ParticleStyle.ParticleSizeAffectsTrailLifetime;
                    TrailInheritParticleColor = ParticleStyle.InheritTrailColorFromParticle;
                    TrailGenerateLightingData = ParticleStyle.GenerateLightingDataForTrails;
                    TrailTextureMode = (ParticleSystemTrailTextureMode)ParticleStyle.TrailTextureMode.Value;
                    TrailLifetime = new UnityEngine.ParticleSystem.MinMaxCurve(ParticleStyle.MinTrailLifetime,
                        ParticleStyle.MaxTrailLifetime);
                    TrailColorOverLifetime =
                        new UnityEngine.ParticleSystem.MinMaxGradient(
                            ParticleStyle.MinTrailColor.Value.ToUnityAuto(Owner.Engine),
                            ParticleStyle.MaxTrailColor.Value.ToUnityAuto(Owner.Engine)
                        );
                    TrailWidthOverTrail = new UnityEngine.ParticleSystem.MinMaxCurve(ParticleStyle.MinTrailWidth,
                        ParticleStyle.MaxTrailWidth);
                }

                if (ParticleStyle.Light.Target == null)
                {
                    LightsEnabled = false;
                    return;
                }

                LightsEnabled = true;
                LightsLight = ParticleStyle.Light.Target.Connector as LightConnector;
                LightsRatio = ParticleStyle.LightsRatio;
                LightsUseRandomDistribution = ParticleStyle.LightRandomDistribution;
                LightsUseParticleColor = ParticleStyle.LightsUseParticleColor;
                LightsSizeAffectsRange = ParticleStyle.SizeAffectsLightRange;
                LightsAlphaAffectsIntensity = ParticleStyle.AlphaAffectsLightIntensity;
                LightsRangeMultiplier = ParticleStyle.LightRangeMultiplier;
                LightsIntensityMultiplier = ParticleStyle.LightIntensityMultiplier;
                LightsMaxLights = ParticleStyle.MaximumLights;
            }
        }
    }

    private ParticleSystem ParticleSystem => Owner.Owner;
    private ParticleStyle ParticleStyle => ParticleSystem.Style.Target;

    public override void Update()
    {
        var comp = Owner.UnityComponent;
        if (ShouldBeEnabled)
        {
            comp.enabled = true;
            var main = comp.pSystem.main;
            var textureSheetAnimation = comp.pSystem.textureSheetAnimation;
            main.maxParticles = MaxParticles;
            if (comp.lastSpace != Space)
            {
                comp.lastSpace = Space;
                if (SpaceIsRoot)
                {
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                }
                else
                {
                    main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                    main.customSimulationSpace = Space.ForceGetGameObject().transform;
                }
            }

            if (StyleEnabled)
            {
                var pRenderer = comp.pRenderer;
                pRenderer.enabled = true;
                pRenderer.motionVectorGenerationMode = MotionVectorGenerationMode;
                pRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                pRenderer.alignment = Alignment;
                pRenderer.allowRoll = AllowRoll;
                pRenderer.material = Material?.UnityMaterial ?? MaterialConnector.NullMaterial;
                pRenderer.trailMaterial = TrailMaterial?.UnityMaterial ?? MaterialConnector.NullMaterial;
                pRenderer.mesh = Mesh?.Mesh;
                pRenderer.minParticleSize = MinParticleSize;
                pRenderer.maxParticleSize = MaxParticleSize;
                main.gravityModifier = GravityModifier;
                var colorOverLifetime = comp.pSystem.colorOverLifetime;
                if (UseColorOverLifetime)
                {
                    colorOverLifetime.enabled = true;
                    colorOverLifetime.color = ColorOverLifetimeGradient;
                }
                else
                {
                    colorOverLifetime.enabled = false;
                }

                pRenderer.renderMode = RenderMode;
                if (RenderMode == ParticleSystemRenderMode.Stretch)
                {
                    pRenderer.lengthScale = LengthScale;
                    pRenderer.velocityScale = VelocityScale;
                }

                if (TextureSheetEnabled)
                {
                    textureSheetAnimation.enabled = true;
                    textureSheetAnimation.numTilesX = TextureSheetNumTilesX;
                    textureSheetAnimation.numTilesY = TextureSheetNumTilesY;
                    textureSheetAnimation.cycleCount = TextureSheetCycleCount;
                    textureSheetAnimation.animation = TextureSheetAnimation;
                    textureSheetAnimation.rowMode = TextureSheetRowMode;
                    textureSheetAnimation.rowIndex = TextureSheetRowIndex;
                }
                else
                {
                    textureSheetAnimation.enabled = false;
                }

                var pTrail = comp.pTrail;
                if (!TrailEnabled)
                {
                    pTrail.enabled = false;
                }
                else
                {
                    pTrail.enabled = true;
                    pTrail.mode = TrailMode;
                    pTrail.ratio = TrailRatio;
                    pTrail.minVertexDistance = TrailMinVertexDistance;
                    pTrail.worldSpace = TrailWorldSpace;
                    pTrail.dieWithParticles = TrailDieWithParticles;
                    pTrail.ribbonCount = TrailRibbonCount;
                    pTrail.sizeAffectsWidth = TrailSizeAffectsWidth;
                    pTrail.sizeAffectsLifetime = TrailSizeAffectsLifetime;
                    pTrail.inheritParticleColor = TrailInheritParticleColor;
                    pTrail.generateLightingData = TrailGenerateLightingData;
                    pTrail.textureMode = TrailTextureMode;
                    pTrail.lifetime = TrailLifetime;
                    pTrail.colorOverLifetime = TrailColorOverLifetime;
                    pTrail.widthOverTrail = TrailWidthOverTrail;
                }

                var pLights = comp.pLights;
                if (!LightsEnabled)
                {
                    pLights.enabled = false;
                    return;
                }

                pLights.enabled = true;
                pLights.light = LightsLight.UnityLight;
                pLights.ratio = LightsRatio;
                pLights.useRandomDistribution = LightsUseRandomDistribution;
                pLights.useParticleColor = LightsUseParticleColor;
                pLights.sizeAffectsRange = LightsSizeAffectsRange;
                pLights.alphaAffectsIntensity = LightsAlphaAffectsIntensity;
                pLights.rangeMultiplier = LightsRangeMultiplier;
                pLights.intensityMultiplier = LightsIntensityMultiplier;
                pLights.maxLights = LightsMaxLights;
            }
            else if (comp.pRenderer.enabled)
            {
                comp.pSystem.Clear();
                comp.pRenderer.enabled = false;
            }
        }
        else
        {
            comp.enabled = false;
        }
    }
}