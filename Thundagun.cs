#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using Thundagun.NewConnectors.AssetConnectors;
using UnityEngine;
using UnityFrooxEngineRunner;
using Monitor = uDesktopDuplication.Monitor;
using Object = UnityEngine.Object;
using RenderConnector = Thundagun.NewConnectors.RenderConnector;
using SlotConnector = Thundagun.NewConnectors.SlotConnector;
using UnityAssetIntegrator = Thundagun.NewConnectors.UnityAssetIntegrator;
using WorldConnector = Thundagun.NewConnectors.WorldConnector;

#endregion

namespace Thundagun;

public class Thundagun : ResoniteMod
{
    public override string Name => "Thundagun";
    public override string Author => "Fro Zen, 989onan, DoubleStyx, Nytra, Merith-TK"; // in order of first commit
    public override string Version => "1.1.1"; // change minor version for config "API" changes

    public static readonly Queue<IUpdatePacket> CurrentPackets = new();

    public static Task FrooxEngineTask;

    public static readonly EngineCompletionStatus EngineCompletionStatus = new();

    public static void QueuePacket(IUpdatePacket packet)
    {
        lock (CurrentPackets)
        {
            CurrentPackets.Enqueue(packet);
        }
    }

    internal static ModConfiguration Config;

    [AutoRegisterConfigKey] internal static readonly ModConfigurationKey<bool> DebugLogging =
        new("DebugLogging", "Debug Logging: Whether to enable debug logging.", () => false,
            false, _ => true);

    [AutoRegisterConfigKey] internal static readonly ModConfigurationKey<double> LoggingRate =
        new("LoggingRate", "Logging Rate: The rate of log updates per second.", () => 10.0,
            false, value => value >= 0.001 || value <= 1000.0);

    [AutoRegisterConfigKey] internal static readonly ModConfigurationKey<double> MaxEngineTickRate =
        new("MaxEngineTickRate", "Max Engine Tick Rate: The max rate per second at which FrooxEngine can update.",
            () => 1000.0,
            false, value => value >= 1.0);

    [AutoRegisterConfigKey] internal static readonly ModConfigurationKey<double> MaxUnityTickRate =
        new("MaxUnityTickRate", "Max Unity Tick Rate: The max rate per second at which Unity can update.", () => 1000.0,
            false, value => value >= 1.0);

    [AutoRegisterConfigKey] internal static readonly ModConfigurationKey<bool> RenderIncompleteUpdates =
        new("RenderIncompleteUpdates",
            "Render Incomplete Updates: Allow Unity to process and render engine changes in realtime. Can look glitchy.",
            () => false,
            false, _ => true);

    public override void OnEngineInit()
    {
        var harmony = new Harmony("com.frozenreflex.Thundagun");
        Config = GetConfiguration();

        PatchEngineTypes();
        PatchComponentConnectors(harmony);

        var workerInitializerMethod = typeof(WorkerInitializer)
            .GetMethods(AccessTools.all)
            .First(static i => i.Name.Contains("Initialize") && i.GetParameters().Length == 1 &&
                               i.GetParameters()[0].ParameterType == typeof(Type));
        var workerInitializerPatch =
            typeof(WorkerInitializerPatch).GetMethod(nameof(WorkerInitializerPatch.Initialize));

        harmony.Patch(workerInitializerMethod, postfix: new HarmonyMethod(workerInitializerPatch));

        harmony.PatchAll();

        PostProcessingInterface.SetupCamera = NewConnectors.CameraInitializer.SetupCamera;

        AsyncLogger.StartLogger();

        void WorldAdded(World w)
        {
            if (w.IsUserspace()) return;

            harmony.Patch(AccessTools.Method(typeof(DuplicableDisplay), "Update"),
                new HarmonyMethod(PatchDesktop.Prefix));
            Engine.Current.WorldManager.WorldFocused -= WorldAdded;
        }

        // Patching DuplicableDisplay too early causes a Unity crash, so schedule it to be patched after the first non-userspace world is focused
        Engine.Current.RunPostInit(() => { Engine.Current.WorldManager.WorldFocused += WorldAdded; });
    }

