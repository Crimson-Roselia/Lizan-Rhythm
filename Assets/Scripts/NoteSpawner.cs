using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoteSpawner : MonoBehaviour
{
    [SerializeField] private List<GameObject> notePrefabs;
    [SerializeField] private List<Transform> noteSpawnPoints;
    [SerializeField] private Transform noteMissLine;


    public NoteObject SpawnNewNote(int trackIndex, int prefabIndex, double noteTime, float fallingTime, double missWindow)
    {
        Transform spawnPoint = noteSpawnPoints[trackIndex];
        NoteObject noteObject = Instantiate(notePrefabs[prefabIndex], spawnPoint.position, notePrefabs[prefabIndex].transform.rotation).GetComponent<NoteObject>();

        // The lanes differ only along Z, so preserve the spawned note's lane while using
        // the Miss line's X coordinate as the destination.
        Vector3 missPosition = new Vector3(noteMissLine.position.x, spawnPoint.position.y, spawnPoint.position.z);
        noteObject.Initialize(
            spawnPoint.position,
            missPosition,
            noteTime - fallingTime,
            noteTime + missWindow);

        return noteObject;
    }
}
