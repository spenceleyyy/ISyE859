using UnityEngine;
using Unity.XR.CoreUtils;

public class TeleopPerchTaskController : MonoBehaviour
{
    [Header("References")]
    public XROrigin xrOrigin;
    public Transform[] perchPoints;
    public PerchTaskLogger logger;

    [Header("Perch Parameters")]
    [Tooltip("Distance within which the participant counts as 'at' the perch point.")]
    public float perchRadius = 0.5f;

    [Tooltip("Seconds the rig must stay within radius to complete a perch.")]
    public float requiredPerchTime = 2f;

    private int _currentIndex = 0;
    private float _timeInside = 0f;
    private bool _taskComplete = false;

    public Transform CurrentPerch
    {
        get
        {
            if (_taskComplete) return null;
            if (perchPoints == null || perchPoints.Length == 0) return null;
            if (_currentIndex < 0 || _currentIndex >= perchPoints.Length) return null;
            return perchPoints[_currentIndex];
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
    }

    private void Update()
    {
        if (_taskComplete) return;
        if (xrOrigin == null) return;
        if (perchPoints == null || perchPoints.Length == 0) return;
        if (_currentIndex >= perchPoints.Length) return;

        Transform currentPerch = perchPoints[_currentIndex];

        Vector3 rigPos = xrOrigin.transform.position;
        float distance = Vector3.Distance(rigPos, currentPerch.position);

        if (distance <= perchRadius)
        {
            _timeInside += Time.deltaTime;

            if (_timeInside >= requiredPerchTime)
            {
                logger?.LogEvent("PerchComplete", _currentIndex, rigPos);
                Debug.Log($"Perch {_currentIndex + 1} complete.");
                _currentIndex++;
                _timeInside = 0f;

                if (_currentIndex >= perchPoints.Length)
                {
                    _taskComplete = true;
                    logger?.LogEvent("TaskComplete", _currentIndex - 1, rigPos);
                    Debug.Log("All perch points completed. Task finished.");
                }
            }
        }
        else
        {
            _timeInside = 0f;
        }
    }
}