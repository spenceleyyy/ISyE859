using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;

[RequireComponent(typeof(LineRenderer))]
public class PerchPathVisualizer : MonoBehaviour
{
    [Header("References")]
    public TeleopPerchTaskController taskController;
    public XROrigin xrOrigin;

    [Header("Anchor")]
    [Tooltip("Force the path to use this transform as its starting point. Defaults to the XR camera.")]
    public Transform startAnchorOverride;
    [Tooltip("When true the visualized path starts at the active XR camera (player POV).")]
    public bool anchorToCamera = true;
    [Tooltip("Offset applied to the anchor position. When relative, the offset follows the anchor rotation.")]
    public Vector3 startOffset = Vector3.zero;
    public bool offsetRelativeToAnchor = true;

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
    [Header("Path Customization")]
    [Tooltip("Normalized curve describing how the path lifts vertically. X=0 -> start, X=1 -> target, Y multiplied by curveLift.")]
    public AnimationCurve heightProfile = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 1f),
        new Keyframe(1f, 0f));
    [Tooltip("Curve used for lateral offsets along the path. Useful for bending the path left/right.")]
    public AnimationCurve lateralProfile = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(1f, 0f));
    public float lateralOffset = 0f;
    [Tooltip("When true the lateral offset axis comes from the view/camera. Otherwise manual reference/axis is used.")]
    public bool lateralRelativeToView = true;
    public Transform lateralReferenceOverride;

[Header("Manual Path")]
[Tooltip("Override the procedural curve with explicit waypoints (useful for weaving around large structures).")]
public bool useManualWaypoints = false;
    [Tooltip("World-space waypoints visited in order when drawing the path.")]
    public Transform[] manualWaypoints;
    [Tooltip("Meters to lift each waypoint so the line doesn't overlap geometry.")]
    public float manualWaypointLift = 0f;
    [Tooltip("Optional list of waypoint object names that will be resolved at runtime (useful when you don't want to assign transforms manually).")]
    public string[] manualWaypointNames;
    [Tooltip("Explicit world positions visited in order when drawing the path (baked coordinates).")]
    public Vector3[] manualWaypointPositions = new[]
    {
        new Vector3(-246.77f, 52.834f, -104.3342f),
        new Vector3(-262.03f, 52.834f, -97.01f),
        new Vector3(-263.45f, 52.834f, -88.64f)
    };
    [Tooltip("When enabled, Catmull-Rom smoothing is applied through manual waypoints. Disable for straight segments passing exactly through each point.")]
    public bool manualUseSmoothCurve = true;

