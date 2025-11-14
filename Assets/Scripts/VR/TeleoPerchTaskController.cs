using UnityEngine;
using UnityEngine.Events;
using Unity.XR.CoreUtils;

public class TeleopPerchTaskController : MonoBehaviour
{
    [Header("References")]
    public XROrigin xrOrigin;
    public Transform[] perchPoints;
    public PerchTaskLogger logger;

    [Header("Perch Colliders")]
    [Tooltip("Automatically add/update solid colliders on each perch point.")]
    public bool autoAddPerchColliders = true;
    [Tooltip("Use MeshCollider instead of BoxCollider for each perch.")]
    public bool useMeshCollider = true;
    [Tooltip("When using MeshCollider, set Convex so Unity allows dynamic interaction.")]
    public bool meshColliderConvex = true;
    [Tooltip("Derive the box collider size from the perch's Renderer bounds (ignored for mesh colliders).")]
    public bool deriveColliderFromRenderers = true;
    public Vector3 perchColliderSize = new Vector3(0.8f, 0.8f, 0.8f);
    public Vector3 perchColliderCenter = Vector3.zero;
    [Tooltip("Add a trigger sphere around each perch to use with other systems.")]
    public bool addTriggerVolume = false;
    public float triggerPadding = 0.05f;

    [Header("Perch Parameters")]
    [Tooltip("Distance within which the participant counts as 'at' the perch point.")]
    public float perchRadius = 0.5f;

    [Tooltip("Seconds the rig must stay within radius to complete a perch.")]
    public float requiredPerchTime = 2f;

    [Header("Perch Landing")]
    [Tooltip("Require the pilot to press a key to confirm a perch once inside the radius.")]
    public bool requireLandingConfirmation = true;
    public KeyCode landingKey = KeyCode.Space;
    [Tooltip("Move the XR Origin to the perch position when a landing is confirmed.")]
    public bool snapRigToPerch = true;
    [Tooltip("Offset applied when snapping to keep the camera from clipping into the perch.")]
    public Vector3 landingPositionOffset = Vector3.zero;

    [Header("Guidance Arrow")]
    public bool showGuidanceArrow = true;
    public LineRenderer guidanceLine;
    public Color guidanceColor = new Color(0.2f, 0.9f, 1f, 0.85f);
    public float guidanceWidth = 0.03f;
    public float guidanceCurveLift = 0.4f;
    public float guidanceTargetLift = 0.05f;
    public int guidanceSegments = 32;
    public float guidanceArrowHeadLength = 0.25f;
    public float guidanceArrowHeadAngle = 25f;

    [Header("Collision Guard")]
    public bool enforcePerchCollision = true;
    [Tooltip("Extra radius around the rig that must stay outside perch volumes.")]
    public float rigCollisionRadius = 0.3f;
    [Tooltip("Extra padding added to perch bounds to define the solid surface.")]
    public float perchCollisionPadding = 0.05f;
    [Tooltip("Perch colliders are forced onto this layer (Default=0).")]
    public int perchCollisionLayer = 0;
    [Tooltip("Mask used by the overlap sphere that checks for penetration.")]
    public LayerMask perchCollisionMask = 1;
    [Tooltip("How many times to iterate the push-out solver per frame.")]
    public int collisionResolutionIterations = 3;
    public bool dampVelocityOnCollision = true;

    [Header("Efficiency Tracking")]
    [Tooltip("Enable to monitor straight-line vs actual travel per perch leg.")]
    public bool trackMovementEfficiency = true;

    [Tooltip("Ignore legs shorter than this when computing efficiency (meters).")]
    public float minEfficiencyDistance = 0.05f;

    [Header("Fuel Settings")]
    public bool useFuelSystem = true;

    [Tooltip("Starting fuel capacity.")]
    public float maxFuel = 100f;

    [Tooltip("Fuel consumed per meter traveled.")]
    public float fuelPerMeter = 1f;

    [Range(0f, 1f)]
    [Tooltip("Fraction of fuel capacity that triggers the low-fuel warning.")]
    public float lowFuelPercent = 0.15f;

    [Tooltip("Optional event fired when fuel drops below the low-fuel threshold.")]
    public UnityEvent onFuelLow;

