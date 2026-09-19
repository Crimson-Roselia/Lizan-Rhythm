using UnityEngine;

public class SongManager : MonoBehaviour
{
    public AudioSource audioSource;
    public float songDelayInSeconds;
    public double marginOfError; // in seconds

    public int inputDelayInMilliseconds;

    [SerializeField] private float noteTime; // 音符下落过程的时长

    public static SongManager Instance { get; private set; }

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
        audioSource.Play();
    }

    public double GetAudioSourceTime()
    {
        return (double)audioSource.timeSamples / audioSource.clip.frequency;
    }

}
