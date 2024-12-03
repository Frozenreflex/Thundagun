#region

using System;
using System.Threading.Tasks;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;

#endregion

namespace Thundagun;

public class UIXUpdatePacket : UpdatePacket<Canvas>
{
    public UIXUpdatePacket(Canvas owner) : base(owner)
    {
        owner.RunInUpdates(0, async () => 
        {
			owner.UpdateCycleIndex++;
			owner.UpdateCycleStart = DateTime.UtcNow;
			owner._cachedWorld = owner.World;
			foreach (var removal in owner._removals) removal();
			owner._removals.Clear();
			owner.PrepareCanvasUpdate();
			await default(ToBackground);
			try
			{
				if (Owner._pruneDisabledTransforms) Owner._computeDirtyTransforms.RemoveAll(Owner.ShouldPrune);
				foreach (var changedChildrenTransform in Owner._changedChildrenTransforms)
					changedChildrenTransform.ProcessChangedChildren();
				Owner._changedChildrenTransforms.Clear();
				if (Owner.IsRemoved) return;
				RectTransform result;
				while (Owner._autoInvalidateRects.TryTake(out result)) result.MarkComputeRectChangedAndPropagate();
				foreach (var computeDirtyTransform in Owner._computeDirtyTransforms)
				{
					computeDirtyTransform.ProcessChanges();
					var hasGraphicsChunk = computeDirtyTransform.HasGraphicsChunk;
					if (hasGraphicsChunk && computeDirtyTransform.StructureChanged) Owner._graphicChunksDirty = true;
					if (hasGraphicsChunk != computeDirtyTransform.RequiresGraphicChunk)
					{
						Owner._graphicChunksDirty = true;
						if (computeDirtyTransform.RequiresGraphicChunk)
						{
							Owner._graphicChunks.Add(new GraphicsChunk(Owner, computeDirtyTransform));
							continue;
						}

						var graphicsChunk = computeDirtyTransform.GraphicsChunk;
						graphicsChunk.Unregister();
						Owner._graphicChunks.Remove(graphicsChunk);
						Owner._removedGraphicChunks.Add(graphicsChunk);
					}
				}

				if (Owner.IsRemoved) return;
				var precomputes = Pool.BorrowList<ValueTask>();
				foreach (var computeDirtyTransform2 in Owner._computeDirtyTransforms)
					if (computeDirtyTransform2.RequiresPreLayoutCompute)
						precomputes.Add(computeDirtyTransform2.RunPreLayoutCompute());

				foreach (var item in precomputes) await item.ConfigureAwait(false);
				await default(ToBackground);
				precomputes.Clear();
				if (Owner._root != null)
				{
					var position = Owner._size * -0.5f;
					var parentRect = new Rect(in position, in Owner._size);
					Owner._root.ComputeRect(ref parentRect, null);
				}

				foreach (var computeDirtyTransform3 in Owner._computeDirtyTransforms)
					if (computeDirtyTransform3.RequiresPreGraphicsCompute)
						precomputes.Add(computeDirtyTransform3.RunPreGraphicsCompute());

				Owner.EnsureSortedGraphicsChunks();
				foreach (var item2 in precomputes) await item2.ConfigureAwait(false);
				Pool.Return(ref precomputes);
				await default(ToBackground);
				if (Owner.IsRemoved) return;
				foreach (var graphicChunk in Owner._graphicChunks)
					if (graphicChunk.IsEnabled)
						await graphicChunk.WaitForLastUpload().ConfigureAwait(false);

				if (Owner._debugChunk != null) await Owner._debugChunk.WaitForLastUpload().ConfigureAwait(false);
				if (Owner.IsRemoved) return;
				var tasks = Pool.BorrowList<Task>();
				foreach (var graphicChunk2 in Owner._graphicChunks)
					if (graphicChunk2.IsEnabled)
						tasks.Add(graphicChunk2.ComputeGraphics());

				if (Owner._debugChunk != null) tasks.Add(Owner._debugChunk.ComputeGraphics());
				foreach (var item3 in tasks) await item3.ConfigureAwait(false);
				Pool.Return(ref tasks);
				if (!Owner.IsRemoved && Owner._root != null) Owner._root.UpdateBoundsAndClear(float2.Zero);
			}
			finally
			{
				Owner._cachedWorld.RunSynchronously(Owner._finishCanvasUpdate, false, Owner, true);
			}
		});
    }

    public override void Update()
    {
    }
}