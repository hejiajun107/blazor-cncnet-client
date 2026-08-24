namespace CnCNetClient.Integration.Lifecycle;

/// <summary>
/// Notifies subscribers when the host application is closing.
/// Replaces <c>windowManager.GameClosing</c> event from XNAUI.
/// </summary>
public interface IAppExitNotifier
{
    /// <summary>
    /// Raised when the application is about to exit.
    /// Subscribers should persist state here (e.g. save user data).
    /// </summary>
    event EventHandler ApplicationExiting;
}
