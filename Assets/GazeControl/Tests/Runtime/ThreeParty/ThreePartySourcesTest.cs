using System.IO;
using NUnit.Framework;

namespace GazeControl.ThreeParty
{
    [TestFixture]
    public class ThreePartySourcesTest
    {
        const string Date = "12-15-2021";
        const int Session = 1;

        string _root;

        [TearDown]
        public void DeleteTheFakeCorpus()
        {
            if (_root != null && Directory.Exists(_root))
                Directory.Delete(_root, true);

            _root = null;
        }

        [Test]
        public void SubjectOf_ReadsTheNameOutOfAConvertedClip()
        {
            Assert.That(ThreePartySources.SubjectOf("Session_1_pc2_Maryum.npz"), Is.EqualTo("Maryum"));
        }

        [Test]
        public void SubjectOf_KeepsAnUnderscoreInsideTheName()
        {
            Assert.That(ThreePartySources.SubjectOf("Session_3_pc1_Mary_Anne.npz"), Is.EqualTo("Mary_Anne"));
        }

        [Test]
        public void SubjectOf_FallsBackToTheStemWhenTheNameIsNotTheConverterS()
        {
            Assert.That(ThreePartySources.SubjectOf("something-else.npz"), Is.EqualTo("something-else"));
        }

        [Test]
        public void AudioAndCaptionNames_UseTheTruePcNumber()
        {
            // Both streams carry real participant numbers, unlike the raw motion
            // export's PC_<N>, so they are addressed by the same index.
            Assert.That(ThreePartySources.AudioFileName(2, 3), Is.EqualTo("Session_2_PC_3_audio.wav"));
            Assert.That(ThreePartySources.CaptionFileName(2, 3), Is.EqualTo("Session_2_PC_3_sentence.csv"));
        }

        [Test]
        public void CaptionDirectory_IsCaptionNotCaptionSentenceBased()
        {
            // The two disagree substantially and the end-of-turn tables were
            // built from Caption/; reading the other would caption a window with
            // text its events do not come from.
            var directory = ThreePartySources.CaptionDirectory(@"D:\data", Date);
            Assert.That(Path.GetFileName(directory), Is.EqualTo("Caption"));
        }

        [Test]
        public void Dates_ListsWhatHasBeenConverted()
        {
            GivenAFakeCorpus();
            Assert.That(ThreePartySources.Dates(MotionRoot), Is.EqualTo(new[] { Date }));
        }

        [Test]
        public void Sessions_ListsTheSessionNumbersOfOneDate()
        {
            GivenAFakeCorpus();
            Assert.That(ThreePartySources.Sessions(MotionRoot, Date), Is.EqualTo(new[] { Session }));
        }

        [Test]
        public void Resolve_ReturnsTheThreeParticipantsInPcOrder()
        {
            GivenAFakeCorpus();

            var clips = ThreePartySources.Resolve(MotionRoot, DataRoot, Date, Session);

            Assert.That(clips.Length, Is.EqualTo(ThreePartySources.ParticipantCount));
            Assert.That(new[] { clips[0].Pc, clips[1].Pc, clips[2].Pc }, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(clips[1].Subject, Is.EqualTo("Maryum"));
            Assert.That(File.Exists(clips[1].AudioPath), Is.True);
        }

        [Test]
        public void Resolve_LeavesAMissingTranscriptNull()
        {
            // Captions only caption the replay, so a session without one is still
            // watchable — unlike a missing clip or a missing voice.
            GivenAFakeCorpus();
            File.Delete(Path.Combine(
                ThreePartySources.CaptionDirectory(DataRoot, Date), ThreePartySources.CaptionFileName(Session, 2)));

            var clips = ThreePartySources.Resolve(MotionRoot, DataRoot, Date, Session);

            Assert.That(clips[1].CaptionPath, Is.Null);
            Assert.That(clips[0].CaptionPath, Is.Not.Null);
        }

        [Test]
        public void Resolve_RefusesASessionWithAParticipantMissing()
        {
            GivenAFakeCorpus();
            File.Delete(Path.Combine(MotionRoot, Date, "Session_1_pc3_Justice.npz"));

            Assert.Throws<FileNotFoundException>(
                () => ThreePartySources.Resolve(MotionRoot, DataRoot, Date, Session));
        }

        string MotionRoot => Path.Combine(_root, "motion");
        string DataRoot => Path.Combine(_root, "data");

        /// <summary>
        /// The file names are the whole contract here, so the fixture is a tree of
        /// empty files rather than a real corpus.
        /// </summary>
        void GivenAFakeCorpus()
        {
            _root = Path.Combine(Path.GetTempPath(), $"three-party-{Path.GetRandomFileName()}");

            var motion = Path.Combine(MotionRoot, Date);
            var audio = ThreePartySources.AudioDirectory(DataRoot, Date);
            var captions = ThreePartySources.CaptionDirectory(DataRoot, Date);
            Directory.CreateDirectory(motion);
            Directory.CreateDirectory(audio);
            Directory.CreateDirectory(captions);

            var names = new[] { "Crystal", "Maryum", "Justice" };
            for (var pc = 1; pc <= names.Length; pc++)
            {
                File.WriteAllText(Path.Combine(motion, $"Session_{Session}_pc{pc}_{names[pc - 1]}.npz"), string.Empty);
                File.WriteAllText(Path.Combine(audio, ThreePartySources.AudioFileName(Session, pc)), string.Empty);
                File.WriteAllText(Path.Combine(captions, ThreePartySources.CaptionFileName(Session, pc)), string.Empty);
            }
        }
    }
}
