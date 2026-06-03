using UnityEngine;

// IMGUI overlay that briefly shows the mag-check estimate. The Harmony patch calls Show(...)
// when the player checks their mag; OnGUI renders it until it expires.
public class MagCheckBehaviour : MonoBehaviour
{
    public MagCheckBehaviour(System.IntPtr ptr) : base(ptr) { }

    private static string _text;
    private static float _hideAt;

    public static void Show(string text, float seconds = 3f)
    {
        _text = text;
        _hideAt = Time.time + seconds;
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(_text) || Time.time > _hideAt) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            normal = { textColor = Color.yellow },
            alignment = TextAnchor.MiddleCenter
        };

        const float w = 360f;
        float x = Screen.width / 2f - w / 2f;
        float y = Screen.height - 110f;
        GUI.Label(new Rect(x, y, w, 30f), _text, style);
    }
}
