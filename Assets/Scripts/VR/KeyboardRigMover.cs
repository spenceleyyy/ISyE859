using UnityEngine;
using Unity.XR.CoreUtils;

public class KeyboardRigMover : MonoBehaviour
{
    [Header("References")]
    public Rigidbody rigBody;
    [Tooltip("Optional orientation reference (e.g., XR Camera). Defaults to this transform.")]
    public Transform orientationTransform;
    [Tooltip("Optional XR Origin used to locate the camera when auto-assigning orientation.")]
    public XROrigin xrOrigin;
    [Tooltip("Auto-assign orientation from the XR Origin camera (falls back to Camera.main).")]
    public bool autoAssignOrientationFromCamera = true;
    [Tooltip("Project the camera forward/right onto the horizontal plane so WASD stays level.")]
    public bool keepMovementLevel = true;
    [Tooltip("Optional collider representing the rig body. Added automatically if missing.")]
    public Collider rigCollider;
    public float colliderRadius = 0.2f;
    public float colliderHeight = 0.6f;
    public Vector3 colliderCenter = new Vector3(0f, -0.1f, 0f);

    [Header("Thruster Forces")]
    public float forwardAcceleration = 4f;
    public float strafeAcceleration = 3f;
    public float verticalAcceleration = 2f;
    public float yawTorque = 6f;
    public float maxLinearSpeed = 2.5f;
    public float maxAngularSpeed = 60f;

    [Header("Zero Gravity")]
    public bool overrideGlobalGravity = false;
    public Vector3 zeroGravityVector = Vector3.zero;
    public float velocityDamping = 0.4f;
    public float angularDamping = 0.2f;

    [Header("Audio")]
    public AudioSource thrusterAudio;
    public float maxThrusterVolume = 0.5f;
    public float audioResponseSpeed = 6f;
    public float audioPitchJitter = 0.2f;

    [Header("Input")]
    public KeyCode ascendKey = KeyCode.LeftShift;
    public KeyCode descendKey = KeyCode.LeftControl;

    public float CurrentThrustIntensity { get; private set; }

    private Vector3 _pendingLinearInput;
    private float _pendingYawInput;
    private Vector3 _cachedGravity;
    private bool _warnedMissingOrientation = false;
    private Transform _lastOrientationTransform;

    private void Awake()
    {
        if (rigBody == null)
            rigBody = GetComponent<Rigidbody>();
        if (rigBody == null)
            rigBody = GetComponentInParent<Rigidbody>();
        if (rigBody == null)
        {
            rigBody = gameObject.AddComponent<Rigidbody>();
            Debug.LogWarning("KeyboardRigMover: Added missing Rigidbody. For best stability, assign the XR Origin root Rigidbody explicitly.");
        }

        if (xrOrigin == null)
            xrOrigin = GetComponentInParent<XROrigin>();

        EnsureOrientationReference();
        if (orientationTransform == null)
        {
            orientationTransform = transform;
        }

        if (rigCollider == null)
        {
            rigCollider = GetComponent<Collider>();
        }
        if (rigCollider == null && rigBody != null)
        {
            rigCollider = rigBody.GetComponent<Collider>();
        }
        if (rigCollider == null)
        {
            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.radius = Mathf.Max(0.05f, colliderRadius);
            capsule.height = Mathf.Max(capsule.radius * 2f, colliderHeight);
            capsule.center = colliderCenter;
            capsule.direction = 1; // Y axis
            rigCollider = capsule;
        }
        else if (rigCollider is CapsuleCollider existingCapsule)
        {
            existingCapsule.radius = Mathf.Max(0.05f, colliderRadius);
            existingCapsule.height = Mathf.Max(existingCapsule.radius * 2f, colliderHeight);
            existingCapsule.center = colliderCenter;
        }

        if (rigCollider != null)
        {
            rigCollider.isTrigger = false;
        }

        rigBody.useGravity = false;
        rigBody.detectCollisions = true;
        rigBody.interpolation = RigidbodyInterpolation.Interpolate;
        rigBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        if (thrusterAudio != null)
        {
            thrusterAudio.loop = true;
            thrusterAudio.playOnAwake = false;
            thrusterAudio.volume = 0f;
        }
    }

    private void OnEnable()
    {
        if (overrideGlobalGravity)
        {
            _cachedGravity = Physics.gravity;
            Physics.gravity = zeroGravityVector;
        }
    }

    private void OnDisable()
    {
        if (overrideGlobalGravity)
        {
            Physics.gravity = _cachedGravity;
        }
    }

    private void Update()
    {
        EnsureOrientationReference();

        float forward = 0f;
        float strafe = 0f;
        float vertical = 0f;
        float yaw = 0f;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            forward += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            forward -= 1f;

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            strafe += 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            strafe -= 1f;

        if (Input.GetKey(ascendKey))
            vertical += 1f;
        if (Input.GetKey(descendKey))
            vertical -= 1f;

        if (Input.GetKey(KeyCode.E))
            yaw += 1f;
        if (Input.GetKey(KeyCode.Q))
            yaw -= 1f;

        _pendingLinearInput = new Vector3(strafe * strafeAcceleration, vertical * verticalAcceleration, forward * forwardAcceleration);
        _pendingYawInput = yaw * yawTorque;

        float thrustIntensity = Mathf.Clamp01(_pendingLinearInput.magnitude / Mathf.Max(0.0001f, forwardAcceleration + strafeAcceleration + verticalAcceleration) + Mathf.Abs(_pendingYawInput) / Mathf.Max(0.0001f, yawTorque));
        CurrentThrustIntensity = thrustIntensity;
        UpdateThrusterAudio(thrustIntensity);
    }

