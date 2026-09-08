using System.IO;
using GazeControl.Motion;
using GazeControl.ThreeParty;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → 3People → Set Up Replay Scene: builds (or repairs) the
    /// scene the three-party captures are watched in.
    ///
    /// <para>A menu command for the same reason as <see cref="XrRigSetup"/>: the
    /// scene keeps what this produces, but the wiring is written down as code
    /// rather than as hand-edited YAML, so it can be reviewed and re-run.</para>
    ///
    /// <para><b>Its own scene, not TriadScene.</b> The two want opposite things
    /// from a camera — the study's viewpoint is fixed because it is a controlled
    /// variable, and this one orbits because the three participants face each
    /// other and a fixed camera always has someone's back to it. Sharing a scene
    /// would also mean turning the conversation, the gaze runner, the
    /// questionnaire and the XR rig off before watching a source session and on
    /// again afterwards. Nothing here is measured, so nothing here belongs in the
    /// scene the measurements come from.</para>
    ///
    /// <para>The three agents stand at the origin with identity transforms and
    /// are placed by the clips themselves: each converted npz carries the
    /// participant's real position in the capture room, and the session's three
    /// clips share that frame. Moving one of these transforms moves that
    /// participant out of the room they were recorded in.</para>
    /// </summary>
    public static class ThreePartyReplaySetup
    {
        public const string ScenePath = "Assets/Scenes/ThreePartyReplay.unity";

        const string BodyFbxPath = "Assets/SMPLX/smplx-visemes-unity.fbx";
        const string ReplayObjectName = "Replay";
        const string LipSyncObjectName = "LipSync";
        const string GroundObjectName = "Ground";

        /// <summary>
        /// Near clip for a camera that closes in on faces. The study scene's
        /// 0.3 m would put a dead zone exactly where this camera is used most.
        /// </summary>
        const float NearClipPlane = 0.05f;

        /// <summary>
        /// One body appearance per seat, so two participants who pass in front of
        /// each other stay tellable apart.
        ///
        /// <para>Which is which means nothing here. The capture records no
        /// appearance, and the overlay's name plates are what identify anybody.
        /// The study scene's rule that appearance follows a segment's measured
        /// voices (<c>AgentSkins</c>) governs takes a participant rates, and
        /// there is no participant here.</para>
        /// </summary>
        static readonly string[] k_SeatMaterials =
        {
            "Assets/SMPLX/Materials/SMPLX-Male-URP.mat",
            "Assets/SMPLX/Materials/SMPLX-Female-URP-AgentA.mat",
            "Assets/SMPLX/Materials/SMPLX-Male-URP-AgentB.mat",
        };

        [MenuItem("GazeControl/3People/Set Up Replay Scene")]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("Set Up Replay Scene: leave play mode first — this opens a scene.");
                return;
            }

            var body = AssetDatabase.LoadAssetAtPath<GameObject>(BodyFbxPath);
            if (body == null)
            {
                Debug.LogError($"Set Up Replay Scene: the SMPL-X body is missing at {BodyFbxPath}.");
                return;
            }

            // This replaces whatever is open, so the operator gets the ordinary
            // "save your changes?" prompt rather than losing an edit to TriadScene.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            BuildGround(scene);
            EnsureLipSyncManager(scene);

            var replayObject = FindOrCreate(scene, ReplayObjectName);
            var replay = GetOrAdd<ThreePartyReplay>(replayObject);
            GetOrAdd<ThreePartyOverlay>(replayObject);

            var seats = new ThreePartyReplay.Seat[ThreePartySources.ParticipantCount];
            for (var i = 0; i < seats.Length; i++)
                seats[i] = BuildSeat(scene, body, i);

            replay.Seats = seats;
            EditorUtility.SetDirty(replay);

            SetUpCamera(scene, replay);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"Set Up Replay Scene: could not save {ScenePath}.");
                return;
            }

            AssetDatabase.SaveAssets();

            Debug.Log(
                $"Set Up Replay Scene: {ScenePath} — three seats placed by the capture's own room frame, an " +
                "orbiting camera and the name-plate overlay. Pick a session with GazeControl → 3People → " +
                $"Session Browser, or set the date, session and window on '{ReplayObjectName}' and press Play.",
                replay);
        }

        static void BuildGround(Scene scene)
        {
            if (Find(scene, GroundObjectName) != null)
                return;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = GroundObjectName;
            ground.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(ground, scene);
        }

        /// <summary>
        /// One of these per scene drives every lip-sync context in it; without it
        /// the contexts analyse nothing and the mouths never move.
        /// </summary>
        static void EnsureLipSyncManager(Scene scene)
        {
            var manager = FindOrCreate(scene, LipSyncObjectName);
            GetOrAdd<OVRLipSync>(manager);
        }

        static ThreePartyReplay.Seat BuildSeat(Scene scene, GameObject bodyAsset, int index)
        {
            var pc = index + 1;
            var root = FindOrCreate(scene, $"Participant {pc}");

            // Identity, always: the clip's root translation is a room position,
            // so anything on this transform is added to where the person stood.
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.transform.localScale = Vector3.one;

            var motion = GetOrAdd<SmplxMotionPlayer>(root);
            motion.Autoplay = false; // the replay owns the clock
            motion.Loop = false;
            motion.ApplyRootTranslation = true;
            motion.RootTranslationRelative = false;
            EditorUtility.SetDirty(motion);

            var body = FindBody(root) ?? InstantiateBody(bodyAsset, root);
            var smplx = GetOrAdd<SMPLX>(body);
            smplx.modelType = SMPLX.ModelType.Male;
            smplx.usePoseCorrectives = true;
            EditorUtility.SetDirty(smplx);

            // On the model rather than on the seat, because the lip-sync context
            // reads the AudioSource on its own GameObject.
            var voice = GetOrAdd<AudioSource>(body);
            voice.playOnAwake = false;
            voice.loop = false;

            // 2D on purpose, and it costs nothing: the seat and the model under
            // it are identity transforms — only the bones below them move — so a
            // 3D source here would sit at the origin rather than at the body
            // anyway. These are close-mic tracks of three people a metre apart,
            // and following the words is the whole reason for watching.
            voice.spatialBlend = 0f;
            EditorUtility.SetDirty(voice);

            SetUpLipSync(body);
            ApplyMaterial(body, index);

            return new ThreePartyReplay.Seat { Motion = motion, Voice = voice };
        }

        static void SetUpLipSync(GameObject body)
        {
            var context = GetOrAdd<OVRLipSyncContext>(body);
            context.audioLoopback = true; // off, and the voice is analysed but never heard
            EditorUtility.SetDirty(context);

            var renderer = body.GetComponentInChildren<SkinnedMeshRenderer>();
            var morph = GetOrAdd<OVRLipSyncContextMorphTarget>(body);
            morph.skinnedMeshRenderer = renderer;
            morph.visemeToBlendTargets = VisemeBlendTargets();

            // The default is 15, which on this mesh is an expression blendshape
            // rather than a laughter one.
            morph.laughterBlendTarget = -1;
            EditorUtility.SetDirty(morph);
        }

        /// <summary>
        /// The mesh's 14 viseme blendshapes are at 506-519 in Oculus order with
        /// no <c>sil</c>, so silence maps to nothing.
        /// </summary>
        static int[] VisemeBlendTargets()
        {
            const int firstViseme = 506;
            var targets = new int[OVRLipSync.VisemeCount];
            targets[0] = -1;
            for (var i = 1; i < targets.Length; i++)
                targets[i] = firstViseme + i - 1;

            return targets;
        }

        static void ApplyMaterial(GameObject body, int index)
        {
            var path = k_SeatMaterials[index % k_SeatMaterials.Length];
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Debug.LogWarning($"Set Up Replay Scene: no material at {path}; leaving the body as imported.");
                return;
            }

            var renderer = body.GetComponentInChildren<SkinnedMeshRenderer>();
            var materials = renderer.sharedMaterials;

            // Element 0 only: element 1 is the mouth interior, assigned at import
            // by SmplxMouthInteriorSubmesh and the same on every body.
            materials[0] = material;
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
        }

        static void SetUpCamera(Scene scene, ThreePartyReplay replay)
        {
            var camera = Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include);
            if (camera == null)
            {
                var created = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener))
                {
                    tag = "MainCamera",
                };
                SceneManager.MoveGameObjectToScene(created, scene);
                camera = created.GetComponent<Camera>();
            }

            camera.nearClipPlane = NearClipPlane;
            EditorUtility.SetDirty(camera);

            var orbit = GetOrAdd<ReplayOrbitCamera>(camera.gameObject);
            orbit.Replay = replay;
            EditorUtility.SetDirty(orbit);
        }

        static GameObject FindBody(GameObject root)
        {
            var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (renderer == null)
                return null;

            // The FBX instance root, not the renderer's own object: that is where
            // the added components live in the study scene too.
            var body = renderer.transform;
            while (body.parent != null && body.parent != root.transform)
                body = body.parent;

            return body.gameObject;
        }

        static GameObject InstantiateBody(GameObject bodyAsset, GameObject root)
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyAsset, root.transform);
            body.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            return body;
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

        static GameObject FindOrCreate(Scene scene, string name)
        {
            var existing = Find(scene, name);
            if (existing != null)
                return existing;

            var created = new GameObject(name);
            SceneManager.MoveGameObjectToScene(created, scene);
            return created;
        }

        static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }
    }
}
