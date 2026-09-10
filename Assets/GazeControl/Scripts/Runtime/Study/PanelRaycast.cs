using UnityEngine;

namespace GazeControl.Study
{
    /// <summary>
    /// Where a controller's ray meets the question panel.
    ///
    /// <para><b>A plane, not a collider</b> (<c>questionnaire-ui-design.md</c>
    /// §0.1). The panel is a flat world-space canvas, so where a ray lands on it
    /// is one division — which needs no physics raycaster, no collider on every
    /// button, no event system and none of the XR Interaction Toolkit, the stack
    /// this design refused for the Varjo loader. It also makes the aiming
    /// testable at a desk: everything here is arithmetic in the panel's own
    /// space.</para>
    /// </summary>
    public static class PanelRaycast
    {
        /// <summary>
        /// A direction whose component into the panel is smaller than this is
        /// treated as parallel to it. A ray this near-grazing meets the plane
        /// hundreds of metres away, which is off the panel in any case, so
        /// refusing it only avoids the arithmetic overflowing on the way.
        /// </summary>
        const float k_MinimumApproach = 1e-6f;

        /// <summary>
        /// Intersect a ray with the panel plane, both expressed in the panel's
        /// own space, where the panel is the z = 0 plane.
        /// </summary>
        /// <param name="origin">Where the ray starts, in panel space.</param>
        /// <param name="direction">Which way it points, in panel space; need not be a unit vector.</param>
        /// <param name="point">Where it lands on the plane, in the panel's own units.</param>
        /// <param name="distance">How far along the ray that is, in panel-space units.</param>
        /// <returns>False when the ray runs parallel to the panel or points away from it.</returns>
        public static bool TryHit(Vector3 origin, Vector3 direction, out Vector2 point, out float distance)
        {
            point = default;
            distance = 0f;

            var along = direction.normalized;
            if (Mathf.Abs(along.z) < k_MinimumApproach)
                return false;

            var t = -origin.z / along.z;
            if (t <= 0f)
                return false;

            var hit = origin + along * t;
            point = new Vector2(hit.x, hit.y);
            distance = t;
            return true;
        }
    }
}
