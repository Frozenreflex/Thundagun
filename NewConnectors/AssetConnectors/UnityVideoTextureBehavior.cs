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

namespace Thundagun.NewConnectors.AssetConnectors;

public class UnityVideoTextureBehavior : MonoBehaviour, IVideoTextureBehaviour
{
	private static int OutputSampleRate;

	private bool _initialized;

	private Texture _lastTexture;

	private VideoPlayer videoPlayer;

	private VideoTextureConnector connector;

	private Action onReady;

	private PlaybackCallback getPlayback;

	private AudioSampleProvider sampleProvider;

	private SpinLock _audioLock = new SpinLock(enableThreadOwnerTracking: false);

	private int outputSampleRate;

	private bool isReading;

	private float[] _audioData;

	private float[] _conversionBuffer;

	private double _lastDspTime;

	private int _lastReadCount;

	private bool _lastCleared;

	private float volume;

	private bool playing;

	private double _lastPosition;

	private MonoSample _lastMonoSample;

	private StereoSample _lastStereoSample;

	private QuadSample _lastQuadSample;

	private Surround51Sample _last51sample;

	public bool IsLoaded { get; private set; }

	public Texture UnityTexture
	{
		get
		{
			object obj = videoPlayer?.texture;
			if (obj == null)
			{
				VideoTextureConnector videoTextureConnector = connector;
				if (videoTextureConnector == null)
				{
					return null;
				}
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
			if (videoPlayer == null || !_initialized)
			{
				return 0f;
			}
			return (float)videoPlayer.frameCount / videoPlayer.frameRate;
		}
	}

	public float CurrentClockError { get; private set; }

	public int2 Size
	{
		get
		{
			if (videoPlayer?.texture == null || !_initialized)
			{
				return int2.Zero;
			}
			return new int2(videoPlayer.texture.width, videoPlayer.texture.height);
		}
	}

	public string PlaybackEngine => "Unity Native";

	private void OnAudioFilterUpdate()
	{
		if (!_initialized)
		{
			return;
		}
		int bufferSize = connector.Engine.AudioSystem.BufferSize;
		if (_audioData == null)
		{
			_audioData = _audioData.EnsureExactSize(bufferSize * 2);
		}
		bool lockTaken = false;
		try
		{
			_audioLock.Enter(ref lockTaken);
			double num = 1.0;
			ushort num2 = sampleProvider?.channelCount ?? 0;
			if (sampleProvider != null)
			{
				num = (double)sampleProvider.sampleRate / (double)outputSampleRate;
			}
			int num3 = MathX.RoundToInt((double)bufferSize * num * (double)(int)num2);
			if (volume > 0f && sampleProvider != null && (float)(sampleProvider.availableSampleFrameCount * sampleProvider.channelCount) >= (float)num3 * 1.05f)
			{
				_lastCleared = false;
				using NativeArray<float> sampleFrames = new NativeArray<float>(num3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
				sampleProvider.ConsumeSampleFrames(sampleFrames);
				_conversionBuffer = _conversionBuffer.EnsureSize(num3);
				sampleFrames.CopyTo(_conversionBuffer);
				Span<StereoSample> target = _audioData.AsStereoBuffer();
				switch (num2)
				{
				case 1:
					_conversionBuffer.AsMonoBuffer().CopySamples(target, ref _lastPosition, ref _lastMonoSample, num);
					break;
				case 2:
					_conversionBuffer.AsStereoBuffer().CopySamples(target, ref _lastPosition, ref _lastStereoSample, num);
					break;
				case 4:
					_conversionBuffer.AsQuadBuffer().CopySamples(target, ref _lastPosition, ref _lastQuadSample, num);
					break;
				case 6:
					_conversionBuffer.AsSurround51Buffer().CopySamples(target, ref _lastPosition, ref _last51sample, num);
					break;
				}
				_lastPosition -= MathX.CeilToInt(_lastPosition);
				if (volume < 1f)
				{
					for (int i = 0; i < _audioData.Length; i++)
					{
						_audioData[i] *= volume;
					}
				}
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
			if (lockTaken)
			{
				_audioLock.Exit();
			}
		}
	}

	public void AudioRead<S>(Span<S> buffer) where S : unmanaged, IAudioSample<S>
	{
		if (!_initialized || !playing)
		{
			for (int i = 0; i < buffer.Length; i++)
			{
				buffer[i] = default(S);
			}
		}
		else if (_audioData == null || _audioData.Length < buffer.Length)
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

	private void Awake()
	{
		if (OutputSampleRate == 0)
		{
			OutputSampleRate = AudioSettings.outputSampleRate;
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
			videoPlayer = base.gameObject.AddComponent<VideoPlayer>();
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

	private void Update()
	{
		if (!_initialized)
		{
			return;
		}
		Texture texture = videoPlayer.texture;
		if (_lastTexture != texture)
		{
			connector?.onTextureChanged?.Invoke();
			_lastTexture = texture;
		}
		if (texture.filterMode != connector.filterMode)
		{
			texture.filterMode = connector.filterMode;
		}
		if (texture.anisoLevel != connector.anisoLevel)
		{
			texture.anisoLevel = connector.anisoLevel;
		}
		if (texture.wrapModeU != connector.wrapU)
		{
			texture.wrapModeU = connector.wrapU;
		}
		if (texture.wrapModeV != connector.wrapV)
		{
			texture.wrapModeV = connector.wrapV;
		}
		PlaybackState playbackState = getPlayback();
		volume = MathX.Clamp01(playbackState.volume);
		videoPlayer.isLooping = playbackState.loop;
		videoPlayer.externalReferenceTime = playbackState.position;
		bool flag = (playing = playbackState.play && connector.world.Focus != World.WorldFocus.Background);
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

	private void VideoPlayer_prepareCompleted(VideoPlayer source)
	{
		IsLoaded = true;
		if (source.audioTrackCount > 0)
		{
			source.EnableAudioTrack(0, enabled: true);
			sampleProvider = source.GetAudioSampleProvider(0);
			sampleProvider.enableSilencePadding = false;
			sampleProvider.sampleFramesOverflow += SampleProvider_sampleFramesOverflow;
		}
		_initialized = true;
		SendReady();
		UniLog.Log($"AudioTrackCount: {source.audioTrackCount}, SampleRate: {sampleProvider?.sampleRate}, MaxSampleFrameCount: {sampleProvider?.maxSampleFrameCount}");
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

	private void OnDestroy()
	{
		connector.Engine.AudioSystem.AudioUpdate -= OnAudioFilterUpdate;
		bool lockTaken = false;
		try
		{
			_audioLock.Enter(ref lockTaken);
			_initialized = false;
			if (videoPlayer != null)
			{
				UnityEngine.Object.Destroy(videoPlayer);
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
			if (lockTaken)
			{
				_audioLock.Exit();
			}
		}
		videoPlayer = null;
		connector = null;
		getPlayback = null;
		sampleProvider = null;
	}
}
