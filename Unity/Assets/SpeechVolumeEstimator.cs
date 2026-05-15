using UnityEngine;
using Ubiq.Avatars;
using Ubiq.Voip;

public class SpeechVolumeEstimator : MonoBehaviour
{
    public float sampleSecondsPerIndicator = 2.0f;

    private Ubiq.Avatars.Avatar avatar;
    private VoipAvatar voipAvatar;
    private VoipPeerConnection subscribedPeerConnection;

    private float currentFrameVolumeSum;
    private int currentFrameSampleCount;
    private float[] volumeFrames;

    public float EstimateCurrentVolume()
    {
        return volumeFrames != null ? volumeFrames[0] : 0f;
    }

    private void Start()
    {
        avatar = GetComponentInParent<Ubiq.Avatars.Avatar>();
        voipAvatar = GetComponentInParent<VoipAvatar>();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void LateUpdate()
    {
        if (!avatar || avatar.IsLocal || !voipAvatar)
        {
            Unsubscribe();
            enabled = false;
            return;
        }

        if (voipAvatar.peerConnection != subscribedPeerConnection)
        {
            Unsubscribe();
            subscribedPeerConnection = voipAvatar.peerConnection;
            if (subscribedPeerConnection)
            {
                subscribedPeerConnection.playbackStatsPushed += OnPlaybackStatsPushed;
            }
        }
    }

    private void OnPlaybackStatsPushed(VoipPeerConnection.AudioStats stats)
    {
        if (volumeFrames == null)
        {
            volumeFrames = new float[1];
        }

        currentFrameVolumeSum += stats.volumeSum;
        currentFrameSampleCount += stats.sampleCount;

        var sampleRate = stats.sampleRate > 0 ? stats.sampleRate : AudioSettings.outputSampleRate;
        var volumeWindowSampleCount = Mathf.Max(1, Mathf.RoundToInt(sampleSecondsPerIndicator * sampleRate));

        if (currentFrameSampleCount > volumeWindowSampleCount)
        {
            PushVolumeSample(currentFrameVolumeSum / currentFrameSampleCount);
            currentFrameVolumeSum = 0;
            currentFrameSampleCount = 0;
        }
    }

    private void PushVolumeSample(float sample)
    {
        for (var i = volumeFrames.Length - 1; i >= 1; i--)
        {
            volumeFrames[i] = volumeFrames[i - 1];
        }
        volumeFrames[0] = sample;
    }

    private void Unsubscribe()
    {
        if (subscribedPeerConnection)
        {
            subscribedPeerConnection.playbackStatsPushed -= OnPlaybackStatsPushed;
            subscribedPeerConnection = null;
        }
    }
}
