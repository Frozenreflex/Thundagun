using FrooxEngine;
using UnityEngine;

namespace Thundagun.NewConnectors;

public class WorldConnector : IWorldConnector
{
    public World Owner { get; set; }
    public GameObject WorldRoot { get; set; }
    
    public void Initialize(World owner)
    {
        //Owner only gets used during Destroy so it doesn't matter if we set it here
        Owner = owner;
        Thundagun.CurrentPackets.Add(new InitializeWorldConnector(this));
    }
    public static void SetLayerRecursively(Transform transform, int layer)
    {
        transform.gameObject.layer = layer;
        for (var index = 0; index < transform.childCount; ++index)
            SetLayerRecursively(transform.GetChild(index), layer);
    }
    public void ChangeFocus(World.WorldFocus focus) => Thundagun.CurrentPackets.Add(new ChangeFocusWorldConnector(this, focus));
    public void Destroy() => Thundagun.CurrentPackets.Add(new DestroyWorldConnector(this));
}

public class InitializeWorldConnector : UpdatePacket<WorldConnector>
{
    public UnityFrooxEngineRunner.WorldManagerConnector connector;
    public InitializeWorldConnector(WorldConnector owner) : base(owner)
    {
        connector = owner.Owner.WorldManager.Connector as UnityFrooxEngineRunner.WorldManagerConnector;
    }

    public override void Update()
    {
        Owner.WorldRoot = new GameObject("World");
        Owner.WorldRoot.SetActive(false);
        Owner.WorldRoot.transform.SetParent(connector.Root.transform);
        Owner.WorldRoot.transform.position = Vector3.zero;
        Owner.WorldRoot.transform.rotation = Quaternion.identity;
        Owner.WorldRoot.transform.localScale = Vector3.one;
    }
}
public class ChangeFocusWorldConnector : UpdatePacket<WorldConnector>
{
    public World.WorldFocus Focus;
    public ChangeFocusWorldConnector(WorldConnector owner, World.WorldFocus focus) : base(owner)
    {
        Focus = focus;
    }

    public override void Update()
    {
        switch (Focus)
        {
            case World.WorldFocus.Background:
                Owner.WorldRoot.SetActive(false);
                break;
            case World.WorldFocus.Focused:
            case World.WorldFocus.Overlay:
                Owner.WorldRoot.SetActive(true);
                break;
            case World.WorldFocus.PrivateOverlay:
                Owner.WorldRoot.SetActive(true);
                WorldConnector.SetLayerRecursively(Owner.WorldRoot.transform, LayerMask.NameToLayer("Private"));
                break;
        }
    }
}

public class DestroyWorldConnector : UpdatePacket<WorldConnector>
{
    public DestroyWorldConnector(WorldConnector owner) : base(owner)
    {
    }

    public override void Update()
    {
        if (Owner.WorldRoot) Object.Destroy(Owner.WorldRoot);
        Owner.WorldRoot = null;
        Owner.Owner = null;
    }
}