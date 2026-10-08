using System.Collections.Generic;
using UnityEngine;

public class NoteSpawner : MonoBehaviour
{
    [SerializeField] private List<GameObject> notePrefabs;
    [SerializeField] private List<Transform> noteSpawnPoints;
    [Tooltip("长音符连接体材质；留空时使用默认半透明材质。")]
    [SerializeField] private Material longNoteMaterial;

    public NoteObject SpawnNewNote(int trackIndex, int prefabIndex, double noteTime, float fallingTime, double missWindow, Transform noteMissLine)
    {
        Transform spawnPoint = noteSpawnPoints[trackIndex];
        return CreateNote(
            prefabIndex,
            spawnPoint.position,
            spawnPoint.rotation,
            GetMissPosition(spawnPoint, noteMissLine),
            noteTime - fallingTime,
            noteTime + missWindow);
    }

    public LongNoteObject SpawnNewLongNote(int trackIndex, int prefabIndex,
        double startTime, double endTime, float fallingTime, double missWindow, Transform noteMissLine)
    {
        Transform spawnPoint = noteSpawnPoints[trackIndex];
        Vector3 missPosition = GetMissPosition(spawnPoint, noteMissLine);
        double travelTime = fallingTime + missWindow;
        Vector3 velocity = travelTime > 0
            ? (missPosition - spawnPoint.position) / (float)travelTime
            : Vector3.zero;

        // 头尾从同一时刻开始移动。尾部在生成点后方，距离等于速度乘以持续时间。
        // 这样在尾部到达生成点之前，连接体也已经具有正确的完整长度。
        Vector3 tailSpawnPosition = spawnPoint.position - velocity * (float)(endTime - startTime);
        NoteObject head = CreateNote(prefabIndex, spawnPoint.position, spawnPoint.rotation, missPosition,
            startTime - fallingTime, startTime + missWindow);
        NoteObject tail = CreateNote(prefabIndex, tailSpawnPosition, spawnPoint.rotation, missPosition,
            startTime - fallingTime, endTime + missWindow);

        GameObject longNote = new GameObject("Long Note");
        longNote.layer = notePrefabs[prefabIndex].layer;
        longNote.transform.position = spawnPoint.position;
        longNote.transform.SetParent(transform, true);
        head.transform.SetParent(longNote.transform, true);
        tail.transform.SetParent(longNote.transform, true);

        Material material = longNoteMaterial != null
            ? longNoteMaterial
            : Resources.Load<Material>("Long Note Body");
        LongNoteObject noteObject = longNote.AddComponent<LongNoteObject>();
        noteObject.Initialize(head, tail, startTime, endTime, material);
        return noteObject;
    }

    private static Vector3 GetMissPosition(Transform spawnPoint, Transform noteMissLine)
    {
        // 各轨道沿 Z 轴排列，终点只取 Miss 线的 X 坐标。
        return new Vector3(noteMissLine.position.x, spawnPoint.position.y, spawnPoint.position.z);
    }

    private NoteObject CreateNote(int prefabIndex, Vector3 spawnPosition, Quaternion spawnRotation, Vector3 missPosition,
        double moveStartTime, double missTime)
    {
        GameObject prefab = notePrefabs[prefabIndex];
        NoteObject noteObject = Instantiate(prefab, spawnPosition, spawnRotation).GetComponent<NoteObject>();
        noteObject.Initialize(spawnPosition, missPosition, moveStartTime, missTime);

        if (SongManager.Instance != null && SongManager.Instance.audioSource != null)
            noteObject.UpdatePosition(SongManager.Instance.GetInputAudioTime());
        return noteObject;
    }
}
