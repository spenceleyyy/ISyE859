using System.Collections.Generic;
using UnityEngine;

public class NextPerchHighlighter : MonoBehaviour
{
    [Header("References")]
    public TeleopPerchTaskController taskController;

    [Header("Highlight Style")]
    public Color highlightColor = new Color(1f, 0.7f, 0.1f, 1f);
    public bool overrideBaseColor = true;
    public bool useEmission = true;
    public float emissionIntensity = 1.5f;

    private readonly Dictionary<Transform, Renderer[]> _perchRenderers = new Dictionary<Transform, Renderer[]>();
    private readonly List<Renderer> _activeRenderers = new List<Renderer>();
    private MaterialPropertyBlock _propBlock;
    private Transform _activePerch;

    private void Awake()
    {
        _propBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        if (taskController == null)
        {
            taskController = FindFirstObjectByType<TeleopPerchTaskController>();
        }

        RefreshRendererCache();
    }

    private void Update()
    {
        if (taskController == null || taskController.TaskComplete || taskController.TaskFailed)
        {
            ClearHighlight();
            return;
        }

        Transform currentTarget = taskController.CurrentPerch;
        if (currentTarget == null)
        {
            ClearHighlight();
            return;
        }

        if (_activePerch == currentTarget)
            return;

        if (!_perchRenderers.ContainsKey(currentTarget))
        {
            RefreshRendererCache();
        }

        ApplyHighlight(currentTarget);
    }

    private void ApplyHighlight(Transform target)
    {
        ClearHighlight();
        _activePerch = target;

        if (!_perchRenderers.TryGetValue(target, out Renderer[] renderers))
            return;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            _propBlock.Clear();

            if (overrideBaseColor)
            {
                _propBlock.SetColor("_Color", highlightColor);
            }

            if (useEmission)
            {
                Color emission = highlightColor * Mathf.Max(0f, emissionIntensity);
                _propBlock.SetColor("_EmissionColor", emission);
            }

            renderer.SetPropertyBlock(_propBlock);
            _activeRenderers.Add(renderer);
        }
    }

    private void ClearHighlight()
    {
        if (_activeRenderers.Count == 0)
            return;

        for (int i = 0; i < _activeRenderers.Count; i++)
        {
            Renderer renderer = _activeRenderers[i];
            if (renderer != null)
            {
                renderer.SetPropertyBlock(null);
            }
        }

        _activeRenderers.Clear();
        _activePerch = null;
    }

    private void RefreshRendererCache()
    {
        _perchRenderers.Clear();
        if (taskController == null || taskController.perchPoints == null)
            return;

        foreach (Transform perch in taskController.perchPoints)
        {
            if (perch == null || _perchRenderers.ContainsKey(perch))
                continue;

            Renderer[] renderers = perch.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                _perchRenderers.Add(perch, renderers);
            }
        }
    }

    private void OnDisable()
    {
        ClearHighlight();
    }

    private void OnValidate()
    {
        if (taskController == null)
        {
            taskController = FindFirstObjectByType<TeleopPerchTaskController>();
        }

        RefreshRendererCache();
    }
}
