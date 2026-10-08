using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class MidiNote
{
    public int pitch;
    public double timeInSeconds;
    public double timeInBeats;
    public int velocity;
    public int channel;
    public bool isNoteOn;
    public float duration;

    public MidiNote(int note, double timeInSec, double timeInBeats, int vel,
        bool noteOn = true, float dur = 0f, int channel = 0)
    {
        pitch = note;
        timeInSeconds = timeInSec;
        this.timeInBeats = timeInBeats;
        velocity = vel;
        isNoteOn = noteOn;
        duration = dur;
        this.channel = channel;
    }
}

[Serializable]
public class MidiLongNote
{
    public int pitch;
    public int velocity;
    public int channel;
    public double startTimeInSeconds;
    public double endTimeInSeconds;
    public double startTimeInBeats;
    public double endTimeInBeats;

    public MidiLongNote(int pitch, int velocity, int channel, double startTimeInSeconds,
        double endTimeInSeconds, double startTimeInBeats, double endTimeInBeats)
    {
        this.pitch = pitch;
        this.velocity = velocity;
        this.channel = channel;
        this.startTimeInSeconds = startTimeInSeconds;
        this.endTimeInSeconds = endTimeInSeconds;
        this.startTimeInBeats = startTimeInBeats;
        this.endTimeInBeats = endTimeInBeats;
    }
}

[Serializable]
public class MidiTrack
{
    public string trackName;
    public List<MidiNote> notes = new List<MidiNote>();
    public List<MidiLongNote> longNotes = new List<MidiLongNote>();
    public bool isLongNote; // 轨道包含长音符；同一轨道也可以包含普通音符。

    public MidiTrack(string name, bool longNote = false)
    {
        trackName = name;
        isLongNote = longNote;
    }
}

public class MidiFileImporter : MonoBehaviour
{
    [Header("MIDI导入设置")]
    [HideInInspector] public int bpm = 128;
    [HideInInspector] public int ticksPerQuarter = 480;

    [Header("导入的MIDI数据")]
    public List<MidiTrack> midiTracks = new List<MidiTrack>();
    public string levelPath = "Level 1";

    [Header("长音符识别")]
    [Tooltip("按 MIDI Note On/Off 的时值识别长音符。文件名含 (long) 时，所有有效音符都作为长音符。")]
    public bool detectLongNotesByDuration = true;
    [Min(0.01f)]
    [Tooltip("时值达到此拍数的音符作为长音符，其余作为普通音符。")]
    public float minimumLongNoteBeats = 1f;

    [Header("按音高拆分轨道")]
    [Tooltip("同一 MIDI 文件按 pitch 从高到低拆分；最高 pitch 对应索引 0。")]
    public bool splitTracksByPitch;

    [Header("测试：音符随机分配（需手动开启）")]
    public bool enableTestNoteDistribute;

    private sealed class RawNote
    {
        public int pitch;
        public int velocity;
        public int channel;
        public int startTicks;
        public int endTicks = -1;
    }

    private struct TempoChange
    {
        public int ticks;
        public int microsecondsPerQuarter;
        public int order;
    }

    private void Awake()
    {
        // 在 Awake 加载，保证 TrackObject.Start 能读到 midiTracks。
        LoadAllMidiFiles();
    }

    public void LoadAllMidiFiles()
    {
        string directory = Path.Combine(Application.streamingAssetsPath, levelPath);
        if (!Directory.Exists(directory))
        {
            Debug.LogError("Level文件夹不存在: " + directory);
            return;
        }

        midiTracks.Clear();
        string[] midiFiles = Directory.GetFiles(directory, "*.mid");
        Array.Sort(midiFiles, StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in midiFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            bool forceLongNotes = fileName.IndexOf("(long)", StringComparison.OrdinalIgnoreCase) >= 0;
            List<MidiTrack> tracks = LoadMidiFile(filePath, forceLongNotes);
            if (tracks != null)
                midiTracks.AddRange(tracks);
        }

        Debug.Log($"成功加载 {midiTracks.Count} 个MIDI轨道");
        if (enableTestNoteDistribute)
            TestDistributeNotesToNewTracks(0, 4);
    }

