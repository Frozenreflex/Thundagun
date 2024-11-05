using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using Leap;
using ResoniteModLoader;
using UnityEngine;
using UnityFrooxEngineRunner;
using Object = UnityEngine.Object;
using RenderConnector = Thundagun.NewConnectors.RenderConnector;
using SlotConnector = Thundagun.NewConnectors.SlotConnector;
using UnityAssetIntegrator = Thundagun.NewConnectors.UnityAssetIntegrator;
using WorldConnector = Thundagun.NewConnectors.WorldConnector;

namespace Thundagun;

public class Thundagun : ResoniteMod
{
    public override string Name => "Thundagun";
    public override string Author => "Fro Zen, DoubleStyx, 989onan";
    public override string Version => "1.0.0";

    public static double Performance;
    
    public static readonly Queue<IUpdatePacket> CurrentPackets = new(); 

    public static Task CurrentTask;

    public static void QueuePacket(IUpdatePacket packet)
    {
        lock (CurrentPackets) CurrentPackets.Enqueue(packet);
    }

    internal static ModConfiguration Config;

    // Implement predicates for all these keys to ensure they're within proper bounds
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<bool> DebugLogging =
        new("DebugLogging", "Debug Logging: Whether to enable debug logging.", () => false); // might want separate logging for Unity and Resonite sides?
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<float> LoggingRate =
      new("LoggingRate", "Logging Rate: The rate of log updates per second.", () => 10.0f);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<float> EngineTickRate =
        new("EngineTickRate", "Engine Tick Rate: The max rate at which FrooxEngine can update.", () => 1000);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<float> UnityTickRate =
        new("UnityTickRate", "Unity Tick Rate: The max rate at which Unity can update.", () => 1000); // not implemented yet
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<double> SyncToAsyncRatioThreshold =
        new("SyncToAsyncRatioThreshold", "Sync To Async Ratio Threshold: The ratio threshold to switch from sync to async.", () => 4.0);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<double> AsyncToDesyncRatioThreshold =
    new("AsyncToDesyncRatioThreshold", "Async To Desync Ratio Threshold: The ratio threshold to switch from async to desync.", () => 16.0);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<double> TimeoutThreshold =
    new("TimeoutThreshold", "Timeout Threshold: The time required to consider Resonite/Unity frozen and to emergency-switch to desync.", () => 100.0);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<double> TimeoutCooldown =
    new("TimeoutCooldown", "Timeout Cooldown: The time required after a panic to listen for thresholds again.", () => 1000.0);

    [AutoRegisterConfigKey] internal readonly static ModConfigurationKey<double> TimeoutWorkInterval =
        new("TimeoutWorkInterval",
            "Timeout Work Interval: The max amount of time Unity will spend processing changes during a timeout.",
            () => 16.67);
    [AutoRegisterConfigKey]
    internal readonly static ModConfigurationKey<float> EMAExponent =
        new("EMAExponent", "EMA Exponent: The exponent used for the exponential moving average for calculating framerate.", () => 0.1f);

    public override void OnEngineInit()
    {
        var harmony = new Harmony("Thundagun");
        Config = GetConfiguration();

        PatchEngineTypes();
        PatchComponentConnectors(harmony);

        var workerInitializerMethod = typeof(WorkerInitializer)
            .GetMethods(AccessTools.all)
            .First(i => i.Name.Contains("Initialize") && i.GetParameters().Length == 1 &&
                        i.GetParameters()[0].ParameterType == typeof(Type));
        var workerInitializerPatch =
            typeof(WorkerInitializerPatch).GetMethod(nameof(WorkerInitializerPatch.Initialize));

        harmony.Patch(workerInitializerMethod, postfix: new HarmonyMethod(workerInitializerPatch));

        harmony.PatchAll();

        PostProcessingInterface.SetupCamera = NewConnectors.CameraInitializer.SetupCamera;
    }

    public static void PatchEngineTypes()
    {
        var engineTypes = typeof(Slot).Assembly.GetTypes()
            .Where(i => i.GetCustomAttribute<ImplementableClassAttribute>() is not null).ToList();
        foreach (var type in engineTypes)
        {
            var field1 = type.GetField("__connectorType",
                BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.Static);
            var field2 = type.GetField("__connectorTypes",
                BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.Static);

            // WorldManager
            // AudioSystem TODO; check this one again

            if (type == typeof(Slot))
            {
                field1.SetValue(null, typeof(SlotConnector));
                Msg($"Patched {type.Name}");
            }
            else if (type == typeof(World))
            {
                field1.SetValue(null, typeof(WorldConnector));
                Msg($"Patched {type.Name}");
            }
            else if (type == typeof(AssetManager))
            {
                field1.SetValue(null, typeof(UnityAssetIntegrator));
                Msg($"Patched {type.Name}");
            }
            else if (type == typeof(RenderManager))
            {
                field1.SetValue(null, typeof(RenderConnector));
                Msg($"Patched {type.Name}");
            }
        }
    }

