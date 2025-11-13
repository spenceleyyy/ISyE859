using System.Collections.Generic;
using UnityEngine;

public class PerchZoneVisualizer : MonoBehaviour
{
    [Header("References")]
    public TeleopPerchTaskController taskController;

    [Header("Appearance")]
    public Material lineMaterial;
    public Color inactiveColor = new Color(1f, 1f, 1f, 0.25f);
    public Color activeColor = new Color(0f, 0.8f, 1f, 0.9f);
    public Color completedColor = new Color(0.2f, 1f, 0.4f, 0.6f);
    public float lineWidth = 0.01f;
    public float lineHeight = 0.02f;
    public int segments = 64;

    [Header("Guidance Line")]
    public bool drawGuidanceLine = true;
    public Color guidanceLineColor = new Color(0.1f, 0.8f, 1f, 0.8f);
    public float guidanceLineWidth = 0.015f;
    public float guidanceTargetLift = 0.1f;

    private readonly Dictionary<Transform, LineRenderer> _perchLines = new Dictionary<Transform, LineRenderer>();
    private TeleopPerchTaskController _resolvedController;
    private LineRenderer _guidanceLine;

    private void Awake()
    {
        if (taskController == null)
        {
            taskController = GetComponent<TeleopPerchTaskController>();
        }
    }

    private void Start()
    {
        _resolvedController = taskController != null
            ? taskController
            : FindFirstObjectByType<TeleopPerchTaskController>();

        if (_resolvedController == null)
        {
            Debug.LogWarning("PerchZoneVisualizer: No TeleopPerchTaskController found.");
            enabled = false;
            return;
        }

        BuildLines();
    }

    private void Update()
    {
        if (_resolvedController == null)
            return;

        if (_resolvedController.perchPoints == null || _resolvedController.perchPoints.Length == 0)
            return;

        float radius = Mathf.Max(0.01f, _resolvedController.perchRadius);
        Transform current = _resolvedController.CurrentPerch;
        Transform xrOrigin = _resolvedController.xrOrigin != null
            ? _resolvedController.xrOrigin.transform
            : null;

        for (int i = 0; i < _resolvedController.perchPoints.Length; i++)
        {
            Transform perch = _resolvedController.perchPoints[i];
            if (perch == null)
                continue;

            if (!_perchLines.TryGetValue(perch, out LineRenderer line) || line == null)
                continue;

            UpdateCircle(line, perch.position, radius);

            Color tint = inactiveColor;
            if (_resolvedController.TaskComplete || i < _resolvedController.CurrentIndex)
                tint = completedColor;
            else if (perch == current)
                tint = activeColor;

            ApplyColor(line, tint);
        }

        if (drawGuidanceLine && xrOrigin != null && current != null)
        {
            UpdateGuidanceLine(xrOrigin.position, current.position);
        }
        else if (_guidanceLine != null)
        {
            _guidanceLine.enabled = false;
        }
    }

    private void BuildLines()
    {
        ClearLines();

        if (_resolvedController.perchPoints == null)
            return;

        foreach (Transform perch in _resolvedController.perchPoints)
        {
            if (perch == null)
                continue;

            GameObject lineObj = new GameObject($"PerchZone_{perch.name}");
            lineObj.transform.SetParent(perch, false);
            lineObj.transform.localPosition = Vector3.zero;

            LineRenderer line = lineObj.AddComponent<LineRenderer>();
            line.loop = false;
            line.useWorldSpace = true;
            line.positionCount = segments + 1;
            line.widthMultiplier = Mathf.Max(0.0005f, lineWidth);

            if (lineMaterial != null)
            {
                line.material = lineMaterial;
            }
            else
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                    line.material = new Material(shader);
            }

            _perchLines.Add(perch, line);
        }

        if (drawGuidanceLine)
        {
            if (_guidanceLine == null)
            {
                _guidanceLine = CreateLineRenderer("PerchGuidanceLine");
            }

            ApplyColor(_guidanceLine, guidanceLineColor);
            _guidanceLine.widthMultiplier = Mathf.Max(0.0005f, guidanceLineWidth);
            _guidanceLine.positionCount = 0;
            _guidanceLine.enabled = false;
        }
    }

    private void UpdateCircle(LineRenderer line, Vector3 center, float radius)
    {
        float height = Mathf.Max(0f, lineHeight);
        for (int i = 0; i <= segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            line.SetPosition(i, new Vector3(center.x + x, center.y + height, center.z + z));
        }
    }

    private void UpdateGuidanceLine(Vector3 originPosition, Vector3 targetPosition)
    {
        if (_guidanceLine == null)
            _guidanceLine = CreateLineRenderer("PerchGuidanceLine");

        Vector3 liftedTarget = targetPosition + Vector3.up * Mathf.Max(0f, guidanceTargetLift);

        _guidanceLine.enabled = true;
        _guidanceLine.positionCount = 2;
        _guidanceLine.SetPosition(0, originPosition);
        _guidanceLine.SetPosition(1, liftedTarget);

        ApplyColor(_guidanceLine, guidanceLineColor);
    }

    private LineRenderer CreateLineRenderer(string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(transform, false);
        LineRenderer line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = false;
        line.material = lineMaterial != null ? new Material(lineMaterial) : new Material(Shader.Find("Sprites/Default"));
        return line;
    }

    private void ApplyColor(LineRenderer line, Color color)
    {
        if (line == null)
            return;

        line.startColor = color;
        line.endColor = color;

        if (line.material != null)
        {
            line.material.color = color;
        }
    }

    private void ClearLines()
    {
        if (_guidanceLine != null)
        {
            Destroy(_guidanceLine.gameObject);
            _guidanceLine = null;
        }

        foreach (var kvp in _perchLines)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value.gameObject);
            }
        }

        _perchLines.Clear();
    }

    private void OnDestroy()
    {
        ClearLines();
    }
}
