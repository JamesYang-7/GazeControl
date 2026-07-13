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
    /// Gaze follows a selectable turn-taking prototype from the ICMI paper
    /// (<see cref="GazePatterns"/>, transcribed from the raw data), played in the
    /// last second of A's turn with both agents' role tracks running in parallel,
    /// optionally stretched by <see cref="PatternTimeScale"/> for visibility.
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
        [field: Tooltip("Stretch factor for the gaze pattern. 1 = data-faithful; raise to make fast segments easier to see.")]
        public float PatternTimeScale { get; set; } = 1f;

        public enum PreTurnPattern
        {
            DoubleGlance8d,
            CheckAvertReengage7e,
        }

        [field: SerializeField]
        [field: Tooltip("Which turn-taking prototype plays before the A→B hand-over")]
        public PreTurnPattern Pattern { get; set; } = PreTurnPattern.CheckAvertReengage7e;

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

                // Play the selected turn-taking prototype in the last second of A's
                // turn, straight from the raw data: both agents' tracks run in
                // parallel and each holds its final gaze until the turn switch.
                var pattern = Pattern == PreTurnPattern.DoubleGlance8d
                    ? GazePatterns.TurnYieldingDoubleGlance
                    : GazePatterns.CheckAvertReengage;
                // End-of-turn is when the speaker stops talking, which can be before
                // clip end (TTS clips carry trailing silence) — anchor to speech end.
                var endOfTurn = SpeechEndSeconds(question);
                var windowStart = endOfTurn - PreTurnWindowSeconds * PatternTimeScale;
                await Awaitable.WaitForSecondsAsync(Mathf.Max(0f, windowStart), ct);
                await Awaitable.WaitForSecondsAsync(pattern.WindowOffsetSeconds * PatternTimeScale, ct);
                var trackA = PlayTrack(GazeA, pattern.CurrentSpeakerTrack, ct);
                var trackB = PlayTrack(GazeB, pattern.NextSpeakerTrack, ct);
                await trackA;
                await trackB;
                await WaitWhileSpeaking(SpeakerA, ct);

                await Awaitable.WaitForSecondsAsync(TurnGapSeconds, ct);

                // Turn switch: user (listener) moves to the new speaker; A now
                // listens and looks at B.
                if (ListenerView != null)
                    ListenerView.SetTarget(GazeB.Head);
                GazeA.SetTarget(GazeB.Head);

                SpeakerB.PlayClip(answer);

                // The 7e prototype ends with the new speaker's gaze averted at turn
                // onset; engaging the addressee shortly after is a heuristic bridge
                // beyond the 1 s data window.
                await Awaitable.WaitForSecondsAsync(0.8f, ct);
                GazeB.SetTarget(GazeA.Head);
                await WaitWhileSpeaking(SpeakerB, ct);

                foreach (var player in MotionPlayers)
                    player.enabled = false; // freezes the skeleton at the current pose
            }
            catch (OperationCanceledException)
            {
                // play mode ended / object destroyed mid-conversation; nothing to clean up
            }
        }

        /// <summary>Time of the last non-silent sample — the end-of-turn anchor for the gaze window.</summary>
        static float SpeechEndSeconds(AudioClip clip)
        {
            const float silenceThreshold = 0.01f;
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            for (var i = samples.Length - 1; i >= 0; i--)
            {
                if (Mathf.Abs(samples[i]) > silenceThreshold)
                    return (float)(i + 1) / (clip.frequency * clip.channels);
            }
            return clip.length;
        }

        /// <summary>Play one role's (duration, target) track on an agent, holding the final target.</summary>
        async Awaitable PlayTrack(GazeController gaze, (float seconds, GazeRole target)[] track, CancellationToken ct)
        {
            foreach (var (seconds, target) in track)
            {
                gaze.SetTarget(ResolveRole(target));
                await Awaitable.WaitForSecondsAsync(seconds * PatternTimeScale, ct);
            }
        }

        /// <summary>Role → transform mapping for the A→B hand-over (A = current speaker, B = next speaker, user = listener).</summary>
        Transform ResolveRole(GazeRole role) => role switch
        {
            GazeRole.CurrentSpeaker => GazeA.Head,
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
