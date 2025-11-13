using UnityEngine;
using UnityEngine.UI;

public class NextPerchIndicator : MonoBehaviour
{
    [Header("References")]
    public TeleopPerchTaskController taskController;
    public Transform xrCamera;
    public Renderer guidanceRenderer;
    public Graphic guidanceGraphic;
    public Text infoText;

    [Header("Display Settings")]
    public float hudDistance = 0.6f;           // meters in front of camera
    public Vector3 hudOffset = new Vector3(0f, -0.2f, 0f);
    [Tooltip("Displayed text format for distance. Uses string.Format with meters value.")]
    public string infoFormat = "{0:F1} m";

    [Header("Guidance Feedback")]
    public Color alignedColor = new Color(0.1f, 0.8f, 0.4f);
    public Color misalignedColor = new Color(0.9f, 0.2f, 0.2f);

    public float colorAlignmentSmoothing = 0.2f;

    private float _currentAlignment = 0f;
    private MaterialPropertyBlock _colorBlock;

    private void Awake()
    {
        if (_colorBlock == null)
        {
            _colorBlock = new MaterialPropertyBlock();
        }
    }

    private void Start()
    {
        if (xrCamera == null && Camera.main != null)
            xrCamera = Camera.main.transform;

        if (taskController == null)
            taskController = FindFirstObjectByType<TeleopPerchTaskController>();

        if (guidanceRenderer == null)
            guidanceRenderer = GetComponentInChildren<Renderer>();
    }

    private void LateUpdate()
    {
        if (xrCamera == null || taskController == null)
            return;

        Transform target = taskController.CurrentPerch;
        if (target == null)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        // Direction from camera to current perch
        Vector3 toTargetWorld = (target.position - xrCamera.position).normalized;
        Vector3 toTargetLocal = xrCamera.InverseTransformDirection(toTargetWorld);

        if (toTargetLocal.z < 0.1f)
            toTargetLocal.z = 0.1f;

        Vector3 localPos = toTargetLocal.normalized * hudDistance + hudOffset;
        transform.position = xrCamera.TransformPoint(localPos);

        Vector3 worldForward = xrCamera.TransformDirection(toTargetLocal);
        transform.rotation = Quaternion.LookRotation(worldForward, xrCamera.up);

        float distanceToTarget = Vector3.Distance(xrCamera.position, target.position);
        UpdateGuidanceFeedback(toTargetWorld, distanceToTarget);
    }

    private void UpdateGuidanceFeedback(Vector3 toTargetWorld, float distance)
    {
        Vector3 heading = xrCamera.forward;
        if (heading.sqrMagnitude < 0.0001f)
            heading = xrCamera.TransformDirection(Vector3.forward);

        float targetAlignment = Vector3.Dot(heading.normalized, toTargetWorld.normalized);
        float lerpFactor = colorAlignmentSmoothing <= 0f
            ? 1f
            : 1f - Mathf.Exp(-colorAlignmentSmoothing * Time.deltaTime);
        _currentAlignment = Mathf.Lerp(_currentAlignment, targetAlignment, lerpFactor);

        Color blendedColor = Color.Lerp(misalignedColor, alignedColor, Mathf.InverseLerp(-1f, 1f, _currentAlignment));
        ApplyColor(blendedColor);

        if (infoText != null)
        {
            infoText.text = string.Format(infoFormat, distance);
        }
    }

    private void ApplyColor(Color color)
    {
        if (_colorBlock == null)
            _colorBlock = new MaterialPropertyBlock();

        if (guidanceRenderer != null)
        {
            guidanceRenderer.GetPropertyBlock(_colorBlock);
            _colorBlock.SetColor("_Color", color);
            guidanceRenderer.SetPropertyBlock(_colorBlock);
        }

        if (guidanceGraphic != null)
        {
            guidanceGraphic.color = color;
        }
    }
}
