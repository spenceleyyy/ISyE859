using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;

public class KeyboardRigMover : MonoBehaviour
{
    [System.Serializable]
    public enum ThrusterInputType
    {
        Forward,
        Backward,
        StrafeRight,
        StrafeLeft,
        VerticalUp,
        VerticalDown,
        YawRight,
        YawLeft
    }

    [System.Serializable]
    public class DirectionalThrusterAudio
    {
        public ThrusterInputType inputType = ThrusterInputType.Forward;
        [Tooltip("AudioSource to drive (optional). Leave empty to spawn one using the clip below.")]
        public AudioSource audioSource;
        [Tooltip("Clip used for this thruster. Required if the AudioSource slot is empty or has no clip.")]
        public AudioClip clip;
        public float maxVolume = 0.35f;
        public float responseSpeed = 10f;
        public float basePitch = 1f;
        public float pitchVariance = 0.1f;
        [Range(0f, 1f)] public float spatialBlend = 0f;
        public bool spatialize = false;
        [HideInInspector] public bool isPlaying;
    }

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
    [Tooltip("Optional audio layers for individual directional thrusters (so overlapping sounds never cut each other off).")]
    public DirectionalThrusterAudio[] directionalThrusterAudio;
    [Header("Debug Audio")]
    public bool debugMainThrusterAudio = false;
    public bool debugDirectionalThrusterAudio = false;

    [Header("Input")]
    public KeyCode ascendKey = KeyCode.LeftShift;
    public KeyCode descendKey = KeyCode.LeftControl;

    public float CurrentThrustIntensity { get; private set; }

    private Vector3 _pendingLinearInput;
    private float _pendingYawInput;
    private Vector3 _cachedGravity;
    private bool _warnedMissingOrientation = false;
    private Transform _lastOrientationTransform;
    private bool _mainThrusterIsPlaying = false;
    private readonly HashSet<DirectionalThrusterAudio> _warnedDirectionalClip = new HashSet<DirectionalThrusterAudio>();

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

        InitializeDirectionalAudioSources();
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

