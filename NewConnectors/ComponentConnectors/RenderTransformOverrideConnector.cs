#region

using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using SkinnedMeshRenderer = FrooxEngine.SkinnedMeshRenderer;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class RenderTransformOverrideConnector : RenderContextOverride<RenderTransformOverride>
{
    private Vector3? _originalPosition;
    private Quaternion? _originalRotation;
    private Vector3? _originalScale;
    private readonly HashSet<SkinnedMeshRendererConnector> _registeredSkinnedConnectors = new();
    public List<SkinnedMeshRenderer> _renderers;
    public bool _renderersDirty;
    public Vector3? TargetPosition;
    public Quaternion? TargetRotation;
    public Vector3? TargetScale;
    public override RenderingContext Context => Owner.Context.Value;

    public override void UpdateSetup()
    {
        if (!_registeredContext.HasValue) ClearRecalcRequests();
    }

    public override void DestroyMethod(bool destroyingWorld)
    {
        if (!destroyingWorld) ClearRecalcRequests();
        base.DestroyMethod(destroyingWorld);
    }

    private void ClearRecalcRequests()
    {
        foreach (var registeredSkinnedConnector in _registeredSkinnedConnectors)
            registeredSkinnedConnector.RemoveRequestForceRecalcPerRender(this);
        _registeredSkinnedConnectors.Clear();
    }

    protected override void Override()
    {
        if (_renderersDirty)
        {
            var flag = true;
            var hashSet = Pool.BorrowHashSet<SkinnedMeshRendererConnector>();
            foreach (var registeredSkinnedConnector in _registeredSkinnedConnectors)
                hashSet.Add(registeredSkinnedConnector);
            foreach (var skinnedMeshRenderer in _renderers)
            {
                if (skinnedMeshRenderer == null) continue;

                if (skinnedMeshRenderer.Connector is SkinnedMeshRendererConnector skinnedMeshRendererConnector)
                {
                    if (!hashSet.Remove(skinnedMeshRendererConnector))
                    {
                        skinnedMeshRendererConnector.RequestForceRecalcPerRender(this);
                        _registeredSkinnedConnectors.Add(skinnedMeshRendererConnector);
                    }
                }
                else
                {
                    flag = false;
                }
            }

            foreach (var item in hashSet)
            {
                item.RemoveRequestForceRecalcPerRender(this);
                _registeredSkinnedConnectors.Remove(item);
            }

            Pool.Return(ref hashSet);
            if (flag)
            {
            }
        }

        var transform = AttachedGameObject.transform;
        if (TargetPosition.HasValue)
        {
            _originalPosition = transform.localPosition;
            transform.localPosition = TargetPosition.Value;
        }
        else
        {
            _originalPosition = null;
        }

        if (TargetRotation.HasValue)
        {
            _originalRotation = transform.localRotation;
            transform.localRotation = TargetRotation.Value;
        }
        else
        {
            _originalRotation = null;
        }

        if (TargetScale.HasValue)
        {
            _originalScale = transform.localScale;
            transform.localScale = TargetScale.Value;
        }
        else
        {
            _originalScale = null;
        }
    }

    protected override void Restore()
    {
        var transform = AttachedGameObject.transform;
        if (_originalPosition.HasValue) transform.localPosition = _originalPosition.Value;
        if (_originalRotation.HasValue) transform.localRotation = _originalRotation.Value;
        if (_originalScale.HasValue) transform.localScale = _originalScale.Value;
    }
}