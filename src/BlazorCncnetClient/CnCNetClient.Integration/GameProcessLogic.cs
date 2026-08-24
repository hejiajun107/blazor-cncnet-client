namespace CnCNetClient.Integration;

/// <summary>
/// Controls launching and exiting of the game executable.
/// Migrated from ClientGUI.GameProcessLogic. UI-dependent dialogs (XNAMessageBox)
/// will be replaced by host-provided error callbacks during integration.
/// <see cref="UseQres"/> / <see cref="SingleCoreAffinity"/> are the only members
/// referenced by Domain (DirectDrawWrapperManager) at this stage.
/// </summary>
public static class GameProcessLogic
{
    public static event Action? GameProcessStarted;

    public static event Action? GameProcessStarting;

    public static event Action? GameProcessExited;

    public static bool UseQres { get; set; }

    public static bool SingleCoreAffinity { get; set; }

    // TODO(integration): migrate StartGameProcess from ClientGUI, replacing
    // XNAMessageBox dialogs with host-provided error callbacks (no XNAUI dependency).
}
