#region

using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class OverlayLayerConnector : ComponentConnectorSingle<OverlayLayer>
{
    public OverlayRootPositioner Positioner;

    public override IUpdatePacket InitializePacket()
    {
        return new InitializeOverlayLayerConnector(this, Owner);
    }

    public override void ApplyChanges()
    {
        Thundagun.QueuePacket(new ApplyChangesOverlayLayerConnector(this));
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (!destroyingWorld) SlotConnector.Owner?.MarkChangeDirty();
        base.Destroy(destroyingWorld);
    }

    public override void DestroyMethod(bool destroyingWorld)
    {
        if (!destroyingWorld)
        {
            SlotConnector.ForceLayer = 0;
            Object.Destroy(Positioner);
        }

        base.DestroyMethod(destroyingWorld);
    }
}

public class InitializeOverlayLayerConnector : InitializeComponentConnectorSingle<OverlayLayer, OverlayLayerConnector>
{
    public InitializeOverlayLayerConnector(OverlayLayerConnector connector, OverlayLayer component) : base(connector,
        component)
    {
    }

    public override void Update()
    {
        base.Update();
        Owner.Positioner = Owner.AttachedGameObject.AddComponent<OverlayRootPositioner>();
    }
}

public class ApplyChangesOverlayLayerConnector : UpdatePacket<OverlayLayerConnector>
{
    public bool Enabled;

    public ApplyChangesOverlayLayerConnector(OverlayLayerConnector owner) : base(owner)
    {
        Enabled = owner.Owner.Enabled;
        owner.SlotConnector.Owner?.MarkChangeDirty();
    }

    public override void Update()
    {
        Owner.SlotConnector.ForceLayer = Enabled ? (byte)LayerMask.NameToLayer("Overlay") : (byte)0;
    }
}