using System.Net.Sockets;
using BmSDK.BmGame;
using BmSDK.BmScript;
using Online.Components;
using Online.Core;

namespace Online.Server;

public class ServerClientConnection : Connection
{
    public bool IsJoined { get; set; }
    public string DisplayName { get; set; } = null;
    public int ClientNetId { get; set; } = -1;

    private readonly List<ServerClientConnection> _allClients;

    public ServerClientConnection(Socket socket, List<ServerClientConnection> allClients) : base(socket)
    {
        _allClients = allClients;
    }

    protected override void ProcessMessage(Message message)
    {
        if (message is JoinMessage joinMessage)
        {
            HandleJoin(joinMessage);
        }
        else if (message is ActorMoveMessage moveMessage)
        {
            // Route through NetworkManager (will forward to other clients)
            NetworkManager.Instance?.HandleActorMove(moveMessage, Socket);
        }
        else if (message is PlayerInputMessage inputMessage)
        {
            NetworkManager.Instance?.HandlePlayerInput(inputMessage, Socket);
        }
        else if (message is InputEventMessage inputEventMessage)
        {
            NetworkManager.Instance?.HandleInputEvent(inputEventMessage, Socket);
        }

        base.ProcessMessage(message);
    }

    private void HandleJoin(JoinMessage joinMessage)
    {
        DisplayName = joinMessage.DisplayName;
        ClientNetId = joinMessage.NetId;
        IsJoined = true;

        Debug.Log($"[Server] Player joined: \"{DisplayName}\" (NetId={ClientNetId})");

        // Spawn a pawn for this client on the server (using same path as clients)
        var gameInfo = Game.GetGameInfo();
        var playerStart = gameInfo.FindPlayerStart(Game.GetPlayerController(0));
        var newClientSpawnMessage = new ActorSpawnMessage(
            ClientNetId,
            nameof(RPawnPlayerBm),
            playerStart.Location,
            playerStart.Rotation
        );
        NetworkManager.Instance?.HandleActorSpawn(newClientSpawnMessage);

        // Tell new client about existing clients
        foreach (var otherClient in _allClients)
        {
            if (!otherClient.IsJoined || otherClient == this)
            {
                continue;
            }

            var existingClientSpawnMessage = new ActorSpawnMessage(
                otherClient.ClientNetId,
                nameof(RPawnPlayerBm),
                playerStart.Location,
                playerStart.Rotation
            );
            existingClientSpawnMessage.Send(Socket);
            Debug.Log($"[Server] Told new client {ClientNetId} about existing client {otherClient.ClientNetId}");
        }

        // Send the host's player info back to the client so they can spawn a remote pawn
        var hostPawn = Game.GetPlayerPawn(0);
        var hostComponent = hostPawn?.GetScriptComponent<NetPlayerComponent>();
        if (hostComponent != null)
        {
            var spawnMessage = new ActorSpawnMessage(
                hostComponent.NetId,
                nameof(RPawnPlayerBm),
                hostPawn.Location,
                hostPawn.Rotation
            );
            spawnMessage.Send(Socket);

            // Send host's current input state so remote starts in sync
            var hostController = hostPawn.Controller as RPlayerControllerCombat;
            var hostInput = hostController?.PlayerInput as RPlayerInput;
            if (hostInput != null)
            {
                var inputMessage = new PlayerInputMessage(
                    hostComponent.NetId,
                    hostInput.aForward,
                    hostInput.aStrafe,
                    hostInput.bCrouchButton,
                    hostInput.bRunButton,
                    hostController.InputHeading(false),
                    hostInput.bReadyGadgetButton,
                    hostInput.aGrappleButton,
                    hostInput.aTurn,
                    hostInput.aLookUp
                );
                inputMessage.Send(Socket);
            }
        }

        // Now that client knows about host, add socket for transform broadcasts
        NetworkManager.Instance?.AddClientSocket(Socket);
    }
}
