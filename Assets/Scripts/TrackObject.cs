using System.Collections.Generic;
using UnityEngine;

public class TrackObject : MonoBehaviour
{
    [SerializeField] private MidiFileImporter midiFileImporter;
    [Tooltip("对应 MidiFileImporter.midiTracks 中的轨道索引，一条 Lane 对应一条 Track")]
    [SerializeField] private int trackIndex;
    [SerializeField] private int noteTheme;
    [SerializeField] private NoteSpawner noteSpawner;
    [SerializeField] private Transform noteMissLine;
    [SerializeField] private KeyCode trackKey;
    [SerializeField] private GameObject trackEffect;
    [Header("Judgement Effect Materials")]
    [SerializeField] private Material perfectMaterial;
    [SerializeField] private Material greatMaterial;
    [SerializeField] private Material goodMaterial;
    [SerializeField] private Material missMaterial;
    [Tooltip("短按时特效的最短显示时间（秒）")]
    [SerializeField, Min(0f)] private float trackEffectDuration = 0.1f;
    [Header("Debug")]
    [Tooltip("开启后自动击打普通音符，并自动按住长音符直到结束")]
    [SerializeField] private bool autoHit;

    public GameObject notePrefab;

    private readonly List<double> timeStamps = new List<double>();
    private readonly List<MidiLongNote> longNoteTimeStamps = new List<MidiLongNote>();
    private readonly List<ScheduledNote> scheduledNotes = new List<ScheduledNote>();
    private readonly List<NoteObject> notes = new List<NoteObject>();
    private readonly List<int> heldNoteIndices = new List<int>();

    private int spawnIndex;
    private int inputIndex;
    private bool timestampsReady;
    private float trackEffectEndTime;
    private bool judgementEffectTriggered;
    private MeshRenderer[] trackEffectRenderers;

    private void OnEnable()
    {
        trackEffectEndTime = float.NegativeInfinity;
        judgementEffectTriggered = false;
        trackEffectRenderers = trackEffect != null
            ? trackEffect.GetComponentsInChildren<MeshRenderer>(true)
            : new MeshRenderer[0];
        SetTrackEffectActive(false);
    }

    private void OnDisable()
    {
        trackEffectEndTime = float.NegativeInfinity;
        judgementEffectTriggered = false;
        SetTrackEffectActive(false);
    }

    private void LateUpdate()
    {
        bool autoHolding = autoHit && heldNoteIndices.Count > 0 &&
            SongManager.Instance != null && SongManager.Instance.audioSource != null &&
            SongManager.Instance.audioSource.isPlaying;
        UpdateTrackEffect(Input.GetKeyDown(trackKey) || judgementEffectTriggered,
            Input.GetKey(trackKey) || autoHolding);
        judgementEffectTriggered = false;
    }

    private void UpdateTrackEffect(bool keyDown, bool keyHeld)
    {
        if (keyDown)
            trackEffectEndTime = Time.unscaledTime + trackEffectDuration;

        SetTrackEffectActive(keyDown || keyHeld || Time.unscaledTime < trackEffectEndTime);
    }

    private void SetTrackEffectActive(bool active)
    {
        if (trackEffect != null && trackEffect.activeSelf != active)
            trackEffect.SetActive(active);
    }

    private void ShowJudgementEffect(NoteJudgement judgement)
    {
        Material material;
        switch (judgement)
        {
            case NoteJudgement.Perfect: material = perfectMaterial; break;
            case NoteJudgement.Great: material = greatMaterial; break;
            case NoteJudgement.Good: material = goodMaterial; break;
            case NoteJudgement.Miss: material = missMaterial; break;
            default: return;
        }

        if (material != null && trackEffectRenderers != null)
        {
            foreach (MeshRenderer effectRenderer in trackEffectRenderers)
            {
                if (effectRenderer != null)
                    effectRenderer.sharedMaterial = material;
            }
        }

        // Judgements (including unattended misses) keep the effect visible without input.
        judgementEffectTriggered = true;
        trackEffectEndTime = Time.unscaledTime + trackEffectDuration;
        SetTrackEffectActive(true);
    }

    private sealed class ScheduledNote
    {
        public double startTime;
        public MidiLongNote longNote;
        public NoteJudgement judgement;
    }

    private void Start()
    {
        if (noteMissLine == null)
        {
            Debug.LogError($"Lane ({name}): noteMissLine is not assigned.", this);
            return;
        }

        InitializeTimestamps();
    }

    private void InitializeTimestamps()
    {
        if (midiFileImporter == null || trackIndex < 0 || trackIndex >= midiFileImporter.midiTracks.Count)
        {
            Debug.LogError($"Lane ({name}): MIDI导入器或 trackIndex={trackIndex} 无效");
            return;
        }

        MidiTrack track = midiFileImporter.midiTracks[trackIndex];
        timeStamps.Clear();
        longNoteTimeStamps.Clear();
        scheduledNotes.Clear();
        notes.Clear();
        heldNoteIndices.Clear();
        spawnIndex = 0;
        inputIndex = 0;

        foreach (MidiNote note in track.notes)
        {
            if (note.isNoteOn)
                timeStamps.Add(note.timeInSeconds);
        }
        timeStamps.Sort();
        longNoteTimeStamps.AddRange(track.longNotes);
        longNoteTimeStamps.Sort((a, b) => a.startTimeInSeconds.CompareTo(b.startTimeInSeconds));

        foreach (double timestamp in timeStamps)
            scheduledNotes.Add(new ScheduledNote { startTime = timestamp });
        foreach (MidiLongNote note in longNoteTimeStamps)
            scheduledNotes.Add(new ScheduledNote { startTime = note.startTimeInSeconds, longNote = note });
        scheduledNotes.Sort((a, b) => a.startTime.CompareTo(b.startTime));

        foreach (ScheduledNote note in scheduledNotes)
            notes.Add(null);

        timestampsReady = true;
        Debug.Log($"Lane ({name}): 已绑定轨道 [{trackIndex}] \"{track.trackName}\"，" +
                  $"{timeStamps.Count} 个普通音符，{longNoteTimeStamps.Count} 个长音符");
    }

