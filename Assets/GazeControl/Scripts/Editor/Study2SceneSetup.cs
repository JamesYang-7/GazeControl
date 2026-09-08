using System.IO;
using GazeControl.Conversation;
using GazeControl.Experiment;
using GazeControl.Motion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Study 2 → Set Up Scene: builds (or repairs) the study 2
    /// scene as a copy of TriadScene whose agents are placed by the clip
    /// instead of by authored vertices.
    ///
    /// <para><b>Study 2 replaces study 1's corpus and nothing else</b> (user,
    /// 2026-09-08). Everything that measured study 1 — the gaze runner, the
    /// conditions, the baker, the questionnaire, the XR rig, the fixed
    /// viewpoint — is copied unchanged. What changes is where the two agents
    /// stand: a 3People-2022 clip carries each person's real position in the
    /// capture room, so the agents are parented to one <c>Room</c> object with
    /// identity local transforms and raw root translation, and
    /// <see cref="RecordedRoomPlacement"/> turns and slides that room for each
    /// clip so that the listener's measured seat lands on the User vertex and
    /// the seat's opening facing lands on the vertex's forward axis. The
    /// participant's viewpoint is therefore the same point, at the same height,
    /// facing the same way, as in study 1.</para>
    ///
    /// <para>A copy rather than a mode switch on TriadScene, because the two
    /// designs want contradictory things from the same objects (vertex-anchored
    /// against clip-placed agents), and a scene that is one flag away from the
    /// other study is a scene a bake can inherit half-switched.</para>
    /// </summary>
    public static class Study2SceneSetup
    {
        public const string ScenePath = "Assets/Scenes/Study2Scene.unity";
        public const string SourceScenePath = "Assets/Scenes/TriadScene.unity";

        const string RoomObjectName = "Room";
        const string UserObjectName = "User";
        const string ConversationObjectName = "Conversation";
        const string RunnerObjectName = "GazeCondition";
        const string SegmentRoot = "Assets/DemoSegments";

        /// <summary>Where this study's logs, session folders and demo videos go; study 1's are under Recordings/Study_01.</summary>
        const string RecordingsRoot = "Recordings/Study_02";

        /// <summary>The seven clips of `Tools/export_3people_segments.py`, in block order.</summary>
        static readonly string[] k_Conversations =
        {
            "study2_c1", "study2_c2", "study2_c3", "study2_c4", "study2_c5", "study2_c6", "study2_c7",
        };

        static readonly string[] k_AgentNames = { "AgentA", "AgentB" };

        [MenuItem("GazeControl/Study 2/Set Up Scene")]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("Set Up Study 2 Scene: leave play mode first — this opens a scene.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = OpenOrCopy();
            if (!scene.IsValid())
                return;

            var user = Find(scene, UserObjectName);
            var conversationObject = Find(scene, ConversationObjectName);
            var runnerObject = Find(scene, RunnerObjectName);
            if (user == null || conversationObject == null || runnerObject == null)
            {
                Debug.LogError(
                    $"Set Up Study 2 Scene: {ScenePath} is missing '{UserObjectName}', '{ConversationObjectName}' " +
                    $"or '{RunnerObjectName}'; it does not look like a copy of TriadScene.");
                return;
            }

            var conversation = conversationObject.GetComponent<RecordedConversation>();
            var session = runnerObject.GetComponent<StudySessionRunner>();
            var runner = runnerObject.GetComponent<GazeConditionRunner>();
            if (conversation == null || session == null || runner == null)
            {
                Debug.LogError("Set Up Study 2 Scene: the conversation, session runner or gaze runner component is missing.");
                return;
            }

            // Its own folder under Recordings/, so study 2's participant labels,
            // take logs and P00 debugging runs never mix with study 1's, and the
            // roster proposes labels from this study's folders alone.
            runner.OutputDirectory = RecordingsRoot;
            EditorUtility.SetDirty(runner);
            var questionnaire = runnerObject.GetComponent<QuestionnaireSession>();
            if (questionnaire != null)
            {
                questionnaire.OutputDirectory = RecordingsRoot;
                EditorUtility.SetDirty(questionnaire);
            }

            var room = BuildRoom(scene, user.transform);
            if (room == null)
                return;

            conversation.Placement = room;
            conversation.SegmentPath = $"{SegmentRoot}/{k_Conversations[0]}/segment.json";
            EditorUtility.SetDirty(conversation);

            session.Conversations = (string[])k_Conversations.Clone();
            session.SegmentRoot = SegmentRoot;

            // One seed per clip, as the runner requires. Study 1 chose its seeds
            // by scanning for a representative aversion fraction; nothing has been
            // scanned for these clips yet, so every seed starts at 1.
            if (session.Seeds == null || session.Seeds.Length != k_Conversations.Length)
            {
                session.Seeds = new int[k_Conversations.Length];
                for (var i = 0; i < session.Seeds.Length; i++)
                    session.Seeds[i] = 1;
            }

            EditorUtility.SetDirty(session);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"Set Up Study 2 Scene: could not save {ScenePath}.");
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Set Up Study 2 Scene: {ScenePath} — AgentA and AgentB under '{RoomObjectName}', placed by each " +
                $"clip and moved onto the User vertex per segment; {k_Conversations.Length} conversations on the " +
                "session runner. Export the segments with Tools/export_3people_segments.py, then bake the tracks " +
                "from the Clip Browser.", room);
        }

        static Scene OpenOrCopy()
        {
            if (File.Exists(ScenePath))
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            if (!File.Exists(SourceScenePath))
            {
                Debug.LogError($"Set Up Study 2 Scene: {SourceScenePath} is missing; nothing to copy.");
                return default;
            }

            // Copied through the asset database rather than saved-as from the
            // open scene, so TriadScene itself is never marked dirty or saved.
            if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
            {
                Debug.LogError($"Set Up Study 2 Scene: could not copy {SourceScenePath} to {ScenePath}.");
                return default;
            }

            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// The room: both agents under one identity-placed transform, in raw
        /// root-translation mode. Their old vertex positions are discarded — the
        /// clip decides where they stand.
        /// </summary>
        static RecordedRoomPlacement BuildRoom(Scene scene, Transform viewpoint)
        {
            var roomObject = Find(scene, RoomObjectName);
            if (roomObject == null)
            {
                roomObject = new GameObject(RoomObjectName);
                SceneManager.MoveGameObjectToScene(roomObject, scene);
            }

            roomObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            roomObject.transform.localScale = Vector3.one;

            foreach (var agentName in k_AgentNames)
            {
                var agent = Find(scene, agentName) ?? roomObject.transform.Find(agentName)?.gameObject;
                if (agent == null)
                {
                    Debug.LogError($"Set Up Study 2 Scene: no '{agentName}' in the scene.");
                    return null;
                }

                var motion = agent.GetComponent<SmplxMotionPlayer>();
                if (motion == null)
                {
                    Debug.LogError($"Set Up Study 2 Scene: '{agentName}' has no SmplxMotionPlayer.", agent);
                    return null;
                }

                agent.transform.SetParent(roomObject.transform, worldPositionStays: false);
                agent.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                agent.transform.localScale = Vector3.one;

                motion.ApplyRootTranslation = true;
                motion.RootTranslationRelative = false;
                EditorUtility.SetDirty(motion);
            }

            var placement = roomObject.GetComponent<RecordedRoomPlacement>();
            if (placement == null)
                placement = roomObject.AddComponent<RecordedRoomPlacement>();

            placement.Viewpoint = viewpoint;
            EditorUtility.SetDirty(placement);
            return placement;
        }

        static GameObject Find(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root;
            }

            return null;
        }
    }
}
