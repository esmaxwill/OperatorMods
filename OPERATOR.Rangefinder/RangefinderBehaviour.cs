using UnityEngine;

public class RangefinderBehaviour : MonoBehaviour
{
    public RangefinderBehaviour(System.IntPtr ptr) : base(ptr) { }

    private float _distance    = -1f;
    private float _currentZero = float.NaN;
    private bool  _inScope     = false;

    // OnGUI runs several times per frame; cache the style and the formatted readout
    // strings (rebuilt once per frame in Update) so OnGUI allocates nothing.
    private GUIStyle _style;
    private string   _rangeText = "";
    private string   _zeroText  = "";
    // Last displayed integers, so BuildReadout re-formats (allocates) only when they actually change.
    private int  _shownDist = int.MinValue;
    private int  _shownZero = int.MinValue;
    private bool _shownNaN  = true;

    // The expensive part is GetComponentsInChildren (native hierarchy walk + array alloc), not the
    // per-element field reads. A weapon's ADS/scope components (incl. canted & flip sights) all exist
    // at once; switching optics only flips which one's IsInADS is true. So scan the hierarchy ONCE per
    // weapon into managed lists, then each frame iterate the cached list and pick the active optic —
    // cheap, allocation-free, and still tracks canted/flip-sight switches on the same weapon.
    private WeaponV3 _scanWeapon;
    private readonly System.Collections.Generic.List<ADS> _adsCache = new System.Collections.Generic.List<ADS>();
    private readonly System.Collections.Generic.List<ScopeInputSetter> _sisCache = new System.Collections.Generic.List<ScopeInputSetter>();

    private void Update()
    {
        if (RangefinderPlugin.Enabled != null && !RangefinderPlugin.Enabled.Value) { _inScope = false; return; }

        var myPN = GameManager.myPlayerNetworking;
        if (myPN == null || !myPN.isADS) { _inScope = false; return; }

        var weapon = myPN.c_activeWeapon;
        Camera cam = GetScopeCamera(weapon);
        _inScope = cam != null;
        if (!_inScope) return;

        // Make sure the scope camera renders everything the main camera does.
        // Binoculars (and some scopes) have a culling mask that omits remote-player
        // body layers, leaving only heads/weapons visible through the optic.
        EnsureFullCullingMask(cam);

        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 2000f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            _distance = hit.distance;
        else
            _distance = -1f;

        _currentZero = ReadScopeZero(weapon);

        if (_distance >= 0f) BuildReadout();
    }

    // Build the overlay strings once per frame (in Update) rather than on every OnGUI pass.
    private void BuildReadout()
    {
        int  targetZero = Mathf.RoundToInt(_distance);
        bool nan        = float.IsNaN(_currentZero);
        int  cur        = nan ? 0 : Mathf.RoundToInt(_currentZero);

        // Nothing the readout shows has changed since last frame — skip the string.Format allocs.
        if (targetZero == _shownDist && cur == _shownZero && nan == _shownNaN) return;
        _shownDist = targetZero; _shownZero = cur; _shownNaN = nan;

        _rangeText = string.Format("Range: {0:F0} m", _distance);

        if (nan)
        {
            _zeroText = string.Format("Set zero: {0}", targetZero);
        }
        else
        {
            int delta = targetZero - cur;
            if      (delta == 0) _zeroText = string.Format("Zero: {0} ✔", cur);
            else if (delta >  0) _zeroText = string.Format("Zero: {0}  (+{1} ↑)", cur, delta);
            else                 _zeroText = string.Format("Zero: {0}  ({1} ↓)",  cur, delta);
        }
    }

    private void OnGUI()
    {
        if (!_inScope || _distance < 0f) return;

        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 18,
                normal    = { textColor = Color.yellow },
                alignment = TextAnchor.MiddleCenter
            };
        }

        float cx = Screen.width  / 2f - 180f;
        float cy = Screen.height - 80f;
        GUI.Label(new Rect(cx, cy,       360f, 26f), _rangeText, _style);
        GUI.Label(new Rect(cx, cy + 26f, 360f, 26f), _zeroText,  _style);
    }

    private static void EnsureFullCullingMask(Camera scopeCam)
    {
        var mainCam = MainCameraSingleton.instance?.GameCamera;
        if (mainCam == null) return;
        int mainMask = mainCam.cullingMask;
        if ((scopeCam.cullingMask & mainMask) != mainMask)
            scopeCam.cullingMask |= mainMask;
    }

    // Re-scan the weapon's child ADS / ScopeInputSetter components only when the active weapon
    // changes. The component set is stable for a given weapon; only IsInADS flips when switching optics.
    // Edge case: changing attachments in-place (modding table) on the SAME WeaponV3 leaves the cache
    // stale — but you can't reach the modding table while ADS, and holstering/redrawing cycles
    // c_activeWeapon through null (clearing the cache here), so it self-heals before the next aim.
    // Destroyed cached elements are null-checked at every read below, so a stale list never throws.
    private void EnsureScans(WeaponV3 weapon)
    {
        if (weapon == _scanWeapon) return;
        _scanWeapon = weapon;
        _adsCache.Clear();
        _sisCache.Clear();
        if (weapon == null) return;

        var ads = weapon.GetComponentsInChildren<ADS>();
        for (int i = 0; i < ads.Length; i++)
            if (ads[i] != null) _adsCache.Add(ads[i]);

        var sis = weapon.GetComponentsInChildren<ScopeInputSetter>();
        for (int i = 0; i < sis.Length; i++)
            if (sis[i] != null) _sisCache.Add(sis[i]);
    }

    private float ReadScopeZero(WeaponV3 weapon)
    {
        if (weapon == null) { EnsureScans(null); return float.NaN; }
        EnsureScans(weapon);

        // The live ScopeZero is the one whose doesZero is set — its totalAdjustment is the value
        // the in-game UI shows. Read live each frame so zero adjustments update immediately.
        for (int i = 0; i < _sisCache.Count; i++)
        {
            var sz = _sisCache[i] != null ? _sisCache[i].scopeZero : null;
            if (sz != null && sz.doesZero) return sz.totalAdjustment;
        }
        return float.NaN;
    }

    private Camera GetScopeCamera(WeaponV3 weapon)
    {
        if (weapon == null) { EnsureScans(null); return null; }
        EnsureScans(weapon);

        // Pick the currently-active optic every frame (IsInADS), so canted/flip-sight switches on
        // the same weapon are tracked — only the cheap field reads run per frame, not a hierarchy scan.
        for (int i = 0; i < _adsCache.Count; i++)
        {
            var ads = _adsCache[i];
            if (ads == null || !ads.IsScope || !ads.IsInADS) continue;
            if (ads.scope?.ScopeCamera != null)
                return ads.scope.ScopeCamera;
            if (ads.Scope?.ScopeCamera?.DualRenderCamera != null)
                return ads.Scope.ScopeCamera.DualRenderCamera;
            return MainCameraSingleton.instance?.GameCamera;
        }
        return null;
    }
}
