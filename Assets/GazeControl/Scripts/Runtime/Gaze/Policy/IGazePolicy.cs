namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// A swappable gaze policy — one per experimental condition. Policies choose
    /// *what* to look at and *when*; how the shift is animated is the shared
    /// animation layer's job and must be identical across conditions, or the study
    /// measures animation quality instead of gaze policy.
    ///
    /// Called on the decision tick (20-50 Hz), never per rendered frame.
    /// </summary>
    public interface IGazePolicy
    {
        /// <summary>Advance by <paramref name="deltaTime"/> seconds and return the target to look at now.</summary>
        GazeTarget Update(float deltaTime, in ConversationState state);

        /// <summary>Restore initial state and re-seed the RNG, so a trial can be replayed exactly.</summary>
        void Reset(int seed);
    }
}