    private static void PatchEngineTypes()
    {
        var engineTypes = typeof(Slot).Assembly.GetTypes()
            .Where(static i => i.GetCustomAttribute<ImplementableClassAttribute>() is not null).ToList();
        foreach (var type in engineTypes)
        {
            var field1 = type.GetField("__connectorType",
                BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.Static);

            if (type == typeof(Slot))
            {
                if (field1 != null) field1.SetValue(null, typeof(SlotConnector));
                Msg($"Patched {type.Name}");
            }
            else if (type == typeof(World))
            {
                if (field1 != null) field1.SetValue(null, typeof(WorldConnector));
                Msg($"Patched {type.Name}");
            }
            else if (type == typeof(AssetManager))
            {
                if (field1 != null) field1.SetValue(null, typeof(UnityAssetIntegrator));
                Msg($"Patched {type.Name}");
            }
            else if (type == typeof(RenderManager))
            {
                if (field1 != null) field1.SetValue(null, typeof(RenderConnector));
                Msg($"Patched {type.Name}");
            }
        }
    }

    [SuppressMessage("ReSharper", "UnusedParameter.Local")]
    private static void PatchComponentConnectors(Harmony harmony)
    {
        var types = typeof(Thundagun).Assembly.GetTypes()
            .Where(i => i.IsClass && i.GetInterfaces().Contains(typeof(IConnector))).ToList();

        var initInfosField = typeof(WorkerInitializer).GetField("initInfos", AccessTools.all);
        var initInfos = (ConcurrentDictionary<Type, WorkerInitInfo>)initInfosField?.GetValue(null);

        Msg("Attempting to patch component types");

        if (initInfos == null) return;

        {
            foreach (var t in initInfos.Keys)
            {
                Msg("Attempting " + t.Name);
                var connectorType =
                    typeof(IConnector<>).MakeGenericType(!t.IsGenericType ? t : t.GetGenericTypeDefinition());
                var array = types.Where(j => j.GetInterfaces().Any(i => i == connectorType)).ToArray();
                if (array.Length != 1) continue;

                initInfos[t].connectorType = array[0];
                Msg("Patched " + t.Name);
            }
        }
    }
}

internal static class PatchDesktop
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static bool Prefix(DuplicableDisplay __instance, Monitor monitor)
    {
        Thundagun.QueuePacket(new DesktopUpdatePacket(__instance, monitor));
        return false;
    }
}

[HarmonyPatch(typeof(FrooxEngineRunner))]
public static class FrooxEngineRunnerPatch
{
    private static readonly Queue<int> AssetsProcessed = new();

    public static DateTime LastRender;
    public static DateTime LastTick;

    private static bool firstRunEngine;
    private static bool shutdown;

    [HarmonyPrefix]
    [HarmonyPatch("Update")]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public static bool Update(FrooxEngineRunner __instance,
        ref Engine ____frooxEngine, ref bool ____shutdownRequest, ref Stopwatch ____externalUpdate,
        ref World ____lastFocusedWorld,
        ref HeadOutput ____vrOutput, ref HeadOutput ____screenOutput, ref AudioListener ____audioListener,
        ref List<World> ____worlds)
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

            if (!firstRunEngine)
            {
                firstRunEngine = true;

                PatchHeadOutput(____vrOutput);
                PatchHeadOutput(____screenOutput);

                var toRemove = __instance.gameObject.scene.GetRootGameObjects()
                    .SelectMany(i => i.GetComponentsInChildren<CameraPostprocessingManager>());
                foreach (var remove in toRemove)
                {
                    ResoniteMod.Msg("deleting a stray post-processing manager");
                    Object.Destroy(remove);
                }
            }

