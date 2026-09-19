using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;

[System.Serializable]
public class MidiNote
{
    public int pitch;               // MIDI音高 (0-127, 60=中央C)
    public double timeInSeconds;     // 播放时间（秒）
    public double timeInBeats;       // 播放时间（拍）
    public int velocity;            // 力度 (0-127)
    public bool isNoteOn;           // true=按下, false=释放（仅用于长音符）
    public float duration;          // 音符长度（秒，仅用于单音符）
    
    public MidiNote(int note, double timeInSec, double timeInBeats, int vel, bool noteOn = true, float dur = 0f)
    {
        pitch = note;
        timeInSeconds = timeInSec;
        this.timeInBeats = timeInBeats;
        velocity = vel;
        isNoteOn = noteOn;
        duration = dur;
    }
}

[System.Serializable]
public class MidiTrack
{
    public string trackName;
    public List<MidiNote> notes;
    public bool isLongNote;  // true=长音符轨道, false=单音符轨道
    
    public MidiTrack(string name, bool longNote = false)
    {
        trackName = name;
        isLongNote = longNote;
        notes = new List<MidiNote>();
    }
}

public class MidiFileImporter : MonoBehaviour
{
    [Header("MIDI导入设置")]
    [HideInInspector] public int bpm = 128;  // 每分钟拍数
    [HideInInspector] public int ticksPerQuarter = 480;  // 每四分音符的tick数
    
    [Header("导入的MIDI数据")]
    public List<MidiTrack> midiTracks = new List<MidiTrack>();

    public string levelPath = "Level 1";

    [Header("测试：音符随机分配（不影响默认流程，需手动开启）")]
    public bool enableTestNoteDistribute = false;
    
    void Awake()
    {
        // 在 Awake 加载，保证 Lane.Start 能读到 midiTracks
        LoadAllMidiFiles();
    }
    
    /// <summary>
    /// 加载所有MIDI文件
    /// </summary>
    public void LoadAllMidiFiles()
    {
        string levelPath = Path.Combine(Application.streamingAssetsPath, this.levelPath);
        
        if (!Directory.Exists(levelPath))
        {
            Debug.LogError("Level文件夹不存在: " + levelPath);
            return;
        }
        
        string[] midiFiles = Directory.GetFiles(levelPath, "*.mid");
        
        foreach (string filePath in midiFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            bool isLongNote = fileName.Contains("(long)");
            
            Debug.Log($"正在加载MIDI文件: {fileName}, 类型: {(isLongNote ? "长音符" : "单音符")}");
            
            MidiTrack track = LoadMidiFile(filePath, isLongNote);
            if (track != null)
            {
                midiTracks.Add(track);
            }
        }
        
        Debug.Log($"成功加载 {midiTracks.Count} 个MIDI轨道");

        if (enableTestNoteDistribute)
        {
            TestDistributeNotesToNewTracks(0, 4);
        }
    }

    /// <summary>
    /// 测试方法：在 midiTracks 中新建 n 个轨道，
    /// 将指定索引轨道中的音符随机分配到这些新轨道中。
    /// 长音符会以 Note On/Off 成对分配，避免拆散。
    /// </summary>
    public void TestDistributeNotesToNewTracks(int sourceTrackIndex, int newTrackCount)
    {
        if (newTrackCount <= 0)
        {
            Debug.LogWarning("TestDistributeNotesToNewTracks: newTrackCount 必须大于 0");
            return;
        }

        if (sourceTrackIndex < 0 || sourceTrackIndex >= midiTracks.Count)
        {
            Debug.LogWarning($"TestDistributeNotesToNewTracks: 无效的源轨道索引 {sourceTrackIndex}");
            return;
        }

        MidiTrack sourceTrack = midiTracks[sourceTrackIndex];
        if (sourceTrack.notes == null || sourceTrack.notes.Count == 0)
        {
            Debug.LogWarning($"TestDistributeNotesToNewTracks: 源轨道 [{sourceTrackIndex}] {sourceTrack.trackName} 没有音符可分配");
            return;
        }

        List<MidiTrack> newTracks = new List<MidiTrack>(newTrackCount);
        for (int i = 0; i < newTrackCount; i++)
        {
            MidiTrack newTrack = new MidiTrack($"{sourceTrack.trackName}_split_{i}", sourceTrack.isLongNote);
            newTracks.Add(newTrack);
            midiTracks.Add(newTrack);
        }

        List<List<MidiNote>> noteUnits = BuildNoteUnits(sourceTrack);
        System.Random rng = new System.Random();

        foreach (List<MidiNote> unit in noteUnits)
        {
            int targetIndex = rng.Next(newTrackCount);
            newTracks[targetIndex].notes.AddRange(unit);
        }

        // 按时间排序，保持各新轨道内事件顺序正确
        foreach (MidiTrack track in newTracks)
        {
            track.notes.Sort((a, b) => a.timeInBeats.CompareTo(b.timeInBeats));
        }

        int movedCount = sourceTrack.notes.Count;
        sourceTrack.notes.Clear();

        Debug.Log($"TestDistributeNotesToNewTracks: 已将轨道 [{sourceTrackIndex}] {sourceTrack.trackName} 的 {movedCount} 个音符事件随机分配到 {newTrackCount} 个新轨道");
    }

