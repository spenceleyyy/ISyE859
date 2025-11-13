using UnityEngine;
using Unity.XR.CoreUtils;

[DisallowMultipleComponent]
public class RigPhysicsBootstrap : MonoBehaviour
{
    [Header("References")]
    public XROrigin xrOrigin;
    public Transform colliderTransform;

    [Header("Rigidbody")]
    public float mass = 50f;
    public float drag = 0f;
    public float angularDrag = 0.05f;
    public bool useGravity = false;
    public RigidbodyInterpolation interpolation = RigidbodyInterpolation.Interpolate;
    public RigidbodyConstraints constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

    [Header("Collider")]
    public float colliderRadius = 0.25f;
    public float colliderHeight = 1.2f;
    public Vector3 colliderCenter = new Vector3(0f, -0.1f, 0f);

    private Rigidbody _rigidbody;
    private Collider _collider;

    private void Reset()
    {
        xrOrigin = GetComponent<XROrigin>();
    }

    private void Awake()
    {
        if (xrOrigin == null)
        {
            xrOrigin = GetComponent<XROrigin>();
        }
        if (colliderTransform == null && xrOrigin != null)
        {
            colliderTransform = xrOrigin.transform;
        }

        EnsureRigidbody();
        EnsureCollider();
    }

    private void EnsureRigidbody()
    {
        Transform target = colliderTransform != null ? colliderTransform : transform;
        _rigidbody = target.GetComponent<Rigidbody>();
        if (_rigidbody == null)
        {
            _rigidbody = target.gameObject.AddComponent<Rigidbody>();
        }

        _rigidbody.mass = Mathf.Max(0.1f, mass);
        _rigidbody.linearDamping = Mathf.Max(0f, drag);
        _rigidbody.angularDamping = Mathf.Max(0f, angularDrag);
        _rigidbody.useGravity = useGravity;
        _rigidbody.interpolation = interpolation;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rigidbody.constraints = constraints;
    }

    private void EnsureCollider()
    {
        Transform target = colliderTransform != null ? colliderTransform : transform;
        _collider = target.GetComponent<Collider>();
        if (_collider == null)
        {
            CapsuleCollider capsule = target.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            _collider = capsule;
        }

        if (_collider is CapsuleCollider capsuleCollider)
        {
            capsuleCollider.radius = Mathf.Max(0.05f, colliderRadius);
            capsuleCollider.height = Mathf.Max(capsuleCollider.radius * 2f, colliderHeight);
            capsuleCollider.center = colliderCenter;
        }
    }
#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        if (xrOrigin == null)
        {
            xrOrigin = GetComponent<XROrigin>();
        }
        if (colliderTransform == null && xrOrigin != null)
        {
            colliderTransform = xrOrigin.transform;
        }
    }
#endif

    public Rigidbody GetRigidbody() => _rigidbody;
    public Collider GetCollider() => _collider;
}
