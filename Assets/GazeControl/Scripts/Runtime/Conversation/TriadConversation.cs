using System;
using GazeControl.Gaze;
using GazeControl.Motion;
using GazeControl.TTS;
using UnityEngine;

namespace GazeControl.Conversation
{
    /// <summary>
    /// Scripted first demo: agent A asks a question, agent B answers, and the body
    /// animation freezes when B finishes. Both utterances are generated before A
    /// starts so there is no TTS delay between the turns.
    ///
    /// This drives speech and turn timing only — gaze belongs entirely to the
    /// experimental condition's policy. It publishes each turn to a
    /// <see cref="ConversationDirector"/> so a policy can see a boundary coming;
    /// with no director wired, the demo still runs, just without a schedule for
    /// anyone to read.
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
        [field: Tooltip("Agent A's gaze actuator; identifies A as the speaker to the director")]
        public GazeController GazeA { get; set; }

        [field: SerializeField]
        [field: Tooltip("Agent B's gaze actuator; identifies B as the speaker to the director")]
        public GazeController GazeB { get; set; }

        [field: SerializeField]
        [field: Tooltip("Publishes this script's turn schedule to the gaze policies")]
        public ConversationDirector Director { get; set; }

        /// <summary>True once the answer has finished and the motion players are frozen.</summary>
        public bool HasFinished { get; private set; }

        async Awaitable Start()
        {
            try
            {
                var ct = destroyCancellationToken;

                var question = await SpeakerA.GenerateAsync(QuestionText);
                var answer = await SpeakerB.GenerateAsync(AnswerText);

                await PlayTurnAsync(SpeakerA, GazeA, question);
                await Awaitable.WaitForSecondsAsync(TurnGapSeconds, ct);
                await PlayTurnAsync(SpeakerB, GazeB, answer);

                foreach (var player in MotionPlayers)
                    player.enabled = false; // freezes the skeleton at the current pose

                HasFinished = true;
            }
            catch (OperationCanceledException)
            {
                // play mode ended / object destroyed mid-conversation; nothing to clean up
            }
        }

        /// <summary>
        /// Speak one utterance, announcing its end to the director *before* it is
        /// audible: the pre-turn gaze has to begin before the utterance ends, so
        /// the schedule cannot be published reactively (§5.4).
        /// </summary>
        async Awaitable PlayTurnAsync(TtsSpeaker speaker, GazeController gaze, AudioClip clip)
        {
            // End-of-turn is when the speaker stops talking, which can be before
            // clip end (TTS clips carry trailing silence) — anchor to speech end.
            var speechSeconds = SpeechEndSeconds(clip);

            speaker.PlayClip(clip);

            if (Director != null)
                Director.BeginTurn(gaze, speechSeconds);

            while (speaker.IsSpeaking)
                await Awaitable.NextFrameAsync(destroyCancellationToken);
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
    }
}
