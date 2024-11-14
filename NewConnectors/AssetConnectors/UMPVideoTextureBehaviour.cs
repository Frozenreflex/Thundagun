using System;
using System.Collections;
using System.Threading;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using UMP;
using UnityEngine;
using UnityFrooxEngineRunner;

namespace Thundagun.NewConnectors.AssetConnectors;

public class UMPVideoTextureBehaviour : MonoBehaviour, IVideoTextureBehaviour
{
	private static int OutputSampleRate;

	private const float MAX_DEVIATION = 2f;

	private const int COLOR_SAMPLES = 4096;

	internal VideoTextureConnector connector;

	private MediaPlayer mediaPlayer;

	private MediaPlayerStandalone standalonePlayer;

	private Action onLoaded;

	private PlaybackCallback getPlayback;

	private string dataSource;

	private Texture _texture;

	private volatile bool _initialized;

	private float playCooloff;

	private float pauseCooloff;

	private double lastReportedPosition;

	private float lastReportedPositionTime;

	private double lastReportedBeforeSeek;

	private float lastReportedPositionTimeBeforeSeek;

	private bool seeking;

	private bool firstReportAfterSeek;

	private double lastSeekError;

	private string _lastUri;

	private int _attemptsLeft;

	private double _lastDspTime;

	private int _lastReadCount;

	private bool readingAudio;

	private float[] _audioData;

	private bool _lastCleared;

	private SpinLock _audioLock = new SpinLock(enableThreadOwnerTracking: false);

	private int? lastAudioTrack;

	private MediaTrackInfo defaultAudioTrack;

	private bool inBackground;

	private float volume;

