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

    protected virtual void LateUpdate()
    {
        if (!_isInitialized || SongManager.Instance == null || SongManager.Instance.audioSource == null)
            return;

        UpdatePosition(SongManager.Instance.GetInputAudioTime());
    }

    public Vector3 GetPositionAtTime(double audioTime)
    {
        // 使用与轨道判定相同的音频时钟，保持移动速度与帧率无关。
        double duration = _missTime - _moveStartTime;
        float progress = duration <= 0.0
            ? 1f
            : Mathf.Clamp01((float)((audioTime - _moveStartTime) / duration));

        return Vector3.Lerp(_spawnPosition, _missPosition, progress);
    }

    public void UpdatePosition(double audioTime)
    {
        if (_isInitialized)
            transform.position = GetPositionAtTime(audioTime);
    }

    public void HoldAt(Vector3 position)
    {
        _isInitialized = false;
        transform.position = position;
    }

    public void PlayHitEffect()
    {
        if (hitVfx != null)
            Instantiate(hitVfx, transform.position + new Vector3(0, 0.05f, 0), Quaternion.identity);
    }

    public virtual void OnHit()
    {
        PlayHitEffect();
        Destroy(gameObject);
    }
    
    public virtual void OnMiss()
    {
        // TrackObject can run before this object's Update in the Miss frame.
        // Snap first so the note is at the requested position at judgement time.
        if (_isInitialized)
            transform.position = _missPosition;

        Destroy(gameObject);
    }
}
