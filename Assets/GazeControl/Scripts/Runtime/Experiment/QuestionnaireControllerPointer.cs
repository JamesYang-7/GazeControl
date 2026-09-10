using System.Collections.Generic;
using GazeControl.Study;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using XRCommonUsages = UnityEngine.XR.CommonUsages;
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDeviceCharacteristics = UnityEngine.XR.InputDeviceCharacteristics;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRInputFeatureUsage = UnityEngine.XR.InputFeatureUsage;

namespace GazeControl.Experiment
{
    /// <summary>
    /// Lets the participant answer for themselves: a ray out of each controller
    /// they are holding, and the trigger, the trackpad click or the grip presses
    /// the button it lands on.
    ///
    /// <para><b>Three buttons, one press</b> (2026-09-10). The trigger is the
    /// one with a threshold to get wrong, and getting it wrong is silent: the
    /// axis on this wand pair does not reach 0.9 and <c>triggerPressed</c> does
    /// not fill the gap, so a press can fail with the ray tracking perfectly.
    /// The trackpad click and the grip are plain microswitches with no
    /// threshold to miss, and the trackpad is the largest surface on a wand and
    /// the easiest to find blind with a headset on. All three are ORed into one
    /// down/up state, so squeezing the trigger and clicking the pad in the same
    /// motion still enters exactly one answer. The System button is not among
    /// them: SteamVR reserves it and it never reaches the application.</para>
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
    /// <para><b>The trigger is found through the device's XR descriptor, not
    /// through the layout's control names</b> (2026-09-10). A vendor layout
    /// declares its controls whether or not the runtime reports the features
    /// behind them, while <c>XRLayoutBuilder</c> gives a control its place in
    /// the state only for a feature the descriptor actually lists — so
    /// <c>trigger</c> and <c>triggerPressed</c> are always <i>found</i> on a
    /// wand and can be backed by nothing, which reads as a pull that never
    /// leaves 0.00. What the descriptor lists is looked up first, under the
    /// name the builder would have given it, and the layout's own names are
    /// only the fallback.</para>
    ///
    /// <para><b>The trigger is read through both XR input surfaces</b>
    /// (2026-09-10, after a session where a wand's pull sat at 0.00 on the
    /// operator panel for a whole screen while the same wand worked SteamVR's
    /// own dashboard). The Input System's XR device and the legacy
    /// <see cref="UnityEngine.XR.InputDevices"/> subsystem carry the same
    /// controller by two different routes: the Input System binds a vendor's
    /// declared layout onto the native state <i>by offset</i>, so a wand that
    /// enumerates under a layout whose controls do not line up with the
    /// features the runtime actually reports reads zero from a control that
    /// exists — while the legacy API asks for the feature <i>by name</i> and is
    /// immune to it. Varjo's own layouts make that concrete: its handed
    /// SteamVR-tracker layout, which is what a wand falls back to when SteamVR
    /// has not given it a controller role, declares <c>triggerPressed</c> and no
    /// trigger axis at all. Both are read and the larger wins, so either route
    /// going quiet still answers.</para>
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
        [field: Tooltip("How far the trigger must be pulled to count as a press. 0.9 puts it on the wand's " +
                        "own click, which is the only feedback a participant gets that they pressed anything. " +
                        "It was tried and withdrawn earlier on 2026-09-10, when nothing registered at all - " +
                        "the axis does not reach it here and the click button did not fill the gap - and it is " +
                        "affordable now only because the trackpad click and the grip press through no " +
                        "threshold, so this number can no longer lock anyone out. Watch the peak on the " +
                        "operator panel to see what a full pull is worth on the hardware in hand.")]
        [field: Range(0.1f, 0.95f)]
        public float TriggerThreshold { get; set; } = 0.9f;

        [field: SerializeField]
        [field: Tooltip("Answer with the mouse in a flat play session, for testing the row without a headset. " +
                        "Off in a participant session: the operator's own clicks land in the same game view, and " +
                        "one of them on the panel would answer a question nobody asked.")]
        public bool MouseFallback { get; set; }

