using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;

namespace Thundagun.NewConnectors;

public class RenderQueueProcessor : MonoBehaviour
{
    public RenderConnector Connector;

    private Queue<Batch> batchQueue = new(); 

    public void MarkAsCompleted()
    {
        lock (batchQueue)
        {
            if (batchQueue.Count > 0)
            {
                batchQueue.Peek().MarkComplete();
            }
        }
    }

    public Task<byte[]> Enqueue(FrooxEngine.RenderSettings settings)
    {
        var task = new TaskCompletionSource<byte[]>();
        var renderTask = new RenderTask(settings, task);

        lock (batchQueue)
        {
            if (batchQueue.Count == 0 || batchQueue.Peek().IsComplete)
            {
                var newBatch = new Batch();
                batchQueue.Enqueue(newBatch);
            }
            batchQueue.Peek().AddTask(renderTask);
        }

        return task.Task;
    }

    private void LateUpdate()
    {
        lock (batchQueue)
        {
            if (batchQueue.Count == 0)
            {
                return;
            }

            var renderingContext = RenderHelper.CurrentRenderingContext;
            RenderHelper.BeginRenderContext(RenderingContext.RenderToAsset);

            double timeElapsed = 0.0;
            DateTime startTime = DateTime.Now;
            int processorCount = Environment.ProcessorCount;

            while (batchQueue.Count > 0)
            {
                var batch = batchQueue.Peek();

                // is it safe to access the sync mode from here?
                if (!batch.IsComplete && SynchronizationManager.CurrentSyncMode == SyncMode.Async && !SynchronizationManager.Timeout)
                {
                    return;
                }

                while (batch.Tasks.Count > 0)
                {
                    var taskGroup = new List<RenderTask>();
                    for (int i = 0; i < processorCount / 2 && batch.Tasks.Count > 0; i++)
                    {
                        taskGroup.Add(batch.Tasks.Dequeue());
                    }
                    Parallel.ForEach(taskGroup,
                        new ParallelOptions { MaxDegreeOfParallelism = processorCount / 2 }, renderTask =>
                        {
                            try
                            {
                                renderTask.task.SetResult(Connector.RenderImmediate(renderTask.settings));
                            }
                            catch (Exception ex)
                            {
                                renderTask.task.SetException(ex);
                            }
                        });
                    timeElapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    if (timeElapsed > Thundagun.Config.GetValue(Thundagun.TimeoutWorkInterval) && SynchronizationManager.Timeout)
                    {
                        break;
                    }
                }
                if (batch.IsComplete && batch.Tasks.Count == 0)
                {
                    batchQueue.Dequeue();
                }
                timeElapsed = (DateTime.Now - startTime).TotalMilliseconds;
                if (timeElapsed > Thundagun.Config.GetValue(Thundagun.TimeoutWorkInterval) && SynchronizationManager.Timeout)
                {
                    break;
                }
            }


            if (renderingContext.HasValue)
            {
                RenderHelper.BeginRenderContext(renderingContext.Value);
            }
        }
    }
}

public class Batch
{
    public Queue<RenderTask> Tasks { get; private set; } = new();
    public bool IsComplete { get; private set; } = false;

    public void AddTask(RenderTask task)
    {
        Tasks.Enqueue(task);
    }

    public void MarkComplete()
    {
        IsComplete = true;
    }
}