    [Tooltip("Optional event fired once when the rig runs out of fuel.")]
    public UnityEvent onFuelDepleted;

    private int _currentIndex = 0;
    private float _timeInside = 0f;
    private bool _taskComplete = false;
    private bool _taskFailed = false;

    private Vector3 _segmentStartPosition;
    private float _segmentDistanceTraveled = 0f;
    private float _totalDistanceTraveled = 0f;
    private Vector3 _lastRigPosition;
    private bool _hasLastRigPosition = false;

    private float _currentFuel;
    private bool _fuelLowRaised = false;
    private bool _fuelDepleted = false;
    private bool _readyToLand;
    private Rigidbody _xrRigidbody;
    private Collider _xrCollider;

    public bool TaskComplete => _taskComplete;
    public bool TaskFailed => _taskFailed;

    public float CurrentFuel => _currentFuel;
    public float FuelPercent => maxFuel <= 0f ? 0f : _currentFuel / maxFuel;
    public int CurrentIndex => _currentIndex;

    public Transform CurrentPerch
    {
        get
        {
            if (_taskComplete || _taskFailed) return null;
            if (perchPoints == null || perchPoints.Length == 0) return null;
            if (_currentIndex < 0 || _currentIndex >= perchPoints.Length) return null;
            return perchPoints[_currentIndex];
        }
    }

    public Vector3 GuidanceVector
    {
        get
        {
            if (xrOrigin == null) return Vector3.zero;
            Transform target = CurrentPerch;
            if (target == null) return Vector3.zero;
            return (target.position - xrOrigin.transform.position).normalized;
        }
    }

    private void Start()
    {
        if (xrOrigin == null)
        {
            xrOrigin = FindFirstObjectByType<XROrigin>();
        }

        if (xrOrigin == null)
        {
            Debug.LogError("TeleopPerchTaskController: No XROrigin found.");
        }

        if (perchPoints == null || perchPoints.Length == 0)
        {
            Debug.LogError("TeleopPerchTaskController: No perch points assigned.");
        }

        EnsurePerchColliders();
        InitializeGuidanceLine();
        CacheRigRigidbody();
        InitializeTrackingState();
    }

    private void Update()
    {
        if (_taskComplete || _taskFailed) return;
        if (xrOrigin == null) return;
        if (perchPoints == null || perchPoints.Length == 0) return;
        if (_currentIndex >= perchPoints.Length) return;

        Transform currentPerch = perchPoints[_currentIndex];

        Vector3 rigPos = xrOrigin.transform.position;
        TrackMovementAndFuel(rigPos);
        if (enforcePerchCollision)
        {
            ApplyPerchCollisionGuard(rigPos);
            rigPos = xrOrigin.transform.position;
        }

        float distance = Vector3.Distance(rigPos, currentPerch.position);

        if (distance <= perchRadius)
        {
            _timeInside += Time.deltaTime;
            bool stableHover = _timeInside >= requiredPerchTime;

            if (!requireLandingConfirmation && stableHover)
            {
                CompletePerch(currentPerch);
            }
            else if (requireLandingConfirmation && stableHover)
            {
                _readyToLand = true;
                if (Input.GetKeyDown(landingKey))
                {
                    CompletePerch(currentPerch);
                }
            }
        }
        else
        {
            _timeInside = 0f;
            _readyToLand = false;
        }

        if (showGuidanceArrow)
        {
            UpdateGuidanceArrow(currentPerch);
        }
    }

    private void InitializeTrackingState()
    {
        if (xrOrigin != null)
        {
            _lastRigPosition = xrOrigin.transform.position;
            _segmentStartPosition = _lastRigPosition;
            _hasLastRigPosition = true;
        }
        else
        {
            _hasLastRigPosition = false;
        }

        _segmentDistanceTraveled = 0f;
        _totalDistanceTraveled = 0f;

        if (useFuelSystem)
        {
            _currentFuel = Mathf.Max(0f, maxFuel);
            _fuelLowRaised = false;
            _fuelDepleted = _currentFuel <= 0f;

            if (_fuelDepleted)
            {
                HandleFuelDepleted();
            }
        }
        else
        {
            _currentFuel = maxFuel;
            _fuelLowRaised = false;
            _fuelDepleted = false;
        }
    }