    public void TestDistributeNotesToNewTracks(int sourceTrackIndex, int newTrackCount)
    {
        if (newTrackCount <= 0 || sourceTrackIndex < 0 || sourceTrackIndex >= midiTracks.Count)
        {
            Debug.LogWarning("TestDistributeNotesToNewTracks: 无效的轨道索引或轨道数量");
            return;
        }

        MidiTrack source = midiTracks[sourceTrackIndex];
        int movedCount = source.notes.Count + source.longNotes.Count;
        if (movedCount == 0)
            return;

        List<MidiTrack> newTracks = new List<MidiTrack>(newTrackCount);
        for (int i = 0; i < newTrackCount; i++)
        {
            MidiTrack track = new MidiTrack($"{source.trackName}_split_{i}");
            newTracks.Add(track);
            midiTracks.Add(track);
        }

        System.Random random = new System.Random();
        foreach (MidiNote note in source.notes)
            newTracks[random.Next(newTrackCount)].notes.Add(note);
        // 长音符是一个包含开始/结束时间的整体，拆分时不会拆散头尾。
        foreach (MidiLongNote note in source.longNotes)
            newTracks[random.Next(newTrackCount)].longNotes.Add(note);

        foreach (MidiTrack track in newTracks)
            SortTrack(track);

        source.notes.Clear();
        source.longNotes.Clear();
        source.isLongNote = false;
        Debug.Log($"已将轨道 [{sourceTrackIndex}] {source.trackName} 的 {movedCount} 个音符分配到 {newTrackCount} 条新轨道");
    }

    private List<MidiTrack> LoadMidiFile(string filePath, bool forceLongNotes)
    {
        try
        {
            MidiTrack track = new MidiTrack(Path.GetFileNameWithoutExtension(filePath), forceLongNotes);
            ParseMidiFile(File.ReadAllBytes(filePath), track);
            return splitTracksByPitch ? SplitTrackByPitch(track) : new List<MidiTrack> { track };
        }
        catch (Exception exception)
        {
            Debug.LogError($"加载MIDI文件失败 {filePath}: {exception.Message}");
            return null;
        }
    }

    private List<MidiTrack> SplitTrackByPitch(MidiTrack source)
    {
        SortedDictionary<int, MidiTrack> byPitch = new SortedDictionary<int, MidiTrack>(
            Comparer<int>.Create((a, b) => b.CompareTo(a)));

        foreach (MidiNote note in source.notes)
        {
            if (!byPitch.ContainsKey(note.pitch))
                byPitch.Add(note.pitch, new MidiTrack($"{source.trackName}_pitch_{note.pitch}"));
            byPitch[note.pitch].notes.Add(note);
        }

        foreach (MidiLongNote note in source.longNotes)
        {
            if (!byPitch.ContainsKey(note.pitch))
                byPitch.Add(note.pitch, new MidiTrack($"{source.trackName}_pitch_{note.pitch}"));
            byPitch[note.pitch].longNotes.Add(note);
        }

        if (byPitch.Count <= 1)
            return new List<MidiTrack> { source };

        List<MidiTrack> tracks = new List<MidiTrack>(byPitch.Values);
        foreach (MidiTrack track in tracks)
            SortTrack(track);
        Debug.Log($"MIDI文件 {source.trackName} 已按 pitch 拆分为 {tracks.Count} 条轨道: {string.Join(", ", byPitch.Keys)}");
        return tracks;
    }

    private static void SortTrack(MidiTrack track)
    {
        track.notes.Sort((a, b) => a.timeInSeconds.CompareTo(b.timeInSeconds));
        track.longNotes.Sort((a, b) => a.startTimeInSeconds.CompareTo(b.startTimeInSeconds));
        track.isLongNote = track.longNotes.Count > 0;
    }

