using System.Net.Sockets;
using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;
using BmSDK.Engine;
using Online.Components;

namespace Online.Core;

public class NetworkManager
{
    public static NetworkManager Instance { get; private set; }

    public bool IsServer { get; private set; }
    public bool IsClient => !IsServer;

    private readonly List<Socket> _clientSockets = [];
    private Socket _serverSocket;

    private readonly Dictionary<int, NetPlayerComponent> _remotePlayers = [];

    public static void InitAsServer()
    {
        Instance = new NetworkManager { IsServer = true };
    }

    public static void InitAsClient()
    {
        Instance = new NetworkManager { IsServer = false };
    }

    public void RegisterRemotePlayer(int netId, NetPlayerComponent component)
    {
        _remotePlayers[netId] = component;
        Debug.Log($"[NetworkManager] Registered remote player NetId={netId}");
    }

    public void UnregisterRemotePlayer(int netId)
    {
        _remotePlayers.Remove(netId);
    }

    public void DestroyAllRemotePlayers()
    {
        foreach (var component in _remotePlayers.Values)
        {
            var pawn = component.Owner;
            if (pawn == null) continue;
            pawn.Controller?.Destroy();
            pawn.Destroy();
        }
        _remotePlayers.Clear();
    }

    public NetPlayerComponent GetRemotePlayer(int netId)
    {
        return _remotePlayers.GetValueOrDefault(netId);
    }

    public void AddClientSocket(Socket socket)
    {
        _clientSockets.Add(socket);
    }

    public void RemoveClientSocket(Socket socket)
    {
        _clientSockets.Remove(socket);
    }

    public void SetServerSocket(Socket socket)
    {
        _serverSocket = socket;
    }

    public static void Shutdown()
    {
        Instance = null;
    }

    public void BroadcastTransform(int netId, Vector3 location, Rotator rotation, Vector3 moveDirection, Rotator controllerRotation)
    {
        var message = new ActorMoveMessage(netId, location, rotation, moveDirection, controllerRotation);
        BroadcastMessage(message);
    }

    public void BroadcastPlayerInput(int netId, float aForward, float aBaseY, float aStrafe, byte bCrouchButton, byte bRunButton, Vector3 inputHeading, byte bReadyGadgetButton, byte aGrappleButton, float aTurn, float aLookUp)
    {
        var message = new PlayerInputMessage(netId, aForward, aBaseY, aStrafe, bCrouchButton, bRunButton, inputHeading, bReadyGadgetButton, aGrappleButton, aTurn, aLookUp);
        BroadcastMessage(message);
    }

    public void BroadcastInputEvent(int netId, string eventName)
    {
        var message = new InputEventMessage(netId, eventName);
        BroadcastMessage(message);
    }

    private void BroadcastMessage(Message message)
    {
        if (IsServer)
        {
            // Server sends to all clients
            foreach (var socket in _clientSockets)
            {
                try
                {
                    message.Send(socket);
                }
                catch (SocketException)
                {
                    // Client disconnected - handle elsewhere
                }
            }
        }
        else
        {
            // Client sends to server
            if (_serverSocket?.Connected == true)
            {
                try
                {
                    message.Send(_serverSocket);
                }
                catch (SocketException)
                {
                    // Server disconnected
                }
            }
        }
    }

    public void HandleActorMove(ActorMoveMessage message, Socket senderSocket = null)
    {
        // Find the remote player component with this NetId
        var component = GetRemotePlayer(message.NetId);
        if (component != null)
        {
            component.ReceiveTransform(message.NewLocation, message.NewRotation, message.MoveDirection, message.ControllerRotation);
        }
        else
        {
            Debug.Log($"[NetworkManager] No remote player found for NetId={message.NetId}, registered: {string.Join(", ", _remotePlayers.Keys)}");
        }

        // If we're the server, forward to other clients
        ForwardToOtherClients(message, senderSocket);
    }

    public void HandlePlayerInput(PlayerInputMessage message, Socket senderSocket = null)
    {
        // Find the remote player's controller
        var playerComponent = GetRemotePlayer(message.NetId);
        if (playerComponent?.Owner?.Controller is RPlayerControllerCombat controller)
        {
            var controllerComponent = controller.GetScriptComponent<NetControllerComponent>();
            controllerComponent?.ReceivePlayerInput(
                message.AForward,
                message.ABaseY,
                message.AStrafe,
                message.BCrouchButton,
                message.BRunButton,
                message.InputHeading,
                message.BReadyGadgetButton,
                message.AGrappleButton,
                message.ATurn,
                message.ALookUp
            );
        }

        // If we're the server, forward to other clients
        ForwardToOtherClients(message, senderSocket);
    }

    public void HandleInputEvent(InputEventMessage message, Socket senderSocket = null)
    {
        var playerComponent = GetRemotePlayer(message.NetId);
        if (playerComponent?.Owner?.Controller is RPlayerControllerCombat controller)
        {
            var controllerComponent = controller.GetScriptComponent<NetControllerComponent>();
            controllerComponent?.ReceiveInputEvent(message.EventName);
        }

        ForwardToOtherClients(message, senderSocket);
    }

    private void ForwardToOtherClients(Message message, Socket senderSocket)
    {
        if (!IsServer) return;

        foreach (var socket in _clientSockets)
        {
            if (socket == senderSocket)
            {
                // Don't echo back to sender
                continue;
            }

            try
            {
                message.Send(socket);
            }
            catch (SocketException)
            {
                // Client disconnected
            }
        }
    }

    public void HandleActorSpawn(ActorSpawnMessage message)
    {
        var gameViewportClient = Game.GetGameViewportClient();

        // Spawn a controller for the remote player
        var player = gameViewportClient.CreatePlayer(1, out _, true);
        var controller = (RPlayerControllerCombat)player.Actor;
        if (controller != null)
        {
            Debug.Log($"[NetworkManager] Spawned controller NetId={message.NetId}");

            // Set up the controller's NetControllerComponent
            var controllerComponent = controller.GetScriptComponent<NetControllerComponent>();
            if (controllerComponent != null)
            {
                controllerComponent.SetNetId(message.NetId);
            }
        }
        else
        {
            Debug.Log($"[NetworkManager] Failed to spawn controller for NetId={message.NetId}");
        }

        var pawn = (RPawnPlayerCombat)controller.Pawn;
        if (pawn == null)
        {
            Debug.Log($"[NetworkManager] Failed to spawn pawn for NetId={message.NetId}");
            return;
        }

        // Set up the pawn's NetPlayerComponent
        var playerComponent = pawn.GetScriptComponent<NetPlayerComponent>();
        if (playerComponent != null)
        {
            playerComponent.SetNetId(message.NetId);
            RegisterRemotePlayer(message.NetId, playerComponent);
        }

        // If we're the server, forward to other clients
        if (IsServer)
        {
            foreach (var socket in _clientSockets)
            {
                try
                {
                    message.Send(socket);
                }
                catch (SocketException)
                {
                    // Client disconnected
                }
            }
        }
    }
}
