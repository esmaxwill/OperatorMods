using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using OPERATOR.Common;

public class BinocularsBehaviour : MonoBehaviour
{
    public BinocularsBehaviour(System.IntPtr ptr) : base(ptr) { }

    private static KeyCode TOGGLE_KEY    => BinocularsPlugin.ToggleKey.Value;
    private static KeyCode ZOOM_IN_KEY   => BinocularsPlugin.ZoomInKey.Value;
    private static KeyCode ZOOM_IN_KEY2  => BinocularsPlugin.ZoomInKey2.Value;
    private static KeyCode ZOOM_OUT_KEY  => BinocularsPlugin.ZoomOutKey.Value;
    private static KeyCode ZOOM_OUT_KEY2 => BinocularsPlugin.ZoomOutKey2.Value;

    private static float FOV_MIN     => BinocularsPlugin.FovMin.Value;
    private static float FOV_MAX     => BinocularsPlugin.FovMax.Value;
    private static float FOV_STEP    => BinocularsPlugin.FovStep.Value;
    private static float FOV_DEFAULT => BinocularsPlugin.FovDefault.Value;
    private static float MAX_RANGE   => BinocularsPlugin.MaxRange.Value;

    private const float FOV_SPEED     = 8f;
    private const float FOV_REFERENCE = 60f;

    // Enemy visual meshes are always on layer 7 (set by CharacterCustomisation.Start).
    // The game's Outline custom pass (CustomPass_SG/Outline) renders layer 7 for ECOTI.
    private const int   ECOTI_LAYER        = 7;
    private const float HIGHLIGHT_INTERVAL = 0.1f;
    private const float BODY_EXTENT        = 1.75f;

    private bool       _active     = false;
    private float      _currentFov = FOV_DEFAULT;
    private float      _targetFov  = FOV_DEFAULT;
    private float      _distance   = -1f;
    private Texture2D  _vignetteTex;
    private GUIStyle   _rangeStyle;
    private GUIStyle   _zoomStyle;

    // OnGUI runs several times per frame; cache the readout strings (rebuilt once per
    // frame in Update) so OnGUI does no per-pass string.Format allocation.
    private string _rangeText = "Range: ----";
    private string _zoomText  = "";

    private Camera           _binoCam;
    private GameObject       _binoCamGO;
    private Camera           _savedGameCam;
    private PlayerNetworking _lastPlayer;

    // HDRP requires a CustomPassVolume targeting the camera to register it for
    // per-camera effects. Without this, the camera renders nothing.
    private GameObject _passVolGO;
    private CustomPassVolume _passVol;   // cached component on _passVolGO (avoids per-tick GetComponent)
    private Material   _outlineMat;
    private float      _highlightTimer;
    private int        _lastEcotiState = -1;   // -1 unknown, 0 off, 1 on; for transition-only logging
    private nvgController _cachedNvg;

    // Membership sets queried every suppress tick — HashSet gives O(1) Contains instead of O(n) List scan.
    private readonly HashSet<CustomPassVolume> _volumesAtActivation = new HashSet<CustomPassVolume>();
    private readonly HashSet<CustomPassVolume> _suppressedVolumes   = new HashSet<CustomPassVolume>();
    private readonly List<CustomPassVolume>    _redirectedVolumes   = new List<CustomPassVolume>();

    private WeaponV3 _lastWeapon;
    private readonly List<Renderer> _hiddenWeaponRenderers = new List<Renderer>();
    private readonly List<Renderer> _hiddenPlayerRenderers = new List<Renderer>();

    private LookWithMouse _playerLook;
    private float         _savedMouseSens;

