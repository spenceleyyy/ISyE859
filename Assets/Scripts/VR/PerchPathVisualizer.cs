using UnityEngine;
using Unity.XR.CoreUtils;

[RequireComponent(typeof(LineRenderer))]
public class PerchPathVisualizer : MonoBehaviour
{
    [Header("References")]
    public TeleopPerchTaskController taskController;
    public XROrigin xrOrigin;

    [Header("Line Style")]
    public Color lineColor = new Color(0.2f, 0.8f, 1f, 0.75f);
    public float lineWidth = 0.025f;
    [Tooltip("Meters to lift the line endpoint so it does not overlap the perch geometry.")]
    public float targetLiftAmount = 0.02f;
    [Tooltip("Meters to raise the curve midpoint for a gentle arc.")]
    public float curveLift = 0.25f;
    public int curveSegments = 24;
    public float arrowHeadLength = 0.2f;
    public float arrowHeadAngle = 24f;

    private LineRenderer _lineRenderer;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.positionCount = 0;
        _lineRenderer.enabled = false;

        if (_lineRenderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _lineRenderer.material = new Material(shader);
            }
        }

        ApplyLineStyle();
    }

    private void Start()
    {
        if (taskController == null)
        {
            taskController = FindFirstObjectByType<TeleopPerchTaskController>();
        }

        if (xrOrigin == null)
        {
            xrOrigin = FindFirstObjectByType<XROrigin>();
        }

    }

    private void Update()
    {
        if (taskController == null || xrOrigin == null)
        {
            HideLine();
            return;
        }

        if (taskController.TaskComplete || taskController.TaskFailed)
        {
            HideLine();
            return;
        }

        Transform currentTarget = taskController.CurrentPerch;
        if (currentTarget == null)
        {
            HideLine();
            return;
        }

        Vector3 start = xrOrigin.transform.position;
        DrawSegment(currentTarget.position, start);
    }

    private void DrawSegment(Vector3 targetPosition, Vector3 startPosition)
    {
        Vector3 liftedTarget = targetPosition + Vector3.up * Mathf.Max(0f, targetLiftAmount);

        _lineRenderer.enabled = true;
        int segmentCount = Mathf.Max(4, curveSegments);
        int totalPoints = segmentCount + 3; // curve + two arrow head points
        _lineRenderer.positionCount = totalPoints;

        Vector3 start = startPosition;
        Vector3 end = liftedTarget;
        Vector3 mid = (start + end) * 0.5f + Vector3.up * curveLift;

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = (float)i / segmentCount;
            Vector3 point = EvaluateQuadratic(start, mid, end, t);
            _lineRenderer.SetPosition(i, point);
        }

        Vector3 dir = (_lineRenderer.GetPosition(segmentCount) - _lineRenderer.GetPosition(segmentCount - 1)).normalized;
        if (dir.sqrMagnitude < 0.0001f)
            dir = (end - start).normalized;

        Vector3 right = Vector3.Cross(dir, Vector3.up);
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.Cross(dir, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, dir).normalized;

        float radians = arrowHeadAngle * Mathf.Deg2Rad;
        Vector3 arrowBase = end - dir * arrowHeadLength;
        Vector3 sideOffset = (right * Mathf.Sin(radians) + up * Mathf.Cos(radians)) * arrowHeadLength * 0.5f;

        _lineRenderer.SetPosition(segmentCount + 1, arrowBase + sideOffset);
        _lineRenderer.SetPosition(segmentCount + 2, arrowBase - sideOffset);
    }

    private void HideLine()
    {
        if (_lineRenderer.positionCount != 0)
        {
            _lineRenderer.positionCount = 0;
        }
        if (_lineRenderer.enabled)
        {
            _lineRenderer.enabled = false;
        }
    }

    private void ApplyLineStyle()
    {
        if (_lineRenderer == null)
            return;

        _lineRenderer.widthMultiplier = Mathf.Max(0.0005f, lineWidth);
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(lineColor, 0f),
                new GradientColorKey(lineColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(lineColor.a, 0f),
                new GradientAlphaKey(lineColor.a, 1f)
            });
        _lineRenderer.colorGradient = gradient;
    }

    private void OnValidate()
    {
        if (_lineRenderer == null)
            _lineRenderer = GetComponent<LineRenderer>();

        ApplyLineStyle();
    }

    private static Vector3 EvaluateQuadratic(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float oneMinusT = 1f - t;
        return oneMinusT * oneMinusT * a + 2f * oneMinusT * t * b + t * t * c;
    }
}