    public static void PatchComponentConnectors(Harmony harmony)
    {
        var types = typeof(Thundagun).Assembly.GetTypes()
            .Where(i => i.IsClass && i.GetInterfaces().Contains(typeof(IConnector))).ToList();

        var initInfosField = typeof(WorkerInitializer).GetField("initInfos", AccessTools.all);
        var initInfos = (ConcurrentDictionary<Type, WorkerInitInfo>)initInfosField?.GetValue(null);

        Msg($"Attempting to patch component types");

        foreach (var t in initInfos.Keys)
        {
            Msg($"Attempting " + t.Name);
            var connectorType = typeof(IConnector<>).MakeGenericType(!t.IsGenericType ? t : t.GetGenericTypeDefinition());
            var array = types.Where(j => j.GetInterfaces().Any(i => i == connectorType)).ToArray();
            if (array.Length == 1)
            {
                initInfos[t].connectorType = array[0];
                Msg($"Patched " + t.Name);
            }
        }
    }
}

[HarmonyPatch(typeof(FrooxEngineRunner))]
public static class FrooxEngineRunnerPatch
{

    public static Queue<int> assets_processed = new();

    public static DateTime lastrender;
    public static DateTime lastTick;

    public static bool firstrunengine;
    public static bool shutdown;

    [HarmonyPrefix]
    [HarmonyPatch("Update")]

