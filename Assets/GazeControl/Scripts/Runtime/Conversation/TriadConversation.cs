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
    /// (Assets/Docs): in the last second of the current speaker's turn, they
    /// glance at the next speaker twice (otherwise gaze None); the next speaker's
    /// gaze stays None; the listener — the user, whose camera plays the listener
    /// track — stays on the current speaker and moves to B when B takes the turn.
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

                // Fig. 8d pattern in the last second of A's turn: two glances at B.
                await Awaitable.WaitForSecondsAsync(Mathf.Max(0f, question.length - PreTurnWindowSeconds), ct);
                await GazeA.GlanceAsync(GazeB.Head, 0.25f, ct);
                await Awaitable.WaitForSecondsAsync(0.15f, ct);
                await GazeA.GlanceAsync(GazeB.Head, 0.3f, ct);
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

        static async Awaitable WaitWhileSpeaking(TtsSpeaker speaker, CancellationToken ct)
        {
            while (speaker.IsSpeaking)
                await Awaitable.NextFrameAsync(ct);
        }
    }
}
