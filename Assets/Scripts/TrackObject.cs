using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrackObject : MonoBehaviour
{
    [SerializeField] private MidiFileImporter midiFileImporter;
    [Tooltip("对应 MidiFileImporter.midiTracks 中的轨道索引，一条 Lane 对应一条 Track")]
    [SerializeField] private int trackIndex;
    [SerializeField] private int noteTheme;
    [SerializeField] private NoteSpawner noteSpawner;
    [SerializeField] private KeyCode trackKey;
    [Header("Debug")]
    [Tooltip("开启后，音符到达 noteTime 时自动 Hit()，无需按键")]
    [SerializeField] private bool autoHit;

    public GameObject notePrefab;

    private readonly List<double> timeStamps = new List<double>();
    private readonly List<NoteObject> notes = new List<NoteObject>();

    private int spawnIndex = 0;
    private int inputIndex = 0;
    private bool timestampsReady;

    void Start()
    {
        InitializeTimestamps();
    }

    void InitializeTimestamps()
    {
        if (trackIndex < 0 || trackIndex >= midiFileImporter.midiTracks.Count)
        {
            Debug.LogError(
                $"Lane ({name}): trackIndex={trackIndex} 无效，当前共有 {midiFileImporter.midiTracks.Count} 条轨道");
            return;
        }

        MidiTrack track = midiFileImporter.midiTracks[trackIndex];
        timeStamps.Clear();

        foreach (MidiNote note in track.notes)
        {
            // 长音符轨道只在 Note On 时生成可视音符
            if (track.isLongNote && !note.isNoteOn)
                continue;

            timeStamps.Add(note.timeInSeconds);
        }

        timestampsReady = true;
        Debug.Log($"Lane ({name}): 已绑定轨道 [{trackIndex}] \"{track.trackName}\"，共 {timeStamps.Count} 个音符");
    }

    void Update()
    {
        if (!timestampsReady || timeStamps.Count == 0)
            return;

        if (SongManager.Instance == null || SongManager.Instance.audioSource == null)
            return;

        if (!SongManager.Instance.audioSource.isPlaying)
            return;

        double audioTime = GetInputAudioTime();
        float fallingTime = SongManager.Instance.GetNoteFallingTime();

        // 可能在同一帧内需要生成多个音符
        while (spawnIndex < timeStamps.Count &&
               SongManager.Instance.GetAudioSourceTime() >= timeStamps[spawnIndex] - fallingTime)
        {
            NoteObject note = noteSpawner.SpawnNewNote(trackIndex, noteTheme);
            notes.Add(note);
            spawnIndex++;
        }

        if (inputIndex >= timeStamps.Count)
        {
            return;
            // end game
        }

        double noteTime = timeStamps[inputIndex];
        double margin = SongManager.Instance.marginOfError;
        double delta = audioTime - noteTime;

        if (autoHit && audioTime >= noteTime)
        {
            Hit();
            inputIndex++;
            return;
        }

        if (Input.GetKeyDown(trackKey))
        {

            if(Math.Abs(delta) < margin)
            {
                Hit();
                inputIndex++;
                return;
            }

            if(delta > margin)
            {
                Miss();
                inputIndex++;
                return;
            }
        }

        if(audioTime > noteTime + margin)
        {
            Miss();
            inputIndex++;
        }
    }


    private void Hit()
    {
        if (inputIndex < notes.Count && notes[inputIndex] != null)
        {
            notes[inputIndex].OnHit();
            notes[inputIndex] = null;
        }

        if (ScoreManager.Instance != null)
            ScoreManager.Instance.AddHitScore();
    }

    private void Miss()
    {
    }

    private double GetInputAudioTime()
    {
        return SongManager.Instance.GetAudioSourceTime() - SongManager.Instance.inputDelayInMilliseconds / 1000.0;
    }
}
