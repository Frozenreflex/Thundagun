#region

using System;
using System.Collections;
using System.Threading;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using UMP;
using UnityEngine;
using UnityFrooxEngineRunner;
using AudioOutput = UMP.AudioOutput;
using Texture2D = UnityEngine.Texture2D;

#endregion

namespace Thundagun.NewConnectors.AssetConnectors;

public class UMPVideoTextureBehaviour : MonoBehaviour, IVideoTextureBehaviour
{
    private const float MAX_DEVIATION = 2f;

    private const int COLOR_SAMPLES = 4096;
    private static int OutputSampleRate;

    private int _attemptsLeft;

    private float[] _audioData;

    private SpinLock _audioLock = new(false);

    private volatile bool _initialized;

    private bool _lastCleared;

    private double _lastDspTime;

    private int _lastReadCount;

    private string _lastUri;

    private Texture _texture;

    internal VideoTextureConnector connector;

    private string dataSource;

    private MediaTrackInfo defaultAudioTrack;

    private bool firstReportAfterSeek;

    private PlaybackCallback getPlayback;

    private bool inBackground;

    private int? lastAudioTrack;

    private double lastReportedBeforeSeek;

    private double lastReportedPosition;

    private float lastReportedPositionTime;

    private float lastReportedPositionTimeBeforeSeek;

    private double lastSeekError;

    private MediaPlayer mediaPlayer;

    private Action onLoaded;

    private float pauseCooloff;

    private float playCooloff;

    private bool readingAudio;

    private bool seeking;

    private MediaPlayerStandalone standalonePlayer;

    private float volume;

    public string PlaybackEngine => "UMP";

    private double EstimatedPositionBeforeSeek =>
        lastReportedBeforeSeek + (Time.time - lastReportedPositionTimeBeforeSeek);

    private double EstimatedPosition => lastReportedPosition + (Time.time - lastReportedPositionTime);

    private void Awake()
    {
        if (OutputSampleRate == 0) OutputSampleRate = AudioSettings.outputSampleRate;
        var options = new PlayerOptions(null);
        switch (UMPSettings.RuntimePlatform)
        {
            case UMPSettings.Platforms.Win:
            case UMPSettings.Platforms.Mac:
            case UMPSettings.Platforms.Linux:
                options = new PlayerOptionsStandalone(null)
                {
                    FixedVideoSize = Vector2.zero,
                    HardwareDecoding = PlayerOptions.States.Disable,
                    FlipVertically = true,
                    UseTCP = false,
                    FileCaching = 300,
                    LiveCaching = 300,
                    DiskCaching = 300,
                    NetworkCaching = 300
                };
                break;
            case UMPSettings.Platforms.Android:
                options = new PlayerOptionsAndroid(null)
                {
                    FixedVideoSize = Vector2.zero,
                    PlayInBackground = false,
                    UseTCP = false,
                    NetworkCaching = 300
                };
                break;
            case UMPSettings.Platforms.iOS:
                options = new PlayerOptionsIPhone(null)
                {
                    FixedVideoSize = Vector2.zero,
                    VideoToolbox = true,
                    VideoToolboxFrameWidth = 4096,
                    VideoToolboxAsync = false,
                    VideoToolboxWaitAsync = true,
                    PlayInBackground = false,
                    UseTCP = false,
                    PacketBuffering = true,
                    MaxBufferSize = 15728640,
                    MinFrames = 50000,
                    Infbuf = false,
                    Framedrop = 0,
                    MaxFps = 31
                };
                break;
        }

        mediaPlayer = new MediaPlayer(this, null, options);
        standalonePlayer = (MediaPlayerStandalone)mediaPlayer.Player;
        mediaPlayer.EventManager.PlayerPositionChangedListener += PositionChanged;
        mediaPlayer.EventManager.PlayerImageReadyListener += OnTextureCreated;
        mediaPlayer.EventManager.PlayerEndReachedListener += EndReached;
        mediaPlayer.EventManager.PlayerEncounteredErrorListener += EventManager_PlayerEncounteredErrorListener;
        mediaPlayer.EventManager.PlayerPreparedListener += EventManager_PlayerPreparedListener;
    }

