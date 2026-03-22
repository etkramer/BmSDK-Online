using System.Net.Sockets;
using BmSDK.BmGame;
using BmSDK.BmScript;
using Online.Components;
using Online.Core;

namespace Online.Server;

public class ServerClientConnection(Socket socket) : Connection(socket)
{
    public bool IsJoined { get; set; }
    public string DisplayName { get; set; } = null;
    public int ClientNetId { get; set; } = -1;

    protected override void ProcessMessage(Message message)
    {
        if (message is JoinMessage joinMessage)
        {
            HandleJoin(joinMessage);
        }
        else if (message is ActorMoveMessage moveMessage)
        {
            // Route through NetworkManager (will forward to other clients)
            NetworkManager.Instance?.HandleActorMove(moveMessage);
        }

        base.ProcessMessage(message);
    }

    private void HandleJoin(JoinMessage joinMessage)
    {
        DisplayName = joinMessage.DisplayName;
        ClientNetId = joinMessage.NetId;
        IsJoined = true;

        Debug.Log($"[Server] Player joined: \"{DisplayName}\" (NetId={ClientNetId})");

        // Spawn a pawn for this client on the server
        var gameInfo = Game.GetGameInfo();
        var playerStart = gameInfo.FindPlayerStart(Game.GetPlayerController(0));
        var clientPawn = Game.SpawnActor<RPawnPlayerCombat>(playerStart.Location, playerStart.Rotation);

        if (clientPawn != null)
        {
            // The pawn auto-attaches NetPlayerComponent, update its NetId to match client
            var component = clientPawn.GetScriptComponent<NetPlayerComponent>();
            if (component != null)
            {
                component.SetNetId(ClientNetId);
                NetworkManager.Instance?.RegisterRemotePlayer(ClientNetId, component);
            }
        }

        // Send the host's player info back to the client so they can spawn a remote pawn
        var hostPawn = Game.GetPlayerPawn(0);
        var hostComponent = hostPawn?.GetScriptComponent<NetPlayerComponent>();
        if (hostComponent != null)
        {
            var spawnMessage = new ActorSpawnMessage(
                hostComponent.NetId,
                "RPawnPlayerBm",
                hostPawn.Location,
                hostPawn.Rotation
            );
            spawnMessage.Send(Socket);
        }
    }
}
