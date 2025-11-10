using UnityEngine;
using Unity.XR.CoreUtils;

public class SimpleVRExperimentController : MonoBehaviour
{
    [Header("References")]
    public XROrigin xrOrigin;

    [Header("Experiment Options")]
    public KeyCode resetKey = KeyCode.R;
    public KeyCode quitKey = KeyCode.Escape;

    private Vector3 _initialOriginPosition;
    private Quaternion _initialOriginRotation;

    private void Start()
    {
        if (xrOrigin == null)
        {
            xrOrigin = FindFirstObjectByType<XROrigin>();
        }

        if (xrOrigin != null)
        {
            _initialOriginPosition = xrOrigin.transform.position;
            _initialOriginRotation = xrOrigin.transform.rotation;
        }
        else
        {
            Debug.LogWarning("SimpleVRExperimentController: No XROrigin found. Basic controls will be disabled.");
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(resetKey))
        {
            ResetRig();
        }

        if (Input.GetKeyDown(quitKey))
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    private void ResetRig()
    {
        if (xrOrigin == null) return;

        xrOrigin.transform.SetPositionAndRotation(_initialOriginPosition, _initialOriginRotation);
        Debug.Log("SimpleVRExperimentController: XR Origin reset to initial pose.");
    }
}