    /// <summary>
    /// 将轨道音符整理为可分配单元：单音符每个独立；长音符 Note On 与对应 Note Off 成对。
    /// </summary>
    List<List<MidiNote>> BuildNoteUnits(MidiTrack track)
    {
        List<List<MidiNote>> units = new List<List<MidiNote>>();

        if (!track.isLongNote)
        {
            foreach (MidiNote note in track.notes)
            {
                units.Add(new List<MidiNote> { note });
            }
            return units;
        }

        // 长音符：为每个 pitch 维护待配对的 Note On 队列
        Dictionary<int, Queue<MidiNote>> pendingNoteOns = new Dictionary<int, Queue<MidiNote>>();

        foreach (MidiNote note in track.notes)
        {
            if (note.isNoteOn)
            {
                if (!pendingNoteOns.ContainsKey(note.pitch))
                {
                    pendingNoteOns[note.pitch] = new Queue<MidiNote>();
                }
                pendingNoteOns[note.pitch].Enqueue(note);
            }
            else
            {
                if (pendingNoteOns.ContainsKey(note.pitch) && pendingNoteOns[note.pitch].Count > 0)
                {
                    MidiNote noteOn = pendingNoteOns[note.pitch].Dequeue();
                    units.Add(new List<MidiNote> { noteOn, note });
                }
                else
                {
                    // 孤立的 Note Off，单独作为单元
                    units.Add(new List<MidiNote> { note });
                }
            }
        }

        // 未配对的 Note On 单独作为单元
        foreach (var kvp in pendingNoteOns)
        {
            while (kvp.Value.Count > 0)
            {
                units.Add(new List<MidiNote> { kvp.Value.Dequeue() });
            }
        }

        return units;
    }
    
