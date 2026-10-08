using UnityEngine;

public class SongManager : MonoBehaviour
{
    public AudioSource audioSource;
    public float songDelayInSeconds;
    public double marginOfError; // in seconds

    public int inputDelayInMilliseconds;

    [Tooltip("音符系统相对 BGM 的同步偏移（毫秒）。正值使音符移动和判定延后，负值使其提前；与输入延迟叠加。")]
    public int noteOffsetInMilliseconds = 0;

    [SerializeField] private float noteTime; // 音符下落过程的时长

    public static SongManager Instance { get; private set; }

    private AudioSource clockSource;
    private AudioClip clockClip;
    private double continuousAudioTime;
    private double previousRealtime;
    private float previousPitch;
    private bool clockWasPlaying;
    private bool clockInitialized;
    private int clockFrame = -1;

    private void Awake()
    {
        Instance = this;
    }

    public float GetNoteFallingTime()
    {
        return noteTime;
    }

    private void Start()
    {
        StartSong();
    }

    public void StartSong()
    {
        clockInitialized = false;
        clockFrame = -1;
        audioSource.Play();
    }

    public double GetAudioSourceTime()
    {
        if (audioSource == null || audioSource.clip == null)
        {
            clockInitialized = false;
            clockFrame = -1;
            return 0.0;
        }

        // Cache once per rendered frame: judgement, heads, tails and bodies must agree.
        if (clockInitialized && clockFrame == Time.frameCount &&
            clockSource == audioSource && clockClip == audioSource.clip)
            return continuousAudioTime;

        double realtime = Time.realtimeSinceStartupAsDouble;
        double sampleTime = (double)audioSource.timeSamples / audioSource.clip.frequency;
        bool playing = audioSource.isPlaying;
        double elapsed = System.Math.Max(0.0, realtime - previousRealtime);

        if (!clockInitialized || clockSource != audioSource || clockClip != audioSource.clip ||
            playing != clockWasPlaying || !playing)
        {
            // Re-anchor on first use, pause/resume, stop or clip/source replacement.
            continuousAudioTime = sampleTime;
        }
        else
        {
            // timeSamples is a block-updated playback cursor, not a render-frame clock.
            // Predict between updates without depending on timeScale or accumulating float time.
            double predicted = continuousAudioTime + elapsed * previousPitch;
            double error = sampleTime - predicted;
            int bufferLength;
            int bufferCount;
            AudioSettings.GetDSPBufferSize(out bufferLength, out bufferCount);
            double bufferDuration = (double)bufferLength * bufferCount /
                                    System.Math.Max(1, AudioSettings.outputSampleRate);
            double seekThreshold = System.Math.Max(0.25, bufferDuration *
                System.Math.Max(1.0, System.Math.Abs(previousPitch)) * 2.0);

            if (System.Math.Abs(error) > seekThreshold)
            {
                // A seek or loop must take effect immediately, rather than glide through the chart.
                continuousAudioTime = sampleTime;
            }
            else
            {
                // Correct drift gently (at most 5% of playback speed), never snap on a buffer tick.
                double correctionLimit = elapsed * System.Math.Abs(previousPitch) * 0.05;
                double correction = error * (1.0 - System.Math.Exp(-elapsed * 2.0));
                correction = System.Math.Max(-correctionLimit,
                    System.Math.Min(correctionLimit, correction));
                continuousAudioTime = predicted + correction;
            }
        }

        clockSource = audioSource;
        clockClip = audioSource.clip;
        previousRealtime = realtime;
        previousPitch = audioSource.pitch;
        clockWasPlaying = playing;
        clockInitialized = true;
        clockFrame = Time.frameCount;
        return continuousAudioTime;
    }

    // Keep visual note timing on the same clock that TrackObject uses for input and Miss checks.
    public double GetInputAudioTime()
    {
        // Apply the offset to the shared note clock so movement, auto-hit and judgement stay aligned.
        return GetAudioSourceTime() - noteOffsetInMilliseconds / 1000.0
                                   - inputDelayInMilliseconds / 1000.0;
    }

}
