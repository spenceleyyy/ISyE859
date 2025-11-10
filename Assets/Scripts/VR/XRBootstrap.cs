using UnityEngine;
using Unity.XR.CoreUtils;

public class XRBootstrap : MonoBehaviour
{
    [Header("Prefabs")]
    [Tooltip("Assign 'XRI Default Continuous Move' or 'XRI Default Locomotion' prefab here.")]
    public XROrigin xrOriginPrefab;

    private void Awake()
    {
        XROrigin existingOrigin = FindFirstObjectByType<XROrigin>();
        if (existingOrigin != null)
        {
            Debug.Log("XRBootstrap: XROrigin already present, skipping spawn.");
            return;
        }

        if (xrOriginPrefab == null)
        {
            Debug.LogError("XRBootstrap: xrOriginPrefab is not assigned.");
            return;
        }

        XROrigin origin = Instantiate(xrOriginPrefab, Vector3.zero, Quaternion.identity);
        origin.name = "XR Origin (Spawned)";
        Debug.Log("XRBootstrap: Spawned XR Origin at world origin.");
    }
}