            try
            {
                UpdateFrameRate(__instance);

                var engine = ____frooxEngine;
                Thundagun.FrooxEngineTask ??= Task.Run(() =>
                {
                    while (!shutdown)
                    {
                        var total = 0;
                        lock (AssetsProcessed)
                        {
                            while (AssetsProcessed.Any())
                            {
                                total += AssetsProcessed.Dequeue();
                            }
                        }

                        engine.AssetsUpdated(total);
                        engine.RunUpdateLoop();

                        SynchronizationManager.OnResoniteUpdate();
                    }
                });

                SynchronizationManager.OnUnityUpdate();

                if (Thundagun.FrooxEngineTask?.Exception is not null) throw Thundagun.FrooxEngineTask.Exception;

                var focusedWorld = engine.WorldManager.FocusedWorld;
                var lastFocused = ____lastFocusedWorld;
                UpdateHeadOutput(focusedWorld, engine, ____vrOutput, ____screenOutput, ____audioListener,
                    ref ____worlds);


                engine.InputInterface.UpdateWindowResolution(new int2(Screen.width, Screen.height));

                if (Thundagun.EngineCompletionStatus.EngineCompleted ||
                    Thundagun.Config.GetValue(Thundagun.RenderIncompleteUpdates))
                {
                    List<IUpdatePacket> updates;
                    lock (Thundagun.CurrentPackets)
                    {
                        updates = [..Thundagun.CurrentPackets];
                        Thundagun.CurrentPackets.Clear();
                    }

                    foreach (var update in updates)
                        try
                        {
                            update.Update();
                        }
                        catch (Exception e)
                        {
                            ResoniteMod.Msg(e);
                        }

                    lock (Thundagun.EngineCompletionStatus)
                    {
                        Thundagun.EngineCompletionStatus.EngineCompleted = false;
                    }
                }

                if (focusedWorld != lastFocused)
                {
                    DynamicGIManager.ScheduleDynamicGIUpdate(true);
                    ____lastFocusedWorld = focusedWorld;
                    ____frooxEngine.GlobalCoroutineManager.RunInUpdates(10,
                        () => DynamicGIManager.ScheduleDynamicGIUpdate(true));
                    ____frooxEngine.GlobalCoroutineManager.RunInSeconds(1f,
                        () => DynamicGIManager.ScheduleDynamicGIUpdate(true));
                    ____frooxEngine.GlobalCoroutineManager.RunInSeconds(5f,
                        () => DynamicGIManager.ScheduleDynamicGIUpdate(true));
                }

                UpdateQualitySettings(__instance);

                LastRender = DateTime.Now;
            }
            catch (Exception ex)
            {
                ResoniteMod.Msg($"Exception updating FrooxEngine:\n{ex}");
                var wait = new Task(static () => Task.Delay(10000));
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
    [SuppressMessage("ReSharper", "UnusedParameter.Local")]
    private static void UpdateFrameRate(object instance)
    {
        throw new NotImplementedException("stub");
    }

    private static void UpdateHeadOutput(World focusedWorld, Engine engine, HeadOutput vr, HeadOutput screen,
        AudioListener listener, ref List<World> worlds)
    {
        if (focusedWorld == null) return;

        var num = engine.InputInterface.VR_Active ? 1 : 0;
        var headOutput1 = num != 0 ? vr : screen;
        var headOutput2 = num != 0 ? screen : vr;
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
    [SuppressMessage("ReSharper", "UnusedParameter.Local")]
    private static void UpdateQualitySettings(object instance)
    {
        throw new NotImplementedException("stub");
    }

    private static void Shutdown(this FrooxEngineRunner runner, ref Engine engine)
    {
        UniLog.Log("Shutting down");
        try
        {
            engine?.Dispose();
        }
        catch (Exception ex)
        {
            UniLog.Error($"Exception: {ex}");
            UniLog.Error("Exception disposing the engine:\n" + engine);
        }

        engine = null;
        try
        {
            runner.OnFinalizeShutdown?.Invoke();
        }
        catch
        {
            // ignored
        }

        Application.Quit();
        Process.GetCurrentProcess().Kill();
    }
}

[HarmonyPatch(typeof(AssetInitializer))]
public static class AssetInitializerPatch
{
    private static readonly Dictionary<Type, Type> Connectors = new();

    static AssetInitializerPatch()
    {
        var ourTypes = typeof(Thundagun).Assembly.GetTypes()
            .Where(i => i.GetInterfaces().Contains(typeof(IAssetConnector))).ToList();
        var theirTypes = typeof(Slot).Assembly.GetTypes().Where(t =>
        {
            if (!t.IsClass || t.IsAbstract || !typeof(Asset).IsAssignableFrom(t))
                return false;

            return t.InheritsFromGeneric(typeof(ImplementableAsset<,>)) ||
                   t.InheritsFromGeneric(typeof(DynamicImplementableAsset<>));
        }).ToList();

        foreach (var t in theirTypes)
        {
            var connectorType = t.GetProperty("Connector",
                BindingFlags.FlattenHierarchy | BindingFlags.Instance | BindingFlags.Public)?.PropertyType;
            if (connectorType is null) continue;

            var list = ourTypes.Where(i => connectorType.IsAssignableFrom(i)).ToList();
            if (list.Count == 1) Connectors.Add(t, list[0]);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch("GetConnectorType")]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static bool GetConnectorType(Asset asset, ref Type __result)
    {
        if (!Connectors.TryGetValue(asset.GetType(), out var t)) return true;

        __result = t;
        return false;
    }
}

public static class WorkerInitializerPatch
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
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

        if (array.Length != 1) return;

        __result.connectorType = array[0];
        ResoniteMod.Msg("Patched " + workerType.Name);
    }
}

public abstract class UpdatePacket<T>(T owner) : IUpdatePacket
{
    protected readonly T Owner = owner;
    public abstract void Update();
}

public interface IUpdatePacket
{
    public void Update();
}

public static class EarlyLogger
{
    private const string LogFilePath = "ThundagunLogs/log.txt";

