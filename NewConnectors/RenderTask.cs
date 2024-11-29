#region

using System.Threading.Tasks;

#endregion

namespace Thundagun.NewConnectors;

public readonly struct RenderTask
{
    public readonly RenderSettings settings;

    public readonly TaskCompletionSource<byte[]> task;

    public RenderTask(RenderSettings settings, TaskCompletionSource<byte[]> task)
    {
        this.settings = settings;
        this.task = task;
    }
}