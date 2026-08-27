using System.Collections.Generic;
using UnityEngine;

namespace GazeControl.Xr
{
    /// <summary>
    /// Resolves which head a gaze ray is aimed at, against head bounding spheres
    /// (<c>user-study-design.md</c> §7.1).
    ///
    /// <para>Spheres rather than the fixed angular cone the agent side uses: an
    /// angle is a different amount of head at every distance, and the
    /// participant may step within the play area. The angle to each candidate is
    /// returned as well, so the threshold stays an analysis choice — the log
    /// carries the raw ray and this angle, and a re-thresholded pass needs no
    /// re-run.</para>
    /// </summary>
    public static class GazeSphereTargeting
    {
        /// <summary>One candidate head: whose it is, where it is, and how big to call it.</summary>
        public readonly struct Head
        {
            public Head(int id, Vector3 centre, float radius)
            {
                Id = id;
                Centre = centre;
                Radius = radius;
            }

            /// <summary>The participant id this head belongs to.</summary>
            public int Id { get; }

            public Vector3 Centre { get; }

            public float Radius { get; }
        }

        /// <summary>What a ray resolved to.</summary>
        public readonly struct Hit
        {
            Hit(bool found, int id, float angleDegrees, float distance)
            {
                Found = found;
                Id = id;
                AngleDegrees = angleDegrees;
                Distance = distance;
            }

            /// <summary>False when the ray met no head — "elsewhere".</summary>
            public bool Found { get; }

            /// <summary>The participant id looked at, or -1.</summary>
            public int Id { get; }

            /// <summary>
            /// Angle between the ray and the line to the chosen head's centre. For
            /// a miss this is the angle to the nearest head, which is what makes a
            /// looser threshold recoverable offline; it is -1 when no head lay in
            /// front of the participant at all.
            /// </summary>
            public float AngleDegrees { get; }

            /// <summary>Distance from the ray origin to that head's centre, metres; -1 with no head in front.</summary>
            public float Distance { get; }

            public static readonly Hit Nothing = new(false, -1, -1f, -1f);

            public static Hit On(int id, float angleDegrees, float distance) =>
                new(true, id, angleDegrees, distance);

            public static Hit Missed(int nearestId, float angleDegrees, float distance) =>
                new(false, nearestId, angleDegrees, distance);
        }

        /// <summary>
        /// Which head <paramref name="direction"/> from <paramref name="origin"/>
        /// lands on. When the ray pierces more than one sphere the nearest wins,
        /// because the near head occludes the far one.
        /// </summary>
        /// <param name="direction">Need not be normalised; a zero direction resolves to nothing.</param>
        public static Hit Resolve(Vector3 origin, Vector3 direction, IReadOnlyList<Head> heads)
        {
            if (heads == null || heads.Count == 0 || direction == Vector3.zero)
                return Hit.Nothing;

            var ray = direction.normalized;
            var best = Hit.Nothing;

            for (var i = 0; i < heads.Count; i++)
            {
                var toCentre = heads[i].Centre - origin;
                var distance = toCentre.magnitude;

                // A head at the ray origin has no direction to it, and a head
                // behind the participant is not being looked at however small the
                // angle between the (undirected) lines would be.
                var along = Vector3.Dot(toCentre, ray);
                if (distance <= Mathf.Epsilon || along <= 0f)
                    continue;

                var angle = Vector3.Angle(ray, toCentre);

                // Perpendicular distance from the centre to the ray: the sphere
                // test in the form that also gives the angle for free.
                var perpendicular = Mathf.Sqrt(Mathf.Max(0f, distance * distance - along * along));
                var inside = perpendicular <= heads[i].Radius;

                if (inside)
                {
                    // A real hit beats any miss, and among hits the nearest wins.
                    if (!best.Found || distance < best.Distance)
                        best = Hit.On(heads[i].Id, angle, distance);
                }
                else if (!best.Found && (best.Id < 0 || angle < best.AngleDegrees))
                {
                    // Nearest miss by angle, kept only so the log can say how far
                    // off "elsewhere" was.
                    best = Hit.Missed(heads[i].Id, angle, distance);
                }
            }

            return best;
        }
    }
}
