#region

using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class BlitToDisplayConnector : ComponentConnectorSingle<BlitToDisplay>
{
    public TextureDisplayBlitter blitter;

    public override IUpdatePacket InitializePacket()
    {
        return new InitializeBlitToDisplayConnector(this, Owner);
    }

    public override void ApplyChanges()
    {
        Thundagun.QueuePacket(new ApplyChangesBlitToDisplayConnector(this));
    }

    public override void DestroyMethod(bool destroyingWorld)
    {
        if (blitter != null)
        {
            blitter.Engine = null;
            blitter.Texture = null;
            if (!destroyingWorld && blitter)
                Object.Destroy(blitter);
            blitter = null;
        }

        base.DestroyMethod(destroyingWorld);
    }
}

public class
    InitializeBlitToDisplayConnector : InitializeComponentConnectorSingle<BlitToDisplay, BlitToDisplayConnector>
{
    public Engine Engine;

    public InitializeBlitToDisplayConnector(BlitToDisplayConnector connector, BlitToDisplay component) : base(connector,
        component)
    {
        Engine = connector.Owner.Engine;
    }

    public override void Update()
    {
        base.Update();
        Owner.blitter = Owner.AttachedGameObject.AddComponent<TextureDisplayBlitter>();
        Owner.blitter.Engine = Engine;
    }
}

public class ApplyChangesBlitToDisplayConnector : UpdatePacket<BlitToDisplayConnector>
{
    public bool Blit;
    public Color Color;
    public int DisplayIndex;
    public bool FlipHorizontally;
    public bool FlipVertically;
    public IUnityTextureProvider Texture;

    public ApplyChangesBlitToDisplayConnector(BlitToDisplayConnector owner) : base(owner)
    {
        var target = owner.Owner.TargetUser.Target;
        Blit = target is not null && target.IsLocalUser;
        if (!Blit) return;

        Texture = owner.Owner.Texture.Asset.Connector as IUnityTextureProvider;
        DisplayIndex = owner.Owner.DisplayIndex.Value;
        Color = owner.Owner.BackgroundColor.Value.ToUnity(ColorProfile.sRGB);
        FlipHorizontally = owner.Owner.FlipHorizontally.Value;
        FlipVertically = owner.Owner.FlipVertically.Value;
    }

    public override void Update()
    {
        if (Blit)
        {
            Owner.blitter.Texture = Texture.UnityTexture;
            Owner.blitter.DisplayIndex = DisplayIndex;
            Owner.blitter.Color = Color;
            Owner.blitter.FlipHorizontally = FlipHorizontally;
            Owner.blitter.FlipVertically = FlipVertically;
        }
        else
        {
            Owner.blitter.Texture = null;
            Owner.blitter.DisplayIndex = -1;
        }
    }
}