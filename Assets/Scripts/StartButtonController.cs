using UnityEngine;
using UnityEngine.SceneManagement;

public class StartButtonController : MonoBehaviour
{
    [SerializeField]
    private string sceneToLoad = "SampleScene";

    [SerializeField]
    private string buttonLabel = "Start";

    [SerializeField]
    private Vector2 buttonSize = new Vector2(220f, 64f);

    [SerializeField]
    private float verticalOffset = 0f;

    private void OnGUI()
    {
        if (!string.IsNullOrWhiteSpace(sceneToLoad))
        {
            var x = (Screen.width - buttonSize.x) * 0.5f;
            var y = (Screen.height - buttonSize.y) * 0.5f + verticalOffset;
            var rect = new Rect(x, y, buttonSize.x, buttonSize.y);

            if (GUI.Button(rect, buttonLabel))
            {
                SceneManager.LoadScene(sceneToLoad);
            }
        }
    }
}
