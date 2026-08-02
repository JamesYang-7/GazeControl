using GazeControl.Conversation;
using GazeControl.Experiment;
using GazeControl.Gaze;
using GazeControl.Motion;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Set Up Gaze Conditions: wires the baseline machinery into the
    /// open scene — a <see cref="GazeParticipant"/> on each triad member and a
    /// <see cref="GazeConditionRunner"/> to drive them.
    ///
    /// Deliberately a menu command rather than committed scene content: the scene
    /// carries unrelated in-flight edits, and this keeps the condition wiring
    /// opt-in and undoable instead of silently changing the demo.
    /// </summary>
    public static class BaselineConditionSetup
    {
        const string RunnerObjectName = "GazeCondition";

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

            // "Logs" is Unity's own editor log folder; repair components wired
            // before the default moved, so gaze CSVs do not land among them.
            if (string.IsNullOrEmpty(runner.LogDirectory) || runner.LogDirectory == "Logs")
                runner.LogDirectory = "GazeLogs";

            runner.Conversation = Object.FindAnyObjectByType<TriadConversation>();

            if (runner.Conversation == null)
                Debug.LogWarning("Set Up Gaze Conditions: no TriadConversation found; its gaze control cannot be switched off automatically.");

            // The director sits on the runner object rather than on the scripted
            // conversation: it is experiment machinery, and keeping it here means
            // the whole condition rig can be removed by deleting one object.
            var director = GetOrAddComponent<ConversationDirector>(runnerObject);
            director.Participants = participants;
            runner.Director = director;

            if (runner.Conversation != null)
                runner.Conversation.Director = director;

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(runner);

            if (runner.Conversation != null)
                EditorUtility.SetDirty(runner.Conversation);
        }

        /// <summary>
        /// The speech AudioSource sits on the imported SMPL-X child alongside the
        /// TtsSpeaker, not on the agent root, so a plain GetComponent misses it.
        /// </summary>
        static AudioSource FindVoice(GameObject agent)
        {
            var speaker = agent.GetComponentInChildren<TTS.TtsSpeaker>(includeInactive: true);
            return speaker != null
                ? speaker.GetComponent<AudioSource>()
                : agent.GetComponentInChildren<AudioSource>(includeInactive: true);
        }

        static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            var existing = target.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(target);
        }
    }
}
