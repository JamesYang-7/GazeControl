using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Xr
{
    [TestFixture]
    public class GazeSphereTargetingTest
    {
        const float Tolerance = 1e-3f;
        const float HeadRadius = 0.15f;

        /// <summary>Agent A one metre ahead, agent B one metre to the right.</summary>
        static List<GazeSphereTargeting.Head> TwoAgents() => new()
        {
            new GazeSphereTargeting.Head(1, new Vector3(0f, 0f, 1f), HeadRadius),
            new GazeSphereTargeting.Head(2, new Vector3(1f, 0f, 0f), HeadRadius),
        };

        [Test]
        public void Resolve_StraightAtAHead_HitsIt()
        {
            var hit = GazeSphereTargeting.Resolve(Vector3.zero, Vector3.forward, TwoAgents());

            Assert.That(hit.Found, Is.True);
            Assert.That(hit.Id, Is.EqualTo(1));
            Assert.That(hit.AngleDegrees, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(hit.Distance, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void Resolve_AtTheOtherHead_HitsThatOneInstead()
        {
            var hit = GazeSphereTargeting.Resolve(Vector3.zero, Vector3.right, TwoAgents());

            Assert.That(hit.Found, Is.True);
            Assert.That(hit.Id, Is.EqualTo(2));
        }

        [Test]
        public void Resolve_JustInsideTheSphereEdge_StillHits()
        {
            // Grazing the sphere at 1 m: the offset is just under the radius, so
            // the perpendicular distance is too.
            var direction = new Vector3(HeadRadius - 0.01f, 0f, 1f);

            var hit = GazeSphereTargeting.Resolve(Vector3.zero, direction, TwoAgents());

            Assert.That(hit.Found, Is.True);
            Assert.That(hit.Id, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_JustOutsideTheSphereEdge_MissesButNamesTheNearestHead()
        {
            var direction = new Vector3(HeadRadius + 0.05f, 0f, 1f);

            var hit = GazeSphereTargeting.Resolve(Vector3.zero, direction, TwoAgents());

            // Missing still records which head was nearly looked at and by how
            // much, so a looser threshold can be applied to a finished log.
            Assert.That(hit.Found, Is.False);
            Assert.That(hit.Id, Is.EqualTo(1));
            Assert.That(hit.AngleDegrees, Is.GreaterThan(0f));
        }

        [Test]
        public void Resolve_BehindTheParticipant_IsNotLookedAt()
        {
            // The line through the head is the same line either way; only the
            // direction distinguishes looking at someone from looking away.
            var hit = GazeSphereTargeting.Resolve(Vector3.zero, Vector3.back, TwoAgents());

            Assert.That(hit.Found, Is.False);
            Assert.That(hit.Id, Is.EqualTo(-1));
        }

        [Test]
        public void Resolve_ThroughTwoAlignedHeads_TakesTheNearerOne()
        {
            var heads = new List<GazeSphereTargeting.Head>
            {
                new(1, new Vector3(0f, 0f, 3f), HeadRadius),
                new(2, new Vector3(0f, 0f, 1f), HeadRadius),
            };

            var hit = GazeSphereTargeting.Resolve(Vector3.zero, Vector3.forward, heads);

            Assert.That(hit.Id, Is.EqualTo(2), "the near head occludes the far one");
            Assert.That(hit.Distance, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void Resolve_PreferringAHitOverACloserMiss()
        {
            // Agent 2 is nearer but off to the side; agent 1 is further away and
            // squarely looked at. Distance only orders hits against each other.
            var heads = new List<GazeSphereTargeting.Head>
            {
                new(2, new Vector3(0.5f, 0f, 0.5f), HeadRadius),
                new(1, new Vector3(0f, 0f, 2f), HeadRadius),
            };

            var hit = GazeSphereTargeting.Resolve(Vector3.zero, Vector3.forward, heads);

            Assert.That(hit.Found, Is.True);
            Assert.That(hit.Id, Is.EqualTo(1));
        }

        [Test]
        public void Resolve_WithAnUnnormalisedDirection_IsUnchanged()
        {
            var hit = GazeSphereTargeting.Resolve(Vector3.zero, Vector3.forward * 42f, TwoAgents());

            Assert.That(hit.Found, Is.True);
            Assert.That(hit.Id, Is.EqualTo(1));
            Assert.That(hit.Distance, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void Resolve_FromAMovedParticipant_UsesTheRayOrigin()
        {
            // The participant has stepped back half a metre; the same world
            // direction no longer points at anybody's head.
            var origin = new Vector3(0.6f, 0f, -0.5f);

            var hit = GazeSphereTargeting.Resolve(origin, Vector3.forward, TwoAgents());

            Assert.That(hit.Found, Is.False);
        }

        [Test]
        public void Resolve_WithNoHeadsOrNoDirection_FindsNothing()
        {
            Assert.That(GazeSphereTargeting.Resolve(
                Vector3.zero, Vector3.forward, new List<GazeSphereTargeting.Head>()).Found, Is.False);
            Assert.That(GazeSphereTargeting.Resolve(
                Vector3.zero, Vector3.forward, null).Found, Is.False);
            Assert.That(GazeSphereTargeting.Resolve(
                Vector3.zero, Vector3.zero, TwoAgents()).Found, Is.False);
        }

        [Test]
        public void Resolve_AtTheTriadGeometry_SeparatesTheTwoAgents()
        {
            // The real case: a 1 m equilateral triangle, the participant at one
            // vertex looking at each of the other two in turn. The two agents sit
            // 60 degrees apart from where the participant stands, so a head
            // sphere this size cannot confuse them.
            var agentA = new Vector3(-0.5f, 0f, Mathf.Sqrt(3f) / 2f);
            var agentB = new Vector3(0.5f, 0f, Mathf.Sqrt(3f) / 2f);
            var heads = new List<GazeSphereTargeting.Head>
            {
                new(1, agentA, HeadRadius),
                new(2, agentB, HeadRadius),
            };

            Assert.That(GazeSphereTargeting.Resolve(Vector3.zero, agentA, heads).Id, Is.EqualTo(1));
            Assert.That(GazeSphereTargeting.Resolve(Vector3.zero, agentB, heads).Id, Is.EqualTo(2));

            // Straight between them is nobody.
            Assert.That(GazeSphereTargeting.Resolve(Vector3.zero, Vector3.forward, heads).Found, Is.False);
        }
    }
}