        [field: SerializeField]
        [field: Tooltip("Log every control on a controller as it is pressed, to find out what an unfamiliar " +
                        "controller calls its trigger, trackpad or grip. A developer switch — leave it off " +
                        "for a participant.")]
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
            var aimingCanPress = false;
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

                // Any pull or any button claims the cursor, not just a completed
                // press, so the hand being used keeps the highlight from the
                // moment it is squeezed or clicked.
                if (pointer.TriggerLevel > 0.1f || pointer.ButtonsPressed)
                    _preferred = i;

                if (pointer.WasPressedThisFrame(TriggerThreshold) && hit)
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
                //
                // A device that cannot press yields to one that can (2026-09-10,
                // on a wand whose trigger read 0.00 while it clicked): a pose
                // with no buttons on it still enumerates, still aims, and can
                // never answer, so letting it hold the cursor is a controller
                // that works everywhere except where it counts.
                if (aiming && i != _preferred && !(pointer.CanPress && !aimingCanPress))
                    continue;

                aiming = true;
                aimingCanPress = pointer.CanPress;
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

            // Both surfaces on the line, not just the number that decides:
            // a pull that shows under XR and 0.00 under IS is a mapped-wrong
            // layout, and one that shows 0.00 under both is the runtime giving
            // this application no button input at all. From the desk those are
            // the same complaint, and they have different answers.
            //
            // The pad and grip come after it, because they are what the
            // participant is told to use: a line reading 0.00 for the trigger
            // and 'pad/grip: primary2daxisclick' in the same breath is a wand
            // whose trigger is dead and whose participant is still answering.
            var line = pointer.CanPress
                ? $"{pointer.Describe()} — {where} · trigger {pointer.TriggerLevel:F2} " +
                  $"(IS {pointer.InputSystemTriggerLevel:F2} · XR {pointer.LegacyTriggerLevel:F2}, " +
                  $"peak {pointer.PeakTriggerLevel:F2}, needs {TriggerThreshold:F2})" +
                  $" · pad/grip: {pointer.DescribeButtons()}"
                : $"{pointer.Describe()} — {where} · NO TRIGGER, PAD OR GRIP CONTROL, cannot answer";

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

        /// <summary>
        /// Everything both XR input surfaces say about every device attached,
        /// in one log entry: the Input System's devices with their layout and
        /// the controls a press could come from, and the legacy subsystem's
        /// devices with every feature usage they carry and what each reads
        /// right now.
        ///
        /// <para>It exists because the question a dead trigger raises cannot be
        /// answered from the operator panel: whether the control is missing,
        /// present and unmapped, or present and simply not being fed by the
        /// runtime. Squeeze the trigger and press this, and the answer is in
        /// the console. On the operator panel as a button, and on the
        /// component's own context menu for a run without a session.</para>
        /// </summary>
        [ContextMenu("Log XR input snapshot")]
        public void LogInputSnapshot()
        {
            var report = new System.Text.StringBuilder();
            report.Append("XR input snapshot - Input System devices:");

            var tracked = 0;
            foreach (var device in InputSystem.devices)
            {
                if (device is not TrackedDevice)
                    continue;

                tracked++;
                report.AppendLine().Append("  '").Append(device.name).Append("' (").Append(device.layout)
                    .Append(')').Append(device.added ? string.Empty : " [not added]");
                report.AppendLine().Append("    product: '").Append(device.description.product)
                    .Append("', manufacturer: '").Append(device.description.manufacturer).Append("'");
                report.AppendLine().Append("    descriptor features: ").Append(DescriptorFeatures(device));
                report.AppendLine().Append("    non-zero controls: ").Append(NonZeroControls(device));
            }

            if (tracked == 0)
                report.Append(" none.");

            report.AppendLine().AppendLine().Append("Legacy XR subsystem devices:");

            var devices = new List<XRInputDevice>();
            XRInputDevices.GetDevices(devices);
            if (devices.Count == 0)
                report.Append(" none.");

            var usages = new List<XRInputFeatureUsage>();
            foreach (var device in devices)
            {
                report.AppendLine().Append("  '").Append(device.name).Append("' [").Append(device.characteristics)
                    .Append("] serial '").Append(device.serialNumber).Append("'");

                if (!device.TryGetFeatureUsages(usages))
                {
                    report.AppendLine().Append("    no feature usages reported");
                    continue;
                }

                foreach (var usage in usages)
                {
                    report.AppendLine().Append("    ").Append(usage.name).Append(" (").Append(usage.type.Name)
                        .Append(") = ").Append(LegacyControls.ReadAsText(device, usage));
                }
            }

            Debug.Log(report.ToString(), this);
        }

