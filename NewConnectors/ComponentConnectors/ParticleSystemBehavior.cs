#region

using System;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;
using ParticleSystem = UnityEngine.ParticleSystem;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class ParticleSystemBehavior : ConnectorBehaviour<ParticleSystemConnector>
{
    private const int GRANULARITY = 100;
    private static ParticleEmission[] _emissions;

    private static color[] _colors;

    internal ParticleSystem.Particle[] _particles;

    internal int lastCount;

    internal float lastDeltaTime;

    internal SlotConnector lastSpace;

    internal ParticleSystem.LightsModule pLights;

    internal ParticleSystemRenderer pRenderer;

    internal ParticleSystem.ShapeModule pShape;

    internal ParticleSystem pSystem;

    internal ParticleSystem.TrailModule pTrail;

    private readonly RandomXGenerator random = new(RandomX.Int);

    internal FrooxEngine.ParticleSystem FrooxEngineParticleSystem => Connector.Owner;

    internal ParticleStyle ParticleStyle => FrooxEngineParticleSystem.Style.Target;

    internal PhysicsManager Physics => Connector?.World?.Physics;

    private void LateUpdate()
    {
        if (ParticleStyle == null) return;

        var particleStyle = ParticleStyle;
        var emitParams = default(ParticleSystem.EmitParams);
        emitParams.applyShapeToPosition = false;
        var particleCount = pSystem.particleCount;
        var num = pSystem.main.maxParticles - particleCount;
        var min = MathX.Max(0f, particleStyle.MinStartLifetime);
        var max = MathX.Max(0f, particleStyle.MaxStartLifetime);
        var world = FrooxEngineParticleSystem.World;
        foreach (var emitter in FrooxEngineParticleSystem.Emitters)
        {
            if (num == 0) break;

            if (emitter.CurrentCount <= 0) continue;

            var generatesColors = emitter.GeneratesColors;
            var num2 = MathX.Min(emitter.CurrentCount, num);
            _emissions = _emissions.EnsureSize(num2);
            if (generatesColors) _colors = _colors.EnsureSize(num2);
            var num3 = emitter.GenerateParticles(FrooxEngineParticleSystem.SimulationSpace.Space, num2, _emissions,
                generatesColors ? _colors : null);
            if (num3 == 0) continue;

            if (world.Focus is World.WorldFocus.PrivateOverlay or World.WorldFocus.Overlay)
            {
                var focusedWorld = world.Engine.WorldManager.FocusedWorld;
                for (var i = 0; i < num3; i++)
                {
                    _emissions[i].Position = WorldManager.TransferPoint(_emissions[i].Position, world, focusedWorld);
                    _emissions[i].Direction =
                        WorldManager.TransferDirection(_emissions[i].Direction, world, focusedWorld);
                }
            }

            bool flag = particleStyle.Use3DRotation;
            for (var j = 0; j < num3; j++)
            {
                emitParams.position = _emissions[j].Position.ToUnity();
                emitParams.velocity = _emissions[j].Direction.ToUnity() *
                                      random.Range(particleStyle.MinStartSpeed.Value,
                                          particleStyle.MaxStartSpeed.Value);
                if (flag)
                {
                    var randomXGenerator = random;
                    float3 min2 = particleStyle.MinStartRotation3D;
                    float3 max2 = particleStyle.MaxStartRotation3D;
                    emitParams.rotation3D = randomXGenerator.Lerp(in min2, in max2).ToUnity();
                    var randomXGenerator2 = random;
                    min2 = particleStyle.MinStartAngularVelocity3D;
                    max2 = particleStyle.MaxStartAngularVelocity3D;
                    emitParams.angularVelocity3D = randomXGenerator2.Lerp(in min2, in max2).ToUnity();
                }
                else
                {
                    emitParams.rotation = random.Range(particleStyle.MinStartRotation, particleStyle.MaxStartRotation);
                    emitParams.angularVelocity = random.Range(particleStyle.MinStartAngularVelocity,
                        particleStyle.MaxStartAngularVelocity.Value);
                }

                emitParams.startSize = random.Range(particleStyle.MinStartSize.Value, particleStyle.MaxStartSize.Value);
                emitParams.startLifetime = random.Range(min, max);
                var randomXGenerator3 = random;
                var min3 = particleStyle.MinStartColor.Value;
                var max3 = particleStyle.MaxStartColor.Value;
                var a = randomXGenerator3.Lerp(in min3, in max3).ToProfile(ColorProfile.sRGB);
                if (generatesColors) a *= _colors[j];
                var c = (color32)a;
                emitParams.startColor = c.ToUnity();
                pSystem.Emit(emitParams, 1);
            }

            num -= num3;
        }

        if (particleStyle.Collisions.Value) ScheduleCollisionComputation();
    }

    public void Init()
    {
        if (!ParticleSystemWorker.Initialized)
        {
            new GameObject("ParticleSystemManager").AddComponent<ParticleSystemWorker>();
            ParticleSystemWorker.RegisterJobProcessor(Connector.Owner.Engine.WorkProcessor);
        }

        var o = new GameObject("ParticleSystem");
        o.transform.SetParent(transform, false);
        pSystem = o.AddComponent<ParticleSystem>();
        pRenderer = o.GetComponent<ParticleSystemRenderer>();
        var main = pSystem.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.useUnscaledTime = true;
        pShape = pSystem.shape;
        pTrail = pSystem.trails;
        pLights = pSystem.lights;
        var emission = pSystem.emission;
        emission.enabled = false;
    }

    public void Cleanup()
    {
        Destroy(pSystem.gameObject);
    }

    private void ScheduleCollisionComputation()
    {
        var particleCount = pSystem.particleCount;
        _particles = _particles.EnsureSize(particleCount);
        lastDeltaTime = Time.deltaTime;
        var particles = pSystem.GetParticles(_particles);
        if (particles == 0) return;

        for (var i = 0; i < particles; i += 100)
        {
            var job = new ParticleJob(this, i, Math.Min(particles, i + 100));
            ParticleSystemWorker.RegisterJob(in job);
        }

        lastCount = particles;
        ParticleSystemWorker.RegisterJobData(this);
    }
}