    /// <summary>
    /// 加载单个MIDI文件
    /// </summary>
    MidiTrack LoadMidiFile(string filePath, bool isLongNote)
    {
        try
        {
            byte[] midiData = File.ReadAllBytes(filePath);
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            MidiTrack track = new MidiTrack(fileName, isLongNote);
            ParseMidiFile(midiData, track);
            
            return track;
        }
        catch (Exception e)
        {
            Debug.LogError($"加载MIDI文件失败 {filePath}: {e.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// 解析MIDI文件数据
    /// </summary>
    void ParseMidiFile(byte[] data, MidiTrack track)
    {
        int pos = 0;
        
        // 检查MIDI文件头
        if (data.Length < 14 || 
            data[0] != 0x4D || data[1] != 0x54 || data[2] != 0x68 || data[3] != 0x64)
        {
            Debug.LogError("无效的MIDI文件格式");
            return;
        }
        
        // 读取头部信息
        pos = 8; // 跳过"MThd"和长度
        int format = (data[pos] << 8) | data[pos + 1];
        pos += 2;
        int trackCount = (data[pos] << 8) | data[pos + 1];
        pos += 2;
        int division = (data[pos] << 8) | data[pos + 1];
        pos += 2;
        
        if (division > 0)
        {
            ticksPerQuarter = division;
        }
        
        Debug.Log($"MIDI格式: {format}, 轨道数: {trackCount}, 分辨率: {ticksPerQuarter}");
        
        // 添加MIDI音高说明
        Debug.Log("MIDI音高参考: 60=中央C, 每+12为高一个八度, 每-12为低一个八度");
        
        // 解析轨道数据
        for (int t = 0; t < trackCount && pos < data.Length; t++)
        {
            pos = ParseTrack(data, pos, track);
        }
    }
    
    /// <summary>
    /// 解析MIDI轨道
    /// </summary>
    int ParseTrack(byte[] data, int startPos, MidiTrack track)
    {
        int pos = startPos;
        
        // 检查轨道头
        if (pos + 8 > data.Length || 
            data[pos] != 0x4D || data[pos + 1] != 0x54 || data[pos + 2] != 0x72 || data[pos + 3] != 0x6B)
        {
            Debug.LogWarning("无效的轨道头");
            return pos + 8;
        }
        
        pos += 4; // 跳过"MTrk"
        
        // 读取轨道长度
        int trackLength = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
        pos += 4;
        
        int trackEnd = pos + trackLength;
        int currentTicks = 0;
        byte lastStatus = 0;
        
        Dictionary<int, int> noteOnTimes = new Dictionary<int, int>(); // 用于长音符的按下时间记录
        
        while (pos < trackEnd && pos < data.Length)
        {
            // 读取delta time
            int deltaTime;
            pos = ReadVariableLength(data, pos, out deltaTime);
            currentTicks += deltaTime;
            
            if (pos >= data.Length) break;
            
            byte status = data[pos];
            
            // 处理运行状态
            if ((status & 0x80) == 0)
            {
                status = lastStatus;
                pos--; // 回退一位，因为这不是状态字节
            }
            
            pos++;
            
            if ((status & 0xF0) == 0x90) // Note On
            {
                if (pos + 1 >= data.Length) break;
                
                int note = data[pos];
                int velocity = data[pos + 1];
                pos += 2;
                
                double timeInSeconds = TicksToSeconds(currentTicks);
                double timeInBeats = TicksToBeats(currentTicks);
                
                if (velocity == 0) // Note On with velocity 0 = Note Off
                {
                    if (track.isLongNote && noteOnTimes.ContainsKey(note))
                    {
                        // 添加Note Off事件
                        track.notes.Add(new MidiNote(note, timeInSeconds, timeInBeats, velocity, false));
                        noteOnTimes.Remove(note);
                    }
                }
                else
                {
                    if (track.isLongNote)
                    {
                        // 长音符：添加Note On事件
                        track.notes.Add(new MidiNote(note, timeInSeconds, timeInBeats, velocity, true));
                        noteOnTimes[note] = currentTicks;
                    }
                    else
                    {
                        // 单音符：创建单个音符事件（duration稍后计算）
                        track.notes.Add(new MidiNote(note, timeInSeconds, timeInBeats, velocity, true, 0.1f));
                    }
                }
            }
            else if ((status & 0xF0) == 0x80) // Note Off
            {
                if (pos + 1 >= data.Length) break;
                
                int note = data[pos];
                int velocity = data[pos + 1];
                pos += 2;
                
                double timeInSeconds = TicksToSeconds(currentTicks);
                double timeInBeats = TicksToBeats(currentTicks);
                
                if (track.isLongNote && noteOnTimes.ContainsKey(note))
                {
                    // 添加Note Off事件
                    track.notes.Add(new MidiNote(note, timeInSeconds, timeInBeats, velocity, false));
                    noteOnTimes.Remove(note);
                }
            }
            else if (status == 0xFF) // Meta Event
            {
                if (pos >= data.Length) break;
                
                byte metaType = data[pos++];
                
                int length;
                pos = ReadVariableLength(data, pos, out length);
                
                if (metaType == 0x51 && length == 3) // Set Tempo
                {
                    if (pos + 2 < data.Length)
                    {
                        int microsecondsPerQuarter = (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2];
                        bpm = 60000000 / microsecondsPerQuarter;
                        Debug.Log($"检测到BPM: {bpm}");
                    }
                }
                
                pos += length;
            }
            else
            {
                // 跳过其他事件
                int length = GetMidiEventLength(status);
                pos += length;
            }
            
            lastStatus = status;
        }
        
        return trackEnd;
    }
    
    /// <summary>
    /// 读取可变长度数值
    /// </summary>
    int ReadVariableLength(byte[] data, int pos, out int value)
    {
        value = 0;
        byte b;
        
        do
        {
            if (pos >= data.Length) break;
            b = data[pos++];
            value = (value << 7) | (b & 0x7F);
        } while ((b & 0x80) != 0);
        
        return pos;
    }
    
    /// <summary>
    /// 获取MIDI事件的长度
    /// </summary>
    int GetMidiEventLength(byte status)
    {
        switch (status & 0xF0)
        {
            case 0x80: // Note Off
            case 0x90: // Note On
            case 0xA0: // Aftertouch
            case 0xB0: // Control Change
            case 0xE0: // Pitch Bend
                return 2;
            case 0xC0: // Program Change
            case 0xD0: // Channel Pressure
                return 1;
            default:
                return 0;
        }
    }
    
    /// <summary>
    /// 将ticks转换为秒
    /// </summary>
    double TicksToSeconds(int ticks)
    {
        double beatsPerSecond = bpm / 60.0;
        double ticksPerSecond = beatsPerSecond * ticksPerQuarter;
        return ticks / ticksPerSecond;
    }
    
    /// <summary>
    /// 将ticks转换为拍
    /// </summary>
    double TicksToBeats(int ticks)
    {
        return (double)ticks / ticksPerQuarter;
    }
    
    /// <summary>
    /// 获取指定轨道的音符数据
    /// </summary>
    public MidiTrack GetTrack(string trackName)
    {
        return midiTracks.Find(track => track.trackName.Contains(trackName));
    }
    
    /// <summary>
    /// 获取所有单音符轨道
    /// </summary>
    public List<MidiTrack> GetSingleNoteTracks()
    {
        return midiTracks.FindAll(track => !track.isLongNote);
    }
    
    /// <summary>
    /// 获取所有长音符轨道
    /// </summary>
    public List<MidiTrack> GetLongNoteTracks()
    {
        return midiTracks.FindAll(track => track.isLongNote);
    }
    
    /// <summary>
    /// 打印所有轨道信息（用于调试）
    /// </summary>
    [ContextMenu("打印MIDI信息")]
    public void PrintMidiInfo()
    {
        Debug.Log($"=== MIDI导入信息 ===");
        Debug.Log($"BPM: {bpm}");
        Debug.Log($"Ticks Per Quarter: {ticksPerQuarter}");
        Debug.Log($"总轨道数: {midiTracks.Count}");
        
        foreach (var track in midiTracks)
        {
            Debug.Log($"\n轨道: {track.trackName}");
            Debug.Log($"类型: {(track.isLongNote ? "长音符" : "单音符")}");
            Debug.Log($"音符数量: {track.notes.Count}");
            
            for (int i = 0; i < track.notes.Count; i++)
            {
                var note = track.notes[i];
                string noteInfo = $"  音符 {i + 1}: Pitch#{note.pitch}, " +
                                 $"时间={note.timeInSeconds:F2}秒/{note.timeInBeats:F2}拍, " +
                                 $"力度={note.velocity}";
                
                if (track.isLongNote)
                {
                    noteInfo += $", {(note.isNoteOn ? "按下" : "释放")}";
                }
                else
                {
                    noteInfo += $", 长度={note.duration:F2}秒";
                }
                
                Debug.Log(noteInfo);
            }
        }
    }
    
    /// <summary>
    /// 打印精确的时间信息（用于调试精度）
    /// </summary>
    [ContextMenu("打印精确时间信息")]
    public void PrintPreciseTimingInfo()
    {
        Debug.Log($"=== 精确时间信息 ===");
        Debug.Log($"BPM: {bpm}");
        Debug.Log($"Ticks Per Quarter: {ticksPerQuarter}");
        
        foreach (var track in midiTracks)
        {
            Debug.Log($"\n轨道: {track.trackName}");
            
            for (int i = 0; i < track.notes.Count; i++) // 只显示前10个音符
            {
                var note = track.notes[i];
                Debug.Log($"  音符 {i + 1}: Pitch#{note.pitch}, " +
                         $"精确时间={note.timeInBeats:F4}拍 ({note.timeInSeconds:F4}秒), " +
                         $"力度={note.velocity}");
            }
            
        }
    }
}
