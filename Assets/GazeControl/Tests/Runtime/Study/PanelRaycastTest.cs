using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Study
{
    [TestFixture]
    public class PanelRaycastTest
    {
        [Test]
        public void TryHit_StraightAtThePanel_LandsUnderTheOrigin()
        {
            var hit = PanelRaycast.TryHit(
                new Vector3(0.2f, -0.1f, -1.5f), Vector3.forward, out var point, out var distance);

            Assert.That(hit, Is.True);
            Assert.That(point.x, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(point.y, Is.EqualTo(-0.1f).Within(1e-5f));
            Assert.That(distance, Is.EqualTo(1.5f).Within(1e-5f));
        }

        [Test]
        public void TryHit_Angled_LandsOffToTheSide()
        {
            // Forty-five degrees right, from a metre out: the hit is a metre right.
            var hit = PanelRaycast.TryHit(
                Vector3.back, new Vector3(1f, 0f, 1f), out var point, out _);

            Assert.That(hit, Is.True);
            Assert.That(point.x, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(point.y, Is.EqualTo(0f).Within(1e-5f));
        }

        /// <summary>A direction need not be a unit vector, and the distance is still in the caller's units.</summary>
        [Test]
        public void TryHit_WithAnUnnormalisedDirection_ReportsATrueDistance()
        {
            var hit = PanelRaycast.TryHit(new Vector3(0f, 0f, -2f), Vector3.forward * 17f, out _, out var distance);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void TryHit_PointingAway_Misses()
        {
            Assert.That(PanelRaycast.TryHit(Vector3.back, Vector3.back, out _, out _), Is.False);
        }

        [Test]
        public void TryHit_ParallelToThePanel_Misses()
        {
            Assert.That(PanelRaycast.TryHit(Vector3.back, Vector3.right, out _, out _), Is.False);
        }

        /// <summary>
        /// A controller held against the panel is on the plane, not through it:
        /// there is no ray left to travel, so there is nothing to hit.
        /// </summary>
        [Test]
        public void TryHit_StartingOnThePanel_Misses()
        {
            Assert.That(PanelRaycast.TryHit(Vector3.zero, Vector3.forward, out _, out _), Is.False);
        }
    }
}