    private void ParseMidiFile(byte[] data, MidiTrack track)
    {
        if (data.Length < 14 || !HasChunkHeader(data, 0, "MThd"))
            throw new FormatException("无效的MIDI文件头");

        int headerLength = ReadInt32(data, 4);
        RequireBytes(8, headerLength, data.Length);
        if (headerLength < 6)
            throw new FormatException("MIDI文件头长度不足");

        int format = (data[8] << 8) | data[9];
        int trackCount = (data[10] << 8) | data[11];
        int division = (data[12] << 8) | data[13];
        if (format > 1)
            throw new NotSupportedException("仅支持 MIDI format 0/1");
        if (division == 0 || (division & 0x8000) != 0)
            throw new NotSupportedException("MIDI 必须使用每拍 tick 分辨率，暂不支持 SMPTE 时间码");
        ticksPerQuarter = division;

        List<RawNote> rawNotes = new List<RawNote>();
        List<TempoChange> tempos = new List<TempoChange>();
        int position = 8 + headerLength;
        for (int i = 0; i < trackCount; i++)
            position = ParseTrack(data, position, rawNotes, tempos);

        tempos.Sort((a, b) => a.ticks != b.ticks ? a.ticks.CompareTo(b.ticks) : a.order.CompareTo(b.order));
        bpm = tempos.Count == 0 ? 120 : (int)Math.Round(60000000.0 / tempos[0].microsecondsPerQuarter);
        bool forceLongNotes = track.isLongNote;
        int unmatchedCount = 0;

        foreach (RawNote note in rawNotes)
        {
            double startSeconds = TicksToSeconds(note.startTicks, tempos);
            double startBeats = (double)note.startTicks / ticksPerQuarter;
            bool hasEnd = note.endTicks > note.startTicks;
            double endSeconds = hasEnd ? TicksToSeconds(note.endTicks, tempos) : startSeconds;
            double endBeats = hasEnd ? (double)note.endTicks / ticksPerQuarter : startBeats;
            double durationBeats = hasEnd ? (double)(note.endTicks - note.startTicks) / ticksPerQuarter : 0;
            bool isLong = hasEnd && (forceLongNotes ||
                (detectLongNotesByDuration && durationBeats >= Math.Max(0.01, minimumLongNoteBeats)));

            if (isLong)
            {
                track.longNotes.Add(new MidiLongNote(note.pitch, note.velocity, note.channel,
                    startSeconds, endSeconds, startBeats, endBeats));
            }
            else
            {
                // 缺少 Note Off 时保留为普通音符，避免生成永远无法完成的长音符。
                track.notes.Add(new MidiNote(note.pitch, startSeconds, startBeats, note.velocity,
                    true, (float)(endSeconds - startSeconds), note.channel));
            }

            if (note.endTicks < 0)
                unmatchedCount++;
        }

        SortTrack(track);
        if (unmatchedCount > 0)
            Debug.LogWarning($"MIDI文件 {track.trackName}: {unmatchedCount} 个 Note On 缺少 Note Off，已保留为普通音符");
        Debug.Log($"MIDI文件 {track.trackName}: {track.notes.Count} 个普通音符，{track.longNotes.Count} 个长音符");
    }

    private int ParseTrack(byte[] data, int position, List<RawNote> notes, List<TempoChange> tempos)
    {
        RequireBytes(position, 8, data.Length);
        if (!HasChunkHeader(data, position, "MTrk"))
            throw new FormatException("无效的MIDI轨道头");
        int trackLength = ReadInt32(data, position + 4);
        position += 8;
        RequireBytes(position, trackLength, data.Length);
        int end = position + trackLength;
        int ticks = 0;
        byte runningStatus = 0;

        // 每条物理 MIDI 轨道独立配对，并按 channel/pitch 区分。
        // 同音高重叠时使用队列，不会覆盖较早的 Note On。
        Dictionary<int, Queue<RawNote>> pending = new Dictionary<int, Queue<RawNote>>();
        while (position < end)
        {
            ticks = checked(ticks + ReadVariableLength(data, ref position, end));
            RequireBytes(position, 1, end);
            byte status = data[position];
            if ((status & 0x80) != 0)
            {
                position++;
                runningStatus = status < 0xF0 ? status : (byte)0;
            }
            else
            {
                if (runningStatus == 0)
                    throw new FormatException("MIDI运行状态缺少有效状态字节");
                status = runningStatus;
            }

            if (status == 0xFF)
            {
                RequireBytes(position, 1, end);
                byte metaType = data[position++];
                int length = ReadVariableLength(data, ref position, end);
                RequireBytes(position, length, end);
                if (metaType == 0x51 && length == 3)
                {
                    int tempo = (data[position] << 16) | (data[position + 1] << 8) | data[position + 2];
                    if (tempo == 0)
                        throw new FormatException("MIDI速度不能为零");
                    tempos.Add(new TempoChange { ticks = ticks, microsecondsPerQuarter = tempo, order = tempos.Count });
                }
                position += length;
                if (metaType == 0x2F)
                    break;
                continue;
            }

            if (status == 0xF0 || status == 0xF7)
            {
                int length = ReadVariableLength(data, ref position, end);
                RequireBytes(position, length, end);
                position += length;
                continue;
            }

            int eventType = status & 0xF0;
            int eventLength = eventType == 0xC0 || eventType == 0xD0 ? 1 : 2;
            if (status >= 0xF0)
                throw new FormatException($"不支持的MIDI状态: {status:X2}");
            RequireBytes(position, eventLength, end);
            int pitch = data[position];
            int velocity = eventLength == 2 ? data[position + 1] : 0;
            position += eventLength;
            if (eventType != 0x80 && eventType != 0x90)
                continue;

            int channel = status & 0x0F;
            int key = (channel << 7) | pitch;
            if (eventType == 0x90 && velocity > 0)
            {
                RawNote note = new RawNote { pitch = pitch, velocity = velocity, channel = channel, startTicks = ticks };
                notes.Add(note);
                if (!pending.TryGetValue(key, out Queue<RawNote> queue))
                {
                    queue = new Queue<RawNote>();
                    pending.Add(key, queue);
                }
                queue.Enqueue(note);
            }
            else if (pending.TryGetValue(key, out Queue<RawNote> pendingQueue) && pendingQueue.Count > 0)
            {
                // 0x80 和力度为零的 0x90 都是 Note Off。
                pendingQueue.Dequeue().endTicks = ticks;
            }
        }

        return end;
    }

