#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Elements.Core;
using UnityEngine;
using UnityFrooxEngineRunner;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class ParticleSystemWorker : MonoBehaviour
{
    public const float STOP_VELOCITY_MAGNITUDE = 1E-10f;

    private static volatile int processedJobs;

    private static readonly List<ParticleSystemBehavior> jobData = new();

    private static readonly SpinQueue<ParticleJob> jobs = new();

    private static WorkProcessor jobProcessor;

    private static Action workerDelegate;

    public int JobsCountDebug;

    public static bool Initialized { get; private set; }

    private void Awake()
    {
        Initialized = true;
        workerDelegate = ParticleCollisionWorker;
    }

    private void LateUpdate()
    {
        JobsCountDebug = jobs.Count;
        if (jobs.Count == 0)
            return;
        var count = jobs.Count;
        processedJobs = 0;
        /*
     for (var i = 0; i < MathX.Min(jobProcessor.WorkerCount - 1, count - 1); i++)
       {
          jobProcessor.Enqueue(workerDelegate, WorkType.HighPriority);
       }
      ParticleCollisionWorker();
     while (processedJobs < count)
      {
          Thread.Yield();
        }
      */
        while (processedJobs < count) ParticleCollisionWorker();
        foreach (var jobDatum in jobData.Where(jobDatum => jobDatum.pSystem != null))
            jobDatum.pSystem.SetParticles(jobDatum._particles, jobDatum.lastCount);
        jobData.Clear();
    }

    public static void RegisterJobProcessor(WorkProcessor jobProcessor)
    {
        ParticleSystemWorker.jobProcessor = jobProcessor;
    }

    public static void RegisterJobData(ParticleSystemBehavior data)
    {
        jobData.Add(data);
    }

    public static void RegisterJob(in ParticleJob job)
    {
        jobs.Enqueue(job);
    }

    private static void ParticleCollisionWorker()
    {
        ParticleJob val;
        while (jobs.TryDequeue(out val))
        {
            try
            {
                var physicsManager = val.behavior?.Physics;
                var array = val.behavior?._particles;
                var particleStyle = val.behavior.ParticleStyle;
                if (physicsManager == null || array == null || particleStyle == null)
                {
                    Interlocked.Increment(ref processedJobs);
                    continue;
                }

                var lastDeltaTime = val.behavior.lastDeltaTime;
                var value = particleStyle.Bounce.Value;
                var value2 = particleStyle.LengthScale.Value;
                var value3 = particleStyle.VelocityScale.Value;
                var value4 = particleStyle.LifetimeLoss.Value;
                for (var i = val.startIndex; i < val.endIndex; i++)
                {
                    var particle = array[i];
                    var magnitude = particle.velocity.magnitude;
                    if (magnitude <= 1E-10f)
                        continue;
                    var a = particle.position.ToEngine();
                    var v = particle.velocity.ToEngine();
                    var num = magnitude * lastDeltaTime;
                    var direction = v / magnitude;
                    var v2 = v * lastDeltaTime;
                    var b = v2 * 0.25f;
                    var origin = a - b;
                    var raycastHit = physicsManager.RaycastOne(in origin, in direction, num * 1.5f);
                    if (!raycastHit.HasValue) continue;
                    var value5 = raycastHit.Value;
                    var val2 = num - value5.Distance;
                    var normalized = Vector3.Reflect(particle.velocity, value5.Normal.ToUnity()).normalized;
                    var num2 = magnitude * value;
                    if (num2 <= 1E-10f)
                        particle.velocity = direction.ToUnity() * 1E-10f;
                    else
                        particle.velocity = normalized * num2;
                    var num3 = particle.startSize * value2 + magnitude * value * value3;
                    particle.position = value5.Point.ToUnity() + normalized * MathX.Max(val2, num3 * 0.5f);
                    particle.remainingLifetime -= value4 * particle.startLifetime;
                    array[i] = particle;
                }
            }
            catch (Exception ex)
            {
                UniLog.Log("Exception in ParticleCollisionJob: " + ex);
            }

            Interlocked.Increment(ref processedJobs);
        }
    }
}