private LineRenderer _lineRenderer;
private readonly List<Vector3> _pathSamples = new List<Vector3>(64);
private readonly List<Vector3> _manualNodes = new List<Vector3>(16);
private readonly Dictionary<string, Transform> _namedWaypointCache = new Dictionary<string, Transform>();
private readonly HashSet<string> _missingWaypointNames = new HashSet<string>();

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

        Transform anchor = ResolveViewAnchor();
        Vector3 start = GetAnchorPosition(anchor);
        Vector3 lateralAxis = DetermineLateralAxis(anchor);
        DrawSegment(currentTarget.position, start, lateralAxis);
    }

    private void DrawSegment(Vector3 targetPosition, Vector3 startPosition, Vector3 lateralAxis)
    {
        Vector3 liftedTarget = targetPosition + Vector3.up * Mathf.Max(0f, targetLiftAmount);

        bool drewManual = useManualWaypoints && TryBuildManualPath(startPosition, liftedTarget);
        if (!drewManual)
        {
            BuildProceduralPath(startPosition, liftedTarget, lateralAxis);
        }

        ApplyLineRenderer(liftedTarget);
    }

    private void BuildProceduralPath(Vector3 start, Vector3 end, Vector3 lateralAxis)
    {
        _pathSamples.Clear();

        int segmentCount = Mathf.Max(4, curveSegments);
        Vector3 upAxis = Vector3.up;
        Vector3 lateral = lateralAxis.sqrMagnitude > 0.0001f ? lateralAxis.normalized : Vector3.zero;

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = (float)i / segmentCount;
            Vector3 point = Vector3.Lerp(start, end, t);
            float heightFactor = EvaluateHeight(t);
            if (Mathf.Abs(heightFactor) > Mathf.Epsilon)
            {
                point += upAxis * curveLift * heightFactor;
            }

            if (lateralOffset != 0f && lateral != Vector3.zero)
            {
                float lateralFactor = EvaluateLateral(t);
                if (Mathf.Abs(lateralFactor) > Mathf.Epsilon)
                {
                    point += lateral * lateralOffset * lateralFactor;
                }
            }

            _pathSamples.Add(point);
        }
    }

    private bool TryBuildManualPath(Vector3 start, Vector3 end)
    {
        _manualNodes.Clear();
        _manualNodes.Add(start);

        bool addedWaypoints = AppendManualWaypoints(manualWaypoints);
        bool addedNamedWaypoints = false;
        bool addedPositionWaypoints = false;

        if (!addedWaypoints)
        {
            addedNamedWaypoints = AppendManualWaypointNames(manualWaypointNames);
        }

        if (!addedWaypoints && !addedNamedWaypoints)
        {
            addedPositionWaypoints = AppendManualWaypointPositions(manualWaypointPositions);
        }

        _manualNodes.Add(end);

        if (_manualNodes.Count < 3 || (!addedWaypoints && !addedNamedWaypoints && !addedPositionWaypoints))
            return false;

        BuildManualSamples();
        return _pathSamples.Count >= 2;
    }

    private void BuildManualSamples()
    {
        if (manualUseSmoothCurve && _manualNodes.Count >= 3)
        {
            BuildCatmullRomSamples();
        }
        else
        {
            BuildLinearSamples();
        }
    }

    private bool AppendManualWaypoints(Transform[] waypoints)
    {
        if (waypoints == null || waypoints.Length == 0)
            return false;

        bool added = false;
        for (int i = 0; i < waypoints.Length; i++)
        {
            Transform waypoint = waypoints[i];
            if (waypoint == null)
                continue;

            Vector3 lifted = waypoint.position + Vector3.up * manualWaypointLift;
            _manualNodes.Add(lifted);
            added = true;
        }
        return added;
    }

    private bool AppendManualWaypointNames(string[] waypointNames)
    {
        if (waypointNames == null || waypointNames.Length == 0)
            return false;

        bool added = false;
        for (int i = 0; i < waypointNames.Length; i++)
        {
            string waypointName = waypointNames[i];
            if (string.IsNullOrWhiteSpace(waypointName))
                continue;

            Transform resolved = ResolveWaypointByName(waypointName.Trim());
            if (resolved == null)
                continue;

            Vector3 lifted = resolved.position + Vector3.up * manualWaypointLift;
            _manualNodes.Add(lifted);
            added = true;
        }
        return added;
    }

    private bool AppendManualWaypointPositions(Vector3[] positions)
    {
        if (positions == null || positions.Length == 0)
            return false;

        bool added = false;
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 lifted = positions[i] + Vector3.up * manualWaypointLift;
            _manualNodes.Add(lifted);
            added = true;
        }

        return added;
    }

    private void BuildCatmullRomSamples()
    {
        _pathSamples.Clear();

        int segments = _manualNodes.Count - 1;
        int samplesPerSegment = Mathf.Max(2, curveSegments / Mathf.Max(1, segments));

        for (int seg = 0; seg < segments; seg++)
        {
            Vector3 p0 = _manualNodes[Mathf.Max(seg - 1, 0)];
            Vector3 p1 = _manualNodes[seg];
            Vector3 p2 = _manualNodes[Mathf.Min(seg + 1, _manualNodes.Count - 1)];
            Vector3 p3 = _manualNodes[Mathf.Min(seg + 2, _manualNodes.Count - 1)];

            for (int i = 0; i < samplesPerSegment; i++)
            {
                float t = (float)i / samplesPerSegment;
                Vector3 point = EvaluateCatmullRom(p0, p1, p2, p3, t);
                _pathSamples.Add(point);
            }
        }

        _pathSamples.Add(_manualNodes[_manualNodes.Count - 1]);
    }

    private void BuildLinearSamples()
    {
        _pathSamples.Clear();
        for (int i = 0; i < _manualNodes.Count; i++)
        {
            _pathSamples.Add(_manualNodes[i]);
        }
    }

    private void ApplyLineRenderer(Vector3 targetPoint)
    {
        if (_pathSamples.Count < 2)
        {
            _lineRenderer.enabled = false;
            _lineRenderer.positionCount = 0;
            return;
        }

        _lineRenderer.enabled = true;
        int baseCount = _pathSamples.Count;
        _lineRenderer.positionCount = baseCount + 2; // + arrow head points

        for (int i = 0; i < baseCount; i++)
        {
            _lineRenderer.SetPosition(i, _pathSamples[i]);
        }

        Vector3 dir = (_pathSamples[baseCount - 1] - _pathSamples[baseCount - 2]).normalized;
        if (dir.sqrMagnitude < 0.0001f)
            dir = (targetPoint - _pathSamples[0]).normalized;

        Vector3 right = Vector3.Cross(dir, Vector3.up);
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.Cross(dir, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, dir).normalized;

        float radians = arrowHeadAngle * Mathf.Deg2Rad;
        Vector3 arrowBase = targetPoint - dir * arrowHeadLength;
        Vector3 sideOffset = (right * Mathf.Sin(radians) + up * Mathf.Cos(radians)) * arrowHeadLength * 0.5f;

        _lineRenderer.SetPosition(baseCount, arrowBase + sideOffset);
        _lineRenderer.SetPosition(baseCount + 1, arrowBase - sideOffset);
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

    private Transform ResolveViewAnchor()
    {
        if (startAnchorOverride != null)
            return startAnchorOverride;

        if (anchorToCamera)
        {
            Transform cameraTransform = GetCameraTransformFromOrigin();
            if (cameraTransform != null)
                return cameraTransform;

            Camera fallback = Camera.main;
            if (fallback == null)
            {
                fallback = FindFirstObjectByType<Camera>();
            }
            if (fallback != null)
                return fallback.transform;
        }

        if (xrOrigin != null)
            return xrOrigin.transform;

        return transform;
    }

    private Transform GetCameraTransformFromOrigin()
    {
        if (xrOrigin == null)
            return null;

        if (xrOrigin.Camera != null)
            return xrOrigin.Camera.transform;

        if (xrOrigin.CameraFloorOffsetObject != null)
        {
            Camera found = xrOrigin.CameraFloorOffsetObject.GetComponentInChildren<Camera>();
            if (found != null)
                return found.transform;
        }

        return null;
    }

    private Vector3 GetAnchorPosition(Transform anchor)
    {
        if (anchor == null)
            return transform.position;

        Vector3 offset = offsetRelativeToAnchor ? anchor.TransformVector(startOffset) : startOffset;
        return anchor.position + offset;
    }

    private Vector3 DetermineLateralAxis(Transform anchor)
    {
        Transform reference = lateralReferenceOverride != null ? lateralReferenceOverride : (lateralRelativeToView ? anchor : null);
        if (reference != null)
        {
            Vector3 forward = reference.forward;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;

            Vector3 flattened = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flattened.sqrMagnitude > 0.0001f)
                forward = flattened.normalized;
            else
                forward.Normalize();

            Vector3 axis = Vector3.Cross(Vector3.up, forward);
            if (axis.sqrMagnitude < 0.0001f)
                axis = reference.right;
            return axis.normalized;
        }

        if (anchor != null && anchor.right.sqrMagnitude > 0.0001f)
            return anchor.right.normalized;

        return Vector3.right;
    }

    private float EvaluateHeight(float t)
    {
        if (heightProfile != null && heightProfile.length > 0)
            return heightProfile.Evaluate(Mathf.Clamp01(t));

        return Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
    }

    private float EvaluateLateral(float t)
    {
        if (lateralProfile != null && lateralProfile.length > 0)
            return lateralProfile.Evaluate(Mathf.Clamp01(t));

        return 0f;
    }

    private static Vector3 EvaluateCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * ((2f * p1) +
                       (-p0 + p2) * t +
                       (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                       (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private Transform ResolveWaypointByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        if (_namedWaypointCache.TryGetValue(name, out Transform cached) && cached != null)
            return cached;

        GameObject obj = GameObject.Find(name);
        if (obj != null)
        {
            Transform result = obj.transform;
            _namedWaypointCache[name] = result;
            _missingWaypointNames.Remove(name);
            return result;
        }

        if (!_missingWaypointNames.Contains(name))
        {
            Debug.LogWarning($"PerchPathVisualizer: Manual waypoint '{name}' not found in the scene.");
            _missingWaypointNames.Add(name);
        }

        return null;
    }
}
