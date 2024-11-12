using System.Collections.Generic;
using System.Linq;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityFrooxEngineRunner;
using Mesh = UnityEngine.Mesh;
using MeshConnector = Thundagun.NewConnectors.AssetConnectors.MeshConnector;
using MaterialConnector = Thundagun.NewConnectors.AssetConnectors.MaterialConnector;
using MaterialPropertyBlockConnector = Thundagun.NewConnectors.AssetConnectors.MaterialPropertyBlockConnector;

namespace Thundagun.NewConnectors.ComponentConnectors;

public abstract class MeshRendererConnectorBase<T, TU> : ComponentConnectorSingle<T>, IRendererConnector
    where T : FrooxEngine.MeshRenderer
    where TU : Renderer
{
    public bool UsesMaterialPropertyBlocks;
    public UnityEngine.Material[] UnityMaterials;
    public MeshFilter MeshFilter;

    public abstract bool UseMeshFilter { get; }

    public abstract void AssignMesh(TU renderer, Mesh mesh);

    public int MaterialCount;

    UnityEngine.Renderer IRendererConnector.Renderer => MeshRenderer;

    public TU MeshRenderer { get; set; }

    public virtual void OnAttachRenderer()
    {
    }

    public virtual void OnCleanupRenderer()
    {
    }

    public override void ApplyChanges() => Thundagun.QueuePacket(new ApplyChangesMeshRendererConnectorBase<T, TU>(this));

    public void CleanupRenderer(bool destroyingWorld)
    {
        if (!destroyingWorld && MeshRenderer != null && MeshRenderer.gameObject)
            Object.Destroy(MeshRenderer.gameObject);
        MeshRenderer = default;
        OnCleanupRenderer();
    }

    public override void DestroyMethod(bool destroyingWorld)
    {
        CleanupRenderer(destroyingWorld);
        UnityMaterials = null;
        MeshFilter = null;
        MeshRenderer = default;
        base.DestroyMethod(destroyingWorld);
    }

    public struct Renderer
    {
        public GameObject GameObject;
        public MeshFilter MeshFilter;
        public TU MeshRenderer;
    }
}

