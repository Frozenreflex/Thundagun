#region

using System.Collections.Generic;
using FrooxEngine;
using UnityFrooxEngineRunner;
using Material = UnityEngine.Material;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class RenderMaterialOverrideConnector : RenderContextOverride<RenderMaterialOverride>
{
    private readonly List<MaterialOverride> overrides = new();

    public IRendererConnector mesh;

    public int OverridesCount;

    public List<MaterialOverride> RmoOverrides;

    public override RenderingContext Context => Owner.Context.Value;

    protected override void Override()
    {
        var renderer = mesh?.Renderer;
        if (renderer == null) return;
        var sharedMaterials = renderer.sharedMaterials;
        foreach (var @override in overrides)
            if (@override.index >= 0 && @override.index < sharedMaterials.Length)
            {
                @override.original = sharedMaterials[@override.index];
                sharedMaterials[@override.index] = @override.replacement;
            }

        renderer.sharedMaterials = sharedMaterials;
    }

    protected override void Restore()
    {
        var renderer = mesh?.Renderer;
        if (renderer == null) return;
        var sharedMaterials = renderer.sharedMaterials;
        foreach (var @override in overrides)
            if (@override.index >= 0 && @override.index < sharedMaterials.Length)
            {
                sharedMaterials[@override.index] = @override.original;
                @override.original = null;
            }

        renderer.sharedMaterials = sharedMaterials;
    }

    public override void UpdateSetup()
    {
        //mesh = base.Owner.Renderer.Target?.Connector as IRendererConnector;
        while (overrides.Count > OverridesCount) overrides.RemoveAt(overrides.Count - 1);
        while (OverridesCount > overrides.Count) overrides.Add(new MaterialOverride());
        for (var i = 0; i < OverridesCount; i++)
        {
            var materialOverride = overrides[i];
            var materialOverride2 = RmoOverrides[i];
            materialOverride.index = materialOverride2.index;
            materialOverride.replacement = materialOverride2.replacement;
        }
    }

    public class MaterialOverride
    {
        public int index;

        public Material original;

        public Material replacement;
    }
}