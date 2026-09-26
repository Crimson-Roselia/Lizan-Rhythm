using UnityEngine;

public class NoteObject : MonoBehaviour
{
    [SerializeField] private GameObject hitVfx;

    private Vector3 _spawnPosition;
    private Vector3 _missPosition;
    private double _moveStartTime;
    private double _missTime;
    private bool _isInitialized;

    public void Initialize(Vector3 spawnPosition, Vector3 missPosition, double moveStartTime, double missTime)
    {
        _spawnPosition = spawnPosition;
        _missPosition = missPosition;
        _moveStartTime = moveStartTime;
        _missTime = missTime;
        _isInitialized = true;

        transform.position = _spawnPosition;
    }

    void Update()
    {
        if (!_isInitialized || SongManager.Instance == null || SongManager.Instance.audioSource == null)
            return;

        // Use the same audio clock as TrackObject's Miss condition. This makes the
        // note's speed distance / (_missTime - _moveStartTime), independent of frame rate.
        double duration = _missTime - _moveStartTime;
        float progress = duration <= 0.0
            ? 1f
            : Mathf.Clamp01((float)((SongManager.Instance.GetInputAudioTime() - _moveStartTime) / duration));

        transform.position = Vector3.Lerp(_spawnPosition, _missPosition, progress);
    }

    public void OnHit()
    {
        Instantiate(hitVfx, transform.position + new Vector3(0, 0.05f, 0), Quaternion.identity);
        Destroy(gameObject);
    }
    
    public void OnMiss()
    {
        // TrackObject can run before this object's Update in the Miss frame.
        // Snap first so the note is at the requested position at judgement time.
        if (_isInitialized)
            transform.position = _missPosition;

        Destroy(gameObject);
    }
}
