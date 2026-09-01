using System.IO;
using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class GitHeadTest
    {
        const string Sha = "0123456789abcdef0123456789abcdef01234567";

        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), $"githead_{Path.GetRandomFileName()}");
            Directory.CreateDirectory(Path.Combine(_root, ".git"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        void Write(string relativePath, string content)
        {
            var path = Path.Combine(_root, ".git", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }

        [Test]
        public void ALooseBranchRefIsFollowed()
        {
            Write("HEAD", "ref: refs/heads/main\n");
            Write("refs/heads/main", Sha + "\n");

            Assert.That(GitHead.Read(_root), Is.EqualTo(Sha));
        }

        [Test]
        public void APackedBranchRefIsFound()
        {
            // The normal state of a fresh clone: no loose ref file exists.
            Write("HEAD", "ref: refs/heads/main\n");
            Write("packed-refs",
                "# pack-refs with: peeled fully-peeled sorted \n" +
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa refs/heads/other\n" +
                $"{Sha} refs/heads/main\n" +
                "^bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\n");

            Assert.That(GitHead.Read(_root), Is.EqualTo(Sha));
        }

        [Test]
        public void ADetachedHeadIsTheCommitItself()
        {
            Write("HEAD", Sha + "\n");

            Assert.That(GitHead.Read(_root), Is.EqualTo(Sha));
        }

        [Test]
        public void NoRepositoryReadsAsNull()
        {
            // Provenance that is merely absent must never stop a participant
            // session, so every failure is a null rather than a throw.
            Assert.That(GitHead.Read(Path.Combine(_root, "nowhere")), Is.Null);
        }

        [Test]
        public void AnUnresolvableRefReadsAsNull()
        {
            Write("HEAD", "ref: refs/heads/missing\n");

            Assert.That(GitHead.Read(_root), Is.Null);
        }
    }
}
