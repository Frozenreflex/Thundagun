using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Thundagun.NewConnectors.AssetConnectors;

public class PlaybackEngine
{
	public string Name { get; private set; }

	public Func<GameObject, IVideoTextureBehaviour> Instantiate { get; private set; }

	public int InitializationAttempts { get; private set; }

	public static List<PlaybackEngine> PlaybackEngines { get; private set; }

	public PlaybackEngine(string name, Func<GameObject, IVideoTextureBehaviour> instantiate, int initializeAttempts)
	{
		Name = name;
		Instantiate = instantiate;
		InitializationAttempts = initializeAttempts;
	}

	static PlaybackEngine()
	{
		PlaybackEngines = new List<PlaybackEngine>();
		PlaybackEngines.Add(new PlaybackEngine("Unity Native", (GameObject go) => go.AddComponent<UnityVideoTextureBehavior>(), 1));
		PlaybackEngines.Add(new PlaybackEngine("libVLC", (GameObject go) => go.AddComponent<UMPVideoTextureBehaviour>(), 5));
	}
}