#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;

#endregion

namespace Thundagun.NewConnectors;

public class RenderQueueProcessor : MonoBehaviour
{
    private readonly Queue<RenderTask> Tasks = new();
    public RenderConnector Connector;

    public RenderQueueProcessor()
    {
        Instance = this;
    }

    public static RenderQueueProcessor Instance { get; private set; }

    private void LateUpdate()
    {
        lock (Tasks)
        {
            if (Tasks.Count == 0) return;

            var renderingContext = RenderHelper.CurrentRenderingContext;
            RenderHelper.BeginRenderContext(RenderingContext.RenderToAsset);

            while (Tasks.Count > 0)
            {
                var renderTask = Tasks.Dequeue();
                try
                {
                    renderTask.task.SetResult(Connector.RenderImmediate(renderTask.settings));
                }
                catch (Exception ex)
                {
                    renderTask.task.SetException(ex);
                }
            }

            if (renderingContext.HasValue) RenderHelper.BeginRenderContext(renderingContext.Value);
        }
    }

    public Task<byte[]> Enqueue(FrooxEngine.RenderSettings settings)
    {
        var task = new TaskCompletionSource<byte[]>();
        var renderTask = new RenderTask(new RenderSettings
        {
            position = settings.position,
            rotation = settings.rotation,
            size = settings.size,
            textureFormat = settings.textureFormat,
            projection = settings.projection,
            fov = settings.fov,
            ortographicSize = settings.ortographicSize,
            clear = settings.clear,
            clearColor = settings.clearColor,
            near = settings.near,
            far = settings.far,
            renderObjects = settings.renderObjects?.Select(s => ((SlotConnector)s.Connector)?.GeneratedGameObject)
                .ToList(),
            excludeObjects = settings.excludeObjects?.Select(s => ((SlotConnector)s.Connector)?.GeneratedGameObject)
                .ToList(),
            renderPrivateUI = settings.renderPrivateUI,
            postProcesing = settings.postProcesing,
            screenspaceReflections = settings.screenspaceReflections,
            customPostProcess = settings.customPostProcess
        }, task);

        lock (Tasks)
        {
            Tasks.Enqueue(renderTask);
        }

        return task.Task;
    }
}