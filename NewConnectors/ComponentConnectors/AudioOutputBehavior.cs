using System;
using Elements.Assets;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;

namespace Thundagun.NewConnectors.ComponentConnectors;

  public class AudioOutputBehavior : MonoBehaviour
  {
    public Engine _engine;
    public AudioSource _unityAudio;
    public IAudioSource _audioSource;
    public bool _playing;
    public float amplitude;

    internal void Init(Engine engine)
    {
      _engine = engine;
      _unityAudio = gameObject.AddComponent<AudioSource>();
      _unityAudio.Stop();
      _unityAudio.loop = true;
      _unityAudio.playOnAwake = false;
    }

    private void OnEnable()
    {
      if (!_playing || _unityAudio.isPlaying)
        return;
      _unityAudio?.Play();
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
      var engine = _engine;
      if (engine == null)
        return;
      var audioSource = _audioSource;
      AudioSystemConnector.InformOfDSPTime(AudioSettings.dspTime);
      if (!_playing)
        engine.EmptyAudioRead();
      else if (audioSource is {IsRemoved: false, IsActive: true})
      {
        engine.AudioRead();
        if (channels != 1)
        {
          if (channels != 2) throw new Exception("Unsupported channel configuration: " + channels);
          Read(audioSource, data.AsStereoBuffer());
        }
        else Read(audioSource, data.AsMonoBuffer());
      }
      else
        engine.EmptyAudioRead();
    }

    private void Read<TS>(IAudioSource audioSource, Span<TS> buffer) where TS : unmanaged, IAudioSample<TS>
    {
      audioSource.Read(buffer);
      var index = 0;
      while (amplitude < 1.0)
      {
        buffer[index] = buffer[index].Multiply(amplitude);
        amplitude += 0.01f;
        index++;
      }
    }

    private void OnDestroy() => Deinitialize();

    internal void Deinitialize()
    {
      _engine = null;
      _unityAudio = null;
      _audioSource = null;
    }
  }