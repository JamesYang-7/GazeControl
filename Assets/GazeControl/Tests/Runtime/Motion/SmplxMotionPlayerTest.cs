using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace GazeControl.Motion
{
    [TestFixture]
    public class SmplxMotionPlayerTest
    {
        /// <summary>The clip committed for tests and examples: 1200 frames at 60 fps.</summary>
        const string ClipPath = "Assets/MotionData/TalkingWithHands/trn_2023_v0_000_main-agent_000.npz";

        /// <summary>
        /// Two segments of that clip, standing in for two clips of a study
        /// session. The first ends where the recorded root has wandered about
        /// 0.36 m from where the segment started — far enough that carrying the
        /// displacement into the next segment is unmistakable.
        /// </summary>
        const int FirstSegmentStart = 0;

        const int FirstSegmentLastFrame = 600;
        const int SecondSegmentStart = 300;

        /// <summary>Where the agent was placed in the scene — its triad vertex.</summary>
        static readonly Vector3 ScenePosition = new(0.37f, 0.91f, -1.23f);

        GameObject _agent;
        Transform _pelvis;

        SmplxMotionPlayer CreateSystemUnderTest()
        {
            // Built inactive so no Unity event function runs: Start would load the
            // clip and Update would seek on its own clock, and this test drives
            // both explicitly.
            _agent = new GameObject(nameof(SmplxMotionPlayerTest));
            _agent.SetActive(false);

            foreach (var jointName in SmplxAnimUtils.JointNames)
            {
                var joint = new GameObject(jointName);
                joint.transform.SetParent(_agent.transform, worldPositionStays: false);
            }

            _pelvis = SmplxAnimUtils.FindChildRecursive(_agent.transform, SmplxAnimUtils.JointNames[0]);
            _pelvis.localPosition = ScenePosition;

            var sut = _agent.AddComponent<SmplxMotionPlayer>();
            sut.ClipPath = ClipPath;
            sut.Autoplay = false;
            sut.Loop = false;
            return sut;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_agent);
        }

        [Test]
        public void Initialize_ArmedAgainForTheNextClip_PutsThePelvisBackOnItsScenePosition()
        {
            var sut = CreateSystemUnderTest();
            sut.StartFrame = FirstSegmentStart;
            sut.Initialize();
            sut.ApplyFrame(FirstSegmentLastFrame);

            sut.StartFrame = SecondSegmentStart;
            sut.Initialize();
            sut.ApplyFrame(SecondSegmentStart);

            Assert.That(_pelvis.localPosition,
                Is.EqualTo(ScenePosition).Using(new Vector3EqualityComparer(1e-4f)));
        }

        [Test]
        public void ApplyFrame_ASegmentsFirstFrame_PutsThePelvisOnItsScenePosition()
        {
            var sut = CreateSystemUnderTest();
            sut.StartFrame = SecondSegmentStart;
            sut.Initialize();

            sut.ApplyFrame(SecondSegmentStart);

            Assert.That(_pelvis.localPosition,
                Is.EqualTo(ScenePosition).Using(new Vector3EqualityComparer(1e-4f)));
        }

        [Test]
        public void ApplyFrame_PartwayThroughASegment_MovesThePelvisWithTheRecordedRoot()
        {
            // Guards the anchoring from a fix that simply pins the pelvis: the
            // agent still has to travel with the mocap inside a clip, and it is
            // that travel the previous test asserts is left behind at the next one.
            var sut = CreateSystemUnderTest();
            sut.StartFrame = FirstSegmentStart;
            sut.Initialize();

            sut.ApplyFrame(FirstSegmentLastFrame);

            Assert.That(Vector3.Distance(_pelvis.localPosition, ScenePosition), Is.GreaterThan(0.1f));
        }

        [Test]
        public void ApplyFrame_WithRootTranslationRelativeCleared_UsesTheRawDatasetPosition()
        {
            // SMPL-X's `trans` places the pelvis at template_J[0] + trans, and the
            // rig's bind pose is template_J[0] — so the raw position is the bind
            // pose plus the clip's translation, not the translation alone.
            var sut = CreateSystemUnderTest();
            sut.StartFrame = SecondSegmentStart;
            sut.RootTranslationRelative = false;
            sut.Initialize();

            sut.ApplyFrame(SecondSegmentStart);

            var trans = SmplxAnimUtils.PositionToUnity(sut.Clip.GetTranslation(SecondSegmentStart));
            Assert.That(_pelvis.position,
                Is.EqualTo(ScenePosition + trans).Using(new Vector3EqualityComparer(1e-4f)));
        }

        [Test]
        public void ApplyFrame_RawPosition_IgnoresAnOffsetBetweenTheAgentAndThePelvisParent()
        {
            // The SMPL-X FBX hangs the pelvis off a `root` node 1.36 m up so that
            // the rest body stands on the floor. The clip's translation is already
            // measured from the floor, so writing it as a local position would add
            // that offset a second time and float every body by it.
            var sut = CreateSystemUnderTest();
            var rig = new GameObject("rig");
            rig.transform.SetParent(_agent.transform, worldPositionStays: false);
            rig.transform.localPosition = new Vector3(0f, 1.36f, 0f);
            _pelvis.SetParent(rig.transform, worldPositionStays: false);

            sut.StartFrame = SecondSegmentStart;
            sut.RootTranslationRelative = false;
            sut.Initialize();

            sut.ApplyFrame(SecondSegmentStart);

            var trans = SmplxAnimUtils.PositionToUnity(sut.Clip.GetTranslation(SecondSegmentStart));
            Assert.That(_pelvis.position,
                Is.EqualTo(ScenePosition + trans).Using(new Vector3EqualityComparer(1e-4f)));
        }

        [Test]
        public void ApplyFrame_WithRootTranslationOff_LeavesThePelvisOnItsScenePosition()
        {
            var sut = CreateSystemUnderTest();
            sut.StartFrame = FirstSegmentStart;
            sut.ApplyRootTranslation = false;
            sut.Initialize();

            sut.ApplyFrame(FirstSegmentLastFrame);

            Assert.That(_pelvis.localPosition,
                Is.EqualTo(ScenePosition).Using(new Vector3EqualityComparer(1e-4f)));
        }
    }
}
