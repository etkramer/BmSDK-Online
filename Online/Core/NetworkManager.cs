using System.Net.Sockets;
using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;
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

    public NetPlayerComponent GetRemotePlayer(int netId)
    {
        return _remotePlayers.GetValueOrDefault(netId);
    }

    public void AddClientSocket(Socket socket)
    {
        _clientSockets.Add(socket);
    }

    public void SetServerSocket(Socket socket)
    {
        _serverSocket = socket;
    }

    public void BroadcastTransform(int netId, Vector3 location, Rotator rotation)
    {
        var message = new ActorMoveMessage(netId, location, rotation);

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
            component.ReceiveTransform(message.NewLocation, message.NewRotation);
        }
        else
        {
            Debug.Log($"[NetworkManager] No remote player found for NetId={message.NetId}, registered: {string.Join(", ", _remotePlayers.Keys)}");
        }

        // If we're the server, forward to other clients
        if (IsServer)
        {
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
    }

    public void HandleActorSpawn(ActorSpawnMessage message)
    {
        // Spawn a remote pawn
        var pawn = Game.SpawnActor<RPawnPlayerBm>(message.Location, message.Rotation);
        if (pawn != null)
        {
            // Manually attach NetPlayerComponent and set NetId
            var component = pawn.AttachScriptComponent<NetPlayerComponent>();
            component.SetNetId(message.NetId);
            RegisterRemotePlayer(message.NetId, component);
            Debug.Log($"[NetworkManager] Spawned remote pawn for NetId={message.NetId}");
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
