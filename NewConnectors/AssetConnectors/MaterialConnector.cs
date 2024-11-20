#region

using System;
using System.Collections.Generic;
using Elements.Core;
using FrooxEngine;
using UnityFrooxEngineRunner;
using Material = UnityEngine.Material;
using Object = UnityEngine.Object;
using Shader = FrooxEngine.Shader;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public class MaterialConnector : MaterialConnectorBase, IMaterialConnector
{
    private static readonly Lazy<UnityEngine.Shader> _null = new(() => UnityEngine.Shader.Find("BuiltIn/Null"));

    private static readonly Lazy<UnityEngine.Shader> _invisible =
        new(() => UnityEngine.Shader.Find("BuiltIn/Invisible"));

    private static readonly Lazy<Material> _nullMaterial = new(() => new Material(NullShader));

    private static readonly Lazy<Material> _invisibleMaterial = new(() => new Material(InvisibleShader));

    private Shader targetShader;

    public static UnityEngine.Shader NullShader => _null.Value;

    public static UnityEngine.Shader InvisibleShader => _invisible.Value;

    public static Material NullMaterial => _nullMaterial.Value;

    public static Material InvisibleMaterial => _invisibleMaterial.Value;

    public Material UnityMaterial { get; private set; }

    public void ApplyChanges(Shader shader, AssetIntegrated onDone)
    {
        targetShader = shader;
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
        var shader = (targetShader?.Connector as ShaderConnector)?.UnityShader;
        if (shader == null)
        {
            if (UnityMaterial != null)
            {
                instanceChanged = true;
                CleanupMaterial();
            }

            return false;
        }

        if (UnityMaterial == null)
        {
            UnityMaterial = new Material(shader);
            instanceChanged = true;
        }
        else if (UnityMaterial.shader != shader)
        {
            UnityMaterial.shader = shader;
        }

        return true;
    }

    protected override void ApplyAction(ref MaterialAction action)
    {
        switch (action.type)
        {
            case ActionType.Flag:
                UnityMaterial.SetKeyword((string)action.obj, action.float4Value.x > 0f);
                break;
            case ActionType.Instancing:
                UnityMaterial.enableInstancing = action.float4Value.x > 0f;
                break;
            case ActionType.RenderQueue:
                UnityMaterial.renderQueue = (int)action.float4Value.x;
                break;
            case ActionType.Tag:
            {
                var propertyIndex2 = (MaterialTag)action.propertyIndex;
                if (propertyIndex2 != MaterialTag.RenderType)
                    throw new ArgumentException("Unknown material tag: " + propertyIndex2);

                UnityMaterial.SetOverrideTag("RenderType", action.obj as string);
                break;
            }
            case ActionType.Float:
                UnityMaterial.SetFloat(action.propertyIndex, action.float4Value.x);
                break;
            case ActionType.Float4:
                UnityMaterial.SetVector(action.propertyIndex, action.float4Value.ToUnity());
                break;
            case ActionType.FloatArray:
                UnityMaterial.SetFloatArray(action.propertyIndex, (List<float>)action.obj);
                break;
            case ActionType.Float4Array:
            {
                var list = GetUnityVectorArray(ref action);
                UnityMaterial.SetVectorArray(action.propertyIndex, list);
                Pool.Return(ref list);
                break;
            }
            case ActionType.Matrix:
            {
                var unityMaterial = UnityMaterial;
                var propertyIndex = action.propertyIndex;
                var m = GetMatrix(ref action);
                unityMaterial.SetMatrix(propertyIndex, m.ToUnity());
                break;
            }
            case ActionType.Texture:
                UnityMaterial.SetTexture(action.propertyIndex, (action.obj as ITexture)?.GetUnity());
                break;
        }
    }

    private void Destroy()
    {
        CleanupMaterial();
    }

    private void CleanupMaterial()
    {
        if (UnityMaterial == null) return;

        if ((bool)UnityMaterial) Object.DestroyImmediate(UnityMaterial, true);
        UnityMaterial = null;
    }
}