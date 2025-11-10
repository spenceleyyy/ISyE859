using UnityEngine;

public class NextPerchIndicator : MonoBehaviour
{
    [Header("References")]
    public TeleopPerchTaskController taskController;
    public Transform xrCamera;

    [Header("Display Settings")]
    public float hudDistance = 0.6f;           // meters in front of camera
    public Vector3 hudOffset = new Vector3(0f, -0.2f, 0f);

    private void Start()
    {
        if (xrCamera == null && Camera.main != null)
            xrCamera = Camera.main.transform;

        if (taskController == null)
            taskController = FindFirstObjectByType<TeleopPerchTaskController>();
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
    }
}