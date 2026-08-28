using GazeControl.Experiment;
using GazeControl.Xr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Set Up Questionnaire: builds the two surfaces the
    /// questionnaire needs into the open scene, and repairs them if either is
    /// missing.
    ///
    /// <para>A menu command for the same reason as <see cref="XrRigSetup"/>: the
    /// scene keeps what this produces, but the wiring is written down as code
    /// rather than as hand-edited YAML, so it can be reviewed and re-run.</para>
    ///
    /// <para>The panel hangs off the participant's own vertex, so it sits where
    /// they are facing however the play area is recentred, at the same eye line
    /// as the agents and at the distance the agents stand.</para>
    /// </summary>
    public static class QuestionnaireSetup
    {
        const string UserObjectName = "User";
        const string PanelObjectName = "Questionnaire Panel";

        /// <summary>
        /// How far in front of the participant the panel hangs, metres. The
        /// agents stand 1.5 m away, so this puts the reading distance and the
        /// distance they have been watching at the same convergence — and inside
        /// the Varjo's focus area.
        /// </summary>
        const float PanelDistance = 1.5f;

        [MenuItem("GazeControl/Set Up Questionnaire")]
        public static void SetUp()
        {
            var user = GameObject.Find(UserObjectName);
            if (user == null)
            {
                Debug.LogError(
                    $"Set Up Questionnaire: open TriadScene first — no '{UserObjectName}' object in the open scene.");
                return;
            }

            var session = Object.FindAnyObjectByType<StudySessionRunner>(FindObjectsInactive.Include);
            var pause = Object.FindAnyObjectByType<ClipPauseController>(FindObjectsInactive.Include);
            var runner = Object.FindAnyObjectByType<GazeConditionRunner>(FindObjectsInactive.Include);

            if (session == null || pause == null || runner == null)
            {
                Debug.LogError(
                    "Set Up Questionnaire: the scene needs a StudySessionRunner, a ClipPauseController and a " +
                    "GazeConditionRunner first — run GazeControl → Set Up Baseline Condition.");
                return;
            }

            Undo.SetCurrentGroupName("Set Up Questionnaire");
            var undoGroup = Undo.GetCurrentGroup();

            var display = FindOrCreateDisplay(user.transform);

            var questionnaire = session.GetComponent<QuestionnaireSession>();
            if (questionnaire == null)
                questionnaire = Undo.AddComponent<QuestionnaireSession>(session.gameObject);

            Undo.RecordObject(questionnaire, "Configure questionnaire session");
            questionnaire.Session = session;
            questionnaire.Pause = pause;
            questionnaire.Runner = runner;
            questionnaire.Display = display;
            questionnaire.OutputDirectory = runner.OutputDirectory;

            var panel = session.GetComponent<QuestionnaireOperatorPanel>();
            if (panel == null)
                panel = Undo.AddComponent<QuestionnaireOperatorPanel>(session.gameObject);

            Undo.RecordObject(panel, "Configure questionnaire operator panel");
            panel.Session = questionnaire;

            WarnIfTheAgentsAreNotHiddenWhileAnswering(pause);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(user.scene);

            Debug.Log(
                $"Set Up Questionnaire: participant panel on '{display.name}' at {PanelDistance:F2} m, " +
                $"{ParticipantEyeHeight.SmplxEyeHeight:F3} m up; operator panel on '{session.name}'. " +
                "The participant reads and answers aloud; the operator types it. Nothing is written unless " +
                "the study session runner is enabled.", questionnaire);
        }

        /// <summary>
        /// The panel is a child of the participant vertex with identity rotation.
        ///
        /// <para>That is what makes it readable: a world-space canvas faces along
        /// its own forward axis away from the viewer — the default scene has the
        /// camera at negative Z looking at an unrotated canvas — and the vertex's
        /// forward already points from the participant at the agents' midpoint.
        /// Parenting it also means recentring the play area cannot leave the
        /// panel behind the participant, because recentring turns the offset
        /// inside this same vertex.</para>
        /// </summary>
        static QuestionnaireDisplay FindOrCreateDisplay(Transform user)
        {
            var existing = user.Find(PanelObjectName);
            var display = existing != null ? existing.GetComponent<QuestionnaireDisplay>() : null;

            if (display == null)
            {
                var created = new GameObject(
                    PanelObjectName, typeof(RectTransform), typeof(Canvas), typeof(QuestionnaireDisplay));
                Undo.RegisterCreatedObjectUndo(created, "Create questionnaire panel");
                Undo.SetTransformParent(created.transform, user, "Parent questionnaire panel");
                display = created.GetComponent<QuestionnaireDisplay>();
            }

            Undo.RecordObject(display, "Place questionnaire panel");
            display.Distance = PanelDistance;
            display.EyeHeight = ParticipantEyeHeight.SmplxEyeHeight;

            // The component owns its own placement, so the scene view shows it
            // where a play session will put it.
            display.ApplyPlacement();
            return display;
        }

        /// <summary>
        /// The agents must be in the pause controller's hide list, or a
        /// participant reads the items with two agents standing behind them,
        /// gazing in a state no condition was baked to produce.
        /// </summary>
        static void WarnIfTheAgentsAreNotHiddenWhileAnswering(ClipPauseController pause)
        {
            if (pause.HideWhilePaused != null && pause.HideWhilePaused.Length > 0)
                return;

            Debug.LogWarning(
                $"Set Up Questionnaire: '{pause.name}' has nothing in HideWhilePaused, so the agents will stand " +
                "behind every questionnaire screen. Put both agents in that list.", pause);
        }
    }
}