    private void Update()
    {
        if (!_initialized) return;

        var flag = float.IsPositiveInfinity(Length);
        if (connector.world.Focus == World.WorldFocus.Background)
        {
            if (mediaPlayer.IsPlaying && !inBackground)
            {
                mediaPlayer.Mute = true;
                if (!flag) mediaPlayer.Pause();
                seeking = false;
                inBackground = true;
            }

            return;
        }

        var unityTexture = UnityTexture;
        if (unityTexture != null)
        {
            if (unityTexture.filterMode != connector.filterMode) unityTexture.filterMode = connector.filterMode;
            if (unityTexture.anisoLevel != connector.anisoLevel) unityTexture.anisoLevel = connector.anisoLevel;
            if (unityTexture.wrapModeU != connector.wrapU) unityTexture.wrapModeU = connector.wrapU;
            if (unityTexture.wrapModeV != connector.wrapV) unityTexture.wrapModeV = connector.wrapV;
        }

        if (defaultAudioTrack == null) defaultAudioTrack = mediaPlayer.AudioTrack;
        if (lastAudioTrack != connector.audioTrackIndex)
        {
            if (!connector.audioTrackIndex.HasValue)
            {
                mediaPlayer.AudioTrack = defaultAudioTrack;
            }
            else
            {
                var audioTracks = mediaPlayer.AudioTracks;
                if (audioTracks != null && audioTracks.Length != 0)
                {
                    var num = MathX.Clamp(connector.audioTrackIndex.Value, 0, audioTracks.Length);
                    mediaPlayer.AudioTrack = audioTracks[num];
                }
            }

            lastAudioTrack = connector.audioTrackIndex;
        }

        inBackground = false;
        var playbackState = getPlayback();
        volume = MathX.Clamp01(playbackState.volume);
        mediaPlayer.Mute = !playbackState.play;
        mediaPlayer.Volume = 100;
        if (flag)
        {
            if (playbackState.play && !mediaPlayer.IsPlaying && playCooloff <= 0f)
            {
                mediaPlayer.Play();
                mediaPlayer.Position = 0f;
                playCooloff = 2f;
                pauseCooloff = 0f;
            }

            playCooloff -= Time.deltaTime;
            pauseCooloff -= Time.deltaTime;
            return;
        }

        if (playbackState.play)
        {
            pauseCooloff = 0f;
            var num2 = MathX.Abs(playbackState.position - EstimatedPosition);
            if (!mediaPlayer.IsPlaying && playCooloff <= 0f)
            {
                mediaPlayer.Time = (long)(playbackState.position * 1000.0);
                PositionChanged((float)(playbackState.position / Length));
                mediaPlayer.Play();
                playCooloff = 2f;
            }
            else if (num2 > 2.0 && !seeking)
            {
                lastReportedBeforeSeek = lastReportedPosition;
                lastReportedPositionTimeBeforeSeek = lastReportedPositionTime;
                mediaPlayer.Time = (long)((playbackState.position + lastSeekError) * 1000.0);
                PositionChanged((float)(playbackState.position / Length));
                seeking = true;
                firstReportAfterSeek = true;
            }

            playCooloff -= Time.deltaTime;
        }
        else
        {
            playCooloff = 0f;
            seeking = false;
            if (mediaPlayer.IsPlaying && pauseCooloff <= 0f)
            {
                pauseCooloff = 2f;
                mediaPlayer.Pause();
            }

            pauseCooloff -= Time.deltaTime;
        }

        var num3 = mediaPlayer.Time * 0.001;
        CurrentClockError = (float)(num3 - playbackState.position);
    }

    private void OnDestroy()
    {
        var mediaPlayer = this.mediaPlayer;
        connector.Engine.AudioSystem.AudioUpdate -= OnAudioFilterUpdate;
        var lockTaken = false;
        try
        {
            _audioLock.Enter(ref lockTaken);
            this.mediaPlayer = null;
            standalonePlayer = null;
            _initialized = false;
        }
        finally
        {
            if (lockTaken) _audioLock.Exit();
        }

        mediaPlayer.Release();
        connector = null;
        onLoaded = null;
        getPlayback = null;
    }

