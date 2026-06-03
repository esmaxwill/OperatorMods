using UnityEngine;

namespace OPERATOR.UsableVlite
{
  public class PartyLight : MonoBehaviour
  {
    private Light light = null;
    private float _timer;
    public PartyLight(System.IntPtr ptr) : base(ptr) { }
    private const float COLOR_SWITCH_DELAY = 0.03f;
    public void Update()
    {
      if (!light || !light.enabled) return;

      _timer += Time.deltaTime;
      if (_timer < COLOR_SWITCH_DELAY) return;

      _timer -= COLOR_SWITCH_DELAY;
      light.color = GetRandomColor();
    }

    public static Color GetRandomColor()
    {
      return Random.ColorHSV(0f, 1f, 1f, 1f, 1f, 1f);
    }

    public void Attach()
    {
      light = gameObject.AddComponent<Light>();
      light.type = LightType.Point;
      light.intensity = 0.1f;
      light.range = 0.1f;
      light.shadows = LightShadows.None;
      light.color = GetRandomColor();
    }
  }
}
