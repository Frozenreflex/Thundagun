#region

using Elements.Core;
using FrooxEngine;
using UnityEngine;
using UnityFrooxEngineRunner;
using AudioRolloffMode = UnityEngine.AudioRolloffMode;

#endregion

namespace Thundagun.NewConnectors.ComponentConnectors;

public class AudioOutputConnector : ComponentConnectorSingle<AudioOutput>
{
    public AudioOutputBehavior _outputBehavior;

    public AudioSource UnityAudioSource => _outputBehavior?._unityAudio;

    public override void ApplyChanges()
    {
        Thundagun.QueuePacket(new ApplyChangesAudioOutputConnector(this));
    }

    public override void DestroyMethod(bool destroyingWorld)
    {
        if (_outputBehavior != null) DestroyBehavior(destroyingWorld);
        base.DestroyMethod(destroyingWorld);
    }

    public void DestroyBehavior(bool destroyingWorld)
    {
        _outputBehavior.Deinitialize();
        if (!destroyingWorld && _outputBehavior.gameObject) Object.Destroy(_outputBehavior.gameObject);
        _outputBehavior = null;
    }
}

public class ApplyChangesAudioOutputConnector : UpdatePacket<AudioOutputConnector>
{
    public float ActualVolume;
    public float DopplerLevel;
    public bool IgnoreReverbZones;
    public float MaxDistance;
    public float MinDistance;
    public int Priority;
    public AudioRolloffMode RolloffMode;
    public bool ShouldBeEnabled;
    public float SpatialBlend;
    public bool Spatialize;
    public IAudioSource Target;

    public ApplyChangesAudioOutputConnector(AudioOutputConnector owner) : base(owner)
    {
        ShouldBeEnabled = owner.Owner.ShouldBeEnabled;
        if (ShouldBeEnabled)
        {
            //TODO: do we need to make audio targets async too? or are they thread safe?
            Target = owner.Owner.Source.Target;
            ActualVolume = owner.Owner.ActualVolume;
            Priority = MathX.Clamp(owner.Owner.Priority.Value, 0, byte.MaxValue);
            SpatialBlend = owner.Owner.SpatialBlend.Value;
            Spatialize = owner.Owner.Spatialize.Value;
            DopplerLevel = MathX.Clamp(MathX.FilterInvalid(owner.Owner.DopplerLevel.Value), 0.0f, 100f);
            owner.Owner.GetActualDistances(out MinDistance, out MaxDistance);
            IgnoreReverbZones = owner.Owner.IgnoreReverbZones.Value;
            RolloffMode = owner.Owner.RolloffMode.Value.ToUnity();
        }
    }

    public override void Update()
    {
        if (!ShouldBeEnabled)
        {
            if (Owner._outputBehavior != null) Owner.DestroyBehavior(false);
        }
        else
        {
            if (Owner._outputBehavior == null)
            {
                var gameObject = new GameObject("");
                gameObject.transform.SetParent(Owner.AttachedGameObject.transform, false);
                Owner._outputBehavior = gameObject.AddComponent<AudioOutputBehavior>();
                Owner._outputBehavior.Init(Owner.Engine);
            }

            var unityAudio = Owner._outputBehavior._unityAudio;
            if (unityAudio.spatializePostEffects) unityAudio.spatializePostEffects = false;
            Owner._outputBehavior._audioSource = Target;
            if (unityAudio.volume != ActualVolume) unityAudio.volume = ActualVolume;
            if (unityAudio.priority != Priority) unityAudio.priority = Priority;
            if (unityAudio.spatialBlend != SpatialBlend) unityAudio.spatialBlend = SpatialBlend;
            if (unityAudio.spatialize != Spatialize)
            {
                unityAudio.spatialize = Spatialize;
                //TODO: ?????
                if (Owner.Owner is not null && !Owner.Owner.IsDestroyed)
                {
                    if (Spatialize) Owner.Engine.AudioSystem.SpatializerEnabled(Owner.Owner);
                    else Owner.Engine.AudioSystem.SpatializerDisabled(Owner.Owner);
                }
            }

            if (unityAudio.dopplerLevel != DopplerLevel) unityAudio.dopplerLevel = DopplerLevel;

            var minDistance = MathX.Clamp(MinDistance, 0.0f, 1000000f);
            var maxDistance = MathX.Clamp(MaxDistance, 0.0f, 1100000f);
            if (unityAudio.bypassReverbZones != IgnoreReverbZones) unityAudio.bypassReverbZones = IgnoreReverbZones;
            if (unityAudio.minDistance != minDistance) unityAudio.minDistance = minDistance;
            if (unityAudio.maxDistance != maxDistance) unityAudio.maxDistance = maxDistance;
            if (unityAudio.rolloffMode != RolloffMode) unityAudio.rolloffMode = RolloffMode;
            if (Owner._outputBehavior._playing) return;
            Owner._outputBehavior._playing = true;
            if (!unityAudio.enabled || !unityAudio.gameObject.activeInHierarchy) return;
            unityAudio.Play();
        }
    }
}