        /// <summary>
        /// The features the device's own XR descriptor lists - which is what
        /// decides which of its layout's controls are backed by anything. A
        /// control the layout declares and this list does not name is a control
        /// that will read zero however hard the trigger is pulled.
        /// </summary>
        static string DescriptorFeatures(InputDevice device)
        {
            if (device is not TrackedDevice tracked)
                return "not a tracked device";

            var names = new System.Text.StringBuilder();
            foreach (var name in Pointer.DescriptorControlNames(tracked))
            {
                if (names.Length > 0)
                    names.Append(", ");

                names.Append(name);
            }

            return names.Length == 0 ? "none reported" : names.ToString();
        }

        /// <summary>
        /// Every control on a device that is reading anything, so the snapshot
        /// says what a squeeze moves rather than only what exists.
        /// </summary>
        static string NonZeroControls(InputDevice device)
        {
            var active = new System.Text.StringBuilder();
            foreach (var control in device.allControls)
            {
                if (control is not AxisControl axis || Mathf.Approximately(axis.ReadValue(), 0f))
                    continue;

                if (active.Length > 0)
                    active.Append(", ");

                active.Append(axis.name).Append('=').Append(axis.ReadValue().ToString("F2"));
            }

            return active.Length == 0 ? "nothing reading" : active.ToString();
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

                // The trackpad click and the grip, resolved the same two ways.
                // They are a list rather than two named controls because a wand
                // reports its grip as a button and an axis both, and an Index
                // controller as an analog squeeze - all of them are just
                // 'something the participant can push', read at half travel.
                var buttons = Pointer.ResolveButtons(tracked);

                // The same controller through the other XR input surface. It is
                // resolved here rather than inside the pointer because whether a
                // device can press at all decides whether it is kept.
                var legacy = LegacyControls.For(tracked);

                // A controller with nothing to press still gets a ray and says
                // so — it is meant to be pointed with. Anything else that is
                // merely tracked (a body tracker, a camera) is passed over in
                // silence.
                if (!trigger && buttons.Count == 0 && !legacy.Exists && tracked is not XRController)
                {
                    skipped++;
                    continue;
                }

                _pointers.Add(new Pointer(tracked, NewRayObject(tracked.name), pressed, axis, buttons, legacy));
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
                    $"{name}: {_pointers.Count} controller(s) can answer; a press is a trigger pull past " +
                    $"{TriggerThreshold:F2} (released under {TriggerThreshold * 0.5f:F2}), a trackpad click " +
                    "or a grip.", this);
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
        /// <para>The trigger is looked up rather than taken from a typed
        /// layout, because the Varjo layouts do not share one: the wand and the
        /// Index controller carry <c>triggerPressed</c> beside an axis, and an
        /// unfamiliar controller may carry only one of them. It is looked up
        /// through the device's own XR descriptor first and its layout's names
        /// second, and read again through the legacy XR subsystem, because a
        /// control that is present and dead looks exactly like a participant
        /// not pressing.</para>
        /// </summary>
        sealed class Pointer
        {
            readonly TrackedDevice _device;
            readonly LineRenderer _line;
            readonly ButtonControl _triggerPressed;
            readonly AxisControl _trigger;
            readonly List<AxisControl> _buttons;
            readonly LegacyControls _legacy;
            bool _isDown;

