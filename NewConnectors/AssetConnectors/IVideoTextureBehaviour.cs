using Elements.Assets;
using FrooxEngine;
using System;
using UnityEngine;
using Elements.Core;

namespace Thundagun.NewConnectors.AssetConnectors;

public interface IVideoTextureBehaviour
{
	Texture UnityTexture { get; }
	bool IsLoaded { get; }
	bool HasAlpha { get; }
	float Length { get; }
	int2 Size { get; }
	float CurrentClockError { get; }
	void AudioRead<S>(Span<S> buffer) where S : unmanaged, IAudioSample<S>;
	void Setup(VideoTextureConnector connector, string dataSource, Action onReady, PlaybackCallback getPlayback);
}