	public Texture UnityTexture
	{
		get
		{
			Texture texture = _texture;
			if ((object)texture == null)
			{
				VideoTextureConnector videoTextureConnector = connector;
				if (videoTextureConnector == null)
				{
					return null;
				}
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

	public string PlaybackEngine => "UMP";

	private double EstimatedPositionBeforeSeek => lastReportedBeforeSeek + (double)(Time.time - lastReportedPositionTimeBeforeSeek);

	private double EstimatedPosition => lastReportedPosition + (double)(Time.time - lastReportedPositionTime);

	public void AudioRead<S>(Span<S> buffer) where S : unmanaged, IAudioSample<S>
	{
		if (standalonePlayer == null)
		{
			for (int i = 0; i < buffer.Length; i++)
			{
				buffer[i] = default(S);
			}
		}
		else if (_audioData == null || _audioData.Length / 2 < buffer.Length)
		{
			for (int j = 0; j < buffer.Length; j++)
			{
				buffer[j] = default(S);
			}
		}
		else
		{
			double sourcePosition = 0.0;
			StereoSample lastSample = default(StereoSample);
			_audioData.AsStereoBuffer().CopySamples(buffer, ref sourcePosition, ref lastSample);
		}
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

	public void Setup(VideoTextureConnector connector, string dataSource, Action onReady, PlaybackCallback getPlayback)
	{
		connector.Engine.AudioSystem.AudioUpdate += OnAudioFilterUpdate;
		this.connector = connector;
		this.dataSource = dataSource;
		onLoaded = onReady;
		this.getPlayback = getPlayback;
		lastAudioTrack = null;
		defaultAudioTrack = null;
		if (mediaPlayer == null)
		{
			throw new InvalidOperationException("MediaPlayer is null! Cannot Setup playback");
		}
		mediaPlayer.DataSource = dataSource;
		mediaPlayer.Mute = true;
		mediaPlayer.Play();
	}

	private void OnAudioFilterUpdate()
	{
		if (!_initialized)
		{
			return;
		}
		bool lockTaken = false;
		try
		{
			_audioLock.Enter(ref lockTaken);
			if (standalonePlayer == null)
			{
				return;
			}
			if (_audioData == null)
			{
				_audioData = new float[connector.Engine.AudioSystem.BufferSize * 2];
			}
			if (standalonePlayer.OnAudioFilterRead(_audioData, UMP.AudioOutput.AudioChannels.Both))
			{
				_lastCleared = false;
				if (volume < 1f)
				{
					for (int i = 0; i < _audioData.Length; i++)
					{
						_audioData[i] *= volume;
					}
				}
			}
			else if (!_lastCleared)
			{
				Array.Clear(_audioData, 0, _audioData.Length);
				_lastCleared = true;
			}
		}
		finally
		{
			if (lockTaken)
			{
				_audioLock.Exit();
			}
		}
	}

	public void SetupCallback(PlaybackCallback callback)
	{
		getPlayback = callback;
	}

	private void Awake()
	{
		if (OutputSampleRate == 0)
		{
			OutputSampleRate = AudioSettings.outputSampleRate;
		}
		PlayerOptions options = new PlayerOptions(null);
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
				NetworkCaching = 300,
				//LogDetail = LogLevels.Disable
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
		double num = (double)normalizedPos * (double)Length;
		if (seeking)
		{
			if (MathX.Abs(EstimatedPositionBeforeSeek - num) > 2.0)
			{
				lastSeekError = lastSeekError + EstimatedPosition - num;
			}
			seeking = false;
		}
		lastReportedPosition = num;
		lastReportedPositionTime = Time.time;
	}

	private void EndReached()
	{
		mediaPlayer.Stop(resetTexture: false);
		seeking = false;
	}

	private void OnTextureCreated(UnityEngine.Texture2D obj)
	{
		_initialized = true;
		if (mediaPlayer.SpuTracks != null && mediaPlayer.SpuTracks.Length != 0)
		{
			MediaTrackInfo[] spuTracks = mediaPlayer.SpuTracks;
			foreach (MediaTrackInfo mediaTrackInfo in spuTracks)
			{
				if (mediaTrackInfo.Id < 0)
				{
					mediaPlayer.SpuTrack = mediaTrackInfo;
					break;
				}
			}
		}
		if (mediaPlayer.Length == 0L && mediaPlayer.AbleToPlay)
		{
			Length = float.PositiveInfinity;
		}
		else
		{
			Length = (float)mediaPlayer.Length / 1000f;
		}
		float2 v = mediaPlayer.VideoSize.ToEngine();
		Size = (int2)v;
		HasAlpha = false;
		_texture = obj;
		IsLoaded = true;
		SendOnLoaded();
	}

	private void EventManager_PlayerPreparedListener(int arg1, int arg2)
	{
		if (!_initialized)
		{
			OnTextureCreated(null);
		}
	}

	private void Update()
	{
		if (!_initialized)
		{
			return;
		}
		bool flag = float.IsPositiveInfinity(Length);
		if (connector.world.Focus == World.WorldFocus.Background)
		{
			if (mediaPlayer.IsPlaying && !inBackground)
			{
				mediaPlayer.Mute = true;
				if (!flag)
				{
					mediaPlayer.Pause();
				}
				seeking = false;
				inBackground = true;
			}
			return;
		}
		Texture unityTexture = UnityTexture;
		if (unityTexture != null)
		{
			if (unityTexture.filterMode != connector.filterMode)
			{
				unityTexture.filterMode = connector.filterMode;
			}
			if (unityTexture.anisoLevel != connector.anisoLevel)
			{
				unityTexture.anisoLevel = connector.anisoLevel;
			}
			if (unityTexture.wrapModeU != connector.wrapU)
			{
				unityTexture.wrapModeU = connector.wrapU;
			}
			if (unityTexture.wrapModeV != connector.wrapV)
			{
				unityTexture.wrapModeV = connector.wrapV;
			}
		}
		if (defaultAudioTrack == null)
		{
			defaultAudioTrack = mediaPlayer.AudioTrack;
		}
		if (lastAudioTrack != connector.audioTrackIndex)
		{
			if (!connector.audioTrackIndex.HasValue)
			{
				mediaPlayer.AudioTrack = defaultAudioTrack;
			}
			else
			{
				MediaTrackInfo[] audioTracks = mediaPlayer.AudioTracks;
				if (audioTracks != null && audioTracks.Length != 0)
				{
					int num = MathX.Clamp(connector.audioTrackIndex.Value, 0, audioTracks.Length);
					mediaPlayer.AudioTrack = audioTracks[num];
				}
			}
			lastAudioTrack = connector.audioTrackIndex;
		}
		inBackground = false;
		PlaybackState playbackState = getPlayback();
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
			double num2 = MathX.Abs(playbackState.position - EstimatedPosition);
			if (!mediaPlayer.IsPlaying && playCooloff <= 0f)
			{
				mediaPlayer.Time = (long)(playbackState.position * 1000.0);
				PositionChanged((float)(playbackState.position / (double)Length));
				mediaPlayer.Play();
				playCooloff = 2f;
			}
			else if (num2 > 2.0 && !seeking)
			{
				lastReportedBeforeSeek = lastReportedPosition;
				lastReportedPositionTimeBeforeSeek = lastReportedPositionTime;
				mediaPlayer.Time = (long)((playbackState.position + lastSeekError) * 1000.0);
				PositionChanged((float)(playbackState.position / (double)Length));
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
		double num3 = (double)mediaPlayer.Time * 0.001;
		CurrentClockError = (float)(num3 - playbackState.position);
	}

	private void OnDestroy()
	{
		MediaPlayer mediaPlayer = this.mediaPlayer;
		connector.Engine.AudioSystem.AudioUpdate -= OnAudioFilterUpdate;
		bool lockTaken = false;
		try
		{
			_audioLock.Enter(ref lockTaken);
			this.mediaPlayer = null;
			standalonePlayer = null;
			_initialized = false;
		}
		finally
		{
			if (lockTaken)
			{
				_audioLock.Exit();
			}
		}
		mediaPlayer.Release();
		connector = null;
		onLoaded = null;
		getPlayback = null;
	}
}