    private void Update()
    {
        bool keyDown = Input.GetKeyDown(trackKey);
        bool keyHeld = Input.GetKey(trackKey);

        if (!timestampsReady || scheduledNotes.Count == 0 || noteSpawner == null)
            return;
        if (SongManager.Instance == null || SongManager.Instance.audioSource == null ||
            !SongManager.Instance.audioSource.isPlaying)
            return;

        double audioTime = SongManager.Instance.GetInputAudioTime();
        float fallingTime = SongManager.Instance.GetNoteFallingTime();
        double margin = ScoreManager.GoodWindow;

        // 同一帧内可能有多个音符需要生成，普通音符和长音符按开始时间统一排序。
        while (spawnIndex < scheduledNotes.Count &&
               audioTime >= scheduledNotes[spawnIndex].startTime - fallingTime)
        {
            ScheduledNote scheduled = scheduledNotes[spawnIndex];
            notes[spawnIndex] = scheduled.longNote == null
                ? noteSpawner.SpawnNewNote(trackIndex, noteTheme, scheduled.startTime, fallingTime, margin, noteMissLine)
                : noteSpawner.SpawnNewLongNote(trackIndex, noteTheme, scheduled.startTime,
                    scheduled.longNote.endTimeInSeconds, fallingTime, margin, noteMissLine);
            spawnIndex++;
        }

        ProcessInput(audioTime, keyDown, keyHeld, Input.GetKeyUp(trackKey));
    }

    private void ProcessInput(double audioTime, bool keyDown, bool keyHeld, bool keyUp)
    {
        // 即使所有音符的头部已经判定完，仍需继续处理正在按住的尾部。
        ProcessHeldNotes(audioTime, keyHeld, keyUp);

        if (autoHit)
        {
            while (inputIndex < scheduledNotes.Count && audioTime >= scheduledNotes[inputIndex].startTime)
            {
                StartNote(inputIndex, NoteJudgement.Perfect);
                inputIndex++;
            }
            // 低帧率时，一帧可能同时跨过长音符的开始和结束。
            ProcessHeldNotes(audioTime, true, false);
            return;
        }

        // 一帧跳过所有已经错过的头部，避免它们阻塞当前可击打的音符。
        while (inputIndex < scheduledNotes.Count &&
               audioTime > scheduledNotes[inputIndex].startTime &&
               ScoreManager.JudgeTiming(audioTime - scheduledNotes[inputIndex].startTime) == NoteJudgement.Miss)
        {
            Miss(inputIndex);
            inputIndex++;
        }

        if (!keyDown || inputIndex >= scheduledNotes.Count)
            return;

        NoteJudgement judgement = ScoreManager.JudgeTiming(audioTime - scheduledNotes[inputIndex].startTime);
        if (judgement != NoteJudgement.Miss)
        {
            StartNote(inputIndex, judgement);
            inputIndex++;
        }
    }

    private void StartNote(int index, NoteJudgement judgement)
    {
        scheduledNotes[index].judgement = judgement;

        if (scheduledNotes[index].longNote == null)
        {
            Hit(index);
            return;
        }

        LongNoteObject longNote = notes[index] as LongNoteObject;
        ShowJudgementEffect(judgement);
        if (longNote != null)
            longNote.BeginHold();
        heldNoteIndices.Add(index);
        // 长音符在尾部成功后才计分，按下头部不会提前计分。
    }

    private void ProcessHeldNotes(double audioTime, bool keyHeld, bool keyUp)
    {
        for (int i = heldNoteIndices.Count - 1; i >= 0; i--)
        {
            int index = heldNoteIndices[i];
            double endTime = scheduledNotes[index].longNote.endTimeInSeconds;
            if (!autoHit && (keyUp || !keyHeld))
            {
                // 允许在结束时间的误差窗口内松手，提前松手则整条长音符 Miss。
                NoteJudgement tailJudgement = ScoreManager.JudgeTiming(audioTime - endTime);
                if (tailJudgement != NoteJudgement.Miss)
                {
                    if ((int)tailJudgement > (int)scheduledNotes[index].judgement)
                        scheduledNotes[index].judgement = tailJudgement;
                    Hit(index);
                }
                else
                    Miss(index);
            }
            else if (audioTime >= endTime)
            {
                // 持续按住到结束即成功，无需额外松手才能完成。
                Hit(index);
            }
            else
            {
                continue;
            }
            heldNoteIndices.RemoveAt(i);
        }
    }

    private void Hit(int index)
    {
        ShowJudgementEffect(scheduledNotes[index].judgement);
        if (index < notes.Count && notes[index] != null)
        {
            if (SongManager.Instance != null && SongManager.Instance.audioSource != null)
                notes[index].UpdatePosition(SongManager.Instance.GetInputAudioTime());
            notes[index].OnHit();
            notes[index] = null;
        }
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.RecordJudgement(scheduledNotes[index].judgement);
    }

    private void Miss(int index)
    {
        ShowJudgementEffect(NoteJudgement.Miss);
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.RecordJudgement(NoteJudgement.Miss);
        if (index < notes.Count && notes[index] != null)
        {
            notes[index].OnMiss();
            notes[index] = null;
        }
    }
}
