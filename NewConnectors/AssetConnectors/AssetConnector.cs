#region

using FrooxEngine;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public abstract class AssetConnector : IAssetConnector
{
    protected Asset Asset;

    public Engine Engine => Asset?.Engine;

    protected AssetManager AssetManager { get; private set; }

    protected UnityAssetIntegrator UnityAssetIntegrator { get; private set; }

    public void Initialize(Asset asset)
    {
        Asset = asset;
        AssetManager = asset.AssetManager;
        UnityAssetIntegrator = (UnityAssetIntegrator)AssetManager.Connector;
    }

    public abstract void Unload();
}