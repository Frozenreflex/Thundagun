#region

using System;
using System.Collections;
using System.Threading;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Audio;
using UnityEngine.Experimental.Video;
using UnityEngine.Video;
using UnityFrooxEngineRunner;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public class UnityVideoTextureBehavior : MonoBehaviour, IVideoTextureBehaviour
{
    private static int OutputSampleRate;

    private float[] _audioData;

    private SpinLock _audioLock = new(false);

    private float[] _conversionBuffer;

    private bool _initialized;

    private Surround51Sample _last51sample;

    private bool _lastCleared;

    private double _lastDspTime;

    private MonoSample _lastMonoSample;

    private double _lastPosition;

    private QuadSample _lastQuadSample;

    private int _lastReadCount;

    private StereoSample _lastStereoSample;

    private Texture _lastTexture;

    private VideoTextureConnector connector;

    private PlaybackCallback getPlayback;

    private bool isReading;

    private Action onReady;

    private int outputSampleRate;

    private bool playing;

    private AudioSampleProvider sampleProvider;

    private VideoPlayer videoPlayer;

    private float volume;

    public string PlaybackEngine => "Unity Native";

    private void Awake()
    {
        if (OutputSampleRate == 0) OutputSampleRate = AudioSettings.outputSampleRate;
    }

    private void Update()
    {
        if (!_initialized) return;

        var texture = videoPlayer.texture;
        if (_lastTexture != texture)
        {
            connector?.onTextureChanged?.Invoke();
            _lastTexture = texture;
        }

        if (texture.filterMode != connector.filterMode) texture.filterMode = connector.filterMode;
        if (texture.anisoLevel != connector.anisoLevel) texture.anisoLevel = connector.anisoLevel;
        if (texture.wrapModeU != connector.wrapU) texture.wrapModeU = connector.wrapU;
        if (texture.wrapModeV != connector.wrapV) texture.wrapModeV = connector.wrapV;
        var playbackState = getPlayback();
        volume = MathX.Clamp01(playbackState.volume);
        videoPlayer.isLooping = playbackState.loop;
        videoPlayer.externalReferenceTime = playbackState.position;
        var flag = playing = playbackState.play && connector.world.Focus != World.WorldFocus.Background;
        if (flag != videoPlayer.isPlaying)
        {
            if (flag)
            {
                videoPlayer.time = playbackState.position;
                videoPlayer.Play();
            }
            else
            {
                videoPlayer.Pause();
            }
        }

        CurrentClockError = (float)(videoPlayer.clockTime - playbackState.position);
    }

    private void OnDestroy()
    {
        connector.Engine.AudioSystem.AudioUpdate -= OnAudioFilterUpdate;
        var lockTaken = false;
        try
        {
            _audioLock.Enter(ref lockTaken);
            _initialized = false;
            if (videoPlayer != null)
            {
                Destroy(videoPlayer);
                videoPlayer = null;
            }

            if (sampleProvider != null)
            {
                sampleProvider.Dispose();
                sampleProvider = null;
            }
        }
        finally
        {
            if (lockTaken) _audioLock.Exit();
        }

        videoPlayer = null;
        connector = null;
        getPlayback = null;
        sampleProvider = null;
    }

    public bool IsLoaded { get; private set; }

    public Texture UnityTexture
    {
        get
        {
            object obj = videoPlayer?.texture;
            if (obj == null)
            {
                var videoTextureConnector = connector;
                if (videoTextureConnector == null) return null;

                obj = videoTextureConnector.Engine.AssetManager.DarkCheckerTexture.GetUnity();
            }

            return (Texture)obj;
        }
    }

    public bool HasAlpha => false;

    public float Length
    {
        get
        {
            if (videoPlayer == null || !_initialized) return 0f;

            return videoPlayer.frameCount / videoPlayer.frameRate;
        }
    }

    public float CurrentClockError { get; private set; }

    public int2 Size
    {
        get
        {
            if (videoPlayer?.texture == null || !_initialized) return int2.Zero;

            return new int2(videoPlayer.texture.width, videoPlayer.texture.height);
        }
    }

    public void AudioRead<S>(Span<S> buffer) where S : unmanaged, IAudioSample<S>
    {
        if (!_initialized || !playing)
        {
            for (var i = 0; i < buffer.Length; i++) buffer[i] = default;
        }
        else if (_audioData == null || _audioData.Length < buffer.Length)
        {
            for (var j = 0; j < buffer.Length; j++) buffer[j] = default;
        }
        else
        {
            var sourcePosition = 0.0;
            var lastSample = default(StereoSample);
            _audioData.AsStereoBuffer().CopySamples(buffer, ref sourcePosition, ref lastSample);
        }
    }

    public void Setup(VideoTextureConnector connector, string dataSource, Action onReady, PlaybackCallback getPlayback)
    {
        UniLog.Log("Preparing UnityVideoTexture: " + dataSource);
        StartCoroutine(InitTimeout());
        try
        {
            connector.Engine.AudioSystem.AudioUpdate += OnAudioFilterUpdate;
            this.connector = connector;
            this.onReady = onReady;
            this.getPlayback = getPlayback;
            videoPlayer = gameObject.AddComponent<VideoPlayer>();
            videoPlayer.playOnAwake = false;
            videoPlayer.renderMode = VideoRenderMode.APIOnly;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.APIOnly;
            videoPlayer.source = VideoSource.Url;
            videoPlayer.skipOnDrop = true;
            videoPlayer.timeReference = VideoTimeReference.ExternalTime;
            videoPlayer.url = dataSource;
            videoPlayer.prepareCompleted += VideoPlayer_prepareCompleted;
            videoPlayer.errorReceived += VideoPlayer_errorReceived;
            outputSampleRate = AudioSettings.outputSampleRate;
            videoPlayer.Prepare();
        }
        catch (Exception ex)
        {
            UniLog.Error("Exception initializing UnityVideoTexture:\n" + ex);
            SendReady();
        }
    }

    private void OnAudioFilterUpdate()
    {
        if (!_initialized) return;

        var bufferSize = connector.Engine.AudioSystem.BufferSize;
        if (_audioData == null) _audioData = _audioData.EnsureExactSize(bufferSize * 2);
        var lockTaken = false;
        try
        {
            _audioLock.Enter(ref lockTaken);
            var num = 1.0;
            var num2 = sampleProvider?.channelCount ?? 0;
            if (sampleProvider != null) num = sampleProvider.sampleRate / (double)outputSampleRate;
            var num3 = MathX.RoundToInt(bufferSize * num * num2);
            if (volume > 0f && sampleProvider != null &&
                sampleProvider.availableSampleFrameCount * sampleProvider.channelCount >= num3 * 1.05f)
            {
                _lastCleared = false;
                using var sampleFrames =
                    new NativeArray<float>(num3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                sampleProvider.ConsumeSampleFrames(sampleFrames);
                _conversionBuffer = _conversionBuffer.EnsureSize(num3);
                sampleFrames.CopyTo(_conversionBuffer);
                var target = _audioData.AsStereoBuffer();
                switch (num2)
                {
                    case 1:
                        _conversionBuffer.AsMonoBuffer()
                            .CopySamples(target, ref _lastPosition, ref _lastMonoSample, num);
                        break;
                    case 2:
                        _conversionBuffer.AsStereoBuffer()
                            .CopySamples(target, ref _lastPosition, ref _lastStereoSample, num);
                        break;
                    case 4:
                        _conversionBuffer.AsQuadBuffer()
                            .CopySamples(target, ref _lastPosition, ref _lastQuadSample, num);
                        break;
                    case 6:
                        _conversionBuffer.AsSurround51Buffer()
                            .CopySamples(target, ref _lastPosition, ref _last51sample, num);
                        break;
                }

                _lastPosition -= MathX.CeilToInt(_lastPosition);
                if (volume < 1f)
                    for (var i = 0; i < _audioData.Length; i++)
                        _audioData[i] *= volume;
                return;
            }

            if (!_lastCleared)
            {
                Array.Clear(_audioData, 0, _audioData.Length);
                _lastCleared = true;
            }
        }
        finally
        {
            if (lockTaken) _audioLock.Exit();
        }
    }

    private IEnumerator InitTimeout()
    {
        yield return new WaitForSeconds(10f);

        if (!_initialized && onReady != null)
        {
            UniLog.Warning("UnityVideoTexture Timeout");
            SendReady();
        }
    }

    private void VideoPlayer_errorReceived(VideoPlayer source, string message)
    {
        UniLog.Warning("UnityVideoTexture Error: " + message);
        _initialized = false;
        SendReady();
    }

    private void VideoPlayer_prepareCompleted(VideoPlayer source)
    {
        IsLoaded = true;
        if (source.audioTrackCount > 0)
        {
            source.EnableAudioTrack(0, true);
            sampleProvider = source.GetAudioSampleProvider(0);
            sampleProvider.enableSilencePadding = false;
            sampleProvider.sampleFramesOverflow += SampleProvider_sampleFramesOverflow;
        }

        _initialized = true;
        SendReady();
        UniLog.Log(
            $"AudioTrackCount: {source.audioTrackCount}, SampleRate: {sampleProvider?.sampleRate}, MaxSampleFrameCount: {sampleProvider?.maxSampleFrameCount}");
        videoPlayer.Play();
    }

    private void SendReady()
    {
        _lastTexture = videoPlayer?.texture;
        onReady?.Invoke();
        onReady = null;
    }

    private void SampleProvider_sampleFramesAvailable(AudioSampleProvider provider, uint sampleFrameCount)
    {
    }

    private void SampleProvider_sampleFramesOverflow(AudioSampleProvider provider, uint sampleFrameCount)
    {
    }
}