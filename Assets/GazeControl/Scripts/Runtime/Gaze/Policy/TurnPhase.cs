namespace GazeControl.Gaze.Policy
{
    /// <summary>Where the agent is inside its own turn, for the Kendon turn-boundary overrides.</summary>
    public enum TurnPhase
    {
        NotSpeaking,
        TurnStart,
        TurnMiddle,
        TurnEndApproaching,
    }
}
