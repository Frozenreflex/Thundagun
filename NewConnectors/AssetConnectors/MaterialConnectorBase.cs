#region

using System;
using System.Collections.Generic;
using System.Linq;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;
using Shader = UnityEngine.Shader;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public abstract class MaterialConnectorBase : AssetConnector, ISharedMaterialConnector
{
    private Queue<MaterialAction> actionQueue;

    private RawValueList<float4x4> matrices;

    private AssetIntegrated onDone;

    private Action uploadMaterialAction;

    public void InitializeProperties(List<MaterialProperty> properties, Action onDone)
    {
        if (properties != null)
            foreach (var property in properties.Where(property => !property.Initialized))
                property.Initialize(Shader.PropertyToID(property.Name));
        onDone();
    }

    public void SetFloat(int property, float value)
    {
        var float4Value = new float4(value);
        var action = new MaterialAction(ActionType.Float, property, in float4Value);
        Enqueue(in action);
    }

    public void SetFloatArray(int property, List<float> values)
    {
        var float4Value = float4.Zero;
        var action = new MaterialAction(ActionType.FloatArray, property, in float4Value, values);
        Enqueue(in action);
    }

    public void SetFloat4Array(int property, List<float4> values)
    {
        var float4Value = float4.Zero;
        var action = new MaterialAction(ActionType.Float4Array, property, in float4Value, values);
        Enqueue(in action);
    }

    public void SetTexture(int property, ITexture texture)
    {
        var float4Value = float4.Zero;
        var action = new MaterialAction(ActionType.Texture, property, in float4Value, texture);
        Enqueue(in action);
    }

    public void SetDebug(bool debug, string tag)
    {
    }

    void ISharedMaterialPropertySetter.SetFloat4(int property, in float4 value)
    {
        SetFloat4(property, in value);
    }

    void ISharedMaterialPropertySetter.SetMatrix(int property, in float4x4 matrix)
    {
        SetMatrix(property, in matrix);
    }

    protected void ApplyChanges(AssetIntegrated onDone)
    {
        uploadMaterialAction ??= UploadMaterial;
        this.onDone = onDone;
        UnityAssetIntegrator.EnqueueProcessing(uploadMaterialAction, Asset.HighPriorityIntegration);
    }

    private void Enqueue(in MaterialAction action)
    {
        actionQueue ??= Pool.BorrowQueue<MaterialAction>();
        actionQueue.Enqueue(action);
    }

    private int StoreMatrix(in float4x4 matrix)
    {
        matrices ??= Pool.BorrowRawValueList<float4x4>();
        matrices.Add(in matrix);
        return matrices.Count - 1;
    }

    protected float4x4 GetMatrix(ref MaterialAction action)
    {
        return matrices[(int)action.float4Value.x];
    }

    protected List<Vector4> GetUnityVectorArray(ref MaterialAction action)
    {
        var list = Pool.BorrowList<Vector4>();
        list.AddRange(from item in (List<float4>)action.obj select item.ToUnity());
        return list;
    }

    protected abstract bool BeginUpload(ref bool instanceChanged);

    protected abstract void ApplyAction(ref MaterialAction action);

    private void UploadMaterial()
    {
        var instanceChanged = false;
        if (BeginUpload(ref instanceChanged))
            while (actionQueue != null && actionQueue.Count > 0)
            {
                var action = actionQueue.Dequeue();
                ApplyAction(ref action);
            }

        if (actionQueue != null) Pool.Return(ref actionQueue);
        if (matrices != null) Pool.Return(ref matrices);
        onDone(instanceChanged);
        onDone = null;
        Engine.MaterialUpdated();
    }

    public void SetFlag(string flag, bool state)
    {
        var float4Value = new float4(state ? 1 : 0);
        var action = new MaterialAction(ActionType.Flag, -1, in float4Value, flag);
        Enqueue(in action);
    }

    public void SetInstancing(bool state)
    {
        var float4Value = new float4(state ? 1 : 0);
        var action = new MaterialAction(ActionType.Instancing, -1, in float4Value);
        Enqueue(in action);
    }

    public void SetRenderQueue(int renderQueue)
    {
        var float4Value = new float4(renderQueue);
        var action = new MaterialAction(ActionType.RenderQueue, -1, in float4Value);
        Enqueue(in action);
    }

    public void SetTag(MaterialTag tag, string value)
    {
        var float4Value = float4.Zero;
        var action = new MaterialAction(ActionType.Tag, (int)tag, in float4Value, value);
        Enqueue(in action);
    }

    public void SetFloat4(int property, in float4 value)
    {
        var action = new MaterialAction(ActionType.Float4, property, in value);
        Enqueue(in action);
    }

    public void SetMatrix(int property, in float4x4 matrix)
    {
        var float4Value = new float4(StoreMatrix(in matrix));
        var action = new MaterialAction(ActionType.Matrix, property, in float4Value);
        Enqueue(in action);
    }

    protected enum ActionType
    {
        Flag,
        Tag,
        Float4,
        Float,
        Float4Array,
        FloatArray,
        Matrix,
        Texture,
        RenderQueue,
        Instancing
    }

    protected readonly struct MaterialAction
    {
        public readonly ActionType type;

        public readonly int propertyIndex;

        public readonly float4 float4Value;

        public readonly object obj;

        public MaterialAction(ActionType type, int propertyIndex, in float4 float4Value, object obj = null)
        {
            this.type = type;
            this.propertyIndex = propertyIndex;
            this.float4Value = float4Value;
            this.obj = obj;
        }
    }
}