    public Texture UnityTexture
    {
        get
        {
            var texture = _texture;
            if ((object)texture == null)
            {
                var videoTextureConnector = connector;
                if (videoTextureConnector == null) return null;

                texture = videoTextureConnector.Engine.AssetManager.DarkCheckerTexture.GetUnity();
            }

            return texture;
        }
    }

    public bool IsLoaded { get; private set; }

    public bool HasAlpha { get; private set; }

    public int2 Size { get; private set; }

    public float Length { get; private set; }

    public float CurrentClockError { get; private set; }

    public void AudioRead<S>(Span<S> buffer) where S : unmanaged, IAudioSample<S>
    {
        if (standalonePlayer == null)
        {
            for (var i = 0; i < buffer.Length; i++) buffer[i] = default;
        }
        else if (_audioData == null || _audioData.Length / 2 < buffer.Length)
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
        connector.Engine.AudioSystem.AudioUpdate += OnAudioFilterUpdate;
        this.connector = connector;
        this.dataSource = dataSource;
        onLoaded = onReady;
        this.getPlayback = getPlayback;
        lastAudioTrack = null;
        defaultAudioTrack = null;
        if (mediaPlayer == null) throw new InvalidOperationException("MediaPlayer is null! Cannot Setup playback");

        mediaPlayer.DataSource = dataSource;
        mediaPlayer.Mute = true;
        mediaPlayer.Play();
    }

    private void SendOnLoaded()
    {
        if (onLoaded == null)
        {
            connector?.onTextureChanged?.Invoke();
            return;
        }

        onLoaded();
        onLoaded = null;
    }

    private void OnAudioFilterUpdate()
    {
        if (!_initialized) return;

        var lockTaken = false;
        try
        {
            _audioLock.Enter(ref lockTaken);
            if (standalonePlayer == null) return;

            if (_audioData == null) _audioData = new float[connector.Engine.AudioSystem.BufferSize * 2];
            if (standalonePlayer.OnAudioFilterRead(_audioData, AudioOutput.AudioChannels.Both))
            {
                _lastCleared = false;
                if (volume < 1f)
                    for (var i = 0; i < _audioData.Length; i++)
                        _audioData[i] *= volume;
            }
            else if (!_lastCleared)
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

    public void SetupCallback(PlaybackCallback callback)
    {
        getPlayback = callback;
    }

    private void EventManager_PlayerEncounteredErrorListener()
    {
        UniLog.Log("UMP Player Encountered Error. LastError: " + standalonePlayer?.GetLastError());
        StartCoroutine(DelayFail());
    }

    private IEnumerator DelayFail()
    {
        yield return new WaitForSecondsRealtime(5f);

        SendOnLoaded();
    }

    private void PositionChanged(float normalizedPos)
    {
        if (firstReportAfterSeek)
        {
            firstReportAfterSeek = false;
            return;
        }

        var num = normalizedPos * (double)Length;
        if (seeking)
        {
            if (MathX.Abs(EstimatedPositionBeforeSeek - num) > 2.0)
                lastSeekError = lastSeekError + EstimatedPosition - num;
            seeking = false;
        }

        lastReportedPosition = num;
        lastReportedPositionTime = Time.time;
    }

    private void EndReached()
    {
        mediaPlayer.Stop(false);
        seeking = false;
    }

    private void OnTextureCreated(Texture2D obj)
    {
        _initialized = true;
        if (mediaPlayer.SpuTracks != null && mediaPlayer.SpuTracks.Length != 0)
        {
            var spuTracks = mediaPlayer.SpuTracks;
            foreach (var mediaTrackInfo in spuTracks)
                if (mediaTrackInfo.Id < 0)
                {
                    mediaPlayer.SpuTrack = mediaTrackInfo;
                    break;
                }
        }

        if (mediaPlayer.Length == 0L && mediaPlayer.AbleToPlay)
            Length = float.PositiveInfinity;
        else
            Length = mediaPlayer.Length / 1000f;
        var v = mediaPlayer.VideoSize.ToEngine();
        Size = (int2)v;
        HasAlpha = false;
        _texture = obj;
        IsLoaded = true;
        SendOnLoaded();
    }

    private void EventManager_PlayerPreparedListener(int arg1, int arg2)
    {
        if (!_initialized) OnTextureCreated(null);
    }
}