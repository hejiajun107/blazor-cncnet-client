namespace CnCNetClient.Online
{
    /// <summary>
    /// Abstraction for a view that can render chat messages.
    /// Migrated from DTAClient.DXGUI.IMessageView and下沉 into CnCNetClient.Online
    /// so that <see cref="Channel"/> can depend on it without反向 referencing the UI layer.
    /// The Blazor UI layer (chat list / channel view components) implements this interface
    /// and subscribes to <see cref="Channel"/> events.
    /// </summary>
    public interface IMessageView
    {
        void AddMessage(ChatMessage message);
    }
}
