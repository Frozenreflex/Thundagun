#region

using System;
using FrooxEngine;
using Mesh = UnityEngine.Mesh;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class MeshRendererConnector : MeshRendererConnectorBase<MeshRenderer, UnityEngine.MeshRenderer>
{
    public override bool UseMeshFilter => true;

    public override void AssignMesh(UnityEngine.MeshRenderer renderer, Mesh mesh)
    {
        throw new NotImplementedException();
    }
}