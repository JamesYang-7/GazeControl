using System;
using System.Linq;
using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class StudySessionRulesTest
    {
        /// <summary>
        /// A run with nothing wrong with it, optionally spoiled in one way — which
        /// is how each test below is written: one thing wrong, everything else as
        /// a real session has it.
        /// </summary>
        static StudySessionState Ready(Action<StudySessionState> spoil = null)
        {
            var state = new StudySessionState
            {
                ParticipantLabel = "P07",
                ParticipantHasRun = false,
                ParticipantFolder = "Recordings/P07",
                TrackMode = "Replay",
                ReplayingBakedTrack = true,
                TrackLoaded = true,
                TrackPath = "Assets/DemoSegments/study_c1/gaze_Proposed_8.json",
                LoggingEnabled = true,
                ParticipantGazeWired = true,
                ParticipantGazeRigWired = true,
                QuestionnairePresent = true,
                DeveloperOverlayOn = null,
                SessionRunnerPresent = true,
                SessionRunnerEnabled = true,
                XrStartsOnPlay = true,
                XrLoader = "Varjo Loader",
            };

            spoil?.Invoke(state);
            return state;
        }

        static StudyCheck CheckNamed(StudySessionState state, string name) =>
            StudySessionRules.Evaluate(state).Single(c => c.Name == name);

        [Test]
        public void AReadyRunHasNoProblem()
        {
            Assert.That(StudySessionRules.FirstProblem(Ready()), Is.Null);
            Assert.That(StudySessionRules.ReadyToLaunch(Ready()), Is.True);
        }

        [Test]
        public void TheDebugLabelIsRefused()
        {
            var state = Ready(s => s.ParticipantLabel = ParticipantLabel.DebugLabel);

            Assert.That(StudySessionRules.FirstProblem(state), Does.Contain(ParticipantLabel.DebugLabel));
        }

        [Test]
        public void AnEmptyLabelIsRefused()
        {
            Assert.That(StudySessionRules.FirstProblem(Ready(s => s.ParticipantLabel = " ")),
                Does.Contain("unattributable"));
        }

        [Test]
        public void ALabelThatHasAlreadyRunIsRefused()
        {
            // The hole this closes: nothing used to catch running P07 twice, and
            // the collision surfaced only when the first questionnaire screen
            // committed — a clip after the participant had put the headset on.
            var problem = StudySessionRules.FirstProblem(Ready(s => s.ParticipantHasRun = true));

            Assert.That(problem, Does.Contain("P07"));
            Assert.That(problem, Does.Contain("already run"));
        }

        [Test]
        public void ALiveTakeIsRefused()
        {
            var state = Ready(s =>
            {
                s.ReplayingBakedTrack = false;
                s.TrackMode = "Bake";
            });

            Assert.That(StudySessionRules.FirstProblem(state), Does.Contain("Bake"));
        }

        [Test]
        public void AMissingTrackIsRefused()
        {
            Assert.That(StudySessionRules.FirstProblem(Ready(s => s.TrackLoaded = false)),
                Does.Contain("no baked track is loaded"));
        }

        [Test]
        public void ARunThatRecordsNothingIsRefused()
        {
            Assert.That(StudySessionRules.FirstProblem(Ready(s => s.LoggingEnabled = false)),
                Does.Contain("no record"));
            Assert.That(StudySessionRules.FirstProblem(Ready(s => s.ParticipantGazeWired = false)),
                Does.Contain("ParticipantGazeLogger"));
            Assert.That(StudySessionRules.FirstProblem(Ready(s => s.ParticipantGazeRigWired = false)),
                Does.Contain("head pose"));
        }

        [Test]
        public void ASessionWithNoQuestionnaireIsRefused()
        {
            Assert.That(StudySessionRules.FirstProblem(Ready(s => s.QuestionnairePresent = false)),
                Does.Contain("QuestionnaireSession"));
        }

        [Test]
        public void ASessionWithNoRunnerIsRefused()
        {
            // Without one, the start key plays a single clip and stops: a session
            // that looks complete after a fifteenth of it.
            Assert.That(StudySessionRules.FirstProblem(Ready(s => s.SessionRunnerPresent = false)),
                Does.Contain("StudySessionRunner"));
        }

        [Test]
        public void ARunThatWillNotStartTheHeadsetIsRefused()
        {
            Assert.That(StudySessionRules.FirstProblem(Ready(s => s.XrStartsOnPlay = false)),
                Does.Contain("StartXrOnPlay"));
        }

        [Test]
        public void ARunWithNoXrLoaderIsRefused()
        {
            Assert.That(StudySessionRules.FirstProblem(Ready(s => s.XrLoader = null)),
                Does.Contain("XR loader"));
        }

        [Test]
        public void TheDeveloperOverlayIsRefused()
        {
            Assert.That(StudySessionRules.FirstProblem(Ready(s => s.DeveloperOverlayOn = "Conversation")),
                Does.Contain("Conversation"));
        }

        [Test]
        public void MissingSegmentsAndTracksAreNamed()
        {
            var state = Ready(s =>
            {
                s.MissingSegments = new[] { "study_c4" };
                s.MissingTracks = new[] { "study_c1/Proposed", "study_c2/RoleConditioned" };
            });

            Assert.That(CheckNamed(state, "Segments exported").Problem, Does.Contain("study_c4"));
            Assert.That(CheckNamed(state, "Tracks baked").Problem, Does.Contain("study_c1/Proposed"));
        }

        [Test]
        public void WhatTheCallerDidNotLookAtIsNotReportedAsPassing()
        {
            // Null is "not my business", not "fine": the runner sees one clip and
            // cannot answer for the session's other fourteen, and a green row
            // saying it could would be worse than no row at all.
            var names = StudySessionRules.Evaluate(Ready()).Select(c => c.Name).ToList();

            Assert.That(names, Does.Not.Contain("Segments exported"));
            Assert.That(names, Does.Not.Contain("Tracks baked"));
        }

        [Test]
        public void TrackLoadedIsSkippedWhenUnknown()
        {
            // Edit Mode has no track loaded, and the window asks the stronger
            // question — whether all fifteen are baked — instead.
            var names = StudySessionRules.Evaluate(Ready(s => s.TrackLoaded = null))
                .Select(c => c.Name).ToList();

            Assert.That(names, Does.Not.Contain("Track loaded"));
        }

        [Test]
        public void TheFieldsALaunchSetsDoNotBlockIt()
        {
            // The whole point of the Start Session window: the operator no longer
            // owns these, so a run that is only wrong about them is still ready to
            // launch — while the runner's own guard still refuses them, in case
            // nothing set them.
            var state = Ready(s =>
            {
                s.ReplayingBakedTrack = false;
                s.TrackMode = "Off";
                s.LoggingEnabled = false;
                s.SessionRunnerEnabled = false;
                s.XrStartsOnPlay = false;
                s.DeveloperOverlayOn = "Conversation";
            });

            Assert.That(StudySessionRules.ReadyToLaunch(state), Is.True);
            Assert.That(StudySessionRules.FirstProblem(state), Is.Not.Null);
        }

        [Test]
        public void AReusedLabelBlocksTheLaunch()
        {
            // Not something a launch can put right, unlike the fields above.
            Assert.That(StudySessionRules.ReadyToLaunch(Ready(s => s.ParticipantHasRun = true)), Is.False);
        }
    }
}
