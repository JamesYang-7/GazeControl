using System;
using System.Collections.Generic;

namespace GazeControl.Study
{
    /// <summary>Which albedo an agent wears for the clip it is playing.</summary>
    public enum AgentSkin
    {
        /// <summary>
        /// The speaker's voice was not measurable, so nothing is decided and
        /// whatever the scene already carries stays on.
        /// </summary>
        Unchanged,

        FemaleStock,
        FemaleGenerated,
        MaleStock,
        MaleGenerated,
    }

    /// <summary>
    /// Which albedo each agent wears in a clip, from the voices the exporter
    /// measured for that clip's speakers.
    ///
    /// <para>The corpus annotates nothing about who is speaking, so
    /// <c>Tools/find_demo_segments.py</c> measures each side's median F0 and
    /// writes "male"/"female"/"unclear" into <c>segment.json</c>. The five study
    /// conversations are not all the same pair — male-male, female-male and
    /// female-female all occur — so this is decided per clip, not once for the
    /// scene.</para>
    ///
    /// <para><b>Stock goes to the lowest speaker code of each voice.</b> There is
    /// exactly one stock albedo per sex, so two agents of the same sex cannot
    /// both have it: speaker 1 keeps stock and speaker 2 falls to a SMPLitex
    /// texture, which carries the known face misregistration. A mixed pair is the
    /// only case where both agents are stock.</para>
    /// </summary>
    public static class AgentSkins
    {
        const string k_Female = "female";
        const string k_Male = "male";

        /// <summary>
        /// Skins for a clip's speakers, index-aligned to <paramref name="agents"/>.
        /// The order they are given in does not matter: who takes the stock
        /// albedo is decided by speaker code.
        /// </summary>
        public static AgentSkin[] Assign(IReadOnlyList<(int Speaker, string Voice)> agents)
        {
            if (agents == null)
                throw new ArgumentNullException(nameof(agents));

            var skins = new AgentSkin[agents.Count];

            for (var i = 0; i < agents.Count; i++)
            {
                var voice = Normalise(agents[i].Voice);
                if (voice == null)
                {
                    skins[i] = AgentSkin.Unchanged;
                    continue;
                }

                skins[i] = (voice, IsStock(agents, i, voice)) switch
                {
                    (k_Female, true) => AgentSkin.FemaleStock,
                    (k_Female, false) => AgentSkin.FemaleGenerated,
                    (k_Male, true) => AgentSkin.MaleStock,
                    _ => AgentSkin.MaleGenerated,
                };
            }

            return skins;
        }

        /// <summary>True when nobody with this voice has a lower speaker code.</summary>
        static bool IsStock(IReadOnlyList<(int Speaker, string Voice)> agents, int index, string voice)
        {
            for (var j = 0; j < agents.Count; j++)
            {
                if (j != index && Normalise(agents[j].Voice) == voice && agents[j].Speaker < agents[index].Speaker)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The measured voice, or null when it decides nothing — "unclear" sits
        /// between the male and female bands and is not a third sex.
        /// </summary>
        static string Normalise(string voice) =>
            voice switch
            {
                k_Female => k_Female,
                k_Male => k_Male,
                _ => null,
            };
    }
}
