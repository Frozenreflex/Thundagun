#region

using System.Collections.Generic;
using UnityEngine;
using UnityFrooxEngineRunner;
using LODGroup = FrooxEngine.LODGroup;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class LODGroupConnector : ComponentConnectorSingle<LODGroup>
{
    public GameObject groupGO;

    public UnityEngine.LODGroup lodGroup;

    public override IUpdatePacket InitializePacket()
    {
        return new InitializeLODGroupConnector(this);
    }

    public override void ApplyChanges()
    {
        Thundagun.QueuePacket(new ApplyChangesLODGroupConnector(this));
    }

    public override void DestroyMethod(bool destroyingWorld)
    {
        if (!destroyingWorld && (bool)groupGO) Object.Destroy(groupGO);
        groupGO = null;
        base.DestroyMethod(destroyingWorld);
    }
}

public class InitializeLODGroupConnector : InitializeComponentConnectorSingle<LODGroup, LODGroupConnector>
{
    public InitializeLODGroupConnector(LODGroupConnector owner) : base(owner, owner.Owner)
    {
    }

    public override void Update()
    {
        base.Update();
        Owner.groupGO = new GameObject("");
        Owner.groupGO.transform.SetParent(Owner.AttachedGameObject.transform, false);
        Owner.lodGroup = Owner.groupGO.AddComponent<UnityEngine.LODGroup>();
    }
}

public class ApplyChangesLODGroupConnector : UpdatePacket<LODGroupConnector>
{
    public int LODCount;
    public List<LODStruct> LODS;

    public ApplyChangesLODGroupConnector(LODGroupConnector owner) : base(owner)
    {
        LODCount = owner.Owner.LODs.Count;
        LODS = new List<LODStruct>();
        foreach (var lod in owner.Owner.LODs)
        {
            var lodStruct = new LODStruct
            {
                ScreenRelativeTransitionHeight = lod.ScreenRelativeTransitionHeight,
                FadeTransitionWidth = lod.FadeTransitionWidth,
                RenderersCount = lod.Renderers.Count
            };
            lodStruct.Renderers = new List<IRendererConnector>();
            foreach (var renderer in lod.Renderers)
                if (renderer?.Connector is IRendererConnector rendererConnector)
                    lodStruct.Renderers.Add(rendererConnector);
                else
                    lodStruct.Renderers.Add(null);

            LODS.Add(lodStruct);
        }
    }

    public override void Update()
    {
        var array = new LOD[LODCount];
        for (var i = 0; i < LODCount; i++)
        {
            var lOD = LODS[i];
            array[i].screenRelativeTransitionHeight = lOD.ScreenRelativeTransitionHeight;
            array[i].fadeTransitionWidth = lOD.FadeTransitionWidth;
            var array2 = new Renderer[lOD.RenderersCount];
            for (var j = 0; j < lOD.RenderersCount; j++) array2[j] = lOD.Renderers[j]?.Renderer;
            array[i].renderers = array2;
        }

        Owner.lodGroup.SetLODs(array);
        Owner.lodGroup.RecalculateBounds();
    }

    public struct LODStruct
    {
        public float ScreenRelativeTransitionHeight;
        public float FadeTransitionWidth;
        public int RenderersCount;
        public List<IRendererConnector> Renderers;
    }
}