    public static bool Update(FrooxEngineRunner __instance,
        ref Engine ____frooxEngine, ref bool ____shutdownRequest, ref Stopwatch ____externalUpdate, ref World ____lastFocusedWorld,
        ref HeadOutput ____vrOutput, ref HeadOutput ____screenOutput, ref AudioListener ____audioListener, ref List<World> ____worlds)
    {
        shutdown = ____shutdownRequest;
        if (!__instance.IsInitialized || ____frooxEngine == null)
            return false;
        if (____shutdownRequest)
        {
            __instance.Shutdown(ref ____frooxEngine);
        }
        else
        {
            ____externalUpdate.Stop();

            if (!firstrunengine)
            {
                firstrunengine = true;
                
                //patch both headoutputs
                
                PatchHeadOutput(____vrOutput);
                PatchHeadOutput(____screenOutput);
                
                //as a last resort, nuke every single old post-processing component
                var toRemove = __instance.gameObject.scene.GetRootGameObjects().SelectMany(i => i.GetComponentsInChildren<CameraPostprocessingManager>());
                foreach (var remove in toRemove)
                {
                    Thundagun.Msg("deleting a stray post-processing manager");
                    Object.Destroy(remove);
                }
            }
            
            try
            {
                UpdateFrameRate(__instance);
                var starttime = DateTime.Now;


                var engine = ____frooxEngine;
                Thundagun.CurrentTask ??= Task.Run(() =>
                {
                    while (!shutdown)
                    {
                        var total = 0;
                        lock (assets_processed)
                            while (assets_processed.Any()) total += assets_processed.Dequeue();

                        var beforeEngine = DateTime.Now;
                        engine.AssetsUpdated(total);
                        engine.RunUpdateLoop();
                        var engineTime = (DateTime.Now - beforeEngine);
                        var ticktime = TimeSpan.FromSeconds((1 / Math.Abs(Thundagun.Config.GetValue(Thundagun.EngineTickRate)) + 1));
                        if (engineTime < ticktime)
                        {
                            Task.Delay(ticktime - engineTime);
                        }


                        RenderConnector.renderQueue.MarkAsCompleted();
                        // I believe this is the last step in the main Resonite update loop
                        SynchronizationManager.OnResoniteUpdate();
                    }
                });
                // technically not the last or first thing called, but it does happen only once per cycle
                SynchronizationManager.OnUnityUpdate();
                
                if (Thundagun.CurrentTask?.Exception is not null) throw Thundagun.CurrentTask.Exception;

                var focusedWorld = engine.WorldManager.FocusedWorld;
                var lastFocused = ____lastFocusedWorld;
                UpdateHeadOutput(focusedWorld, engine, ____vrOutput, ____screenOutput, ____audioListener, ref ____worlds);


                engine.InputInterface.UpdateWindowResolution(new int2(Screen.width, Screen.height));

                var boilerplateTime = DateTime.Now;
                List<IUpdatePacket> updates;
                lock (Thundagun.CurrentPackets)
                {
                    updates = [..Thundagun.CurrentPackets];
                    Thundagun.CurrentPackets.Clear();
                }


                if (UnityAssetIntegrator._instance is not null)
                    lock (assets_processed) assets_processed.Enqueue(UnityAssetIntegrator._instance.ProcessQueue1(1000));

                var assetTime = DateTime.Now;
                var loopTime = DateTime.Now;

                foreach (var update in updates)
                {
                    try
                    {
                        update.Update();
                    }
                    catch (Exception e)
                    {
                        Thundagun.Msg(e);
                    }
                }
                
                var updateTime = DateTime.Now;

                if (focusedWorld != lastFocused)
                {
                    DynamicGIManager.ScheduleDynamicGIUpdate(true);
                    ____lastFocusedWorld = focusedWorld;
                    ____frooxEngine.GlobalCoroutineManager.RunInUpdates(10, () => DynamicGIManager.ScheduleDynamicGIUpdate(true));
                    ____frooxEngine.GlobalCoroutineManager.RunInSeconds(1f, () => DynamicGIManager.ScheduleDynamicGIUpdate(true));
                    ____frooxEngine.GlobalCoroutineManager.RunInSeconds(5f, () => DynamicGIManager.ScheduleDynamicGIUpdate(true));
                }
                UpdateQualitySettings(__instance);

                var finishTime = DateTime.Now;
                
                if (Thundagun.Config.GetValue(Thundagun.DebugLogging))
                {
                    Thundagun.Msg($"LastRender vs now: {(lastrender - starttime).TotalSeconds}");
                    Thundagun.Msg($"Boilerplate: {(boilerplateTime - starttime).TotalSeconds} Asset Integration time: {(assetTime - boilerplateTime).TotalSeconds} Loop time: {(loopTime - assetTime).TotalSeconds} Update time: {(updateTime - loopTime).TotalSeconds} Finished: {(finishTime - updateTime).TotalSeconds} total time: {(finishTime - starttime).TotalSeconds} Current mode: {SynchronizationManager.CurrentSyncMode} Unity update time: {SynchronizationManager.UnityEMA} FrooxEngine update time: {SynchronizationManager.ResoniteEMA}");
                }
                lastrender = DateTime.Now;
            }
            catch (Exception ex)
            {
                Thundagun.Msg($"Exception updating FrooxEngine:\n{ex}");
                var startwait = DateTime.Now;
                var i = 0;
                var wait = new Task(() => Task.Delay(10000));
                wait.Start();
                wait.Wait();
                UniLog.Error($"Exception updating FrooxEngine:\n{ex}");
                ____frooxEngine = null;
                __instance.Shutdown(ref ____frooxEngine);

                return false;
            }
            __instance.DynamicGI?.UpdateDynamicGI();
            ____externalUpdate.Restart();
        }
        return false;
    }

    private static void PatchHeadOutput(HeadOutput output)
    {
        if (output == null) return;
        var cameraSettings = new CameraSettings
        {
            IsPrimary = true,
            IsVR = output.Type == HeadOutput.HeadOutputType.VR,
            MotionBlur = output.AllowMotionBlur,
            ScreenSpaceReflection = output.AllowScreenSpaceReflection,
            SetupPostProcessing = Application.platform != RuntimePlatform.Android
        };
        foreach (var camera in output.cameras)
        {
            var toRemove = camera.gameObject.GetComponents<CameraPostprocessingManager>();
            foreach (var r in toRemove) Object.Destroy(r);
            PostProcessingInterface.SetupCamera(camera, cameraSettings);
        }
    }

    [HarmonyReversePatch]
    [HarmonyPatch("UpdateFrameRate")]
    public static void UpdateFrameRate(object instance) => throw new NotImplementedException("stub");

