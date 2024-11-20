#region

using System;
using System.Collections.Generic;
using UnityEngine;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public class PlaybackEngine
{
    static PlaybackEngine()
    {
        PlaybackEngines = new List<PlaybackEngine>();
        PlaybackEngines.Add(new PlaybackEngine("Unity Native", go => go.AddComponent<UnityVideoTextureBehavior>(), 1));
        PlaybackEngines.Add(new PlaybackEngine("libVLC", go => go.AddComponent<UMPVideoTextureBehaviour>(), 5));
    }

    public PlaybackEngine(string name, Func<GameObject, IVideoTextureBehaviour> instantiate, int initializeAttempts)
    {
        Name = name;
        Instantiate = instantiate;
        InitializationAttempts = initializeAttempts;
    }

    public string Name { get; private set; }

    public Func<GameObject, IVideoTextureBehaviour> Instantiate { get; private set; }

    public int InitializationAttempts { get; private set; }

    public static List<PlaybackEngine> PlaybackEngines { get; }
}