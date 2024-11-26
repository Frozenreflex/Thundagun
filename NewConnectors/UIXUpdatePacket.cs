using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using uDesktopDuplication;
using uWindowCapture;

namespace Thundagun;

public class UIXUpdatePacket : UpdatePacket<FrooxEngine.UIX.Canvas>
{
    public UIXUpdatePacket(FrooxEngine.UIX.Canvas owner) : base(owner)
    {
		owner.UpdateCycleIndex++;
		owner.UpdateCycleStart = DateTime.UtcNow;
		owner._cachedWorld = owner.World;
		foreach (Action removal in owner._removals)
		{
			removal();
		}
		owner._removals.Clear();
		owner.PrepareCanvasUpdate();
    }

    public override void Update()
	{
		Task.Run(async () => 
		{ 
			await default(ToBackground);
			try
			{
				if (Owner._pruneDisabledTransforms)
				{
					Owner._computeDirtyTransforms.RemoveAll(Owner.ShouldPrune);
				}
				foreach (RectTransform changedChildrenTransform in Owner._changedChildrenTransforms)
				{
					changedChildrenTransform.ProcessChangedChildren();
				}
				Owner._changedChildrenTransforms.Clear();
				if (Owner.IsRemoved)
				{
					return;
				}
				RectTransform result;
				while (Owner._autoInvalidateRects.TryTake(out result))
				{
					result.MarkComputeRectChangedAndPropagate();
				}
				foreach (RectTransform computeDirtyTransform in Owner._computeDirtyTransforms)
				{
					computeDirtyTransform.ProcessChanges();
					bool hasGraphicsChunk = computeDirtyTransform.HasGraphicsChunk;
					if (hasGraphicsChunk && computeDirtyTransform.StructureChanged)
					{
						Owner._graphicChunksDirty = true;
					}
					if (hasGraphicsChunk != computeDirtyTransform.RequiresGraphicChunk)
					{
						Owner._graphicChunksDirty = true;
						if (computeDirtyTransform.RequiresGraphicChunk)
						{
							Owner._graphicChunks.Add(new GraphicsChunk(Owner, computeDirtyTransform));
							continue;
						}
						GraphicsChunk graphicsChunk = computeDirtyTransform.GraphicsChunk;
						graphicsChunk.Unregister();
						Owner._graphicChunks.Remove(graphicsChunk);
						Owner._removedGraphicChunks.Add(graphicsChunk);
					}
				}
				if (Owner.IsRemoved)
				{
					return;
				}
				List<ValueTask> precomputes = Pool.BorrowList<ValueTask>();
				foreach (RectTransform computeDirtyTransform2 in Owner._computeDirtyTransforms)
				{
					if (computeDirtyTransform2.RequiresPreLayoutCompute)
					{
						precomputes.Add(computeDirtyTransform2.RunPreLayoutCompute());
					}
				}
				foreach (ValueTask item in precomputes)
				{
					await item.ConfigureAwait(continueOnCapturedContext: false);
				}
				await default(ToBackground);
				precomputes.Clear();
				if (Owner._root != null)
				{
					float2 position = Owner._size * -0.5f;
					Rect parentRect = new Rect(in position, in Owner._size);
					Owner._root.ComputeRect(ref parentRect, null);
				}
				foreach (RectTransform computeDirtyTransform3 in Owner._computeDirtyTransforms)
				{
					if (computeDirtyTransform3.RequiresPreGraphicsCompute)
					{
						precomputes.Add(computeDirtyTransform3.RunPreGraphicsCompute());
					}
				}
				Owner.EnsureSortedGraphicsChunks();
				foreach (ValueTask item2 in precomputes)
				{
					await item2.ConfigureAwait(continueOnCapturedContext: false);
				}
				Pool.Return(ref precomputes);
				await default(ToBackground);
				if (Owner.IsRemoved)
				{
					return;
				}
				foreach (GraphicsChunk graphicChunk in Owner._graphicChunks)
				{
					if (graphicChunk.IsEnabled)
					{
						await graphicChunk.WaitForLastUpload().ConfigureAwait(continueOnCapturedContext: false);
					}
				}
				if (Owner._debugChunk != null)
				{
					await Owner._debugChunk.WaitForLastUpload().ConfigureAwait(continueOnCapturedContext: false);
				}
				if (Owner.IsRemoved)
				{
					return;
				}
				List<Task> tasks = Pool.BorrowList<Task>();
				foreach (GraphicsChunk graphicChunk2 in Owner._graphicChunks)
				{
					if (graphicChunk2.IsEnabled)
					{
						tasks.Add(graphicChunk2.ComputeGraphics());
					}
				}
				if (Owner._debugChunk != null)
				{
					tasks.Add(Owner._debugChunk.ComputeGraphics());
				}
				foreach (Task item3 in tasks)
				{
					await item3.ConfigureAwait(continueOnCapturedContext: false);
				}
				Pool.Return(ref tasks);
				if (!Owner.IsRemoved && Owner._root != null)
				{
					Owner._root.UpdateBoundsAndClear(float2.Zero);
				}
			}
			finally
			{
				Owner._cachedWorld.RunSynchronously(Owner._finishCanvasUpdate, immediatellyIfPossible: false, Owner, evenIfDisposed: true);
			}
		});
	}
}