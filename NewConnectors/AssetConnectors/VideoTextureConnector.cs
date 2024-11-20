#region

using System;
using System.Collections;
using System.Linq;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using UMP.Services;
using UnityEngine;
using UnityFrooxEngineRunner;
using Object = UnityEngine.Object;
using TextureWrapMode = FrooxEngine.TextureWrapMode;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public class VideoTextureConnector : AssetConnector, IVideoTextureConnector, IUnityTextureProvider
{
    private static GameObject videoTextureBehaviors;
    private static DummyBehaviour dummyBehaviour;
    private static DateTime _lastVideoLoad;
    private static DateTime _lastStreamLoad;
    private int _attemptsLeft;
    private int _playbackEngineIndex;
    internal int anisoLevel;
    internal int? audioTrackIndex;
    internal FilterMode filterMode;
    private PlaybackCallback getPlayback;
    private VideoServices hostingParser;
    internal Action onTextureChanged;
    private IVideoTextureBehaviour videoBehaviour;
    internal World world;
    internal UnityEngine.TextureWrapMode wrapU;
    internal UnityEngine.TextureWrapMode wrapV;
    public Texture UnityTexture => videoBehaviour?.UnityTexture;
    public int2 Size => videoBehaviour?.Size ?? int2.Zero;
    public bool HasAlpha => videoBehaviour?.HasAlpha ?? false;
    public float Length => videoBehaviour?.Length ?? 0f;
    public float CurrentClockError => videoBehaviour?.CurrentClockError ?? 0f;
    public string PlaybackEngine { get; private set; }

    public void LoadLocal(string path, string forcePlaybackEngine, string mime, AssetIntegrated onReady,
        Action onTextureChanged)
    {
        LoadStream(path, forcePlaybackEngine, mime, onReady, onTextureChanged);
    }

    public void LoadStream(string uri, string forcePlaybackEngine, string mime, AssetIntegrated onReady,
        Action onTextureChanged)
    {
        this.onTextureChanged = onTextureChanged;
        PlaybackEngine = forcePlaybackEngine;
        UnityAssetIntegrator.EnqueueTask(delegate
        {
            InitParser();
            dummyBehaviour.StartCoroutine(LoadFromStream(uri, onReady));
        });
    }

    public void AudioRead<S>(Span<S> buffer) where S : unmanaged, IAudioSample<S>
    {
        videoBehaviour?.AudioRead(buffer);
    }

    public override void Unload()
    {
        getPlayback = null;
        UnityAssetIntegrator.EnqueueProcessing(Destroy, false);
    }

    public void Setup(World world, PlaybackCallback callback)
    {
        this.world = world;
        getPlayback = callback;
    }

    public void SetTextureProperties(TextureFilterMode filterMode, int anisoLevel, TextureWrapMode wrapU,
        TextureWrapMode wrapV)
    {
        if (filterMode == TextureFilterMode.Anisotropic)
        {
            this.filterMode = FilterMode.Trilinear;
            this.anisoLevel = anisoLevel;
        }
        else
        {
            this.filterMode = filterMode.ToUnity();
            this.anisoLevel = 0;
        }

        this.wrapU = wrapU.ToUnity();
        this.wrapV = wrapV.ToUnity();
    }

    public void SetPlaybackProperties(int? audioTrackIndex)
    {
        this.audioTrackIndex = audioTrackIndex;
    }

    private IEnumerator LoadFromStream(string uri, AssetIntegrated onReady)
    {
        while (world.Time.LocalUpdateIndex < 200) yield return new WaitForEndOfFrame();
        while (world.Engine.SystemInfo.FPS < 10f || (DateTime.UtcNow - _lastStreamLoad).TotalSeconds < 4.0)
            yield return new WaitForEndOfFrame();
        _lastStreamLoad = DateTime.UtcNow;
        UnityAssetIntegrator.EnqueueTask(delegate { ParseUrl(uri, onReady); });
    }

    private void InitParser()
    {
        if (hostingParser == null && dummyBehaviour == null)
            dummyBehaviour = new GameObject("VideoHostingParser").AddComponent<DummyBehaviour>();
    }

    private void ParseUrl(string uri, AssetIntegrated onReady)
    {
        InitParser();
        LoadFromSource(uri, onReady);
    }

    private void LoadFromSource(string uri, AssetIntegrated onLoaded)
    {
        if (videoTextureBehaviors == null) videoTextureBehaviors = new GameObject("VideoTextureBehaviors");
        PlaybackEngine engine = null;
        if (!string.IsNullOrWhiteSpace(PlaybackEngine))
        {
            engine = AssetConnectors.PlaybackEngine.PlaybackEngines.FirstOrDefault(e =>
                e.Name.ToLower().Contains(PlaybackEngine.ToLower()));
            if (_attemptsLeft == 0) _attemptsLeft = engine?.InitializationAttempts ?? 0;
        }

        if (engine == null)
        {
            PlaybackEngine = null;
            engine = AssetConnectors.PlaybackEngine.PlaybackEngines[_playbackEngineIndex];
            if (_attemptsLeft == 0) _attemptsLeft = engine.InitializationAttempts;
        }

        videoBehaviour = engine.Instantiate(videoTextureBehaviors);
        videoBehaviour.Setup(this, uri, delegate
        {
            UniLog.Log(
                $"IsLoaded: {videoBehaviour.IsLoaded}, PlaybackEngineIndex: {_playbackEngineIndex}, AttemptsLeft: {_attemptsLeft} Count: {UnityFrooxEngineRunner.PlaybackEngine.PlaybackEngines.Count}, PlaybackEngine: {PlaybackEngine}");
            var flag = false;
            if (!videoBehaviour.IsLoaded)
            {
                if (--_attemptsLeft == 0)
                {
                    if (string.IsNullOrWhiteSpace(PlaybackEngine))
                        _playbackEngineIndex++;
                    else
                        flag = true;
                }

                if (!flag && _playbackEngineIndex < UnityFrooxEngineRunner.PlaybackEngine.PlaybackEngines.Count)
                {
                    Object.Destroy((Object)videoBehaviour);
                    videoBehaviour = null;
                    UniLog.Log("Trying Next Playback Engine");
                    LoadFromSource(uri, onLoaded);
                }
                else
                {
                    flag = true;
                }
            }
            else
            {
                flag = true;
            }

            if (flag)
            {
                _attemptsLeft = 0;
                UniLog.Log("Finished Load, IsLoaded: " + videoBehaviour.IsLoaded);
                if (videoBehaviour.IsLoaded) PlaybackEngine = engine.Name;
                onLoaded(true);
            }
        }, GetPlayback);
    }

    private void Destroy()
    {
        if (videoBehaviour != null) Object.Destroy((Object)videoBehaviour);
        hostingParser = null;
        videoBehaviour = null;
        getPlayback = null;
        world = null;
        onTextureChanged = null;
    }

    private PlaybackState GetPlayback()
    {
        if (getPlayback == null) return new PlaybackState(false, false, 0.0, 0f);
        return getPlayback();
    }
}