    private static void UpdateHeadOutput(World focusedWorld, Engine engine, HeadOutput VR, HeadOutput screen, AudioListener listener, ref List<World> worlds)
    {
        if (focusedWorld == null) return;
        var num = engine.InputInterface.VR_Active ? 1 : 0;
        var headOutput1 = num != 0 ? VR : screen;
        var headOutput2 = num != 0 ? screen : VR;
        if (headOutput2 != null && headOutput2.gameObject.activeSelf) headOutput2.gameObject.SetActive(false);
        if (!headOutput1.gameObject.activeSelf) headOutput1.gameObject.SetActive(true);
        headOutput1.UpdatePositioning(focusedWorld);
        Vector3 position;
        Quaternion rotation;
        if (focusedWorld.OverrideEarsPosition)
        {
            position = focusedWorld.LocalUserEarsPosition.ToUnity();
            rotation = focusedWorld.LocalUserEarsRotation.ToUnity();
        }
        else
        {
            var cameraRoot = headOutput1.CameraRoot;
            position = cameraRoot.position;
            rotation = cameraRoot.rotation;
        }
        listener.transform.SetPositionAndRotation(position, rotation);
        engine.WorldManager.GetWorlds(worlds);
        var transform1 = headOutput1.transform;
        foreach (var world in worlds)
        {
            if (world.Focus != World.WorldFocus.Overlay && world.Focus != World.WorldFocus.PrivateOverlay) continue;
            var transform2 = ((WorldConnector)world.Connector).WorldRoot.transform;
            var userGlobalPosition = world.LocalUserGlobalPosition;
            var userGlobalRotation = world.LocalUserGlobalRotation;

            var t = transform2.transform;

            t.position = transform1.position - userGlobalPosition.ToUnity();
            t.rotation = transform1.rotation * userGlobalRotation.ToUnity();
            t.localScale = transform1.localScale;
        }
        worlds.Clear();
    }

    [HarmonyReversePatch]
    [HarmonyPatch("UpdateQualitySettings")]
    public static void UpdateQualitySettings(object instance) => throw new NotImplementedException("stub");
    private static void Shutdown(this FrooxEngineRunner runner, ref Engine engine)
    {
        UniLog.Log("Shutting down");
        try
        {
            engine?.Dispose();
        }
        catch (Exception ex)
        {
            UniLog.Error("Exception disposing the engine:\n" + engine);
        }
        engine = null;
        try
        {
            runner.OnFinalizeShutdown?.Invoke();
        }
        catch
        {
        }
        Application.Quit();
        Process.GetCurrentProcess().Kill();
    }
}

[HarmonyPatch(typeof(AssetInitializer))]
public static class AssetInitializerPatch
{
    public static readonly Dictionary<Type, Type> Connectors = new();
    static AssetInitializerPatch()
    {
        var ourTypes = typeof(Thundagun).Assembly.GetTypes()
            .Where(i => i.GetInterfaces().Contains(typeof(IAssetConnector))).ToList();
        var theirTypes = typeof(Slot).Assembly.GetTypes().Where(t =>
        {
            if (!t.IsClass || t.IsAbstract || !typeof(Asset).IsAssignableFrom(t))
                return false;
            return t.InheritsFromGeneric(typeof(ImplementableAsset<,>)) || t.InheritsFromGeneric(typeof(DynamicImplementableAsset<>));
        }).ToList();

        foreach (var t in theirTypes)
        {
            var connectorType = t.GetProperty("Connector", BindingFlags.FlattenHierarchy | BindingFlags.Instance | BindingFlags.Public)?.PropertyType;
            if (connectorType is null) continue;
            var list = ourTypes.Where(i => connectorType.IsAssignableFrom(i)).ToList();
            if (list.Count == 1)
            {
                Connectors.Add(t, list[0]);
            }
        }
    }
    [HarmonyPrefix]
    [HarmonyPatch("GetConnectorType")]
    public static bool GetConnectorType(Asset asset, ref Type __result)
    {
        if (!Connectors.TryGetValue(asset.GetType(), out var t)) return true;
        __result = t;
        return false;
    }
}

public static class WorkerInitializerPatch
{
    public static void Initialize(Type workerType, WorkerInitInfo __result)
    {
        if (!workerType.GetInterfaces().Contains(typeof(IImplementable))) return;

        //TODO: make this static
        //get all connector types from this mod
        var types = typeof(Thundagun)
            .Assembly
            .GetTypes()
            .Where(i => i.IsClass && i.GetInterfaces().Contains(typeof(IConnector)))
            .ToList();

        var connectorType = typeof(IConnector<>)
            .MakeGenericType(workerType.IsGenericType ? workerType.GetGenericTypeDefinition() : workerType);
        var array = types.Where(j => j.GetInterfaces().Any(i => i == connectorType)).ToArray();

        if (array.Length == 1)
        {
            __result.connectorType = array[0];
            Thundagun.Msg($"Patched " + workerType.Name);
        }
    }
}

public abstract class UpdatePacket<T> : IUpdatePacket
{
    public T Owner;
    public abstract void Update();

