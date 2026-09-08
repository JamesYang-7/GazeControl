using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace GazeControl.ThreeParty
{
    [TestFixture]
    public class ThreePartyViewpointTest
    {
        [Test]
        public void TryCompute_StandsAtTheMeanOfTheSamples()
        {
            // The seat is where the person was over the window, not where they
            // happened to be leaning at any one frame.
            var samples = new[]
            {
                new Vector3(0f, 1.6f, -1f),
                new Vector3(0.2f, 1.7f, -1f),
                new Vector3(-0.2f, 1.5f, -1f),
            };

            Assert.That(ThreePartyViewpoint.TryCompute(samples, Speaker(-1f), Speaker(1f), out var viewpoint), Is.True);
            Assert.That(viewpoint.Position, Is.EqualTo(new Vector3(0f, 1.6f, -1f)).Using(Vector3Comparer));
        }

        [Test]
        public void TryCompute_FacesTheMidpointOfTheTwoSpeakers()
        {
            // Seat on -Z, speakers either side of +Z: the midpoint is straight
            // ahead, which is yaw zero.
            var samples = new[] { new Vector3(0f, 1.6f, -1f) };

            Assert.That(ThreePartyViewpoint.TryCompute(samples, Speaker(-1f), Speaker(1f), out var viewpoint), Is.True);
            Assert.That(viewpoint.Yaw, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void TryCompute_TurnsTowardsAnOffCentreMidpoint()
        {
            var samples = new[] { Vector3.zero };

            // Midpoint at (1, _, 1) from the origin: 45° to the right.
            Assert.That(ThreePartyViewpoint.TryCompute(
                samples, new Vector3(1f, 1.6f, 0f), new Vector3(1f, 1.6f, 2f), out var viewpoint), Is.True);
            Assert.That(viewpoint.Yaw, Is.EqualTo(45f).Within(1e-3f));
        }

        [Test]
        public void TryCompute_IgnoresHeightWhenFacing()
        {
            // Two speakers a head taller than the seat still give the same yaw:
            // pitch is reported against gravity by the headset and is already
            // right, so turning it would tilt the virtual horizon.
            var samples = new[] { new Vector3(0f, 1.5f, -1f) };

            Assert.That(ThreePartyViewpoint.TryCompute(
                samples, new Vector3(-1f, 2.2f, 1f), new Vector3(1f, 2.2f, 1f), out var viewpoint), Is.True);
            Assert.That(viewpoint.Yaw, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void Rotation_IsTheYawAndNothingElse()
        {
            var samples = new[] { Vector3.zero };
            ThreePartyViewpoint.TryCompute(
                samples, new Vector3(1f, 1.6f, 0f), new Vector3(1f, 1.6f, 2f), out var viewpoint);

            var euler = viewpoint.Rotation.eulerAngles;

            Assert.That(euler.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(euler.z, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(euler.y, Is.EqualTo(45f).Within(1e-3f));
        }

        [Test]
        public void TryCompute_RefusesAWindowWithNothingToAverage()
        {
            Assert.That(
                ThreePartyViewpoint.TryCompute(new Vector3[0], Speaker(-1f), Speaker(1f), out _), Is.False);
        }

        [Test]
        public void TryCompute_RefusesASeatOnTopOfTheSpeakersMidpoint()
        {
            // A facing is undefined there, and guessing one would silently point
            // the participant at a wall.
            var samples = new[] { new Vector3(0f, 1.6f, 0f) };

            Assert.That(ThreePartyViewpoint.TryCompute(
                samples, new Vector3(-0.01f, 1.6f, 0f), new Vector3(0.01f, 1.6f, 0f), out _), Is.False);
        }

        static Vector3 Speaker(float x) => new(x, 1.6f, 1f);

        static Vector3EqualityComparer Vector3Comparer => new(1e-4f);
    }
}
