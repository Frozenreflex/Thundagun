#region

using FrooxEngine;
using UnityFrooxEngineRunner;
using MaterialConnector = Thundagun.NewConnectors.AssetConnectors.MaterialConnector;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class SkyboxConnector : ComponentConnectorSingle<Skybox>
{
    public override void ApplyChanges()
    {
        Thundagun.QueuePacket(new ApplyChangesSkyboxConnector(this));
    }
}

public class ApplyChangesSkyboxConnector : UpdatePacket<SkyboxConnector>
{
    public bool Active;
    public MaterialConnector Material;

    public ApplyChangesSkyboxConnector(SkyboxConnector owner) : base(owner)
    {
        Active = owner.Owner.ActiveSkybox && owner.World.Focus == World.WorldFocus.Focused;
        if (!Active) return;
        Material = owner.Owner.Material?.Asset?.Connector as MaterialConnector;
    }

    public override void Update()
    {
        if (!Active) return;
		var skybox = Material?.UnityMaterial ?? MaterialConnector.NullMaterial;
		var old = UnityEngine.RenderSettings.skybox;
		UnityEngine.RenderSettings.skybox = skybox;
	}
}