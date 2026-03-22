using System.Net.Sockets;
using Online.Core;

namespace Online.Client;

public class ClientServerConnection(Socket socket) : Connection(socket)
{
    protected override void ProcessMessage(Message message)
    {
        if (message is ActorMoveMessage moveMessage)
        {
            // Route through NetworkManager for interpolated handling
            NetworkManager.Instance?.HandleActorMove(moveMessage);
        }
        else if (message is ActorSpawnMessage spawnMessage)
        {
            // Server is telling us to spawn a remote player (the host)
            NetworkManager.Instance?.HandleActorSpawn(spawnMessage);
        }

        base.ProcessMessage(message);
    }
}