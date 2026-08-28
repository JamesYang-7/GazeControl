using System;
using System.Collections.Generic;
using GazeControl.Study;
using UnityEngine;

namespace GazeControl.Conversation
{
    /// <summary>
    /// Puts the albedo matching its own speaker's measured voice on each agent,
    /// for the clip that is loading.
    ///
    /// <para><b>Every segment load goes through here</b>, because the five study
    /// conversations are not all the same pair of voices. A session that ran all
    /// fifteen takes on whatever textures the scene was last left on showed two
    /// women speaking a male-male recording, and the mismatch is on every frame
    /// of the take a participant then rates.</para>
    ///
    /// <para>Which skin each speaker gets is <see cref="AgentSkins"/>; this
    /// resolves it to a project material and hangs it on the renderer.</para>
    /// </summary>
    public static class AgentAppearance
    {
        /// <summary>
        /// Slot 1 is the mouth interior, split off by UV at import
        /// (<c>SmplxMouthInteriorSubmesh</c>); only slot 0 carries the albedo.
        /// </summary>
        const int k_BodyMaterialSlot = 0;

        const string k_MaterialRoot = "Assets/SMPLX/Materials";

        /// <summary>
        /// Give every speaker the albedo its measured voice asks for, and return
        /// how many renderers changed.
        ///
        /// <para><paramref name="beforeChange"/> is called with each renderer
        /// that is about to be written — the clip browser uses it to register an
        /// Undo, since its own apply is a persistent edit to the scene rather
        /// than a Play Mode one.</para>
        /// </summary>
        public static int MatchToVoices(
            DemoSegment segment,
            IReadOnlyList<RecordedConversation.Speaker> speakers,
            UnityEngine.Object context,
            Action<SkinnedMeshRenderer> beforeChange = null)
        {
            if (segment?.agents == null || speakers == null)
                return 0;

            var voices = new (int Speaker, string Voice)[segment.agents.Length];
            for (var i = 0; i < segment.agents.Length; i++)
                voices[i] = (segment.agents[i].speaker, segment.agents[i].voice);

            var skins = AgentSkins.Assign(voices);
            var applied = new List<string>();

            foreach (var speaker in speakers)
            {
                if (speaker?.Participant == null)
                    continue;

                var index = Array.FindIndex(segment.agents, a => a.speaker == speaker.SpeakerCode);
                if (index < 0)
                    continue;

                if (skins[index] == AgentSkin.Unchanged)
                {
                    Debug.LogWarning(
                        $"Agent appearance: speaker {speaker.SpeakerCode} of '{segment.name}' has no usable measured " +
                        "voice ('unclear' sits between the male and female bands), so its texture is left as it is — " +
                        "decide it by ear and set the material by hand.", context);
                    continue;
                }

                var material = MaterialFor(skins[index]);
                if (material == null)
                    continue;

                var renderer = speaker.Participant.GetComponentInChildren<SkinnedMeshRenderer>();
                if (renderer == null)
                {
                    Debug.LogWarning($"Agent appearance: {speaker.Participant.name} has no SkinnedMeshRenderer.", context);
                    continue;
                }

                var materials = renderer.sharedMaterials;
                if (materials.Length <= k_BodyMaterialSlot || materials[k_BodyMaterialSlot] == material)
                    continue;

                beforeChange?.Invoke(renderer);
                materials[k_BodyMaterialSlot] = material;
                renderer.sharedMaterials = materials;
                applied.Add($"{speaker.Participant.name} → {material.name}");
            }

            if (applied.Count > 0)
                Debug.Log($"Agent appearance: {string.Join(", ", applied)}.", context);

            return applied.Count;
        }

        /// <summary>The project material a skin names, or null if it is missing.</summary>
        public static Material MaterialFor(AgentSkin skin)
        {
            var name = skin switch
            {
                AgentSkin.FemaleStock => "SMPLX-Female-URP-AgentA",
                AgentSkin.FemaleGenerated => "SMPLX-Female-URP-AgentB",
                AgentSkin.MaleStock => "SMPLX-Male-URP",
                AgentSkin.MaleGenerated => "SMPLX-Male-URP-AgentB",
                _ => null,
            };

            if (name == null)
                return null;

            var material = Load($"{k_MaterialRoot}/{name}.mat");
            if (material == null)
                Debug.LogWarning($"Agent appearance: material not found at {k_MaterialRoot}/{name}.mat.");

            return material;
        }

        /// <summary>
        /// Loaded from the project rather than from a serialized reference, for
        /// the same reason the segment's audio clips are: the study runs in the
        /// editor, on segments chosen after the scene was built, and a wired
        /// field per material would have to be re-dragged whenever the clip set
        /// changed. A build would need them wired in the scene instead.
        /// </summary>
        static Material Load(string assetPath)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(assetPath);
#else
            return null;
#endif
        }
    }
}
