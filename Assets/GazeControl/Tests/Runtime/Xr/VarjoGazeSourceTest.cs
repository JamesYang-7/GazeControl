using NUnit.Framework;
#if VARJO_XR
using Varjo.XR;
#endif

namespace GazeControl.Xr
{
    [TestFixture]
    public class VarjoGazeSourceTest
    {
        [Test]
        public void Read_WithNoHeadsetRunning_YieldsNoGazeRatherThanThrowing()
        {
            // A desktop Play has no headset, and the plugin's native library may
            // not even be loaded. Either way this must degrade to "no gaze"
            // rather than throw once per decision tick for a whole take. The
            // exact status is the runtime's to say — with the library resident it
            // answers Invalid, without it Unavailable — so what is asserted is
            // the property the logger actually branches on.
            var source = new VarjoGazeSource();

            Assert.That(source.Read().HasGaze, Is.False);
            Assert.That(source.IsCalibrated, Is.False);
            Assert.That(source.IsAvailable, Is.False);
            Assert.That(source.UnavailableReason, Is.Not.Null, "the operator must be told which gate failed");
        }

#if VARJO_XR
        [Test]
        public void ParticipantEyeStatus_MatchesTheVarjoEnumItIsCastFrom()
        {
            // VarjoGazeSource casts GazeEyeStatus straight across. That is only
            // sound while the two enums number the same states the same way, and
            // a plugin update could renumber them without a compile error.
            Assert.That((int)ParticipantEyeStatus.Invalid,
                Is.EqualTo((int)VarjoEyeTracking.GazeEyeStatus.Invalid));
            Assert.That((int)ParticipantEyeStatus.Visible,
                Is.EqualTo((int)VarjoEyeTracking.GazeEyeStatus.Visible));
            Assert.That((int)ParticipantEyeStatus.Compensated,
                Is.EqualTo((int)VarjoEyeTracking.GazeEyeStatus.Compensated));
            Assert.That((int)ParticipantEyeStatus.Tracked,
                Is.EqualTo((int)VarjoEyeTracking.GazeEyeStatus.Tracked));
        }
#endif
    }
}