            public Pointer(
                TrackedDevice device, LineRenderer line, ButtonControl pressed, AxisControl axis,
                List<AxisControl> buttons, LegacyControls legacy)
            {
                _device = device;
                _line = line;
                _triggerPressed = pressed;
                _trigger = axis;
                _buttons = buttons;
                _legacy = legacy;

                if (!CanPress)
                {
                    Debug.LogWarning(
                        $"'{device.name}' ({device.layout}) has no trigger, trackpad click or grip this can find " +
                        $"on either XR input surface, so it cannot answer anything. Its Input System controls " +
                        $"are: {ControlNames(device)}. The legacy subsystem says: {_legacy.Describe()}. The " +
                        "participant's spoken answers still reach the operator panel.");
                    return;
                }

                Debug.Log(
                    $"Questionnaire pointer: '{device.name}' ({device.layout}) — " +
                    $"triggerPressed {(_triggerPressed != null ? "found" : "missing")}, " +
                    $"trigger axis {(_trigger != null ? _trigger.name : "missing")}, " +
                    $"pad/grip {(_buttons.Count == 0 ? "none" : string.Join(" ", _buttons.ConvertAll(b => b.name)))}, " +
                    $"legacy XR {_legacy.Describe()}. " +
                    "All of them are read, so one going dead does not stop the participant answering.");
            }

            /// <summary>
            /// The controls that read this device's trigger: whatever the
            /// runtime's own descriptor calls it, and the layout's usual names
            /// only where the descriptor says nothing. A layout this project has
            /// not seen is the case worth surviving, since what a runtime calls
            /// a wand's controls is not guaranteed.
            /// </summary>
            /// <returns>True where at least one of them was found.</returns>
            public static bool ResolveTrigger(TrackedDevice device, out ButtonControl pressed, out AxisControl axis)
            {
                pressed = null;
                axis = null;

                // The features the device itself reports, first. A control the
                // descriptor does not back exists all the same - it is
                // inherited from the vendor's layout - and reads zero forever,
                // so looking 'trigger' up by name finds something on every wand
                // and proves nothing.
                foreach (var name in DescriptorControlNames(device))
                {
                    // A touch sensor is not a press: on a wand it reports a
                    // finger resting on the trigger, which would answer the
                    // question the participant is still reading.
                    if (!name.Contains("trigger") || name.Contains("touch"))
                        continue;

                    var control = device.TryGetChildControl(name);
                    if (control is ButtonControl button)
                        pressed ??= button;
                    else if (control is AxisControl analog)
                        axis ??= analog;
                }

                if (pressed != null || axis != null)
                    return true;

                // No descriptor, or nothing trigger-shaped in it. The layout's
                // own names are what is left.
                pressed = device.TryGetChildControl<ButtonControl>("triggerPressed");
                axis = device.TryGetChildControl<AxisControl>("trigger");
                return pressed != null || axis != null;
            }

            /// <summary>
            /// The trackpad click and the grip, found the same two ways the
            /// trigger is: what the runtime's own descriptor backs first, the
            /// layout's usual names where the descriptor says nothing.
            ///
            /// <para>Both are read digitally at half travel, whether the runtime
            /// hands them over as a button or as an axis - a wand's grip is 0/1
            /// under either name, and an Index controller's analog squeeze is
            /// past half only when it is being deliberately closed.</para>
            ///
            /// <para><b>Touch is not press.</b> A wand's trackpad reports a
            /// finger resting on it (<c>primary2DAxisTouch</c>) long before the
            /// switch under it closes, and a participant reading the question
            /// with a thumb on the pad would otherwise answer it. Only names
            /// carrying "click" or "press" are taken, which drops the touch
            /// sensors of both surfaces without naming them.</para>
            /// </summary>
            public static List<AxisControl> ResolveButtons(TrackedDevice device)
            {
                var buttons = new List<AxisControl>();

                foreach (var name in DescriptorControlNames(device))
                {
                    if (!IsPadClick(name) && !IsGrip(name))
                        continue;

                    if (device.TryGetChildControl(name) is AxisControl control)
                        buttons.Add(control);
                }

                if (buttons.Count > 0)
                    return buttons;

                // The layout's own names, in the order a wand is likeliest to
                // carry them. Every one that exists is kept: which of them the
                // runtime actually feeds is exactly what is not knowable here.
                foreach (var name in FallbackButtonNames)
                {
                    var control = device.TryGetChildControl<AxisControl>(name);
                    if (control != null)
                        buttons.Add(control);
                }

                return buttons;
            }

