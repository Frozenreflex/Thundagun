#region

using System.Collections.Generic;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;
using BoundingSphere = Elements.Core.BoundingSphere;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class SkinBoundsUpdater : MonoBehaviour
{
    public SkinnedBounds boundsMethod;
    public IReadOnlyList<ApproximateBoneBounds> approximateBounds;
    public IReadOnlyList<BoneMetadata> boneMetadata;
    public SkinnedMeshRendererConnector connector;

    private void LateUpdate()
    {
        if (connector?.MeshRenderer?.sharedMesh == null)
        {
            if (connector == null)
                return;
            connector.LocalBoundingBoxAvailable = false;
        }
        else
        {
            switch (boundsMethod)
            {
                case SkinnedBounds.FastDisjointRootApproximate when approximateBounds == null:
                    return;
                case SkinnedBounds.FastDisjointRootApproximate:
                {
                    var rootBone = connector.MeshRenderer.rootBone;
                    var bounds = BoundingBox.Empty();
                    foreach (var approximateBound in approximateBounds)
                    {
                        var bone = connector.MeshRenderer.bones[approximateBound.rootBoneIndex];
                        var unity = approximateBound.bounds.center.ToUnity();
                        var radius = approximateBound.bounds.radius;
                        var center = float3.One;
                        var vector = (radius * center).ToUnity();
                        var position = bone.TransformPoint(unity);
                        vector = bone.TransformVector(vector);
                        var v = rootBone.InverseTransformPoint(position);
                        vector = rootBone.InverseTransformVector(vector);
                        ref var local2 = ref bounds;
                        center = v.ToEngine();
                        var sphere = new BoundingSphere(in center, vector.magnitude);
                        local2.Encapsulate(sphere);
                    }

                    connector.MeshRenderer.localBounds = bounds.ToUnity();
                    connector.LocalBoundingBoxAvailable = false;
                    connector.SendBoundsUpdated();
                    break;
                }
                case SkinnedBounds.MediumPerBoneApproximate when boneMetadata == null:
                    return;
                case SkinnedBounds.MediumPerBoneApproximate:
                {
                    var rootBone = connector.MeshRenderer.rootBone;
                    var bounds1 = BoundingBox.Empty();
                    for (var index1 = 0; index1 < connector.MeshRenderer.bones.Length; index1++)
                    {
                        var bounds2 = boneMetadata[index1].bounds;
                        var bone = connector.MeshRenderer.bones[index1];
                        if (!bounds2.IsValid || bone == null) continue;
                        for (var index2 = 0; index2 < 8; index2++)
                        {
                            var vertexPoint = bounds2.GetVertexPoint(index2);
                            var position = bone.TransformPoint(vertexPoint.ToUnity());
                            var v = rootBone.InverseTransformPoint(position);
                            bounds1.Encapsulate(v.ToEngine());
                        }
                    }

                    connector.MeshRenderer.localBounds = bounds1.ToUnity();
                    connector.LocalBoundingBoxAvailable = false;
                    connector.SendBoundsUpdated();
                    break;
                }
                default:
                {
                    if (boundsMethod != SkinnedBounds.SlowRealtimeAccurate)
                        return;
                    var engine = connector.MeshRenderer.bounds.ToEngine();
                    var boundingBox = BoundingBox.Empty();
                    for (var index = 0; index < 8; index++)
                    {
                        var v = connector.MeshRenderer.transform.InverseTransformPoint(engine.GetVertexPoint(index)
                            .ToUnity());
                        boundingBox.Encapsulate(v.ToEngine());
                    }

                    connector.LocalBoundingBox = boundingBox;
                    connector.LocalBoundingBoxAvailable = true;
                    connector.SendBoundsUpdated();
                    break;
                }
            }
        }
    }

    private void OnDestroy()
    {
        connector = null;
        boneMetadata = null;
        approximateBounds = null;
    }
}