    private void Update()
    {
        var myPN = GameManager.myPlayerNetworking;

        if (_active && (myPN == null || myPN.isADS))
        {
            Deactivate();
            return;
        }

        if (Input.GetKeyDown(TOGGLE_KEY))
        {
            if (_active) Deactivate();
            else         Activate();
        }

        if (!_active) return;

        if (Input.GetKeyDown(ZOOM_IN_KEY) || Input.GetKeyDown(ZOOM_IN_KEY2))
            _targetFov = Mathf.Max(FOV_MIN, _targetFov - FOV_STEP);
        if (Input.GetKeyDown(ZOOM_OUT_KEY) || Input.GetKeyDown(ZOOM_OUT_KEY2))
            _targetFov = Mathf.Min(FOV_MAX, _targetFov + FOV_STEP);

        if (_binoCam == null) { Deactivate(); return; }

        _currentFov = Mathf.MoveTowards(_currentFov, _targetFov, FOV_SPEED * Time.deltaTime);
        _binoCam.fieldOfView = _currentFov;
        _zoomText = string.Format("FOV: {0:F1}°", _currentFov);

        if (_playerLook != null)
        {
            float zoomTan = Mathf.Tan(_currentFov   * 0.5f * Mathf.Deg2Rad);
            float refTan  = Mathf.Tan(FOV_REFERENCE * 0.5f * Mathf.Deg2Rad);
            _playerLook.mouseSensitivity = _savedMouseSens * (zoomTan / refTan);
        }

        if (myPN != _lastPlayer)
        {
            _lastPlayer = myPN;
            _cachedNvg  = null;
            HidePlayerRenderers(myPN);
        }

        var currentWeapon = myPN.c_activeWeapon;
        if (currentWeapon != _lastWeapon)
        {
            _lastWeapon = currentWeapon;
            HideWeaponRenderers(currentWeapon);
        }

        _highlightTimer -= Time.deltaTime;
        if (_highlightTimer <= 0f)
        {
            _highlightTimer = HIGHLIGHT_INTERVAL;
            UpdateEcotiHighlight();
            SuppressNewGlobalVolumes();
        }

        _distance = Rangefinder.GetDistance(_binoCam, MAX_RANGE);
        _rangeText = _distance >= 0f ? string.Format("Range: {0:F0} m", _distance) : "Range: ----";
    }

