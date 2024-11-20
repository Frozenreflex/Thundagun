#region

using System;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using UnityFrooxEngineRunner;
using MaterialPropertyBlock = UnityEngine.MaterialPropertyBlock;
using Texture2D = UnityEngine.Texture2D;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public class MaterialPropertyBlockConnector : MaterialConnectorBase, IMaterialPropertyBlockConnector
{
    private MaterialPropertyBlock _unityBlock;

    public MaterialPropertyBlock UnityBlock => _unityBlock;

    void IMaterialPropertyBlockConnector.ApplyChanges(AssetIntegrated onDone)
    {
        ApplyChanges(onDone);
    }

    public override void Unload()
    {
        UnityAssetIntegrator.EnqueueProcessing(Destroy, false);
    }

    void ISharedMaterialPropertySetter.SetFloat4(int property, in float4 value)
    {
        SetFloat4(property, in value);
    }

    void ISharedMaterialPropertySetter.SetMatrix(int property, in float4x4 matrix)
    {
        SetMatrix(property, in matrix);
    }

    protected override bool BeginUpload(ref bool instanceChanged)
    {
        _unityBlock ??= Pool<MaterialPropertyBlock>.Borrow();
        instanceChanged = true;
        return true;
    }

    protected override void ApplyAction(ref MaterialAction action)
    {
        switch (action.type)
        {
            case ActionType.Flag:
            case ActionType.Tag:
            case ActionType.RenderQueue:
            case ActionType.Instancing:
                throw new InvalidOperationException("Invalid operation for MaterialPropertyBlock: " + action);
            case ActionType.Float:
                UnityBlock.SetFloat(action.propertyIndex, action.float4Value.x);
                break;
            case ActionType.Float4:
                UnityBlock.SetVector(action.propertyIndex, action.float4Value.ToUnity());
                break;
            case ActionType.FloatArray:
                UnityBlock.SetFloatArray(action.propertyIndex, (List<float>)action.obj);
                break;
            case ActionType.Float4Array:
            {
                var list = GetUnityVectorArray(ref action);
                UnityBlock.SetVectorArray(action.propertyIndex, list);
                Pool.Return(ref list);
                break;
            }
            case ActionType.Matrix:
            {
                var unityBlock = UnityBlock;
                var propertyIndex = action.propertyIndex;
                var m = GetMatrix(ref action);
                unityBlock.SetMatrix(propertyIndex, m.ToUnity());
                break;
            }
            case ActionType.Texture:
                UnityBlock.SetTexture(action.propertyIndex,
                    (action.obj as ITexture)?.GetUnity() ?? Texture2D.whiteTexture);
                break;
        }
    }

    private void Destroy()
    {
        if (_unityBlock == null) return;
        _unityBlock.Clear();
        Pool<MaterialPropertyBlock>.ReturnCleaned(ref _unityBlock);
    }
}