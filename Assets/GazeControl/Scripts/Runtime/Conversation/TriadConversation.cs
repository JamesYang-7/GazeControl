using System;
using System.Threading;
using GazeControl.Gaze;
using GazeControl.Motion;
using GazeControl.TTS;
using UnityEngine;

namespace GazeControl.Conversation
{
    /// <summary>
    /// Scripted first demo: agent A asks a question, agent B answers, and the
    /// body animation freezes when B finishes. Both utterances are generated
    /// before A starts so there is no TTS delay between the turns.
    ///
    /// Gaze follows the "turn-taking" prototype of Fig. 8d in the ICMI paper
    /// (raw data: p3_ks40_4): early in the last second of the current speaker's
    /// turn they micro-glance at the next speaker twice, then hold gaze aversion
    /// (None) up to the turn boundary; the next speaker's gaze stays None. The
    /// pattern plays verbatim from <see cref="GazePatterns"/> at 60 fps, optionally
    /// stretched by <see cref="PatternTimeScale"/> for visibility.
    /// </summary>
    public class TriadConversation : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Speaker on agent A (asks the question)")]
        public TtsSpeaker SpeakerA { get; set; }

        [field: SerializeField]
        [field: Tooltip("Speaker on agent B (gives the answer)")]
        public TtsSpeaker SpeakerB { get; set; }

        [field: SerializeField]
        [field: TextArea(2, 4)]
        public string QuestionText { get; set; }

        [field: SerializeField]
        [field: TextArea(2, 4)]
        public string AnswerText { get; set; }

        [field: SerializeField]
        [field: Tooltip("Pause between A finishing and B starting, seconds")]
        public float TurnGapSeconds { get; set; } = 0.4f;

        [field: SerializeField]
        [field: Tooltip("Motion players frozen (disabled mid-pose) when B finishes the answer")]
        public SmplxMotionPlayer[] MotionPlayers { get; set; }

        [field: SerializeField]
        public GazeController GazeA { get; set; }

        [field: SerializeField]
        public GazeController GazeB { get; set; }

        [field: SerializeField]
        [field: Tooltip("Optional: user view playing the listener gaze track. Leave empty for a fixed camera that shows both agents.")]
        public ListenerCamera ListenerView { get; set; }

        [field: SerializeField]
        [field: Tooltip("Pre-turn gaze window: the pattern runs in the last second of A's turn (paper uses 1 s windows)")]
        public float PreTurnWindowSeconds { get; set; } = 1f;

        [field: SerializeField]
        [field: Tooltip("Stretch factor for the gaze pattern. 1 = data-faithful (the two glances span ~0.1 s); raise to make the micro-glances easier to see.")]
        public float PatternTimeScale { get; set; } = 1f;

        async Awaitable Start()
        {
            try
            {
                var ct = destroyCancellationToken;

                var question = await SpeakerA.GenerateAsync(QuestionText);
                var answer = await SpeakerB.GenerateAsync(AnswerText);

                // Pre-turn roles (Fig. 8d): speaker A gaze None, next speaker B gaze
                // None, listener (user camera) on the current speaker.
                GazeA.SetTarget(null);
                GazeB.SetTarget(null);
                if (ListenerView != null)
                    ListenerView.SetTarget(GazeA.Head);

                SpeakerA.PlayClip(question);

                // p3_ks40_4 (Fig. 8d) in the last second of A's turn, straight from the
                // raw prototype: double micro-glance at B, then aversion to the boundary.
                var windowStart = question.length - PreTurnWindowSeconds * PatternTimeScale;
                await Awaitable.WaitForSecondsAsync(Mathf.Max(0f, windowStart), ct);
                await Awaitable.WaitForSecondsAsync(GazePatterns.TurnYieldingWindowOffsetSeconds * PatternTimeScale, ct);
                foreach (var (seconds, target) in GazePatterns.TurnYieldingDoubleGlance)
                {
                    GazeA.SetTarget(ResolveRoleForA(target));
                    await Awaitable.WaitForSecondsAsync(seconds * PatternTimeScale, ct);
                }
                GazeA.SetTarget(null); // hold the pattern's final aversion until the turn
                await WaitWhileSpeaking(SpeakerA, ct);

                await Awaitable.WaitForSecondsAsync(TurnGapSeconds, ct);

                // Turn switch: user (listener) moves to the new speaker; A now
                // listens and looks at B; B addresses A.
                if (ListenerView != null)
                    ListenerView.SetTarget(GazeB.Head);
                GazeA.SetTarget(GazeB.Head);
                GazeB.SetTarget(GazeA.Head);

                SpeakerB.PlayClip(answer);
                await WaitWhileSpeaking(SpeakerB, ct);

                foreach (var player in MotionPlayers)
                    player.enabled = false; // freezes the skeleton at the current pose
            }
            catch (OperationCanceledException)
            {
                // play mode ended / object destroyed mid-conversation; nothing to clean up
            }
        }

        /// <summary>Role → transform mapping for agent A as the current speaker (user is the listener).</summary>
        Transform ResolveRoleForA(GazeRole role) => role switch
        {
            GazeRole.NextSpeaker => GazeB.Head,
            GazeRole.Listener => Camera.main != null ? Camera.main.transform : null,
            _ => null,
        };

        static async Awaitable WaitWhileSpeaking(TtsSpeaker speaker, CancellationToken ct)
        {
            while (speaker.IsSpeaking)
                await Awaitable.NextFrameAsync(ct);
        }
    }
}