    internal static void Log(string message)
    {
        try
        {
            using var writer = new StreamWriter(LogFilePath, true);
            writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write to log: {ex.Message}");
        }
    }
}

public static class AsyncLogger
{
    private static Task asyncLoggerTask;

    [SuppressMessage("ReSharper", "FunctionNeverReturns")]
    internal static void StartLogger()
    {
        if (asyncLoggerTask is not null)
            return;

        asyncLoggerTask = Task.Run(() =>
        {
            while (true)
            {
                var now = DateTime.Now;
                if (Thundagun.Config.GetValue(Thundagun.DebugLogging))
                    EarlyLogger.Log(
                        $"Unity current: {now - SynchronizationManager.UnityStartTime} Resonite current: {now - SynchronizationManager.ResoniteStartTime} UnityLastUpdateInterval: {SynchronizationManager.UnityLastUpdateInterval} ResoniteLastUpdateInterval: {SynchronizationManager.ResoniteLastUpdateInterval}");

                Thread.Sleep((int)(1000.0 / Thundagun.Config.GetValue(Thundagun.LoggingRate)));
            }
        });
    }
}

public static class SynchronizationManager
{
    internal static DateTime UnityStartTime { get; private set; } = DateTime.Now;
    internal static DateTime ResoniteStartTime { get; private set; } = DateTime.Now;
    internal static TimeSpan UnityLastUpdateInterval { get; private set; } = TimeSpan.Zero;
    internal static TimeSpan ResoniteLastUpdateInterval { get; private set; } = TimeSpan.Zero;

    internal static void OnUnityUpdate()
    {
        UnityLastUpdateInterval = DateTime.Now - UnityStartTime;

        var tickTime = TimeSpan.FromMilliseconds(1000.0 / Thundagun.Config.GetValue(Thundagun.MaxUnityTickRate));
        if (DateTime.Now - UnityStartTime < tickTime) Thread.Sleep(tickTime - UnityLastUpdateInterval);

        UnityStartTime = DateTime.Now;
    }

    internal static void OnResoniteUpdate()
    {
        ResoniteLastUpdateInterval = DateTime.Now - ResoniteStartTime;
        lock (Thundagun.EngineCompletionStatus)
        {
            Thundagun.EngineCompletionStatus.EngineCompleted = true;
        }

        while (Thundagun.EngineCompletionStatus.EngineCompleted &&
               !Thundagun.Config.GetValue(Thundagun.RenderIncompleteUpdates))
            Thread.Sleep(TimeSpan.FromMilliseconds(0.1));

        var tickTime = TimeSpan.FromMilliseconds(1000.0 / Thundagun.Config.GetValue(Thundagun.MaxEngineTickRate));
        if (DateTime.Now - ResoniteStartTime < tickTime) Thread.Sleep(tickTime - ResoniteLastUpdateInterval);

        ResoniteStartTime = DateTime.Now;
    }
}

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class EngineCompletionStatus
{
    public bool EngineCompleted;
}