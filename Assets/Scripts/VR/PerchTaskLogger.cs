using System.IO;
using UnityEngine;

public class PerchTaskLogger : MonoBehaviour
{
    [Header("Logging")]
    public string fileNamePrefix = "perch_log_";

    private string _filePath;
    private bool _headerWritten = false;

    private void Start()
    {
        // One file per run, timestamped
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = fileNamePrefix + timestamp + ".csv";
        _filePath = Path.Combine(Application.persistentDataPath, fileName);

        WriteHeaderIfNeeded();
        Debug.Log("PerchTaskLogger: logging to " + _filePath);
    }

    private void WriteHeaderIfNeeded()
    {
        if (_headerWritten) return;

        using (var writer = new StreamWriter(_filePath, false))
        {
            writer.WriteLine("Timestamp,EventType,PerchIndex,RigPosX,RigPosY,RigPosZ,ExtraData");
        }

        _headerWritten = true;
    }

    public void LogEvent(string eventType, int perchIndex, Vector3 rigPosition, string extraData = "")
    {
        if (string.IsNullOrEmpty(_filePath))
            return;

        if (!_headerWritten)
            WriteHeaderIfNeeded();

        string timestamp = System.DateTime.Now.ToString("o"); // ISO 8601
        string sanitized = string.IsNullOrEmpty(extraData) ? string.Empty : extraData.Replace(',', ';');

        string line = string.Format(
            "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6}",
            timestamp,
            eventType,
            perchIndex,
            rigPosition.x,
            rigPosition.y,
            rigPosition.z,
            sanitized
        );

        using (var writer = new StreamWriter(_filePath, true))
        {
            writer.WriteLine(line);
        }
    }
}
