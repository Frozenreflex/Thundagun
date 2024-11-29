#region

using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;

#endregion

namespace Thundagun.NewConnectors;

public class InitializeComponentConnector<TD, TC, T> : UpdatePacket<T>
    where TD : ImplementableComponent<TC>
    where TC : class, IConnector
    where T : ComponentConnector<TD, TC>
{
    public SlotConnector Connector;

    public InitializeComponentConnector(T connector, TD component) : base(connector)
    {
        Owner.SlotConnector = (SlotConnector)component.Slot.Connector;
    }

    public override void Update()
    {
        Owner.AttachedGameObject = Owner.SlotConnector.RequestGameObject();
    }
}

public class InitializeComponentConnectorSingle<TD, T> : InitializeComponentConnector<TD, IConnector, T>
    where TD : ImplementableComponent<IConnector>
    where T : ComponentConnectorSingle<TD>
{
    public InitializeComponentConnectorSingle(T connector, TD component) : base(connector, component)
    {
    }
}

public class InitializeUnityComponentConnector<TC, TU, T> : InitializeComponentConnectorSingle<TC, T>
    where TC : ImplementableComponent
    where TU : MonoBehaviour, IConnectorBehaviour
    where T : UnityComponentConnector<TC, TU>
{
    public InitializeUnityComponentConnector(T connector, TC component) : base(connector, component)
    {
    }

    public override void Update()
    {
        base.Update();
        Owner.UnityComponent = Owner.AttachedGameObject.AddComponent<TU>();
        Owner.UnityComponent.AssignConnector(Owner);
    }
}

public class DestroyComponentConnector<TD, TC> : UpdatePacket<ComponentConnector<TD, TC>>
    where TD : ImplementableComponent<TC>
    where TC : class, IConnector
{
    public bool DestroyingWorld;

    public DestroyComponentConnector(ComponentConnector<TD, TC> connector, TD component, bool destroyingWorld) :
        base(connector)
    {
        DestroyingWorld = destroyingWorld;
    }

    public override void Update()
    {
        Owner.DestroyMethod(DestroyingWorld);
        if (Owner.SlotConnector != null && !DestroyingWorld)
            Owner.SlotConnector.FreeGameObject();
        Owner.SlotConnector = null;
        Owner.AttachedGameObject = null;
    }
}

public class DestroyComponentConnector<TD> : DestroyComponentConnector<TD, IConnector>
    where TD : ImplementableComponent<IConnector>
{
    public DestroyComponentConnector(ComponentConnectorSingle<TD> connector, TD component, bool destroyingWorld) :
        base(connector, component, destroyingWorld)
    {
    }
}

public class DestroyUnityComponentConnector<TC, TU> : DestroyComponentConnector<TC>
    where TC : ImplementableComponent
    where TU : MonoBehaviour, IConnectorBehaviour
{
    public DestroyUnityComponentConnector(UnityComponentConnector<TC, TU> connector, TC component,
        bool destroyingWorld) :
        base(connector, component, destroyingWorld)
    {
    }

    public UnityComponentConnector<TC, TU> OwnerConnector => Owner as UnityComponentConnector<TC, TU>;

    public override void Update()
    {
        base.Update();
        if (OwnerConnector.UnityComponent != null)
        {
            OwnerConnector.UnityComponent.ClearConnector();
            if (!DestroyingWorld && OwnerConnector.UnityComponent)
                Object.Destroy(OwnerConnector.UnityComponent);
            OwnerConnector.UnityComponent = default;
        }
    }
}