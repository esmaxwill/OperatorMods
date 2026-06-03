using UnityEngine;

namespace OPERATOR.Common
{
    public static class Rangefinder
    {
        public static float GetDistance(Camera cam, float maxRange = 2000f)
        {
            return TryHit(cam, out RaycastHit hit, maxRange) ? hit.distance : -1f;
        }

        // Returns the full RaycastHit so callers can access distance, hit point, normal, collider, etc.
        public static bool TryHit(Camera cam, out RaycastHit hit, float maxRange = 2000f)
        {
            if (cam == null) { hit = default(RaycastHit); return false; }
            return Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, maxRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