    private void FixedUpdate()
    {
        if (_pendingLinearInput.sqrMagnitude > 0.0001f)
        {
            Transform orient = orientationTransform != null ? orientationTransform : transform;
            Vector3 forward = orient.forward;
            Vector3 right = orient.right;
            Vector3 up = orient.up;

            if (keepMovementLevel)
            {
                forward = Vector3.ProjectOnPlane(forward, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                forward.Normalize();

                up = Vector3.up;
                right = Vector3.Cross(up, forward);
                if (right.sqrMagnitude < 0.0001f)
                    right = Vector3.Cross(up, transform.forward);
            }
            else
            {
                if (forward.sqrMagnitude < 0.0001f)
                    forward = transform.forward;
                if (right.sqrMagnitude < 0.0001f)
                    right = transform.right;
                forward.Normalize();
                right.Normalize();
                up.Normalize();
            }

            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.Cross(up, forward);
            }
            right.Normalize();

            Vector3 worldAccel =
                right * _pendingLinearInput.x +
                up * _pendingLinearInput.y +
                forward * _pendingLinearInput.z;

            rigBody.AddForce(worldAccel, ForceMode.Acceleration);
        }

        if (Mathf.Abs(_pendingYawInput) > 0.0001f)
        {
            rigBody.AddTorque(Vector3.up * _pendingYawInput, ForceMode.Acceleration);
        }

        ApplyDamping();
        ClampVelocities();

        _pendingLinearInput = Vector3.zero;
        _pendingYawInput = 0f;
    }

    private void ApplyDamping()
    {
        if (velocityDamping > 0f && rigBody.linearVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 dampingForce = -rigBody.linearVelocity * velocityDamping;
            rigBody.AddForce(dampingForce, ForceMode.Acceleration);
        }

        if (angularDamping > 0f && rigBody.angularVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 angularDamp = -rigBody.angularVelocity * angularDamping;
            rigBody.AddTorque(angularDamp, ForceMode.Acceleration);
        }
    }

    private void ClampVelocities()
    {
        if (maxLinearSpeed > 0f && rigBody.linearVelocity.sqrMagnitude > maxLinearSpeed * maxLinearSpeed)
        {
            rigBody.linearVelocity = rigBody.linearVelocity.normalized * maxLinearSpeed;
        }

        if (maxAngularSpeed > 0f)
        {
            float maxRad = maxAngularSpeed * Mathf.Deg2Rad;
            if (rigBody.angularVelocity.sqrMagnitude > maxRad * maxRad)
            {
                rigBody.angularVelocity = rigBody.angularVelocity.normalized * maxRad;
            }
        }
    }

    private void EnsureOrientationReference()
    {
        if (orientationTransform != null)
        {
            _warnedMissingOrientation = false;
            _lastOrientationTransform = orientationTransform;
            return;
        }

        if (!autoAssignOrientationFromCamera)
        {
            if (!_warnedMissingOrientation)
            {
                Debug.LogWarning("KeyboardRigMover: orientationTransform is null and auto-assign is disabled. Movement will use this GameObject's transform.");
                _warnedMissingOrientation = true;
            }
            return;
        }

        Transform cameraTransform = null;
        if (xrOrigin != null)
        {
            var xrCamera = xrOrigin.Camera;
            if (xrCamera != null)
            {
                cameraTransform = xrCamera.transform;
            }
            else if (xrOrigin.CameraFloorOffsetObject != null)
            {
                cameraTransform = xrOrigin.CameraFloorOffsetObject.transform.GetComponentInChildren<Camera>()?.transform;
            }
        }

        if (cameraTransform == null)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = FindFirstObjectByType<Camera>();
            }
            if (cam != null)
                cameraTransform = cam.transform;
        }

        if (cameraTransform != null)
        {
            orientationTransform = cameraTransform;
            _warnedMissingOrientation = false;
            if (_lastOrientationTransform != orientationTransform)
            {
                Debug.Log($"KeyboardRigMover: Orientation set to {orientationTransform.name}");
                _lastOrientationTransform = orientationTransform;
            }
            return;
        }

        if (!_warnedMissingOrientation)
        {
            Debug.LogWarning("KeyboardRigMover: Could not locate a camera to derive orientation from. Movement will use this GameObject's forward.");
            _warnedMissingOrientation = true;
        }
    }

    private void UpdateThrusterAudio(float intensity)
    {
        if (thrusterAudio == null)
            return;

        float targetVolume = Mathf.Clamp01(intensity) * maxThrusterVolume;
        bool hasThrust = targetVolume > 0.01f;

        if (hasThrust && !thrusterAudio.isPlaying)
        {
            thrusterAudio.Play();
        }

        thrusterAudio.volume = Mathf.MoveTowards(thrusterAudio.volume, targetVolume, Time.deltaTime * audioResponseSpeed);
        thrusterAudio.pitch = 1f + audioPitchJitter * Mathf.Clamp01(intensity);

        if (!hasThrust && thrusterAudio.isPlaying && Mathf.Approximately(thrusterAudio.volume, 0f))
        {
            thrusterAudio.Stop();
        }
    }
}