public class ApplyChangesMeshRendererConnectorBase<T, TU> : UpdatePacket<MeshRendererConnectorBase<T, TU>>
    where T : FrooxEngine.MeshRenderer
    where TU : Renderer
{
    public bool ShouldBeEnabled;
    public bool MeshWasChanged;
    public MeshConnector Mesh;
    public bool MaterialsChanged;
    public bool IsLocalElement;
    public List<IMaterialConnector> Materials;
    public bool MaterialPropertyBlocksChanged;
    public List<IMaterialPropertyBlockConnector> MaterialPropertyBlocks;
    public bool Enabled;
    public bool SortingOrderChanged;
    public int SortingOrder;
    public bool ShadowCastingModeChanged;
    public ShadowCastingMode ShadowCastingMode;
    public bool MotionVectorModeChanged;
    public MotionVectorGenerationMode MotionVectorMode;

    public ApplyChangesMeshRendererConnectorBase(MeshRendererConnectorBase<T, TU> owner) : base(owner)
    {
        ShouldBeEnabled = owner.Owner.ShouldBeEnabled;
        if (ShouldBeEnabled)
        {
            MeshWasChanged = owner.Owner.Mesh.GetWasChangedAndClear() || owner.MeshRenderer == null;
            Mesh = owner.Owner.Mesh?.Asset?.Connector as MeshConnector;
            MaterialsChanged = owner.Owner.MaterialsChanged;
            IsLocalElement = Owner.Owner.IsLocalElement;
            MaterialPropertyBlocksChanged = owner.Owner.MaterialPropertyBlocksChanged;
            if (MaterialsChanged || MeshWasChanged)
            {
                owner.Owner.MaterialsChanged = false;
            }
            if (MaterialsChanged || MeshWasChanged || MaterialPropertyBlocksChanged)
            {
                owner.Owner.MaterialPropertyBlocksChanged = false;
            }
            Materials = owner.Owner.Materials.Select(i => i?.Asset?.Connector).ToList();
            MaterialPropertyBlocks = owner.Owner.MaterialPropertyBlocks.Select(i => i?.Asset?.Connector).ToList();
            Enabled = owner.Owner.Enabled;
            SortingOrderChanged = owner.Owner.SortingOrder.GetWasChangedAndClear();
            SortingOrder = owner.Owner.SortingOrder.Value;
            ShadowCastingModeChanged = owner.Owner.ShadowCastMode.GetWasChangedAndClear();
            ShadowCastingMode = owner.Owner.ShadowCastMode.Value.ToUnity();
            MotionVectorModeChanged = owner.Owner.MotionVectorMode.GetWasChangedAndClear();
            MotionVectorMode = owner.Owner.MotionVectorMode.Value.ToUnity();
        }
    }

    public override void Update()
    {
        if (!ShouldBeEnabled)
        {
            Owner.CleanupRenderer(false);
        }
        else
        {
            bool instantiated = false;
            if (Owner.MeshRenderer == null)
            {
                var gameObject = new GameObject("");
                gameObject.transform.SetParent(Owner.AttachedGameObject.transform, false);
                gameObject.layer = Owner.AttachedGameObject.layer;
                if (Owner.UseMeshFilter)
                    Owner.MeshFilter = gameObject.AddComponent<MeshFilter>();
                Owner.MeshRenderer = gameObject.AddComponent<TU>();
                Owner.OnAttachRenderer();
                instantiated = true;
            }

            if (MeshWasChanged || instantiated)
            {
                var unity = Mesh?.Mesh;
                if (Owner.UseMeshFilter) Owner.MeshFilter.sharedMesh = unity;
                else Owner.AssignMesh(Owner.MeshRenderer, unity);
            }

            var flag = false;
            if (MaterialsChanged || MeshWasChanged)
            {
                flag = true;
                Owner.MaterialCount = 1;
                var nullMaterial = IsLocalElement
                    ? MaterialConnector.InvisibleMaterial
                    : MaterialConnector.NullMaterial;
                if (Materials.Count > 1 || Owner.UnityMaterials != null)
                {
                    Owner.UnityMaterials = Owner.UnityMaterials.EnsureExactSize(Materials.Count, allowZeroSize: true);
                    for (var i = 0; i < Owner.UnityMaterials.Length; i++)
                        Owner.UnityMaterials[i] = (Materials[i] as MaterialConnector)?.UnityMaterial ?? nullMaterial;
                    Owner.MeshRenderer.sharedMaterials = Owner.UnityMaterials;
                    Owner.MaterialCount = Owner.UnityMaterials.Length;
                }
                else if (Materials.Count == 1)
                    Owner.MeshRenderer.sharedMaterial = (Materials[0] as MaterialConnector)?.UnityMaterial ?? nullMaterial;
                else
                    Owner.MeshRenderer.sharedMaterial = nullMaterial;
            }

            if (MaterialPropertyBlocksChanged || flag)
            {
                if (MaterialPropertyBlocks.Count > 0)
                {
                    for (var i = 0; i < Owner.MaterialCount; i++)
                    {
                        if (i < MaterialPropertyBlocks.Count)
                        {
                            var materialPropertyBlock = MaterialPropertyBlocks[i] as MaterialPropertyBlockConnector;
                            var unity = materialPropertyBlock?.UnityBlock;
                            Owner.MeshRenderer.SetPropertyBlock(unity, i);
                        }
                        else
                            Owner.MeshRenderer.SetPropertyBlock(null, i);
                    }

                    Owner.UsesMaterialPropertyBlocks = true;
                }
                else if (Owner.UsesMaterialPropertyBlocks)
                {
                    for (var i = 0; i < Owner.MaterialCount; i++) Owner.MeshRenderer.SetPropertyBlock(null, i);
                    Owner.UsesMaterialPropertyBlocks = false;
                }
            }

            if (Owner.MeshRenderer.enabled != Enabled) Owner.MeshRenderer.enabled = Enabled;
            if (SortingOrderChanged || instantiated) Owner.MeshRenderer.sortingOrder = SortingOrder;
            if (ShadowCastingModeChanged || instantiated) Owner.MeshRenderer.shadowCastingMode = ShadowCastingMode;
            if (MotionVectorModeChanged || instantiated) Owner.MeshRenderer.motionVectorGenerationMode = MotionVectorMode;
            OnUpdateRenderer(instantiated);
        }
    }
    public virtual void OnUpdateRenderer(bool instantiated)
    {
    }
}