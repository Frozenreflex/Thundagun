using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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