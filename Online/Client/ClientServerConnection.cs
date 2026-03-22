using System.Net.Sockets;
using Online.Core;

namespace Online.Client;

public class ClientServerConnection(Socket socket) : Connection(socket)
{
    protected override void ProcessMessage(Message message)
    {
        if (message is ActorMoveMessage moveMessage)
        {
            NetworkManager.Instance?.HandleActorMove(moveMessage);
        }
        else if (message is ActorSpawnMessage spawnMessage)
        {
            Debug.Log($"[Client] Received spawn message for NetId={spawnMessage.NetId}");
            NetworkManager.Instance?.HandleActorSpawn(spawnMessage);
        }

        base.ProcessMessage(message);
    }
}