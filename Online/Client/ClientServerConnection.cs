using System.Net.Sockets;
using BmSDK;

namespace Online.Client;

public class ClientServerConnection(Socket socket) : Connection(socket)
{
    protected override void ProcessMessage(Message message)
    {
        // TODO: Respect NetId
        if (message is ActorMoveMessage actorMoveMessage)
        {
            var hostPawn = ClientScript.HostPawn;
            if (hostPawn != null)
            {
                hostPawn.SetLocation(actorMoveMessage.NewLocation);
                hostPawn.SetRotation(actorMoveMessage.NewRotation);
            }
        }

        base.ProcessMessage(message);
    }
}