    private double TicksToSeconds(int ticks, List<TempoChange> tempos)
    {
        // 先读取整个文件的 tempo map，避免跨轨道和变速音符的头尾时间错误。
        int previousTicks = 0;
        int tempo = 500000; // MIDI 默认速度为 120 BPM。
        double seconds = 0;
        foreach (TempoChange change in tempos)
        {
            if (change.ticks > ticks)
                break;
            seconds += (double)(change.ticks - previousTicks) * tempo / ticksPerQuarter / 1000000.0;
            previousTicks = change.ticks;
            tempo = change.microsecondsPerQuarter;
        }
        return seconds + (double)(ticks - previousTicks) * tempo / ticksPerQuarter / 1000000.0;
    }

    private static int ReadVariableLength(byte[] data, ref int position, int end)
    {
        int value = 0;
        for (int i = 0; i < 4; i++)
        {
            RequireBytes(position, 1, end);
            byte next = data[position++];
            value = (value << 7) | (next & 0x7F);
            if ((next & 0x80) == 0)
                return value;
        }
        throw new FormatException("MIDI可变长度数值超过四字节");
    }

    private static int ReadInt32(byte[] data, int position)
    {
        RequireBytes(position, 4, data.Length);
        int value = (data[position] << 24) | (data[position + 1] << 16) |
                    (data[position + 2] << 8) | data[position + 3];
        if (value < 0)
            throw new FormatException("MIDI数据块过长");
        return value;
    }

    private static bool HasChunkHeader(byte[] data, int position, string header)
    {
        for (int i = 0; i < header.Length; i++)
        {
            if (data[position + i] != header[i])
                return false;
        }
        return true;
    }

    private static void RequireBytes(int position, int count, int end)
    {
        if (position < 0 || count < 0 || position > end || count > end - position)
            throw new FormatException("MIDI数据不完整或数据块长度无效");
    }

    public MidiTrack GetTrack(string trackName)
    {
        return midiTracks.Find(track => track.trackName.Contains(trackName));
    }

    public List<MidiTrack> GetSingleNoteTracks()
    {
        return midiTracks.FindAll(track => track.notes.Count > 0);
    }

    public List<MidiTrack> GetLongNoteTracks()
    {
        return midiTracks.FindAll(track => track.longNotes.Count > 0);
    }

    [ContextMenu("打印MIDI信息")]
    public void PrintMidiInfo()
    {
        Debug.Log($"MIDI轨道数: {midiTracks.Count}, BPM: {bpm}, Ticks Per Quarter: {ticksPerQuarter}");
        foreach (MidiTrack track in midiTracks)
        {
            Debug.Log($"轨道 {track.trackName}: {track.notes.Count} 个普通音符，{track.longNotes.Count} 个长音符");
            foreach (MidiNote note in track.notes)
                Debug.Log($"  普通音符 Pitch#{note.pitch}: {note.timeInSeconds:F4}秒/{note.timeInBeats:F4}拍，时值={note.duration:F4}秒");
            foreach (MidiLongNote note in track.longNotes)
                Debug.Log($"  长音符 Pitch#{note.pitch}: {note.startTimeInSeconds:F4}秒 → {note.endTimeInSeconds:F4}秒，" +
                          $"{note.startTimeInBeats:F4}拍 → {note.endTimeInBeats:F4}拍");
        }
    }

    [ContextMenu("打印精确时间信息")]
    public void PrintPreciseTimingInfo()
    {
        PrintMidiInfo();
    }
}
