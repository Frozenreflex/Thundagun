using Elements.Core;
using FrooxEngine;
using UnityFrooxEngineRunner;
using UnityEngine.Rendering;

namespace Thundagun.NewConnectors.ComponentConnectors;

public class AmbientLightSH2Connector : ComponentConnectorSingle<AmbientLightSH2>
{
	public override void ApplyChanges()
	{
		Thundagun.QueuePacket(new ApplyChangesAmbientLightSH2Connector(this));
	}
}

public class ApplyChangesAmbientLightSH2Connector : UpdatePacket<AmbientLightSH2Connector>
{
	bool ShouldBeActive;
	SphericalHarmonicsL2 AmbientProbe;
	public ApplyChangesAmbientLightSH2Connector(AmbientLightSH2Connector owner) : base(owner)
	{
		ShouldBeActive = owner.Owner.ShouldBeActive;
		AmbientProbe = owner.Owner.AmbientLight.Value.ToUnity(ColorProfile.Linear);
	}

	public override void Update()
	{
		if (ShouldBeActive)
		{
			UnityEngine.RenderSettings.ambientProbe = AmbientProbe;
		}
	}
}