    private void HideWeaponRenderers(WeaponV3 weapon)
    {
        RestoreWeaponRenderers();
        if (weapon == null) return;

        var renderers = weapon.GetComponentsInChildren<Renderer>(true);
        if (renderers == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled) continue;
            _hiddenWeaponRenderers.Add(r);
            r.enabled = false;
        }
    }

    private void RestoreWeaponRenderers()
    {
        for (int i = 0; i < _hiddenWeaponRenderers.Count; i++)
        {
            var r = _hiddenWeaponRenderers[i];
            if (r != null) r.enabled = true;
        }
        _hiddenWeaponRenderers.Clear();
    }

    private void HidePlayerRenderers(PlayerNetworking myPN)
    {
        RestorePlayerRenderers();
        if (myPN?.gameObject == null) return;

        var renderers = myPN.gameObject.GetComponentsInChildren<Renderer>(true);
        if (renderers == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled) continue;
            _hiddenPlayerRenderers.Add(r);
            r.enabled = false;
        }
    }

    private void RestorePlayerRenderers()
    {
        for (int i = 0; i < _hiddenPlayerRenderers.Count; i++)
        {
            var r = _hiddenPlayerRenderers[i];
            if (r != null) r.enabled = true;
        }
        _hiddenPlayerRenderers.Clear();
    }

    private void Activate()
    {
        var gameCam = MainCameraSingleton.instance?.GameCamera;
        if (gameCam == null)
        {
            BinocularsPlugin.Logger.LogWarning("Binoculars: no GameCamera available.");
            return;
        }
        BinocularsPlugin.Logger.LogInfo(string.Format(
            "Binoculars: GameCamera found — name={0} enabled={1} depth={2} cullingMask={3:X} targetTexture={4}",
            gameCam.name, gameCam.enabled, gameCam.depth, gameCam.cullingMask,
            gameCam.targetTexture != null ? gameCam.targetTexture.name : "null"));

        _savedGameCam = gameCam;
        _currentFov   = FOV_DEFAULT;
        _targetFov    = FOV_DEFAULT;

        _binoCamGO = new GameObject("OPERATOR_BinocularsCam");
        _binoCam   = _binoCamGO.AddComponent<Camera>();

        _binoCam.CopyFrom(gameCam);
        _binoCam.fieldOfView = _currentFov;
        _binoCam.depth       = gameCam.depth + 100f;

        var origHd = gameCam.GetComponent<HDAdditionalCameraData>();
        var ourHd  = _binoCamGO.AddComponent<HDAdditionalCameraData>();
        if (origHd != null)
        {
            origHd.CopyTo(ourHd);
            BinocularsPlugin.Logger.LogInfo("Binoculars: HDAdditionalCameraData copied.");
        }
        else
        {
            BinocularsPlugin.Logger.LogWarning("Binoculars: GameCamera has no HDAdditionalCameraData.");
        }

        _binoCamGO.transform.SetParent(gameCam.transform, false);
        _binoCamGO.transform.localPosition = Vector3.zero;
        _binoCamGO.transform.localRotation = Quaternion.identity;

        gameCam.enabled = false;
        BinocularsPlugin.Logger.LogInfo(string.Format(
            "Binoculars: GameCamera disabled. BinoCam cullingMask={0:X} depth={1} targetTexture={2}",
            _binoCam.cullingMask, _binoCam.depth,
            _binoCam.targetTexture != null ? _binoCam.targetTexture.name : "null"));

        // Log all CustomPassVolumes in the scene so we can see what NVG is doing.
        var allVols = Object.FindObjectsOfType<CustomPassVolume>();
        BinocularsPlugin.Logger.LogInfo(string.Format("Binoculars: {0} CustomPassVolumes in scene:", allVols.Length));
        for (int i = 0; i < allVols.Length; i++)
        {
            var v = allVols[i];
            if (v == null) continue;
            BinocularsPlugin.Logger.LogInfo(string.Format(
                "  [{0}] '{1}' enabled={2} isGlobal={3} useTargetCam={4} targetCam={5} passes={6} injectionPoint={7}",
                i, v.gameObject.name, v.enabled, v.isGlobal, v.useTargetCamera,
                v.useTargetCamera && v.targetCamera != null ? v.targetCamera.name : "none",
                v.customPasses.Count, v.injectionPoint));
        }

        _lastPlayer = GameManager.myPlayerNetworking;
        BinocularsPlugin.Logger.LogInfo(string.Format(
            "Binoculars: myPlayerNetworking={0}", _lastPlayer != null ? "valid" : "NULL"));
        HidePlayerRenderers(_lastPlayer);

        _lastWeapon = _lastPlayer?.c_activeWeapon;
        HideWeaponRenderers(_lastWeapon);

        _playerLook = _lastPlayer?.gameObject?.GetComponentInChildren<LookWithMouse>(true);
        if (_playerLook != null)
            _savedMouseSens = _playerLook.mouseSensitivity;

        var outlineShader = Shader.Find("CustomPass_SG/Outline");
        BinocularsPlugin.Logger.LogInfo(string.Format(
            "Binoculars: CustomPass_SG/Outline shader {0}", outlineShader != null ? "FOUND" : "NOT FOUND — falling back to HDRP/Unlit"));
        if (outlineShader == null)
            outlineShader = Shader.Find("HDRP/Unlit");

        _outlineMat = outlineShader != null ? new Material(outlineShader) : null;
        if (_outlineMat != null)
            _outlineMat.color = Color.white;

        _passVolGO          = new GameObject("OPERATOR_BinoPassVol");
        var vol             = _passVolGO.AddComponent<CustomPassVolume>();
        _passVol            = vol;
        vol.isGlobal        = false;
        vol.injectionPoint  = CustomPassInjectionPoint.BeforeTransparent;
        vol.useTargetCamera = true;
        vol.targetCamera    = _binoCam;

        var pass = new DrawRenderersCustomPass();
        pass.layerMask                 = 1 << ECOTI_LAYER;
        pass.overrideMode              = DrawRenderersCustomPass.OverrideMaterialMode.Material;
        pass.overrideMaterial          = _outlineMat;
        pass.overrideMaterialPassIndex = 0;
        pass.overrideDepthState        = false;
        vol.customPasses.Add(pass);

        // Snapshot which volumes exist now so SuppressNewGlobalVolumes can detect volumes
        // created later (e.g. NVG PASS when player activates night vision mid-session).
        _volumesAtActivation.Clear();
        var existingVols = Object.FindObjectsOfType<CustomPassVolume>();
        for (int i = 0; i < existingVols.Length; i++)
            if (existingVols[i] != null) _volumesAtActivation.Add(existingVols[i]);

        bool ecoti = HasEcotiEquipped();
        BinocularsPlugin.Logger.LogInfo(string.Format(
            "Binoculars: ECOTI={0} volumesAtActivation={1}", ecoti, _volumesAtActivation.Count));

        _highlightTimer = 0f;

        _active = true;
        BinocularsPlugin.Logger.LogInfo("Binoculars activated.");
    }

    private void Deactivate()
    {
        RestoreWeaponRenderers();
        RestorePlayerRenderers();

        if (_playerLook != null)
            _playerLook.mouseSensitivity = _savedMouseSens;

        RestoreSuppressedVolumes();
        if (_passVolGO != null) { Object.Destroy(_passVolGO); _passVolGO = null; }
        _passVol = null;
        _lastEcotiState = -1;
        if (_outlineMat != null) { Object.Destroy(_outlineMat); _outlineMat = null; }

        if (_savedGameCam != null) _savedGameCam.enabled = true;
        if (_binoCamGO   != null) Object.Destroy(_binoCamGO);

        _binoCam      = null;
        _binoCamGO    = null;
        _savedGameCam = null;
        _lastPlayer   = null;
        _lastWeapon   = null;
        _playerLook   = null;
        _cachedNvg    = null;
        _active       = false;
        _distance     = -1f;
        if (_vignetteTex != null) { Object.Destroy(_vignetteTex); _vignetteTex = null; }
        _rangeStyle = null;
        _zoomStyle  = null;
        BinocularsPlugin.Logger.LogInfo("Binoculars deactivated.");
    }

    private void OnDestroy()
    {
        if (_active) Deactivate();
    }

    private void UpdateEcotiHighlight()
    {
        if (_passVol == null || _passVol.customPasses.Count == 0) return;
        bool ecoti = HasEcotiEquipped();
        _passVol.customPasses[0].enabled = ecoti;

        // Log only on transition, not every 0.1s tick (this ran 10x/sec while active).
        int state = ecoti ? 1 : 0;
        if (state != _lastEcotiState)
        {
            _lastEcotiState = state;
            BinocularsPlugin.Logger.LogInfo(string.Format("Binoculars: highlight pass enabled={0}", ecoti));
        }
    }

    // Suppress only NVG PASS volumes that appear after activation — they need per-camera
    // history buffers not present on our new camera and cause a solid black screen.
    // Highlight_CustomPass (ECOTI outline) and everything else is allowed through.
    private void SuppressNewGlobalVolumes()
    {
        var vols = Object.FindObjectsOfType<CustomPassVolume>();
        for (int i = 0; i < vols.Length; i++)
        {
            var v = vols[i];
            if (v == null || !v.enabled) continue;
            if (_volumesAtActivation.Contains(v)) continue;
            if (_suppressedVolumes.Contains(v)) continue;
            if (v == _passVol) continue;

            var name = v.gameObject.name;
            if (name.Contains("NVG") || name.Contains("nvg"))
            {
                _suppressedVolumes.Add(v);
                v.enabled = false;
                BinocularsPlugin.Logger.LogInfo(string.Format(
                    "Binoculars: suppressed NVG volume '{0}'", name));
            }
            else
            {
                // New non-NVG volume (e.g. Highlight_CustomPass) — log its properties.
                _volumesAtActivation.Add(v);
                BinocularsPlugin.Logger.LogInfo(string.Format(
                    "Binoculars: new volume '{0}' isGlobal={1} useTargetCam={2} targetCam={3} passes={4} injectionPoint={5}",
                    name, v.isGlobal, v.useTargetCamera,
                    v.useTargetCamera && v.targetCamera != null ? v.targetCamera.name : "none",
                    v.customPasses.Count, v.injectionPoint));

                // If it targets the now-disabled game camera, redirect it to ours.
                if (v.useTargetCamera && v.targetCamera == _savedGameCam)
                {
                    v.targetCamera = _binoCam;
                    _redirectedVolumes.Add(v);
                    BinocularsPlugin.Logger.LogInfo(string.Format(
                        "Binoculars: redirected '{0}' from GameCamera to BinoCam", name));
                }
            }
        }
    }

    private void RestoreSuppressedVolumes()
    {
        foreach (var v in _suppressedVolumes)
            if (v != null) v.enabled = true;
        for (int i = 0; i < _redirectedVolumes.Count; i++)
        {
            var v = _redirectedVolumes[i];
            if (v != null) v.targetCamera = _savedGameCam;
        }
        _suppressedVolumes.Clear();
        _redirectedVolumes.Clear();
        _volumesAtActivation.Clear();
    }

    private bool HasEcotiEquipped()
    {
        if (_cachedNvg != null) return true;
        var myPN = GameManager.myPlayerNetworking;
        if (myPN == null || myPN.gameObject == null) return false;
        _cachedNvg = myPN.gameObject.GetComponentInChildren<nvgController>(true);
        return _cachedNvg != null;
    }

    private void OnGUI()
    {
        if (!_active) return;

        if (_rangeStyle == null)
        {
            _rangeStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.UpperLeft };
            _rangeStyle.normal.textColor = Color.yellow;
            _zoomStyle  = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            _zoomStyle.normal.textColor  = Color.yellow;
        }

        if (_vignetteTex == null) BuildVignetteTexture();

        DrawVignette();
        DrawReticle();
        DrawRangeReadout();
        DrawZoomIndicator();
    }

    private void DrawVignette()
    {
        float side = Mathf.Min(Screen.width, Screen.height);
        float boxX = (Screen.width  - side) * 0.5f;
        float boxY = (Screen.height - side) * 0.5f;

        GUI.DrawTexture(new Rect(boxX, boxY, side, side), _vignetteTex);

        var prev = GUI.color;
        GUI.color = Color.black;
        var white = Texture2D.whiteTexture;
        GUI.DrawTexture(new Rect(0,           0,                Screen.width,                       boxY),                              white);
        GUI.DrawTexture(new Rect(0,           boxY + side,      Screen.width,                       Screen.height - (boxY + side)),     white);
        GUI.DrawTexture(new Rect(0,           boxY,             boxX,                               side),                              white);
        GUI.DrawTexture(new Rect(boxX + side, boxY,             Screen.width - (boxX + side),       side),                              white);
        GUI.color = prev;
    }

    private void DrawReticle()
    {
        float side    = Mathf.Min(Screen.width, Screen.height);
        float circleR = side * 0.45f;
        float armLen  = circleR * 0.15f;
        float cx      = Screen.width  * 0.5f;
        float cy      = Screen.height * 0.5f;

        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.9f);
        var white = Texture2D.whiteTexture;

        GUI.DrawTexture(new Rect(cx - armLen, cy - 0.5f,  armLen * 2f, 1f), white);
        GUI.DrawTexture(new Rect(cx - 0.5f,   cy - armLen, 1f, armLen * 2f), white);
        GUI.DrawTexture(new Rect(cx - 1f,     cy - 1f,    2f, 2f), white);

        float tickSpacing = armLen / 5f;
        float tickLen     = 3f;
        for (int i = 1; i <= 4; i++)
        {
            float off = i * tickSpacing;
            GUI.DrawTexture(new Rect(cx + off - 0.5f, cy - tickLen * 0.5f, 1f, tickLen), white);
            GUI.DrawTexture(new Rect(cx - off - 0.5f, cy - tickLen * 0.5f, 1f, tickLen), white);
            GUI.DrawTexture(new Rect(cx - tickLen * 0.5f, cy + off - 0.5f, tickLen, 1f), white);
            GUI.DrawTexture(new Rect(cx - tickLen * 0.5f, cy - off - 0.5f, tickLen, 1f), white);
        }

        GUI.color = prev;
    }

    private void DrawRangeReadout()
    {
        float side   = Mathf.Min(Screen.width, Screen.height);
        float offset = side * 0.08f;
        float cx     = Screen.width  * 0.5f + offset;
        float cy     = Screen.height * 0.5f + offset;
        GUI.Label(new Rect(cx, cy, 220f, 30f), _rangeText, _rangeStyle);
    }

    private void DrawZoomIndicator()
    {
        GUI.Label(new Rect(20f, 20f, 200f, 24f), _zoomText, _zoomStyle);
    }

    private void BuildVignetteTexture()
    {
        const int   size   = 512;
        const float center = 255.5f;
        const float radius = 256f;
        const float fadeLo = 0.98f;
        const float fadeHi = 1.02f;

        _vignetteTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _vignetteTex.wrapMode   = TextureWrapMode.Clamp;
        _vignetteTex.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float d  = Mathf.Sqrt(dx * dx + dy * dy) / radius;

                float a;
                if      (d <= fadeLo) a = 0f;
                else if (d >= fadeHi) a = 1f;
                else                  a = (d - fadeLo) / (fadeHi - fadeLo);

                _vignetteTex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
            }
        }
        _vignetteTex.Apply(false, false);
    }
}
