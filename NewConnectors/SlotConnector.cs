#region

using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;

#endregion

namespace Thundagun.NewConnectors;

public class SlotConnector : Connector<Slot>, ISlotConnector
{
    public bool Active;
    public byte ForceLayer;
    public ushort GameObjectRequests;
    public SlotConnector ParentConnector;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;
    public bool ShouldDestroy;
    public Transform Transform;

    public WorldConnector WorldConnector => (WorldConnector)World.Connector;

    public GameObject GeneratedGameObject { get; private set; }

    public int Layer => GeneratedGameObject == null ? 0 : GeneratedGameObject.layer;

    public override void Initialize()
    {
        ParentConnector = Owner.Parent?.Connector as SlotConnector;
        Thundagun.QueuePacket(new ApplyChangesSlotConnector(this, !Owner.IsRootSlot));
    }

    public override void ApplyChanges()
    {
        Thundagun.QueuePacket(new ApplyChangesSlotConnector(this));
    }

    public override void Destroy(bool destroyingWorld)
    {
        Thundagun.QueuePacket(new DestroySlotConnector(this, destroyingWorld));
    }

    public static IConnector<Slot> Constructor()
    {
        return new SlotConnector();
    }

    public GameObject ForceGetGameObject()
    {
        if (GeneratedGameObject == null)
            GenerateGameObject();
        return GeneratedGameObject;
    }

    public GameObject RequestGameObject()
    {
        GameObjectRequests++;
        return ForceGetGameObject();
    }

    public void FreeGameObject()
    {
        GameObjectRequests--;
        TryDestroy();
    }

    public void TryDestroy(bool destroyingWorld = false)
    {
        if (!ShouldDestroy || GameObjectRequests != 0)
            return;

        if (!destroyingWorld)
        {
            if (GeneratedGameObject) Object.Destroy(GeneratedGameObject);
            ParentConnector?.FreeGameObject();
        }

        GeneratedGameObject = null;
        Transform = null;
        ParentConnector = null;
    }

    private void GenerateGameObject()
    {
        GeneratedGameObject = new GameObject("");
        Transform = GeneratedGameObject.transform;
        UpdateParent();
        UpdateLayer();
        SetData();
    }

    private void UpdateParent()
    {
        var gameObject = ParentConnector != null ? ParentConnector.RequestGameObject() : WorldConnector.WorldRoot;
        Transform.SetParent(gameObject.transform, false);
    }

    public void UpdateLayer()
    {
        var layer = ForceLayer <= 0 ? Transform.parent.gameObject.layer : ForceLayer;
        if (layer == GeneratedGameObject.layer)
            return;

        SetHiearchyLayer(GeneratedGameObject, layer);
    }

    public static void SetHiearchyLayer(GameObject root, int layer)
    {
        root.layer = layer;
        for (var index = 0; index < root.transform.childCount; ++index)
            SetHiearchyLayer(root.transform.GetChild(index).gameObject, layer);
    }

    public void SetData()
    {
        GeneratedGameObject.SetActive(Active);
        var transform = Transform;
        transform.localPosition = Position;
        transform.localRotation = Rotation;
        transform.localScale = Scale;
    }
}

public class ApplyChangesSlotConnector : UpdatePacket<SlotConnector>
{
    public bool Active;
    public bool ActiveChanged;
    public SlotConnector NewParentSlot;
    public Vector3 Position;
    public bool PositionChanged;
    public bool Reparent;
    public Quaternion Rotation;
    public bool RotationChanged;
    public Vector3 Scale;
    public bool ScaleChanged;

    public ApplyChangesSlotConnector(SlotConnector owner, bool forceReparent) : base(owner)
    {
        var o = owner.Owner;
        var parent = o.Parent;
        if ((parent != null && parent.Connector != owner.ParentConnector) || forceReparent)
        {
            Reparent = true;
            NewParentSlot = o.Parent.Connector as SlotConnector;
        }

        ActiveChanged = o.ActiveSelf_Field.GetWasChangedAndClear();
        Active = o.ActiveSelf;
        PositionChanged = o.Position_Field.GetWasChangedAndClear();
        Position = o.Position_Field.Value.ToUnity();
        RotationChanged = o.Rotation_Field.GetWasChangedAndClear();
        Rotation = o.Rotation_Field.Value.ToUnity();
        ScaleChanged = o.Scale_Field.GetWasChangedAndClear();
        Scale = o.Scale_Field.Value.ToUnity();
    }

    public ApplyChangesSlotConnector(SlotConnector owner) : base(owner)
    {
        var o = owner.Owner;
        var parent = o.Parent;
        if (parent?.Connector != owner.ParentConnector && parent != null)
        {
            Reparent = true;
            NewParentSlot = o.Parent.Connector as SlotConnector;
        }

        ActiveChanged = o.ActiveSelf_Field.GetWasChangedAndClear();
        Active = o.ActiveSelf;
        PositionChanged = o.Position_Field.GetWasChangedAndClear();
        Position = o.Position_Field.Value.ToUnity();
        RotationChanged = o.Rotation_Field.GetWasChangedAndClear();
        Rotation = o.Rotation_Field.Value.ToUnity();
        ScaleChanged = o.Scale_Field.GetWasChangedAndClear();
        Scale = o.Scale_Field.Value.ToUnity();
    }

    public override void Update()
    {
        Owner.Active = Active;
        Owner.Position = Position;
        Owner.Rotation = Rotation;
        Owner.Scale = Scale;

        var generatedGameObject = Owner.GeneratedGameObject;
        if (!(generatedGameObject != null))
            return;

        if (Reparent)
        {
            Owner.ParentConnector?.FreeGameObject();
            GameObject gameObject;
            if (NewParentSlot != null)
            {
                Owner.ParentConnector = NewParentSlot;
                gameObject = Owner.ParentConnector.RequestGameObject();
            }
            else
            {
                gameObject = Owner.WorldConnector.WorldRoot;
            }

            Owner.Transform.SetParent(gameObject.transform, false);
        }

        Owner.UpdateLayer();
        Owner.SetData();
    }
}

public class DestroySlotConnector : UpdatePacket<SlotConnector>
{
    public bool DestroyingWorld;

    public DestroySlotConnector(SlotConnector owner, bool destroyingWorld) : base(owner)
    {
        DestroyingWorld = destroyingWorld;
    }

    public override void Update()
    {
        Owner.ShouldDestroy = true;
        Owner.TryDestroy(DestroyingWorld);
    }
}