    public UpdatePacket(T owner)
    {
        Owner = owner;
    }
}

public interface IUpdatePacket
{
    public void Update();
}
public class PerformanceTimer
{
    private string Name;
    private Stopwatch timer;

    public PerformanceTimer(string name)
    {
        Name = name;
        timer = new Stopwatch();
        timer.Start();
    }

    public void End()
    {
        timer.Stop();
        Thundagun.Msg($"{Name}: {timer.Elapsed.TotalSeconds}");
    }
}

public static class ThreadLogger
{
    public static Task Task;

    static ThreadLogger()
    {
        Task = Task.Run(() =>
        {
            while (true)
            {
                Thundagun.Msg($"Unity: {SynchronizationManager.UnityEMA} Resonite: {SynchronizationManager.ResoniteEMA} Mode: {SynchronizationManager.CurrentSyncMode}");
                Thread.Sleep((int)(1.0f / Thundagun.Config.GetValue(Thundagun.LoggingRate)));
            }
        });
    }
}


public static class SynchronizationManager
{
    private static readonly object SyncLock = new();
    private static DateTime _unityStartTime = DateTime.Now;
    private static DateTime _resoniteStartTime = DateTime.Now;
    private static DateTime _timeoutStartTime = DateTime.Now;
    private static bool _lockResoniteUnlockUnity;
    // is accessing like this thread-safe?
    public static double UnityEMA { get; private set; } = 16.67;
    public static double ResoniteEMA { get; private set; } = 16.67;
    public static void OnUnityUpdate()
    {
        var elapsed = (DateTime.Now - _unityStartTime).TotalMilliseconds;
        double alpha = Mathf.Clamp(Thundagun.Config.GetValue(Thundagun.EMAExponent), 0.001f, 0.999f);
        UnityEMA = alpha * elapsed + (1 - alpha) * UnityEMA;

        var ratio = UnityEMA / ResoniteEMA;
        if (ratio > Thundagun.Config.GetValue(Thundagun.AsyncToDesyncRatioThreshold) || 1.0 / ratio > Thundagun.Config.GetValue(Thundagun.AsyncToDesyncRatioThreshold))
            CurrentSyncMode = SyncMode.Desync;
        else if (ratio > Thundagun.Config.GetValue(Thundagun.SyncToAsyncRatioThreshold) || 1.0 / ratio > Thundagun.Config.GetValue(Thundagun.SyncToAsyncRatioThreshold))
            CurrentSyncMode = SyncMode.Async;
        else
            CurrentSyncMode = SyncMode.Sync;

        // is it safe to have Unity skip over the lock like this? Only Unity will ever take advantage of the timeout.
        lock (SyncLock)
        {
            while (!_lockResoniteUnlockUnity)
            {
                if (Timeout || CurrentSyncMode != SyncMode.Sync) break;

                // we need some form of polling to see if the timeout has been triggered
                // or do we? try removing the wait
                Monitor.Wait(SyncLock, TimeSpan.FromMilliseconds(0.1));
            }

            Monitor.Pulse(SyncLock);

            _lockResoniteUnlockUnity = false;
        }

        _unityStartTime = DateTime.Now;
    }
    public static void OnResoniteUpdate()
    {
        var elapsed = (DateTime.Now - _resoniteStartTime).TotalMilliseconds;
        double alpha = Mathf.Clamp(Thundagun.Config.GetValue(Thundagun.EMAExponent), 0.001f, 0.999f);
        ResoniteEMA = alpha * elapsed + (1 - alpha) * ResoniteEMA;


        lock (SyncLock)
        {
            while (_lockResoniteUnlockUnity) Monitor.Wait(SyncLock);
            Monitor.Pulse(SyncLock);
            _lockResoniteUnlockUnity = true;
        }

        
        _resoniteStartTime = DateTime.Now;
    }
    public static SyncMode CurrentSyncMode { get; private set; } = SyncMode.Sync;
    public static bool Timeout
    {
        get
        {
            // not thread safe
            if ((DateTime.Now - _timeoutStartTime).TotalMilliseconds < Thundagun.Config.GetValue(Thundagun.TimeoutCooldown)) return true;
            if ((DateTime.Now - _timeoutStartTime).TotalMilliseconds > Thundagun.Config.GetValue(Thundagun.TimeoutThreshold))
            {
                _timeoutStartTime = DateTime.Now;
                return true;
            }
            return false;
        }
    }
}

public enum SyncMode
{
    Sync,
    Async,
    Desync,
}