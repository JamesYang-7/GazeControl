using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace GazeControl.Experiment
{
    /// <summary>
    /// Lets the participant answer for themselves: a ray out of each controller
    /// they are holding, and the trigger presses the button it lands on.
    ///
    /// <para><b>Added 2026-09-09</b>, alongside the spoken path rather than
    /// instead of it (<c>questionnaire-ui-design.md</c> §0.1). Every press goes
    /// through <see cref="QuestionnaireSession.Press"/>, which is the same
    /// entry, the same refusals and the same commit the operator's keys reach,
    /// so nothing downstream can tell which surface an answer came from and the
    /// operator can still take over mid-screen.</para>
    ///
    /// <para><b>No interaction toolkit</b> (§0). The XR Interaction Toolkit is
    /// not in this project and a ray interactor under the Varjo loader is an
    /// untested path on the one piece of hardware that has already produced a
    /// surprise. What is here instead is the pose off the device, a ray, and a
    /// plane intersection against a canvas — a hundred lines that can be read,
    /// and whose arithmetic is unit-tested at a desk.</para>
    ///
    /// <para><b>Controllers are read straight from the Input System</b>, not
    /// through an action asset. The Varjo plugin publishes its own layouts —
    /// <c>VarjoViveWand</c>, <c>VarjoIndexController</c>, <c>VarjoController</c>
    /// — all deriving from <see cref="XRController"/>, and all carrying
    /// <c>devicePosition</c>, <c>deviceRotation</c> and a trigger under names
    /// this looks up rather than assumes. An action asset would need a binding
    /// per layout and would silently bind none of them if the loader ever
    /// changes again.</para>
    ///
    /// <para><b>Nothing is drawn unless a screen is asking something.</b> The
    /// ray follows <see cref="QuestionnaireSession.Buttons"/>, which is empty
    /// while a clip plays, on the passages the operator advances, and in every
    /// run that is not a participant session.</para>
    /// </summary>
    public sealed class QuestionnaireControllerPointer : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("The run whose buttons are being pressed; found in the scene when left empty")]
        public QuestionnaireSession Session { get; set; }

        [field: SerializeField]
        [field: Tooltip("The participant's panel, which owns the buttons and answers what a ray is aiming at")]
        public QuestionnaireDisplay Display { get; set; }

        [field: SerializeField]
        [field: Tooltip("The rig's Camera Offset. Controller poses are reported in this space, exactly as the " +
                        "head pose the camera's TrackedPoseDriver consumes is, so recentring the play area moves " +
                        "the ray with the participant.")]
        public Transform TrackingSpace { get; set; }

        [field: SerializeField]
        [field: Tooltip("How long the ray is drawn when it is not pointing at the panel, metres")]
        public float RayLength { get; set; } = 3f;

        [field: SerializeField]
        [field: Tooltip("Ray thickness in metres")]
        public float RayWidth { get; set; } = 0.004f;

        [field: SerializeField]
        [field: Tooltip("Ray colour")]
        public Color RayColor { get; set; } = new(0.55f, 0.68f, 0.85f, 1f);

        [field: SerializeField]
        [field: Tooltip("How far the trigger must be pulled to count as a press. Near the bottom on purpose: " +
                        "a Vive wand clicks at full pull, and that click is the only feedback the participant " +
                        "gets that they pressed anything. Not 1.0, so a controller whose click button reports " +
                        "nothing still fires on an axis that stops a little short.")]
        [field: Range(0.1f, 0.95f)]
        public float TriggerThreshold { get; set; } = 0.9f;

        [field: SerializeField]
        [field: Tooltip("Answer with the mouse in a flat play session, for testing the row without a headset. " +
                        "Off in a participant session: the operator's own clicks land in the same game view, and " +
                        "one of them on the panel would answer a question nobody asked.")]
        public bool MouseFallback { get; set; }

        [field: SerializeField]
        [field: Tooltip("Log every control on a controller as it is pressed, to find out what an unfamiliar " +
                        "controller calls its trigger. A developer switch — leave it off for a participant.")]
        public bool LogControlsAsPressed { get; set; }

        readonly List<Pointer> _pointers = new();

        /// <summary>
        /// The controller that last pressed something, so that with two in hand
        /// the one being used keeps the cursor rather than losing it to whichever
        /// happens to be swept across the panel first.
        /// </summary>
        int _preferred = -1;

        Material _rayMaterial;

        /// <summary>What the participant is aiming at, for the operator panel and for tests.</summary>
        public int AimedAtButton { get; private set; } = -1;

        /// <summary>How many controllers are being tracked. Zero on a desk.</summary>
        public int ControllerCount => _pointers.Count;

        /// <summary>
        /// One line per controller for the operator's panel: which it is, where
        /// it is pointing and how far its trigger is pulled. Rebuilt each frame
        /// a screen is up.
        ///
        /// <para>Per controller rather than one line for the pair, because every
        /// fault this has had so far looked the same from the desk — a
        /// participant reporting that "the controller does nothing" while the
        /// other one worked, or that the trigger did nothing while the ray
        /// tracked. Both are one glance to tell apart once each controller
        /// reports for itself.</para>
        /// </summary>
        public IReadOnlyList<string> ControllerReports => _reports;

        readonly List<string> _reports = new();

        void Awake()
        {
            if (Session == null)
                Session = FindFirstObjectByType<QuestionnaireSession>();

            if (Display == null)
                Display = Session != null ? Session.Display : FindFirstObjectByType<QuestionnaireDisplay>();
        }

        void OnEnable()
        {
            InputSystem.onDeviceChange += InputSystem_DeviceChange;
            Rescan();
        }

        void OnDisable()
        {
            InputSystem.onDeviceChange -= InputSystem_DeviceChange;
            Clear();

            if (Display != null)
                Display.ShowPointer(false, Vector3.zero, -1);
        }

        void OnDestroy()
        {
            if (_rayMaterial != null)
                Destroy(_rayMaterial);
        }

        void Update()
        {
            if (Session == null || Display == null)
                return;

            // Empty while a clip plays, on the passages only the operator
            // advances, and in every run that is not a participant session.
            if (Session.Buttons.Length == 0)
            {
                Hide();
                return;
            }

            var aiming = false;
            var aimedPoint = Vector3.zero;
            AimedAtButton = -1;
            _reports.Clear();

            for (var i = 0; i < _pointers.Count; i++)
            {
                var pointer = _pointers[i];
                if (!pointer.TryAim(TrackingSpace, out var ray))
                {
                    // Still reported: a controller the runtime has stopped
                    // tracking and one that is simply not being pointed at the
                    // panel are the same silence from the desk.
                    pointer.Hide();
                    _reports.Add($"{pointer.Describe()} — not tracked");
                    continue;
                }

                var hit = Display.TryAim(ray, out var point, out var button);
                pointer.Draw(ray, hit ? Vector3.Distance(ray.origin, point) : RayLength, RayWidth, RayColor);

                // Any pull claims the cursor, not just a completed press, so the
                // hand being used keeps the highlight from the moment it is
                // squeezed.
                if (pointer.TriggerLevel > 0.1f)
                    _preferred = i;

                if (pointer.WasTriggerPressedThisFrame(TriggerThreshold) && hit)
                    Session.Press(button);

                _reports.Add(Report(pointer, hit, button));

                if (LogControlsAsPressed)
                    ReportControls(pointer);

                if (!hit)
                    continue;

                // Every controller aims; the cursor goes to whichever is on the
                // panel, and when both are, to the one last squeezed. Before
                // 2026-09-09 the preference was set only by a completed press,
                // so with the trigger not reporting it stayed at -1, which no
                // index matches — and the first controller to hit kept the
                // cursor for the whole session while the other read as dead.
                if (aiming && i != _preferred)
                    continue;

                aiming = true;
                aimedPoint = point;
                AimedAtButton = button;
            }

            if (!aiming && MouseFallback)
                aiming = TryMouse(out aimedPoint);

            Display.ShowPointer(aiming, aimedPoint, AimedAtButton);
        }

        /// <summary>
        /// The mouse standing in for a controller, so the row can be walked
        /// through at a desk. Deliberately opt-in and off by default: in a
        /// participant session the operator's own clicks land in the same game
        /// view, and one of them on the panel would answer a question nobody
        /// asked.
        /// </summary>
        bool TryMouse(out Vector3 point)
        {
            point = Vector3.zero;

            var mouse = Mouse.current;
            var camera = Camera.main;
            if (mouse == null || camera == null)
                return false;

            var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            if (!Display.TryAim(ray, out point, out var button))
                return false;

            AimedAtButton = button;

            if (mouse.leftButton.wasPressedThisFrame)
                Session.Press(button);

            return true;
        }

        /// <summary>One controller's line on the operator's panel.</summary>
        string Report(Pointer pointer, bool hit, int button)
        {
            var buttons = Session.Buttons;
            var where = !hit ? "off the panel"
                : button >= 0 && button < buttons.Length ? $"on '{buttons[button].Label}'"
                : "on the panel";

            var line = $"{pointer.Describe()} — {where} · trigger {pointer.TriggerLevel:F2}";
            if (!LogControlsAsPressed)
                return line;

            var held = pointer.ActiveControls();
            return $"{line} · held: {(held.Length == 0 ? "nothing" : held)}";
        }

        /// <summary>
        /// Say what is being held on a controller, once per change. The switch
        /// that answers "the ray tracks but the trigger does nothing — is the
        /// button dead, or is it called something else?", which is a question
        /// that otherwise costs a whole headset session to ask.
        /// </summary>
        void ReportControls(Pointer pointer)
        {
            var active = pointer.ActiveControls();
            if (active == pointer.LastReportedControls)
                return;

            pointer.LastReportedControls = active;
            Debug.Log($"{pointer.Describe()} holding: {(active.Length == 0 ? "nothing" : active)}", this);
        }

        void Hide()
        {
            foreach (var pointer in _pointers)
                pointer.Hide();

            AimedAtButton = -1;
            _reports.Clear();
            Display.ShowPointer(false, Vector3.zero, -1);
        }

        void InputSystem_DeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is TrackedDevice)
                Rescan();
        }

        /// <summary>
        /// Rebuild the list of controllers. Cheap and rare — it runs when a
        /// controller wakes, sleeps or reconnects, which in a session is when the
        /// participant picks one up.
        ///
        /// <para><b>Any tracked device with a trigger counts, not only an
        /// <see cref="XRController"/></b> (widened 2026-09-09, after a session
        /// where one wand of the pair answered and the other did nothing). What
        /// a runtime enumerates a wand as is not guaranteed: the Varjo plugin
        /// alone publishes handed SteamVR *tracker* layouts that derive from
        /// <see cref="TrackedDevice"/> and not from <c>XRController</c>, so a
        /// filter on the controller type can drop one hand and keep the other,
        /// which from inside a headset reads as a dead controller. The headset
        /// is excluded by name — it is a tracked device too, and a ray out of
        /// the participant's forehead is not what anyone meant.</para>
        /// </summary>
        void Rescan()
        {
            Clear();

            var skipped = 0;
            foreach (var device in InputSystem.devices)
            {
                if (device is not TrackedDevice tracked || !tracked.added || tracked is XRHMD)
                    continue;

                var trigger = Pointer.ResolveTrigger(tracked, out var pressed, out var axis);

                // A controller with no trigger still gets a ray and says so —
                // it is meant to be pointed with. Anything else that is merely
                // tracked (a body tracker, a camera) is passed over in silence.
                if (!trigger && tracked is not XRController)
                {
                    skipped++;
                    continue;
                }

                _pointers.Add(new Pointer(tracked, NewRayObject(tracked.name), pressed, axis));
            }

            // Both hands are equal here — every controller found gets a ray and
            // can press. Which one holds the cursor when both are on the panel
            // is decided per frame in Update, by which was last used.
            if (_pointers.Count == 0)
            {
                Debug.Log(
                    $"{name}: no XR controller tracked ({skipped} other tracked device(s) seen); " +
                    "the participant answers aloud.", this);
            }
            else
            {
                // The threshold in the log because it is serialized: the scene
                // decides it, not the C# default, and a scene left on an old
                // number is otherwise silent for a whole session.
                Debug.Log(
                    $"{name}: {_pointers.Count} controller(s) can answer; a press is a pull past " +
                    $"{TriggerThreshold:F2}, released under {TriggerThreshold * 0.5f:F2}.", this);
            }

            if (_preferred >= _pointers.Count)
                _preferred = -1;
        }

        void Clear()
        {
            foreach (var pointer in _pointers)
                pointer.Dispose();

            _pointers.Clear();
        }

        LineRenderer NewRayObject(string deviceName)
        {
            var host = new GameObject($"Ray ({deviceName})");
            host.transform.SetParent(transform, worldPositionStays: false);

            var line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            // sharedMaterial, not material: assigning through material clones
            // it per renderer, and the clones are then this component's to
            // destroy.
            line.sharedMaterial = RayMaterial();
            line.enabled = false;
            return line;
        }

        /// <summary>
        /// One unlit material for every ray. <c>Sprites/Default</c> rather than
        /// a URP shader because it is the one always-included shader that
        /// multiplies by vertex colour, which is how a <see cref="LineRenderer"/>
        /// is tinted; the URP Unlit shader ignores it and every ray would come
        /// out white.
        /// </summary>
        Material RayMaterial()
        {
            if (_rayMaterial != null)
                return _rayMaterial;

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogWarning($"{name}: no unlit shader for the pointer ray; it will draw in magenta.", this);
                return null;
            }

            _rayMaterial = new Material(shader) { name = "Questionnaire Pointer Ray", hideFlags = HideFlags.DontSave };
            return _rayMaterial;
        }

        /// <summary>
        /// One controller: where it is pointing, whether its trigger just went
        /// down, and the line drawn out of it.
        ///
        /// <para>The trigger is looked up by name rather than taken from a typed
        /// layout, because the Varjo layouts do not share one: the wand and the
        /// Index controller carry <c>triggerPressed</c> beside an axis, and an
        /// unfamiliar controller may carry only one of them. Anything still
        /// unfound is searched for by name across the device's own controls,
        /// which is the difference between a strange controller working and it
        /// silently doing nothing.</para>
        /// </summary>
        sealed class Pointer
        {
            readonly TrackedDevice _device;
            readonly LineRenderer _line;
            readonly ButtonControl _triggerPressed;
            readonly AxisControl _trigger;
            bool _isDown;

            public Pointer(TrackedDevice device, LineRenderer line, ButtonControl pressed, AxisControl axis)
            {
                _device = device;
                _line = line;
                _triggerPressed = pressed;
                _trigger = axis;

                if (_triggerPressed == null && _trigger == null)
                {
                    Debug.LogWarning(
                        $"'{device.name}' ({device.layout}) has no trigger control this can find, so it cannot " +
                        $"answer anything. Its controls are: {ControlNames(device)}. The participant's spoken " +
                        "answers still reach the operator panel.");
                    return;
                }

                Debug.Log(
                    $"Questionnaire pointer: '{device.name}' ({device.layout}) — " +
                    $"triggerPressed {(_triggerPressed != null ? "found" : "missing")}, " +
                    $"trigger axis {(_trigger != null ? _trigger.name : "missing")}. " +
                    "Both are read, so one of them going dead does not stop the participant answering.");
            }

            /// <summary>
            /// The controls that read this device's trigger, by their usual
            /// names and then by any name containing "trigger" — a layout this
            /// project has not seen is the case worth surviving, since what a
            /// runtime calls a wand's controls is not guaranteed.
            /// </summary>
            /// <returns>True where at least one of them was found.</returns>
            public static bool ResolveTrigger(TrackedDevice device, out ButtonControl pressed, out AxisControl axis)
            {
                pressed = device.TryGetChildControl<ButtonControl>("triggerPressed");
                axis = device.TryGetChildControl<AxisControl>("trigger");

                if (pressed != null || axis != null)
                    return true;

                foreach (var control in device.allControls)
                {
                    if (control is AxisControl candidate && candidate.name.ToLowerInvariant().Contains("trigger"))
                    {
                        Debug.LogWarning(
                            $"'{device.name}' ({device.layout}) carries no 'trigger' or 'triggerPressed', so " +
                            $"'{candidate.name}' is being used as its trigger.");
                        axis = candidate;
                        return true;
                    }
                }

                return false;
            }

            static string ControlNames(TrackedDevice device)
            {
                var names = new System.Text.StringBuilder();
                foreach (var control in device.allControls)
                {
                    if (names.Length > 0)
                        names.Append(", ");

                    names.Append(control.name);
                }

                return names.ToString();
            }

            /// <summary>
            /// Every control on the device that is currently pressed or pulled,
            /// for the developer switch that finds out what a strange
            /// controller's buttons are called. Empty while nothing is held.
            /// </summary>
            public string ActiveControls()
            {
                var active = new System.Text.StringBuilder();
                foreach (var control in _device.allControls)
                {
                    // Buttons are AxisControls too, so one test covers both, and
                    // the pose controls are not AxisControls at all.
                    if (control is not AxisControl axis || axis.ReadValue() < 0.5f)
                        continue;

                    if (active.Length > 0)
                        active.Append(", ");

                    active.Append(axis.name).Append('=').Append(axis.ReadValue().ToString("F2"));
                }

                return active.ToString();
            }

            /// <summary>
            /// What this device is, for the operator panel and the log. The hand
            /// first, because that is what the operator can see the participant
            /// holding.
            /// </summary>
            public string Describe()
            {
                var hand = string.Empty;
                foreach (var usage in _device.usages)
                {
                    if (usage == UnityEngine.InputSystem.CommonUsages.LeftHand)
                        hand = "left ";
                    else if (usage == UnityEngine.InputSystem.CommonUsages.RightHand)
                        hand = "right ";
                }

                return $"{hand}{_device.name}";
            }

            /// <summary>What <see cref="ActiveControls"/> last said, so the log carries changes and not every frame.</summary>
            public string LastReportedControls { get; set; } = string.Empty;

            /// <summary>How far the trigger is pulled, 0-1, over whichever control reports the most.</summary>
            public float TriggerLevel
            {
                get
                {
                    var level = _trigger != null ? _trigger.ReadValue() : 0f;
                    if (_triggerPressed != null)
                        level = Mathf.Max(level, _triggerPressed.ReadValue());

                    return level;
                }
            }

            /// <summary>The ray out of the controller, in world space, or false when it is not being tracked.</summary>
            public bool TryAim(Transform trackingSpace, out Ray ray)
            {
                ray = default;

                if (!_device.added || _device.isTracked.ReadValue() < 0.5f)
                    return false;

                var position = _device.devicePosition.ReadValue();
                var rotation = _device.deviceRotation.ReadValue();

                // The pose is reported in tracking space, the same space the
                // camera's TrackedPoseDriver consumes, so it goes through the
                // rig's Camera Offset — which is what recentring turns and
                // moves. Reading it as a world pose would leave the ray behind
                // whenever the play area was recentred.
                if (trackingSpace != null)
                {
                    position = trackingSpace.TransformPoint(position);
                    rotation = trackingSpace.rotation * rotation;
                }

                ray = new Ray(position, rotation * Vector3.forward);
                return true;
            }

            /// <summary>True on the frame the trigger goes down, and not again until it is released.</summary>
            /// <remarks>
            /// <para><b>Both controls, not one or the other</b> (fixed
            /// 2026-09-09, on a Vive wand whose trigger did nothing while its
            /// ray tracked perfectly). Preferring <c>triggerPressed</c> and
            /// falling back to the axis only where that control is <i>absent</i>
            /// leaves no path at all for a layout that declares the button and
            /// never updates it. The trigger is down when either says so.</para>
            ///
            /// <para>One edge per pull, because the two are ORed into a single
            /// state rather than each giving its own edge: the axis crosses the
            /// threshold a frame or two before the click bottoms out, and two
            /// edges from one squeeze would enter two answers.</para>
            ///
            /// <para>Release takes it back under half the threshold, so a
            /// trigger held exactly on the line cannot chatter. With the
            /// threshold at the click (0.9) that is 0.45, which is most of the
            /// travel — a finger resting on the trigger between answers is well
            /// below it, and the participant does not have to think about
            /// letting go.</para>
            /// </remarks>
            public bool WasTriggerPressedThisFrame(float threshold)
            {
                var level = TriggerLevel;
                var down = _isDown ? level >= threshold * 0.5f : level >= threshold;
                var pressed = down && !_isDown;
                _isDown = down;
                return pressed;
            }

            public void Draw(Ray ray, float length, float width, Color color)
            {
                _line.enabled = true;
                _line.startWidth = width;
                _line.endWidth = width;
                _line.startColor = color;
                _line.endColor = color;
                _line.SetPosition(0, ray.origin);
                _line.SetPosition(1, ray.origin + ray.direction * length);
            }

            public void Hide()
            {
                if (_line != null)
                    _line.enabled = false;
            }

            public void Dispose()
            {
                // Object.Destroy explicitly: this is not a MonoBehaviour, so it
                // does not inherit the shorthand.
                if (_line != null)
                    UnityEngine.Object.Destroy(_line.gameObject);
            }
        }
    }
}
