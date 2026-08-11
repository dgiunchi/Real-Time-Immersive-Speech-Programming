public struct PlaybackStats
{
    public int sampleCount;
    public float volumeSum;

    public float volume
    {
        get { return sampleCount > 0 ? volumeSum / sampleCount : 0f; }
    }
}

public interface IPlaybackStatsSource
{
    PlaybackStats lastFrameStats { get; }
}