            /// <summary>
            /// The names a trackpad click and a grip go under when there is no
            /// descriptor to read them off. Unity's own XR layouts and the
            /// vendor ones do not agree, and a name that is not there costs a
            /// lookup that returns null.
            /// </summary>
            static readonly string[] FallbackButtonNames =
            {
                "trackpadClicked", "trackpadPressed", "touchpadClicked", "touchpadPressed", "primary2DAxisClick",
                "gripPressed", "gripButton", "squeezePressed", "grip",
            };

            /// <summary>The trackpad's own switch, under whichever of its names the runtime reports.</summary>
            static bool IsPadClick(string name) =>
                (name.Contains("2daxis") || name.Contains("trackpad") || name.Contains("touchpad"))
                && (name.Contains("click") || name.Contains("press"));

            /// <summary>The grip, as a button or as a squeeze. Never the capacitive touch beside it.</summary>
            static bool IsGrip(string name) =>
                (name.Contains("grip") || name.Contains("squeeze")) && !name.Contains("touch");

            /// <summary>
            /// The control names the Input System would have built from this
            /// device's XR descriptor - the only way to tell a control that is
            /// backed by the hardware from one the layout merely declares. The
            /// naming rule is <see cref="XrFeatureControlName"/>.
            /// </summary>
            public static IEnumerable<string> DescriptorControlNames(TrackedDevice device)
            {
                // The parse is a method of its own because an iterator may not
                // hold a yield inside a try that catches.
                if (!TryReadDescriptor(device, out var descriptor) || descriptor.inputFeatures == null)
                    yield break;

                foreach (var feature in descriptor.inputFeatures)
                {
                    var name = XrFeatureControlName.For(feature.name);
                    if (name.Length > 0)
                        yield return name;
                }
            }