    private void TrackMovementAndFuel(Vector3 rigPos)
    {
        if (!_hasLastRigPosition)
        {
            _lastRigPosition = rigPos;
            _segmentStartPosition = rigPos;
            _hasLastRigPosition = true;
            return;
        }

        if (!trackMovementEfficiency && !useFuelSystem)
        {
            _lastRigPosition = rigPos;
            return;
        }

        float frameDistance = Vector3.Distance(rigPos, _lastRigPosition);
        if (frameDistance > 0.0001f)
        {
            _totalDistanceTraveled += frameDistance;

            if (trackMovementEfficiency)
            {
                _segmentDistanceTraveled += frameDistance;
            }

            if (useFuelSystem)
            {
                ConsumeFuel(frameDistance);
            }
        }

        _lastRigPosition = rigPos;
    }

    private void BeginNextSegment()
    {
        if (xrOrigin == null) return;

        _segmentStartPosition = xrOrigin.transform.position;
        _segmentDistanceTraveled = 0f;
    }

    private string ComposeSegmentData(float straightDistance, float actualDistance)
    {
        string efficiencyData = string.Empty;
        if (trackMovementEfficiency && straightDistance > 0f)
        {
            float efficiency = ComputeEfficiency(straightDistance, actualDistance);
            efficiencyData = $"segmentStraight={straightDistance:F2}|segmentActual={actualDistance:F2}|segmentEfficiency={efficiency:F3}";
        }

        string fuelData = useFuelSystem ? $"fuel={_currentFuel:F2}" : string.Empty;

        if (!string.IsNullOrEmpty(efficiencyData) && !string.IsNullOrEmpty(fuelData))
            return efficiencyData + "|" + fuelData;

        return !string.IsNullOrEmpty(efficiencyData) ? efficiencyData : fuelData;
    }

    private void ReportSegmentEfficiency(Vector3 targetPosition, Vector3 rigPosition, float straightDistance, float actualDistance)
    {
        if (!trackMovementEfficiency) return;

        if (straightDistance < minEfficiencyDistance)
        {
            _segmentDistanceTraveled = 0f;
            return;
        }

        float efficiency = ComputeEfficiency(straightDistance, actualDistance);
        logger?.LogEvent("SegmentEfficiency", _currentIndex, rigPosition,
            $"straight={straightDistance:F2}|actual={actualDistance:F2}|efficiency={efficiency:F3}");

        _segmentDistanceTraveled = 0f;
    }

    private static float ComputeEfficiency(float straightDistance, float actualDistance)
    {
        if (straightDistance <= 0.0001f)
            return 1f;

        if (actualDistance <= 0.0001f)
            return 0f;

        float ratio = straightDistance / actualDistance;
        return Mathf.Clamp01(ratio);
    }

    private void ConsumeFuel(float distance)
    {
        if (!useFuelSystem || _fuelDepleted) return;
        if (distance <= 0f || fuelPerMeter <= 0f) return;

        float fuelUsed = distance * fuelPerMeter;
        _currentFuel = Mathf.Max(0f, _currentFuel - fuelUsed);

        if (!_fuelLowRaised && _currentFuel <= maxFuel * lowFuelPercent)
        {
            _fuelLowRaised = true;
            Vector3 rigPos = xrOrigin != null ? xrOrigin.transform.position : Vector3.zero;
            logger?.LogEvent("FuelLow", _currentIndex, rigPos, $"fuel={_currentFuel:F2}");
            onFuelLow?.Invoke();
        }

        if (_currentFuel <= 0f)
        {
            _fuelDepleted = true;
            HandleFuelDepleted();
        }
    }

    private void HandleFuelDepleted()
    {
        _currentFuel = 0f;
        _taskFailed = true;
        _taskComplete = true;

        Vector3 pos = xrOrigin != null ? xrOrigin.transform.position : Vector3.zero;
        logger?.LogEvent("FuelDepleted", _currentIndex, pos,
            $"totalDistance={_totalDistanceTraveled:F2}");
        logger?.LogEvent("TaskFailed", _currentIndex, pos, "reason=FuelDepleted");

        Debug.LogWarning("TeleopPerchTaskController: Fuel depleted. Task failed.");
        onFuelDepleted?.Invoke();
    }

