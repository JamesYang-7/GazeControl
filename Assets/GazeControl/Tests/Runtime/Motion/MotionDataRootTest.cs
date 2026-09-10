using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GazeControl.Motion
{
    [TestFixture]
    public class MotionDataRootTest
    {
        const string Recorded = @"F:\Data\3People-2022-SMPLX\12-15-2021\Session_2_pc2_Maryum.npz";
        const string Tail = "3People-2022-SMPLX/12-15-2021/Session_2_pc2_Maryum.npz";

        string _root;

        [TearDown]
        public void DeleteTheFakeCorpus()
        {
            if (_root != null && Directory.Exists(_root))
                Directory.Delete(_root, true);

            _root = null;
        }

        [Test]
        public void Rebase_KeepsTheCorpusFolderAndTheTailUnderIt()
        {
            Assert.That(MotionDataRoot.Rebase("E:/Project", Recorded), Is.EqualTo("E:/Project/" + Tail));
        }

        [Test]
        public void Rebase_AnswersInForwardSlashesWhateverTheRecordedPathUsed()
        {
            // The manifests are written on Windows and carry backslashes; the
            // rest of the codebase moves paths around in forward slashes.
            Assert.That(MotionDataRoot.Rebase("E:/Project", Recorded), Does.Not.Contain(@"\"));
        }

        [Test]
        public void Rebase_LeavesAnotherCorpusAlone()
        {
            // Study 1's clips: hundreds of gigabytes that are never copied into
            // a project, so redirecting them at the local root would be wrong.
            Assert.That(
                MotionDataRoot.Rebase("E:/Project", @"F:\Data\TalkingWithHandsCentered\SMPLX-60fps-grounded\x.npz"),
                Is.Null);
        }

        [Test]
        public void Rebase_MatchesTheCorpusFolderOnlyAsAWholeName()
        {
            Assert.That(
                MotionDataRoot.Rebase("E:/Project", @"F:\Data\3People-2022-SMPLX-old\12-15-2021\x.npz"),
                Is.Null);
            Assert.That(
                MotionDataRoot.Rebase("E:/Project", @"F:\Data\Old3People-2022-SMPLX\12-15-2021\x.npz"),
                Is.Null);
        }

        [Test]
        public void Rebase_LeavesTheCorpusFolderWithNothingUnderItAlone()
        {
            Assert.That(MotionDataRoot.Rebase("E:/Project", @"F:\Data\3People-2022-SMPLX"), Is.Null);
        }

        [Test]
        public void Rebase_AnswersNothingWithoutARoot()
        {
            Assert.That(MotionDataRoot.Rebase(null, Recorded), Is.Null);
            Assert.That(MotionDataRoot.Rebase("", Recorded), Is.Null);
        }

        [Test]
        public void Rebase_AnswersNothingForNoPath()
        {
            Assert.That(MotionDataRoot.Rebase("E:/Project", null), Is.Null);
        }

        [Test]
        public void Resolve_PrefersTheClipUnderTheConfiguredRoot()
        {
            GiveTheRootACopyOfTheClip();

            Assert.That(MotionDataRoot.Resolve(_root, Recorded), Is.EqualTo(_root.Replace('\\', '/') + "/" + Tail));
        }

        [Test]
        public void Resolve_FallsBackToWhereTheSegmentRecordedIt()
        {
            // A machine that still has the corpus where it was exported from
            // keeps working with no local copy and no config.
            _root = Path.Combine(Path.GetTempPath(), "MotionDataRootTest_" + Guid.NewGuid().ToString("N"));
            var recorded = Path.Combine(_root, "exported", Tail.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(recorded));
            File.WriteAllText(recorded, "");

            var noLocalCopy = Path.Combine(_root, "local");
            Assert.That(MotionDataRoot.Resolve(noLocalCopy, recorded), Is.EqualTo(recorded));
        }

        [Test]
        public void Resolve_NamesBothPlacesItLookedWhenTheClipIsInNeither()
        {
            // Not the manifests' own F:\Data path: on the machine the segments
            // were exported from that file is really there, and the fallback
            // would find it.
            var nowhere = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N"),
                Tail.Replace('/', Path.DirectorySeparatorChar));

            LogAssert.Expect(LogType.Warning, new Regex("motion clip not found"));

            Assert.That(
                MotionDataRoot.Resolve("E:/Project", nowhere),
                Is.EqualTo("E:/Project/" + Tail));
        }

        [Test]
        public void Resolve_LeavesAPathItDoesNotRebaseUntouched()
        {
            const string other = @"F:\Data\TalkingWithHandsCentered\SMPLX-60fps-grounded\x.npz";
            Assert.That(MotionDataRoot.Resolve("E:/Project", other), Is.EqualTo(other));
        }

        [Test]
        public void RootFrom_TakesTheConfiguredRoot()
        {
            Assert.That(MotionDataRoot.RootFrom("{\"dataRoot\": \"F:/Data\"}"), Is.EqualTo("F:/Data"));
        }

        [Test]
        public void RootFrom_TrimsIt()
        {
            Assert.That(MotionDataRoot.RootFrom("{\"dataRoot\": \"  F:/Data  \"}"), Is.EqualTo("F:/Data"));
        }

        [Test]
        public void RootFrom_DefaultsToTheProjectWhenTheDocumentNamesNoRoot()
        {
            Assert.That(MotionDataRoot.RootFrom("{}"), Is.EqualTo(MotionDataRoot.DefaultRoot));
            Assert.That(MotionDataRoot.RootFrom("{\"dataRoot\": \"\"}"), Is.EqualTo(MotionDataRoot.DefaultRoot));
            Assert.That(MotionDataRoot.RootFrom("   "), Is.EqualTo(MotionDataRoot.DefaultRoot));
        }

        [Test]
        public void RootFrom_RefusesADocumentThatIsNotJson()
        {
            Assert.Throws<ArgumentException>(() => MotionDataRoot.RootFrom("F:/Data"));
        }

        void GiveTheRootACopyOfTheClip()
        {
            _root = Path.Combine(Path.GetTempPath(), "MotionDataRootTest_" + Guid.NewGuid().ToString("N"));
            var clip = Path.Combine(_root, Tail.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(clip));
            File.WriteAllText(clip, "");
        }
    }
}
