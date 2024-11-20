#region

using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;
using Mesh = UnityEngine.Mesh;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public class MeshConnector : AssetConnector, IMeshConnector
{
    public static volatile int MeshDataCount;
    private BoundingBox _bounds;
    private UnityMeshData _meshGenData;
    private AssetIntegrated _onLoaded;
    private MeshUploadHint _uploadHint;

    public Mesh Mesh { get; private set; }

    public void UpdateMeshData(
        MeshX meshx,
        MeshUploadHint uploadHint,
        BoundingBox bounds,
        AssetIntegrated onLoaded)
    {
        var data = new UnityMeshData();
        meshx.GenerateUnityMeshData(ref data, ref uploadHint, Engine.SystemInfo);
        UnityAssetIntegrator.EnqueueProcessing(() => Upload2(data, uploadHint, bounds, onLoaded),
            Asset.HighPriorityIntegration);
    }

    public override void Unload()
    {
        UnityAssetIntegrator.EnqueueProcessing(Destroy, true);
    }

    private void Upload2(UnityMeshData data, MeshUploadHint hint, BoundingBox bounds, AssetIntegrated onLoaded)
    {
        if (data == null)
            return;
        if (Mesh != null && !Mesh.isReadable)
        {
            if (Mesh)
                Object.Destroy(Mesh);
            Mesh = null;
        }

        var environmentInstanceChanged = false;
        if (Mesh == null)
        {
            Mesh = new Mesh();
            environmentInstanceChanged = true;
            if (hint[MeshUploadHint.Flag.Dynamic])
                Mesh.MarkDynamic();
        }

        data.Assign(Mesh, hint);

        Mesh.bounds = bounds.ToUnity();
        Mesh.UploadMeshData(!hint[MeshUploadHint.Flag.Readable]);
        if (hint[MeshUploadHint.Flag.Dynamic])
        {
            _meshGenData = data;
            _uploadHint = hint;
            _bounds = bounds;
            _onLoaded = onLoaded;
        }

        onLoaded(environmentInstanceChanged);
        Engine.MeshUpdated();
    }

    private void Upload()
    {
        if (_meshGenData == null)
            return;
        if (Mesh != null && !Mesh.isReadable)
        {
            if (Mesh)
                Object.Destroy(Mesh);
            Mesh = null;
        }

        var environmentInstanceChanged = false;
        if (Mesh == null)
        {
            Mesh = new Mesh();
            environmentInstanceChanged = true;
            if (_uploadHint[MeshUploadHint.Flag.Dynamic])
                Mesh.MarkDynamic();
        }

        _meshGenData.Assign(Mesh, _uploadHint);

        Mesh.bounds = _bounds.ToUnity();
        Mesh.UploadMeshData(!_uploadHint[MeshUploadHint.Flag.Readable]);
        if (!_uploadHint[MeshUploadHint.Flag.Dynamic])
            _meshGenData = null;
        _onLoaded(environmentInstanceChanged);
        _onLoaded = null;
        Engine.MeshUpdated();
    }

    private void Destroy()
    {
        if (Mesh != null)
            Object.Destroy(Mesh);
        Mesh = null;
        _meshGenData = null;
    }
}