    private void CompletePerch(Transform currentPerch)
    {
        if (currentPerch == null)
            return;

        Vector3 rigPos = xrOrigin != null ? xrOrigin.transform.position : Vector3.zero;

        if (snapRigToPerch && xrOrigin != null)
        {
            Vector3 snapPosition = currentPerch.position + landingPositionOffset;
            xrOrigin.transform.position = snapPosition;
            rigPos = snapPosition;

            _lastRigPosition = snapPosition;
        }

        float straightDistance = trackMovementEfficiency
            ? Vector3.Distance(_segmentStartPosition, currentPerch.position)
            : 0f;
        float actualDistance = _segmentDistanceTraveled;

        ReportSegmentEfficiency(currentPerch.position, rigPos, straightDistance, actualDistance);

        string extraData = ComposeSegmentData(straightDistance, actualDistance);
        logger?.LogEvent("PerchComplete", _currentIndex, rigPos, extraData);

        Debug.Log($"Perch {_currentIndex + 1} complete.");
        _currentIndex++;
        _timeInside = 0f;
        _readyToLand = false;

        if (_currentIndex >= perchPoints.Length)
        {
            _taskComplete = true;
            logger?.LogEvent("TaskComplete", _currentIndex - 1, rigPos,
                $"totalDistance={_totalDistanceTraveled:F2}|fuelRemaining={_currentFuel:F2}");
            Debug.Log("All perch points completed. Task finished.");
        }
        else
        {
            BeginNextSegment();
        }
    }

