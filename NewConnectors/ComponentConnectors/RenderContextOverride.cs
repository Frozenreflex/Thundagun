#region

using System;
using System.Collections.Generic;
using System.Linq;
using FrooxEngine;
using UnityFrooxEngineRunner;
using MaterialConnector = Thundagun.NewConnectors.AssetConnectors.MaterialConnector;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public abstract class RenderContextOverride<D> : ComponentConnectorSingle<D>
    where D : ImplementableComponent<IConnector>
{
    public RenderingContextHandler _handler;

    public bool _isOverriden;

    public RenderingContext? _overridenContext;

    public RenderingContext? _registeredContext { get; set; }

    public abstract RenderingContext Context { get; }

    public override void ApplyChanges()
    {
        Thundagun.QueuePacket(new ApplyChangesRenderContextOverrideConnector<D>(Owner));
    }

    protected abstract void Override();

    protected abstract void Restore();

    public abstract void UpdateSetup();

    public override IUpdatePacket InitializePacket()
    {
        return new InitializeRenderContextOverrideConnector<D>(this);
    }

    public override void DestroyMethod(bool destroyingWorld)
    {
        UnregisterHandler();
        base.DestroyMethod(destroyingWorld);
    }

    public void RunOverride()
    {
        if (_isOverriden) throw new Exception("RenderTransform is already overriden!");

        _isOverriden = true;
        _overridenContext = RenderHelper.CurrentRenderingContext;
        Override();
    }

    public void RunRestore()
    {
        if (!_isOverriden) throw new Exception("RenderTransform is not overriden");

        Restore();
        _isOverriden = false;
        _overridenContext = null;
    }

    public void HandleRenderingContextSwitch(RenderingContextStage stage)
    {
        switch (stage)
        {
            case RenderingContextStage.Begin:
                RunOverride();
                break;
            case RenderingContextStage.End:
                if (_isOverriden) RunRestore();
                break;
        }
    }

    public void UnregisterHandler()
    {
        if (_registeredContext.HasValue)
        {
            RenderHelper.UnregisterRenderContextEvents(_registeredContext.Value, _handler);
            _registeredContext = null;
        }
    }
}

public class
    InitializeRenderContextOverrideConnector<D> : InitializeComponentConnectorSingle<D, RenderContextOverride<D>>
    where D : ImplementableComponent<IConnector>
{
    public InitializeRenderContextOverrideConnector(RenderContextOverride<D> owner) : base(owner, owner.Owner)
    {
        Owner._handler = Owner.HandleRenderingContextSwitch;
    }

    public override void Update()
    {
        base.Update();
    }
}

public class ApplyChangesRenderContextOverrideConnector<D> : UpdatePacket<D>
    where D : ImplementableComponent<IConnector>
{
    private RenderingContext? _currentRenderingContext;
    private readonly RenderingContextHandler _handler;
    private bool _isOverriden;
    private RenderingContext? _overridenContext;
    private readonly RenderingContext? _renderingContext;
    public RenderContextOverride<D> RenderContextOverride;

    public ApplyChangesRenderContextOverrideConnector(D owner) : base(owner)
    {
        RenderContextOverride = owner.Connector as RenderContextOverride<D>;
        _renderingContext = owner.Enabled && owner.Slot.IsActive
            ? new RenderingContext?(RenderContextOverride.Context)
            : null;
        if (_renderingContext == RenderingContext.UserView && !Owner.IsUnderLocalUser) _renderingContext = null;
        _registeredContext = RenderContextOverride._registeredContext;
        _handler = RenderContextOverride._handler;
        _isOverriden = RenderContextOverride._isOverriden;
        _overridenContext = RenderContextOverride._overridenContext;
        _currentRenderingContext = RenderHelper.CurrentRenderingContext;
        if (owner is RenderTransformOverride rto)
        {
            var rtoConn = rto.Connector as RenderTransformOverrideConnector;
            rtoConn._renderersDirty = rto.RenderersDirty;
            rto.RenderersDirty = true;
            rtoConn._renderers = rto.SkinnedMeshRenderers.ToList();

            foreach (var skinnedMeshRenderer in rto.SkinnedMeshRenderers)
            {
                if (skinnedMeshRenderer == null) continue;

                if (skinnedMeshRenderer.Connector is not SkinnedMeshRendererConnector skinnedMeshRendererConnector)
                    rto.RenderersDirty = false;
            }

            rtoConn.TargetPosition = rtoConn.Owner.PositionOverride.Value?.ToUnity();
            rtoConn.TargetRotation = rtoConn.Owner.RotationOverride.Value?.ToUnity();
            rtoConn.TargetScale = rtoConn.Owner.ScaleOverride.Value?.ToUnity();
        }
        else if (owner is RenderMaterialOverride rmo)
        {
            var rmoConn = rmo.Connector as RenderMaterialOverrideConnector;
            rmoConn.mesh = rmo.Renderer.Target?.Connector as IRendererConnector;
            rmoConn.OverridesCount = rmo.Overrides.Count;
            List<RenderMaterialOverrideConnector.MaterialOverride> list = new();
            foreach (var rmoOverride in rmo.Overrides)
            {
                var materialConnector = rmoOverride.Material.Target?.Asset?.Connector as MaterialConnector;
                list.Add(new RenderMaterialOverrideConnector.MaterialOverride
                    { index = rmoOverride.Index.Value, replacement = materialConnector?.UnityMaterial });
            }

            rmoConn.RmoOverrides = list;
        }
    }

    protected RenderingContext? _registeredContext { get; private set; }
    protected RenderingContext Context { get; }

    public override void Update()
    {
        if (RenderContextOverride._registeredContext != _renderingContext)
        {
            if (RenderContextOverride._isOverriden) RenderContextOverride.RunRestore();
            RenderContextOverride.UnregisterHandler();
            if (_renderingContext.HasValue) RenderHelper.RegisterRenderContextEvents(_renderingContext.Value, _handler);
            RenderContextOverride._registeredContext = _renderingContext;
        }

        RenderContextOverride.UpdateSetup();
        if (RenderContextOverride._isOverriden)
            throw new InvalidOperationException("RenderTransform is overriden while being updated. " +
                                                $"Current: {RenderHelper.CurrentRenderingContext}, Overriden: {RenderContextOverride._overridenContext}");

        if (RenderContextOverride._registeredContext.HasValue &&
            RenderHelper.CurrentRenderingContext == RenderContextOverride._registeredContext)
            RenderContextOverride.RunOverride();
    }
}