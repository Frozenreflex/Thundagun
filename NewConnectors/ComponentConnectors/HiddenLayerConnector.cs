#region

using FrooxEngine;
using UnityEngine;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class HiddenLayerConnector : ComponentConnectorSingle<HiddenLayer>
{
    public override void ApplyChanges()
    {
        Thundagun.QueuePacket(new ApplyChangesHiddenLayerConnector(this));
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (!destroyingWorld) SlotConnector.Owner?.MarkChangeDirty();
        base.Destroy(destroyingWorld);
    }

    public override void DestroyMethod(bool destroyingWorld)
    {
        if (!destroyingWorld) SlotConnector.ForceLayer = 0;
        base.DestroyMethod(destroyingWorld);
    }
}

public class ApplyChangesHiddenLayerConnector : UpdatePacket<HiddenLayerConnector>
{
    public bool Enabled;

    public ApplyChangesHiddenLayerConnector(HiddenLayerConnector owner) : base(owner)
    {
        Enabled = owner.Owner.Enabled;
        owner.SlotConnector.Owner?.MarkChangeDirty();
    }

    public override void Update()
    {
        Owner.SlotConnector.ForceLayer = Enabled ? (byte)LayerMask.NameToLayer("Hidden") : (byte)0;
    }
}