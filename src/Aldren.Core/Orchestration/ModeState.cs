namespace Aldren.Core.Orchestration;

/// <summary>Runtime modes (Phase 9) that last until switched off or ALDREN restarts: silent (text only) and dictation.</summary>
public sealed class ModeState
{
    private volatile bool _silent;
    private volatile bool _dictating;

    public bool Silent
    {
        get => _silent;
        set => _silent = value;
    }

    public bool Dictating
    {
        get => _dictating;
        set => _dictating = value;
    }
}