    private void EnsurePerchColliders()
    {
        if (!autoAddPerchColliders || perchPoints == null)
            return;

        foreach (Transform perch in perchPoints)
        {
            if (perch == null) continue;

            Collider blocking;
            if (useMeshCollider)
            {
                MeshCollider meshCollider = perch.GetComponent<MeshCollider>();
                if (meshCollider == null)
                {
                    meshCollider = perch.gameObject.AddComponent<MeshCollider>();
                    Debug.Log($"TeleopPerchTaskController: Added MeshCollider to {perch.name}");
                }

                MeshFilter filter = perch.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    meshCollider.sharedMesh = filter.sharedMesh;
                }
                else if (meshCollider.sharedMesh == null)
                {
                    Debug.LogWarning($"TeleopPerchTaskController: {perch.name} has no MeshFilter. MeshCollider will use default cube mesh.");
                }

                meshCollider.convex = meshColliderConvex;
                meshCollider.isTrigger = false;
                blocking = meshCollider;
            }
            else
            {
                BoxCollider boxCollider = perch.GetComponent<BoxCollider>();
                if (boxCollider == null)
                {
                    boxCollider = perch.gameObject.AddComponent<BoxCollider>();
                    Debug.Log($"TeleopPerchTaskController: Added BoxCollider to {perch.name}");
                }

                ConfigureBlockingCollider(perch, boxCollider);
                blocking = boxCollider;
            }

            if (addTriggerVolume)
            {
                SphereCollider trigger = null;
                SphereCollider[] spheres = perch.GetComponents<SphereCollider>();
                foreach (SphereCollider s in spheres)
                {
                    if (s != null && s.isTrigger)
                    {
                        trigger = s;
                        break;
                    }
                }

                if (trigger == null)
                {
                    trigger = perch.gameObject.AddComponent<SphereCollider>();
                    Debug.Log($"TeleopPerchTaskController: Added trigger SphereCollider to {perch.name}");
                }

                trigger.isTrigger = true;
                trigger.radius = Mathf.Max(0.01f, perchRadius + triggerPadding);
                trigger.center = Vector3.zero;
            }

            if (perchCollisionLayer >= 0 && perchCollisionLayer <= 31)
            {
                SetLayerRecursive(perch, perchCollisionLayer);
            }
        }
    }

    private void ConfigureBlockingCollider(Transform perch, BoxCollider blocking)
    {
        blocking.isTrigger = false;

        if (deriveColliderFromRenderers)
        {
            Renderer[] renderers = perch.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds localBounds = CalculateLocalBounds(perch, renderers);
                blocking.center = localBounds.center;
                blocking.size = localBounds.size;
                return;
            }
        }

        blocking.center = perchColliderCenter;
        blocking.size = perchColliderSize;
    }

    private static Bounds CalculateLocalBounds(Transform root, Renderer[] renderers)
    {
        Matrix4x4 worldToLocal = root.worldToLocalMatrix;
        Vector3 firstCenter = worldToLocal.MultiplyPoint3x4(renderers[0].bounds.center);
        Bounds bounds = new Bounds(firstCenter, Vector3.zero);

        foreach (Renderer rend in renderers)
        {
            Bounds rb = rend.bounds;
            Vector3 center = rb.center;
            Vector3 ext = rb.extents;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = center + Vector3.Scale(ext, new Vector3(x, y, z));
                        Vector3 localCorner = worldToLocal.MultiplyPoint3x4(corner);
                        bounds.Encapsulate(localCorner);
                    }
                }
            }
        }

        return bounds;
    }

    private void InitializeGuidanceLine()
    {
        if (!showGuidanceArrow)
            return;

        if (guidanceLine == null)
        {
            GameObject arrowObj = new GameObject("PerchGuidanceArrow");
            arrowObj.transform.SetParent(transform, false);
            guidanceLine = arrowObj.AddComponent<LineRenderer>();
        }

        guidanceLine.useWorldSpace = true;
        guidanceLine.loop = false;
        guidanceLine.enabled = false;
        guidanceLine.widthMultiplier = Mathf.Max(0.0005f, guidanceWidth);

        if (guidanceLine.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                guidanceLine.material = new Material(shader);
            }
        }

        ApplyGuidanceColor();
    }

    private void ApplyGuidanceColor()
    {
        if (guidanceLine == null)
            return;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(guidanceColor, 0f),
                new GradientColorKey(guidanceColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(guidanceColor.a, 0f),
                new GradientAlphaKey(guidanceColor.a, 1f)
            });
        guidanceLine.colorGradient = gradient;
    }

    private void UpdateGuidanceArrow(Transform currentPerch)
    {
        if (guidanceLine == null)
            InitializeGuidanceLine();

        if (guidanceLine == null)
            return;

        if (currentPerch == null || xrOrigin == null || guidanceSegments < 2)
        {
            guidanceLine.enabled = false;
            return;
        }

        Vector3 start = _segmentStartPosition;
        if (!_hasLastRigPosition)
        {
            start = xrOrigin.transform.position;
        }

        Vector3 target = currentPerch.position + Vector3.up * Mathf.Max(0f, guidanceTargetLift);

        int segmentCount = Mathf.Max(2, guidanceSegments);
        int totalPoints = segmentCount + 3;
        guidanceLine.positionCount = totalPoints;

        Vector3 mid = (start + target) * 0.5f + Vector3.up * guidanceCurveLift;

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = (float)i / segmentCount;
            Vector3 point = EvaluateQuadratic(start, mid, target, t);
            guidanceLine.SetPosition(i, point);
        }

        Vector3 dir = (guidanceLine.GetPosition(segmentCount) - guidanceLine.GetPosition(segmentCount - 1)).normalized;
        if (dir.sqrMagnitude < 0.0001f)
            dir = (target - start).normalized;

        Vector3 right = Vector3.Cross(dir, Vector3.up);
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.Cross(dir, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, dir).normalized;

        float radians = guidanceArrowHeadAngle * Mathf.Deg2Rad;
        Vector3 arrowBase = target - dir * guidanceArrowHeadLength;
        Vector3 sideOffset = (right * Mathf.Sin(radians) + up * Mathf.Cos(radians)) * guidanceArrowHeadLength * 0.5f;

        guidanceLine.SetPosition(segmentCount + 1, arrowBase + sideOffset);
        guidanceLine.SetPosition(segmentCount + 2, arrowBase - sideOffset);

        guidanceLine.enabled = true;
    }

    private void ApplyPerchCollisionGuard(Vector3 rigPos)
    {
        if (xrOrigin == null || perchPoints == null || perchPoints.Length == 0 || _xrCollider == null)
            return;

        for (int iteration = 0; iteration < collisionResolutionIterations; iteration++)
        {
            bool adjusted = false;

            for (int i = 0; i < perchPoints.Length; i++)
            {
                Transform perch = perchPoints[i];
                if (perch == null) continue;

                Collider blocking = perch.GetComponent<Collider>();
                if (blocking == null)
                {
                    blocking = perch.GetComponentInChildren<Collider>();
                }

                if (blocking == null) continue;

                Vector3 separationDir;
                float separationDist;
                bool penetrating = Physics.ComputePenetration(
                    _xrCollider, xrOrigin.transform.position, xrOrigin.transform.rotation,
                    blocking, blocking.transform.position, blocking.transform.rotation,
                    out separationDir, out separationDist);

                if (!penetrating)
                {
                    float minDistance = Mathf.Max(0.01f, rigCollisionRadius + perchCollisionPadding);
                    Vector3 closestPoint = blocking.ClosestPoint(rigPos);
                    Vector3 offset = rigPos - closestPoint;
                    float dist = offset.magnitude;
                    if (dist >= minDistance)
                        continue;

                    separationDir = offset.sqrMagnitude > 0.0001f ? offset.normalized : Vector3.up;
                    separationDist = minDistance - dist;
                }

                Vector3 correction = separationDir * separationDist;
                xrOrigin.transform.position += correction;
                Physics.SyncTransforms();
                Debug.Log($"CollisionGuard: pushing from {perch.name} dir={separationDir} dist={separationDist:F3}");

                if (dampVelocityOnCollision && _xrRigidbody != null && !_xrRigidbody.isKinematic)
                {
                    _xrRigidbody.position = xrOrigin.transform.position;
                    Vector3 velocity = _xrRigidbody.linearVelocity;
                    float inwardSpeed = Vector3.Dot(velocity, separationDir);
                    if (inwardSpeed < 0f)
                    {
                        _xrRigidbody.linearVelocity = velocity - separationDir * inwardSpeed;
                        Debug.Log($"CollisionGuard: damped velocity from {velocity} to {_xrRigidbody.linearVelocity}");
                    }
                }

                rigPos = xrOrigin.transform.position;
                adjusted = true;
            }

            if (!adjusted)
            {
                break;
            }
        }
    }

    private void CacheRigRigidbody()
    {
        if (_xrRigidbody != null && _xrCollider != null)
            return;

        if (xrOrigin != null)
        {
            _xrRigidbody = xrOrigin.GetComponent<Rigidbody>();
            if (_xrRigidbody == null)
            {
                _xrRigidbody = xrOrigin.GetComponentInChildren<Rigidbody>();
            }

            _xrCollider = xrOrigin.GetComponent<Collider>();
            if (_xrCollider == null)
            {
                _xrCollider = xrOrigin.GetComponentInChildren<Collider>();
            }
        }

        if (( _xrRigidbody == null || _xrCollider == null) && xrOrigin != null)
        {
            RigPhysicsBootstrap bootstrap = xrOrigin.GetComponent<RigPhysicsBootstrap>();
            if (bootstrap == null)
            {
                bootstrap = xrOrigin.gameObject.AddComponent<RigPhysicsBootstrap>();
            }

            if (bootstrap != null)
            {
                _xrRigidbody = bootstrap.GetRigidbody();
                _xrCollider = bootstrap.GetCollider();
            }
        }

        if (_xrRigidbody == null || _xrCollider == null)
        {
            KeyboardRigMover mover = FindFirstObjectByType<KeyboardRigMover>();
            if (mover != null)
            {
                _xrRigidbody = mover.rigBody != null ? mover.rigBody : mover.GetComponent<Rigidbody>();
                _xrCollider = mover.rigCollider != null ? mover.rigCollider : mover.GetComponent<Collider>();
            }
        }

        if (_xrCollider == null)
        {
            Debug.LogWarning("TeleopPerchTaskController: Unable to locate rig collider. Collision guard disabled.");
            enforcePerchCollision = false;
        }
    }

    private static void SetLayerRecursive(Transform root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;
        foreach (Transform child in root)
        {
            SetLayerRecursive(child, layer);
        }
    }

    private static Vector3 EvaluateQuadratic(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float oneMinusT = 1f - t;
        return oneMinusT * oneMinusT * a + 2f * oneMinusT * t * b + t * t * c;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            EnsurePerchColliders();
        }

        if (perchCollisionLayer >= 0 && perchCollisionLayer <= 31)
        {
            perchCollisionMask = 1 << perchCollisionLayer;
        }
    }
#endif
}
