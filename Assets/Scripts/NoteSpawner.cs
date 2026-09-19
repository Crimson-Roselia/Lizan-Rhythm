using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NoteSpawner : MonoBehaviour
{
    [SerializeField] private List<GameObject> notePrefabs;
    [SerializeField] private List<Transform> noteSpawnPoints;
    [SerializeField] private Transform noteMissLine;


    public NoteObject SpawnNewNote(int trackIndex, int prefabIndex)
    {
        NoteObject noteObject = Instantiate(notePrefabs[prefabIndex], noteSpawnPoints[trackIndex].position, notePrefabs[prefabIndex].transform.rotation).GetComponent<NoteObject>();
        noteObject.SetXThereshold(noteMissLine.transform.position.x);
        return noteObject;
    }
}
