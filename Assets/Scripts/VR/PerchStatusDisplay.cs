using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;

public class PerchStatusDisplay : MonoBehaviour
{
    [Header("References")]
public TeleopPerchTaskController perchController;
    public XROrigin xrOrigin;
    public Text statusText;

    [Header("Messages")]
    public string idleMessage = "Fly to the highlighted perch";
    public string completeMessage = "All perches complete";
    public string failedMessage = "Task failed";
    public string noTargetMessage = "Waiting for next perch";

    private float _holdTimer = 0f;
    private Transform _lastPerch;

    private void Awake()
    {
        if (perchController == null)
            perchController = FindFirstObjectByType<TeleopPerchTaskController>();
        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>();
    }

    private void Update()
    {
        if (statusText == null || perchController == null)
            return;

        if (perchController.TaskFailed)
        {
            statusText.text = failedMessage;
            return;
        }

        if (perchController.TaskComplete)
        {
            statusText.text = completeMessage;
            return;
        }

        Transform currentPerch = perchController.CurrentPerch;
        if (currentPerch == null)
        {
            statusText.text = noTargetMessage;
            _holdTimer = 0f;
            return;
        }

        if (_lastPerch != currentPerch)
        {
            _holdTimer = 0f;
            _lastPerch = currentPerch;
        }

        Vector3 rigPos = xrOrigin != null ? xrOrigin.transform.position : perchController.transform.position;
        float distance = Vector3.Distance(rigPos, currentPerch.position);
        float radius = Mathf.Max(0.01f, perchController.perchRadius);

        if (distance > radius)
        {
            _holdTimer = 0f;
            statusText.text = $"Perch {perchController.CurrentIndex + 1}/{perchController.perchPoints.Length}: {distance:F1}m away";
            return;
        }

        _holdTimer += Time.deltaTime;
        float requiredTime = Mathf.Max(0.01f, perchController.requiredPerchTime);
        float remaining = Mathf.Max(0f, requiredTime - _holdTimer);
        bool ready = _holdTimer >= requiredTime;

        if (perchController.requireLandingConfirmation)
        {
            if (ready)
            {
                statusText.text = $"Perch locked. Press {perchController.landingKey} to confirm.";
            }
            else
            {
                statusText.text = $"Hold steady... {remaining:F1}s";
            }
        }
        else
        {
            statusText.text = ready ? "Perch locked" : $"Hold steady... {remaining:F1}s";
        }
    }
}
