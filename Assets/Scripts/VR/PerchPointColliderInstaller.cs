using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Utility that guarantees every perch point has the needed colliders for docking cues
/// (solid hit box plus optional trigger volume). Attach alongside TeleopPerchTaskController
/// and press the context button or let it run automatically on Start.
/// </summary>
public class PerchPointColliderInstaller : MonoBehaviour
{
    [Header("References")]
    public TeleopPerchTaskController taskController;

    [Header("Behavior")]
    public bool runOnStart = true;
    public bool ensureBlockingCollider = true;
    public bool ensureTriggerCollider = true;

    [Header("Blocking Collider (non-trigger)")]
    public Vector3 blockingBoxSize = new Vector3(0.4f, 0.4f, 0.4f);
    public Vector3 blockingCenterOffset = Vector3.zero;

    [Header("Trigger Collider (optional)")]
    public float triggerRadiusScale = 1f;

    private void Reset()
    {
        taskController = GetComponent<TeleopPerchTaskController>();
    }

    private void Start()
    {
        if (runOnStart)
        {
            EnsurePerchColliders();
        }
    }

    [ContextMenu("Ensure Perch Colliders")]
    public void EnsurePerchColliders()
    {
        if (taskController == null)
        {
            taskController = FindFirstObjectByType<TeleopPerchTaskController>();
        }

        if (taskController == null || taskController.perchPoints == null)
        {
            Debug.LogWarning("PerchPointColliderInstaller: No TeleopPerchTaskController or perch points found.");
            return;
        }

        foreach (Transform perch in taskController.perchPoints)
        {
            if (perch == null) continue;

            BoxCollider blocking = null;
            SphereCollider trigger = null;

            Collider[] colliders = perch.GetComponents<Collider>();
            foreach (Collider col in colliders)
            {
                if (col == null) continue;

                if (!col.isTrigger && col is BoxCollider box && blocking == null)
                {
                    blocking = box;
                }
                else if (col.isTrigger && col is SphereCollider sphere && trigger == null)
                {
                    trigger = sphere;
                }
            }

            if (ensureBlockingCollider)
            {
                if (blocking == null)
                {
                    blocking = perch.gameObject.AddComponent<BoxCollider>();
                }

                blocking.isTrigger = false;
                blocking.size = blockingBoxSize;
                blocking.center = blockingCenterOffset;
            }

            if (ensureTriggerCollider)
            {
                float targetRadius = Mathf.Max(0.01f, taskController.perchRadius * Mathf.Max(0.01f, triggerRadiusScale));
                if (trigger == null)
                {
                    trigger = perch.gameObject.AddComponent<SphereCollider>();
                }

                trigger.isTrigger = true;
                trigger.radius = targetRadius;
                trigger.center = Vector3.zero;
            }
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(PerchPointColliderInstaller))]
public class PerchPointColliderInstallerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var installer = (PerchPointColliderInstaller)target;
        if (GUILayout.Button("Add/Update Colliders Now"))
        {
            installer.EnsurePerchColliders();
        }
    }
}
#endif
