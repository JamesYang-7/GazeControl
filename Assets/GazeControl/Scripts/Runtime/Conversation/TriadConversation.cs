using System;
using System.Threading;
using GazeControl.Motion;
using GazeControl.TTS;
using UnityEngine;

namespace GazeControl.Conversation
{
    /// <summary>
    /// Scripted first demo: agent A asks a question, agent B answers, and the
    /// body animation freezes when B finishes. Both utterances are generated
    /// before A starts so there is no TTS delay between the turns.
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

        async Awaitable Start()
        {
            try
            {
                var ct = destroyCancellationToken;

                var question = await SpeakerA.GenerateAsync(QuestionText);
                var answer = await SpeakerB.GenerateAsync(AnswerText);

                SpeakerA.PlayClip(question);
                await WaitWhileSpeaking(SpeakerA, ct);

                await Awaitable.WaitForSecondsAsync(TurnGapSeconds, ct);

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