        bool forwardPositivePressed = false;
        bool forwardNegativePressed = false;
        bool strafeRightPressed = false;
        bool strafeLeftPressed = false;
        bool verticalUpPressed = false;
        bool verticalDownPressed = false;
        bool yawRightPressed = false;
        bool yawLeftPressed = false;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
        {
            forward += 1f;
            forwardPositivePressed = true;
        }
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
        {
            forward -= 1f;
            forwardNegativePressed = true;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            strafe += 1f;
            strafeRightPressed = true;
        }
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            strafe -= 1f;
            strafeLeftPressed = true;
        }

        if (Input.GetKey(ascendKey))
        {
            vertical += 1f;
            verticalUpPressed = true;
        }
        if (Input.GetKey(descendKey))
        {
            vertical -= 1f;
            verticalDownPressed = true;
        }

        if (Input.GetKey(KeyCode.E))
        {
            yaw += 1f;
            yawRightPressed = true;
        }
        if (Input.GetKey(KeyCode.Q))
        {
            yaw -= 1f;
            yawLeftPressed = true;
        }

        _pendingLinearInput = new Vector3(strafe * strafeAcceleration, vertical * verticalAcceleration, forward * forwardAcceleration);
        _pendingYawInput = yaw * yawTorque;

        float thrustIntensity = Mathf.Clamp01(_pendingLinearInput.magnitude / Mathf.Max(0.0001f, forwardAcceleration + strafeAcceleration + verticalAcceleration) + Mathf.Abs(_pendingYawInput) / Mathf.Max(0.0001f, yawTorque));
        CurrentThrustIntensity = thrustIntensity;
        UpdateThrusterAudio(thrustIntensity);
        UpdateDirectionalThrusterAudio(
            forwardPositivePressed,
            forwardNegativePressed,
            strafeRightPressed,
            strafeLeftPressed,
            verticalUpPressed,
            verticalDownPressed,
            yawRightPressed,
            yawLeftPressed);
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
            _mainThrusterIsPlaying = true;
            if (debugMainThrusterAudio)
            {
                Debug.Log($"[KeyboardRigMover] Main thruster START (intensity={intensity:0.00})");
            }
        }

        thrusterAudio.volume = targetVolume;
        thrusterAudio.pitch = 1f + audioPitchJitter * Mathf.Clamp01(intensity);

        if (!hasThrust && thrusterAudio.isPlaying)
        {
            thrusterAudio.Stop();
            if (_mainThrusterIsPlaying && debugMainThrusterAudio)
            {
                Debug.Log("[KeyboardRigMover] Main thruster STOP");
            }
            _mainThrusterIsPlaying = false;
        }
    }

    private void UpdateDirectionalThrusterAudio(
        bool forwardPressed,
        bool backwardPressed,
        bool strafeRightPressed,
        bool strafeLeftPressed,
        bool verticalUpPressed,
        bool verticalDownPressed,
        bool yawRightPressed,
        bool yawLeftPressed)
    {
        if (directionalThrusterAudio == null || directionalThrusterAudio.Length == 0)
            return;

        for (int i = 0; i < directionalThrusterAudio.Length; i++)
        {
            DirectionalThrusterAudio channel = directionalThrusterAudio[i];
            if (channel == null)
                continue;

            AudioSource source = SetupDirectionalAudioSource(channel, false);
            if (source == null)
                continue;

            bool isActive = IsDirectionActive(channel.inputType, forwardPressed, backwardPressed, strafeRightPressed, strafeLeftPressed, verticalUpPressed, verticalDownPressed, yawRightPressed, yawLeftPressed);
            if (isActive)
            {
                if (!channel.isPlaying)
                {
                    source.Stop();
                    source.time = 0f;
                    source.Play();
                    channel.isPlaying = true;
                    if (debugDirectionalThrusterAudio)
                    {
                        Debug.Log($"[KeyboardRigMover] Thruster {channel.inputType} START");
                    }
                }

                source.volume = channel.maxVolume;
                source.pitch = channel.basePitch + channel.pitchVariance;
            }
            else if (channel.isPlaying || source.isPlaying)
            {
                source.Stop();
                source.time = 0f;
                source.volume = 0f;
                source.pitch = channel.basePitch;
                channel.isPlaying = false;
                if (debugDirectionalThrusterAudio)
                {
                    Debug.Log($"[KeyboardRigMover] Thruster {channel.inputType} STOP");
                }
            }
        }
    }

    private static bool IsDirectionActive(
        ThrusterInputType type,
        bool forwardPressed,
        bool backwardPressed,
        bool strafeRightPressed,
        bool strafeLeftPressed,
        bool verticalUpPressed,
        bool verticalDownPressed,
        bool yawRightPressed,
        bool yawLeftPressed)
    {
        switch (type)
        {
            case ThrusterInputType.Forward:
                return forwardPressed;
            case ThrusterInputType.Backward:
                return backwardPressed;
            case ThrusterInputType.StrafeRight:
                return strafeRightPressed;
            case ThrusterInputType.StrafeLeft:
                return strafeLeftPressed;
            case ThrusterInputType.VerticalUp:
                return verticalUpPressed;
            case ThrusterInputType.VerticalDown:
                return verticalDownPressed;
            case ThrusterInputType.YawRight:
                return yawRightPressed;
            case ThrusterInputType.YawLeft:
                return yawLeftPressed;
            default:
                return false;
        }
    }

    private void InitializeDirectionalAudioSources()
    {
        if (directionalThrusterAudio == null)
            return;

        for (int i = 0; i < directionalThrusterAudio.Length; i++)
        {
            SetupDirectionalAudioSource(directionalThrusterAudio[i], true);
        }
    }

    private AudioSource SetupDirectionalAudioSource(DirectionalThrusterAudio channel, bool resetVolume)
    {
        if (channel == null)
            return null;

        AudioSource src = channel.audioSource;
        bool created = false;

        if (src == null && channel.clip != null)
        {
            GameObject child = new GameObject($"ThrusterAudio_{channel.inputType}");
            child.transform.SetParent(transform, false);
            src = child.AddComponent<AudioSource>();
            channel.audioSource = src;
            created = true;
        }

        if (src == null)
        {
            WarnMissingDirectionalClip(channel);
            return null;
        }

        if (src.clip == null && channel.clip != null)
        {
            src.clip = channel.clip;
        }

        if (src.clip == null)
        {
            WarnMissingDirectionalClip(channel);
            return null;
        }

        src.loop = true;
        src.playOnAwake = false;
        if (created || resetVolume)
        {
            src.volume = 0f;
        }
        src.spatialBlend = Mathf.Clamp01(channel.spatialBlend);
        src.spatialize = channel.spatialize;

        return src;
    }

    private void WarnMissingDirectionalClip(DirectionalThrusterAudio channel)
    {
        if (channel == null || _warnedDirectionalClip.Contains(channel))
            return;

        Debug.LogWarning($"KeyboardRigMover: Directional thruster '{channel.inputType}' must define an AudioClip or AudioSource.");
        _warnedDirectionalClip.Add(channel);
    }
}