            /// <summary>
            /// The XR descriptor a tracked device carries in its capabilities:
            /// what the runtime says this piece of hardware reports, as opposed
            /// to what its layout declares.
            /// </summary>
            public static bool TryReadDescriptor(TrackedDevice device, out XRDeviceDescriptor descriptor)
            {
                descriptor = null;

                var capabilities = device.description.capabilities;
                if (string.IsNullOrEmpty(capabilities))
                    return false;

                try
                {
                    descriptor = XRDeviceDescriptor.FromJson(capabilities);
                }
                catch (System.Exception e)
                {
                    // A tracked device whose capabilities are not an XR
                    // descriptor at all. It still gets a ray off its pose.
                    Debug.LogWarning($"'{device.name}' carries no readable XR descriptor: {e.Message}");
                    return false;
                }

                return descriptor != null;
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

            /// <summary>
            /// Whether this device has anything to press at all. False for one
            /// that enumerates with a pose and no buttons, which happens: it can
            /// be aimed and can never answer.
            /// </summary>
            public bool CanPress =>
                _triggerPressed != null || _trigger != null || _buttons.Count > 0 || _legacy.Exists;

            /// <summary>
            /// Whether the trackpad click or the grip is down, on either surface.
            /// Digital: there is no threshold to reach and none to get wrong,
            /// which is the reason they are here beside the trigger.
            /// </summary>
            public bool ButtonsPressed
            {
                get
                {
                    foreach (var button in _buttons)
                    {
                        if (button.ReadValue() >= 0.5f)
                            return true;
                    }

                    return _legacy.ButtonsPressed;
                }
            }

            /// <summary>
            /// Which of the pad and grip controls are down, by name, for the
            /// operator panel. The names rather than a tick, because "the pad
            /// does nothing" and "the pad is feeding a control nothing reads"
            /// are the same complaint from the desk and different faults.
            /// </summary>
            public string DescribeButtons()
            {
                if (_buttons.Count == 0 && !_legacy.HasButtons)
                    return "none found";

                var held = new System.Text.StringBuilder();
                foreach (var button in _buttons)
                {
                    if (button.ReadValue() < 0.5f)
                        continue;

                    if (held.Length > 0)
                        held.Append(", ");

                    held.Append(button.name);
                }

                if (_legacy.ButtonsPressed)
                {
                    if (held.Length > 0)
                        held.Append(", ");

                    held.Append("XR ").Append(_legacy.DescribeButtonsHeld());
                }

                return held.Length == 0 ? "idle" : held.ToString();
            }

            /// <summary>
            /// The highest pull this controller has reported since the session
            /// began. It is what says whether a threshold is even reachable:
            /// a full squeeze that peaks at 0.78 cannot answer at 0.9, and from
            /// the desk that is indistinguishable from a participant not
            /// pressing.
            /// </summary>
            public float PeakTriggerLevel { get; private set; }

            /// <summary>How far the trigger is pulled, 0-1, over whichever control on whichever surface reports the most.</summary>
            public float TriggerLevel => Mathf.Max(InputSystemTriggerLevel, LegacyTriggerLevel);

            /// <summary>The pull as the Input System's device reports it, for the operator panel.</summary>
            public float InputSystemTriggerLevel
            {
                get
                {
                    var level = _trigger != null ? _trigger.ReadValue() : 0f;
                    if (_triggerPressed != null)
                        level = Mathf.Max(level, _triggerPressed.ReadValue());

                    return level;
                }
            }

            /// <summary>The pull as the legacy XR subsystem reports it, for the operator panel.</summary>
            public float LegacyTriggerLevel => _legacy.Level;

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

            /// <summary>True on the frame the participant presses, and not again until everything is released.</summary>
            /// <remarks>
            /// <para><b>Every control, not one or the other</b> (fixed
            /// 2026-09-09 for the trigger's two controls, widened 2026-09-10 to
            /// the trackpad click and the grip). Preferring <c>triggerPressed</c>
            /// and falling back to the axis only where that control is
            /// <i>absent</i> left no path at all for a layout that declares the
            /// button and never updates it; the same reasoning is what puts two
            /// more buttons here, since a trigger that reads 0.00 on both
            /// surfaces is a participant who cannot answer.</para>
            ///
            /// <para><b>One edge per press</b>, because all of them are ORed
            /// into a single state rather than each giving its own edge: the
            /// axis crosses the threshold a frame or two before the click
            /// bottoms out, and a participant who squeezes the trigger and
            /// clicks the pad in one motion means one answer, not two.</para>
            ///
            /// <para>Release takes the trigger back under half the threshold, so
            /// one held exactly on the line cannot chatter; the pad and the grip
            /// are switches and release at their own edge. A press stays down
            /// while any of them is down, so rolling from one to another does
            /// not enter a second answer either.</para>
            /// </remarks>
            public bool WasPressedThisFrame(float threshold)
            {
                var level = TriggerLevel;
                if (level > PeakTriggerLevel)
                    PeakTriggerLevel = level;

                // The peak is the trigger's alone: it is the number that says
                // whether a threshold is reachable on this hardware, and a
                // trackpad click folded into it would read 1.00 for ever and
                // answer that question wrongly for the rest of the session.
                var down = ButtonsPressed || (_isDown ? level >= threshold * 0.5f : level >= threshold);
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

        /// <summary>
        /// One controller's trigger, trackpad click and grip as the <b>legacy XR
        /// input subsystem</b> reports them - the second route to the same
        /// hardware, read beside the Input System's device rather than instead
        /// of it.
        ///
        /// <para><b>Why a second route at all</b> (2026-09-10, on a wand whose
        /// pull sat at 0.00 for a whole screen while the same wand worked
        /// SteamVR's own dashboard): the Input System's device is assembled by
        /// <c>XRLayoutBuilder</c> from the descriptor the runtime reports, and a
        /// control the vendor's layout declares but the descriptor does not back
        /// is still there to be found - reading zero for ever. Which controls
        /// those are depends on what the runtime chose to call this device's
        /// features, and a wand that SteamVR has not given a hand role
        /// enumerates under a different layout again.
        /// <see cref="XRInputDevices"/> goes past all of that and asks the
        /// runtime for the feature by its usage, so it cannot be fooled the same
        /// way. Neither route is the trustworthy one; the pull is whichever
        /// reports more.</para>
        ///
        /// <para>The match is by serial number where the descriptor carries one,
        /// and by device name and hand where it does not - a wand pair differs
        /// only by the hand.</para>
        /// </summary>
        sealed class LegacyControls
        {
            /// <summary>Reused across every controller and every frame: the enumeration is per-frame and per-device.</summary>
            static readonly List<XRInputDevice> Devices = new();

            static readonly List<XRInputFeatureUsage> Usages = new();

            readonly string _serial;
            readonly string _name;
            readonly XRInputDeviceCharacteristics _characteristics;

            XRInputDevice _device;
            bool _hasAxis;
            bool _hasButton;
            bool _hasPadClick;
            bool _hasGrip;
            int _resolvedFrame = -1;

            LegacyControls(string serial, string name, XRInputDeviceCharacteristics characteristics)
            {
                _serial = serial;
                _name = name;
                _characteristics = characteristics;
                Resolve();
            }

            /// <summary>
            /// The legacy device behind an Input System one, identified from the
            /// XR descriptor the Input System device carries - which is where
            /// the serial number and the handedness live.
            /// </summary>
            public static LegacyControls For(TrackedDevice device)
            {
                var serial = string.Empty;
                var name = device.description.product;
                var characteristics = default(XRInputDeviceCharacteristics);

                if (Pointer.TryReadDescriptor(device, out var descriptor))
                {
                    serial = descriptor.serialNumber;
                    name = descriptor.deviceName;
                    characteristics = descriptor.characteristics;
                }

                return new LegacyControls(serial, name, characteristics);
            }

            /// <summary>Whether a matching device with something pressable was found.</summary>
            public bool Exists
            {
                get
                {
                    Resolve();
                    return _device.isValid && (_hasAxis || _hasButton || _hasPadClick || _hasGrip);
                }
            }

            /// <summary>Whether this surface carries a trackpad click or a grip at all, pressed or not.</summary>
            public bool HasButtons
            {
                get
                {
                    Resolve();
                    return _device.isValid && (_hasPadClick || _hasGrip);
                }
            }

            /// <summary>
            /// Whether the trackpad click or the grip is down on this surface.
            /// The grip is read as a button first and as a squeeze second: a
            /// wand reports 0/1 either way, and a controller with only the
            /// analog feature still answers at half travel.
            /// </summary>
            public bool ButtonsPressed
            {
                get
                {
                    if (!Resolve())
                        return false;

                    if (_device.TryGetFeatureValue(XRCommonUsages.primary2DAxisClick, out var pad) && pad)
                        return true;

                    if (_device.TryGetFeatureValue(XRCommonUsages.gripButton, out var grip) && grip)
                        return true;

                    return _device.TryGetFeatureValue(XRCommonUsages.grip, out var squeeze) && squeeze >= 0.5f;
                }
            }

            /// <summary>Which of them is down, for the operator panel's line.</summary>
            public string DescribeButtonsHeld()
            {
                if (!Resolve())
                    return "none";

                if (_device.TryGetFeatureValue(XRCommonUsages.primary2DAxisClick, out var pad) && pad)
                    return "pad";

                if (_device.TryGetFeatureValue(XRCommonUsages.gripButton, out var grip) && grip)
                    return "grip";

                return _device.TryGetFeatureValue(XRCommonUsages.grip, out var squeeze) && squeeze >= 0.5f
                    ? "squeeze"
                    : "none";
            }

            /// <summary>How far this surface says the trigger is pulled, 0-1. Zero where there is nothing to read.</summary>
            public float Level
            {
                get
                {
                    if (!Resolve())
                        return 0f;

                    var level = 0f;
                    if (_device.TryGetFeatureValue(XRCommonUsages.trigger, out var axis))
                        level = axis;

                    // A button reads as a full pull, so a controller that
                    // reports only the click can still cross any threshold.
                    if (_device.TryGetFeatureValue(XRCommonUsages.triggerButton, out var pressed) && pressed)
                        level = 1f;

                    return level;
                }
            }

            /// <summary>What this surface found, for the log and the warning that nothing can press.</summary>
            public string Describe()
            {
                if (!Resolve())
                    return "no matching device";

                var features = (_hasAxis, _hasButton) switch
                {
                    (true, true) => "trigger axis and button",
                    (true, false) => "trigger axis only",
                    (false, true) => "trigger button only",
                    _ => "no trigger feature",
                };

                var buttons = (_hasPadClick, _hasGrip) switch
                {
                    (true, true) => ", pad click and grip",
                    (true, false) => ", pad click",
                    (false, true) => ", grip",
                    _ => ", no pad or grip",
                };

                return $"'{_device.name}' [{_device.characteristics}] {features}{buttons}";
            }

            /// <summary>
            /// One feature's current value as text, for the snapshot. Poses and
            /// rotations are named but not printed: the snapshot is asked when a
            /// button is not answering.
            /// </summary>
            public static string ReadAsText(XRInputDevice device, XRInputFeatureUsage usage)
            {
                if (usage.type == typeof(bool))
                    return device.TryGetFeatureValue(usage.As<bool>(), out var flag) ? flag.ToString() : "unreadable";

                if (usage.type == typeof(float))
                    return device.TryGetFeatureValue(usage.As<float>(), out var value) ? value.ToString("F2") : "unreadable";

                if (usage.type == typeof(Vector2))
                    return device.TryGetFeatureValue(usage.As<Vector2>(), out var axis) ? axis.ToString("F2") : "unreadable";

                if (usage.type == typeof(uint))
                    return device.TryGetFeatureValue(usage.As<uint>(), out var bits) ? bits.ToString() : "unreadable";

                return "(not shown)";
            }

            /// <summary>
            /// Find the legacy device, once per frame at most. A controller that
            /// has not woken up yet is looked for again next frame rather than
            /// given up on, because the two surfaces do not necessarily
            /// enumerate a controller on the same frame.
            /// </summary>
            bool Resolve()
            {
                if (_device.isValid)
                    return true;

                if (string.IsNullOrEmpty(_serial) && string.IsNullOrEmpty(_name))
                    return false;

                if (_resolvedFrame == Time.frameCount)
                    return false;

                _resolvedFrame = Time.frameCount;
                _hasAxis = false;
                _hasButton = false;
                _hasPadClick = false;
                _hasGrip = false;
                _device = default;

                XRInputDevices.GetDevices(Devices);
                foreach (var candidate in Devices)
                {
                    if (!candidate.isValid)
                        continue;

                    if (!string.IsNullOrEmpty(_serial) && candidate.serialNumber == _serial)
                    {
                        _device = candidate;
                        break;
                    }

                    // No serial to go on. The name and the hand are what is
                    // left, and they are what tells a wand pair apart.
                    if (candidate.name == _name && candidate.characteristics == _characteristics)
                        _device = candidate;
                }

                if (!_device.isValid)
                    return false;

                if (_device.TryGetFeatureUsages(Usages))
                {
                    foreach (var usage in Usages)
                    {
                        if (usage.name == XRCommonUsages.trigger.name)
                            _hasAxis = true;
                        else if (usage.name == XRCommonUsages.triggerButton.name)
                            _hasButton = true;
                        else if (usage.name == XRCommonUsages.primary2DAxisClick.name)
                            _hasPadClick = true;
                        else if (usage.name == XRCommonUsages.gripButton.name || usage.name == XRCommonUsages.grip.name)
                            _hasGrip = true;
                    }
                }

                return true;
            }
        }
    }
}
