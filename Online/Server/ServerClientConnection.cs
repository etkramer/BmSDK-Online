using System.Net.Sockets;

namespace Online.Server;

public class ServerClientConnection(Socket socket) : Connection(socket)
{
    // True if the "join" message has been recieved
    public bool IsJoined {  get; set; }

    public string DisplayName { get; set; } = null;

    protected override void ProcessMessage(Message message)
    {
        // Handle message types
        if (message is JoinMessage joinMessage)
        {
            // Store user details
            DisplayName = joinMessage.DisplayName;
            IsJoined = true;

            // Report join
            Debug.Log($"Joined user \"{DisplayName}\"");
        }

        base.ProcessMessage(message);
    }
}
