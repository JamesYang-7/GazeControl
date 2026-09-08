using GazeControl.Conversation;
using GazeControl.Experiment;
using GazeControl.Gaze;
using GazeControl.Motion;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Set Up Gaze Conditions: wires the experiment machinery into
    /// the open scene — a <see cref="GazeParticipant"/> on each triad member, a
    /// <see cref="RecordedConversation"/> playing the chosen corpus segment, and
    /// a <see cref="GazeConditionRunner"/> to drive them.
    ///
    /// A menu command rather than the only source of the wiring: the scene keeps
    /// what this produces, and re-running it repairs the rig after a refactor
    /// without anyone hand-editing scene YAML.
    /// </summary>
    public static class BaselineConditionSetup
    {
        const string RunnerObjectName = "GazeCondition";
        const string ConversationObjectName = "Conversation";
        const string DefaultSegmentPath = "Assets/DemoSegments/case1_seg01/segment.json";

        [MenuItem("GazeControl/Set Up Gaze Conditions")]
        public static void SetUp()
        {
            var agentA = GameObject.Find("AgentA");
            var agentB = GameObject.Find("AgentB");
            var user = GameObject.Find("User");

            if (agentA == null || agentB == null || user == null)
            {
                Debug.LogError("Set Up Gaze Conditions: open TriadScene first (AgentA, AgentB and User must exist).");
                return;
            }

            Undo.SetCurrentGroupName("Set Up Gaze Conditions");
            var undoGroup = Undo.GetCurrentGroup();

            var participantA = ConfigureAgent(agentA, id: 0, "AgentA");
            var participantB = ConfigureAgent(agentB, id: 1, "AgentB");
            var participantUser = ConfigureHuman(user, id: 2, "User");

            if (participantA == null || participantB == null)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                return;
            }

            participantA.DefaultAddressee = participantB;
            participantB.DefaultAddressee = participantA;

            var participants = new[] { participantA, participantB, participantUser };
            ConfigureRunner(participants);

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"Set Up Gaze Conditions: wired AgentA, AgentB and User to a GazeConditionRunner on '{RunnerObjectName}'.");
        }

        static GazeParticipant ConfigureAgent(GameObject agent, int id, string displayName)
        {
            var gaze = agent.GetComponent<GazeController>();
            if (gaze == null)
            {
                Debug.LogError($"Set Up Gaze Conditions: {displayName} has no GazeController.", agent);
                return null;
            }

            // The head bone is both the gaze actuator's root and where the other
            // participants aim, so it is looked up the same way GazeController does.
            var head = SmplxAnimUtils.FindChildRecursive(agent.transform, "head");
            if (head == null)
            {
                Debug.LogError($"Set Up Gaze Conditions: {displayName} has no 'head' bone.", agent);
                return null;
            }

            var participant = GetOrAddComponent<GazeParticipant>(agent);
            participant.Id = id;
            participant.DisplayName = displayName;
            participant.IsHuman = false;
            participant.LookAtAnchor = head;
            participant.Gaze = gaze;
            participant.Voice = FindVoice(agent);

            if (participant.Voice == null)
                Debug.LogWarning($"Set Up Gaze Conditions: {displayName} has no AudioSource, so it will never register as speaking.", agent);

            EditorUtility.SetDirty(participant);
            return participant;
        }

        static GazeParticipant ConfigureHuman(GameObject user, int id, string displayName)
        {
            var participant = GetOrAddComponent<GazeParticipant>(user);
            participant.Id = id;
            participant.DisplayName = displayName;
            participant.IsHuman = true;

            // Aim at the camera, not the User root: the root sits on the floor and
            // the agents would gaze at the user's feet.
            var camera = user.GetComponentInChildren<Camera>();
            participant.LookAtAnchor = camera != null ? camera.transform : user.transform;
            participant.Gaze = null;
            participant.Voice = null;
            participant.DefaultAddressee = null;

            EditorUtility.SetDirty(participant);
            return participant;
        }

        static void ConfigureRunner(GazeParticipant[] participants)
        {
            var runnerObject = GameObject.Find(RunnerObjectName);
            if (runnerObject == null)
            {
                runnerObject = new GameObject(RunnerObjectName);
                Undo.RegisterCreatedObjectUndo(runnerObject, "Create GazeCondition");
            }

            var runner = GetOrAddComponent<GazeConditionRunner>(runnerObject);
            runner.Participants = participants;

            // Repair components wired before the video and the gaze log were
            // brought together under one folder per take.
            if (string.IsNullOrEmpty(runner.OutputDirectory))
                runner.OutputDirectory = "Recordings/Study_01";

            if (string.IsNullOrEmpty(runner.CaseName))
                runner.CaseName = "case1_01";

            // The director sits on the runner object rather than on the
            // conversation: it is experiment machinery, and keeping it here means
            // the whole condition rig can be removed by deleting one object.
            var director = GetOrAddComponent<ConversationDirector>(runnerObject);
            director.Participants = participants;
            runner.Director = director;

            runner.Conversation = ConfigureConversation(participants, director);

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(runner);
        }

        /// <summary>
        /// Wire the recorded segment onto the two agents: corpus speaker 1
        /// (main-agent) plays agent A and speaker 2 (interloctr) plays agent B.
        /// The pairing is the corpus's own — both sides come from one take, and
        /// mixing takes would put two unrelated conversations in one room.
        /// </summary>
        static RecordedConversation ConfigureConversation(GazeParticipant[] participants, ConversationDirector director)
        {
            var conversationObject = GameObject.Find(ConversationObjectName);
            if (conversationObject == null)
            {
                conversationObject = new GameObject(ConversationObjectName);
                Undo.RegisterCreatedObjectUndo(conversationObject, "Create Conversation");
            }

            var conversation = GetOrAddComponent<RecordedConversation>(conversationObject);
            conversation.Director = director;

            if (string.IsNullOrEmpty(conversation.SegmentPath))
                conversation.SegmentPath = DefaultSegmentPath;

            conversation.Speakers = new[]
            {
                SpeakerFor(participants[0], speakerCode: 1),
                SpeakerFor(participants[1], speakerCode: 2),
            };

            EditorUtility.SetDirty(conversation);
            return conversation;
        }

        static RecordedConversation.Speaker SpeakerFor(GazeParticipant participant, int speakerCode)
        {
            var motion = participant.GetComponentInChildren<SmplxMotionPlayer>(includeInactive: true);
            if (motion == null)
                Debug.LogWarning($"Set Up Gaze Conditions: {participant.DisplayName} has no SmplxMotionPlayer.", participant);

            return new RecordedConversation.Speaker
            {
                SpeakerCode = speakerCode,
                Participant = participant,
                Motion = motion,
                // Left empty on purpose: the conversation loads the trimmed wav
                // named in the segment file, so switching segments is one path.
                Clip = null,
            };
        }

        /// <summary>
        /// The speech AudioSource sits on the imported SMPL-X child (next to the
        /// lipsync context), not on the agent root, so a plain GetComponent
        /// misses it.
        /// </summary>
        static AudioSource FindVoice(GameObject agent) =>
            agent.GetComponentInChildren<AudioSource>(includeInactive: true);

        static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            var existing = target.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(target);
        }
    }
}
