using UnityEngine;
using UnityEngine.Rendering;

public class LongNoteObject : NoteObject
{
    private NoteObject head;
    private NoteObject tail;
    private Renderer[] headRenderers;
    private Renderer[] tailRenderers;
    private Mesh bodyMesh;
    private MeshRenderer bodyRenderer;
    private readonly Vector3[] vertices = new Vector3[8];

    // 两个 X 截面各四个顶点，六个面共十二个三角形。
    private static readonly int[] Triangles =
    {
        0, 2, 1, 0, 3, 2,
        4, 5, 6, 4, 6, 7,
        0, 4, 7, 0, 7, 3,
        1, 2, 6, 1, 6, 5,
        0, 1, 5, 0, 5, 4,
        3, 7, 6, 3, 6, 2
    };

    public double StartTime { get; private set; }
    public double EndTime { get; private set; }
    public bool IsHolding { get; private set; }

    public void Initialize(NoteObject head, NoteObject tail, double startTime, double endTime, Material material)
    {
        this.head = head;
        this.tail = tail;
        StartTime = startTime;
        EndTime = endTime;
        headRenderers = head.GetComponentsInChildren<Renderer>();
        tailRenderers = tail.GetComponentsInChildren<Renderer>();

        GameObject body = new GameObject("Long Note Body");
        body.layer = gameObject.layer;
        body.transform.SetParent(transform, false);
        bodyMesh = new Mesh { name = "Long Note Body" };
        bodyMesh.MarkDynamic();
        bodyMesh.vertices = vertices;
        bodyMesh.triangles = Triangles;
        body.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
        bodyRenderer = body.AddComponent<MeshRenderer>();
        bodyRenderer.sharedMaterial = material;
        bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
        bodyRenderer.receiveShadows = false;
        UpdateBody();
    }

    public void BeginHold()
    {
        if (IsHolding || head == null)
            return;

        IsHolding = true;
        head.HoldAt(head.GetPositionAtTime(StartTime));
        head.PlayHitEffect();
        UpdateBody();
    }

    protected override void LateUpdate()
    {
        if (head == null || tail == null)
            return;

        if (SongManager.Instance != null && SongManager.Instance.audioSource != null)
        {
            double audioTime = SongManager.Instance.GetInputAudioTime();
            head.UpdatePosition(audioTime);
            tail.UpdatePosition(audioTime);
        }

        // 使用同一帧的共享时间更新头尾，再重建连接体，不依赖脚本执行顺序。
        UpdateBody();
    }

    private void UpdateBody()
    {
        if (bodyMesh == null || head == null || tail == null)
            return;

        Bounds headBounds = GetNoteBounds(head, headRenderers);
        Bounds tailBounds = GetNoteBounds(tail, tailRenderers);
        float headX = head.transform.TransformPoint(headBounds.center).x;
        float tailX = tail.transform.TransformPoint(tailBounds.center).x;
        if (headX <= tailX)
        {
            WriteCrossSection(head.transform, headBounds, 0);
            WriteCrossSection(tail.transform, tailBounds, 4);
        }
        else
        {
            WriteCrossSection(tail.transform, tailBounds, 0);
            WriteCrossSection(head.transform, headBounds, 4);
        }
        bodyMesh.vertices = vertices;
        bodyMesh.RecalculateNormals();
        bodyMesh.RecalculateBounds();
        bodyRenderer.enabled = Mathf.Abs(tailX - headX) > 0.0001f;
    }

    private void WriteCrossSection(Transform noteTransform, Bounds bounds, int offset)
    {
        float x = bounds.center.x;
        // 在音符自身的局部空间切截面，再转换到连接体局部空间。
        // 绕 X 轴旋转时，四个角跟随音符旋转，不会被世界空间 AABB 扩大。
        Matrix4x4 noteToBody = transform.worldToLocalMatrix * noteTransform.localToWorldMatrix;
        vertices[offset] = noteToBody.MultiplyPoint3x4(new Vector3(x, bounds.min.y, bounds.min.z));
        vertices[offset + 1] = noteToBody.MultiplyPoint3x4(new Vector3(x, bounds.max.y, bounds.min.z));
        vertices[offset + 2] = noteToBody.MultiplyPoint3x4(new Vector3(x, bounds.max.y, bounds.max.z));
        vertices[offset + 3] = noteToBody.MultiplyPoint3x4(new Vector3(x, bounds.min.y, bounds.max.z));
    }

    private static Bounds GetNoteBounds(NoteObject note, Renderer[] renderers)
    {
        bool hasSolidRenderer = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                !(renderer is ParticleSystemRenderer))
            {
                hasSolidRenderer = true;
                break;
            }
        }

        Bounds bounds = new Bounds();
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                (hasSolidRenderer && renderer is ParticleSystemRenderer))
                continue;

            Bounds rendererBounds = renderer.localBounds;
            if (rendererBounds.size.sqrMagnitude <= 0.00000001f)
                continue;
            // 合并子 Renderer 时，也必须从其局部 bounds 出发。
            // 反向变换世界 AABB 无法恢复旋转前的宽高。
            Matrix4x4 rendererToNote = note.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Vector3 min = rendererBounds.min;
            Vector3 max = rendererBounds.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = rendererToNote.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z));
                if (!hasBounds)
                    bounds = new Bounds(point, Vector3.zero);
                else
                    bounds.Encapsulate(point);
                hasBounds = true;
            }
        }

        if (!hasBounds)
        {
            // 首帧粒子可能没有 bounds。使用局部单位尺寸，缩放由 Transform 统一处理。
            bounds = new Bounds(Vector3.zero, Vector3.one);
        }

        return bounds;
    }

    public override void OnHit()
    {
        if (tail != null)
        {
            tail.UpdatePosition(EndTime);
            tail.OnHit();
        }
        Destroy(gameObject);
    }

    public override void OnMiss()
    {
        if (head != null)
            head.OnMiss();
        if (tail != null)
            tail.OnMiss();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (bodyMesh != null)
        {
            if (Application.isPlaying)
                Destroy(bodyMesh);
            else
                DestroyImmediate(bodyMesh);
        }
    }
}
