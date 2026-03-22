using System.Net.Sockets;
using BmSDK.BmGame;
using BmSDK.BmScript;
using BmSDK.Engine;
using Online.Components;
using Online.Core;

namespace Online.Client;

[Script]
public class ClientScript : Script
{
    private Socket _socket = null;
    private ClientServerConnection _connection = null;

    public override void OnKeyDown(Keys key)
    {
        if (key == Keys.OemCloseBrackets)
        {
            StartClient();
        }

        base.OnKeyDown(key);
    }

    public override void OnTick()
    {
        // Do nothing if not connected
        if (_connection == null)
        {
            return;
        }

        // Check for new messages from server
        lock (_connection.MessageQueue)
        {
            _connection.ProcessMessages();
        }

        // Transform sync is now handled by NetPlayerComponent.OnTick()

        base.OnTick();
    }

    private void StartClient()
    {
        // Initialize NetworkManager as client
        NetworkManager.InitAsClient();

        // Attach NetPlayerComponent to local pawn
        var localPawn = Game.GetPlayerPawn(0);
        localPawn.AttachScriptComponent<NetPlayerComponent>();
        Debug.Log($"[Client] Attached NetPlayerComponent to local pawn");

        // Create client socket
        _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        // Connect to host (non-blocking)
        Debug.Log("[Client] Connecting to server...");
        _socket.BeginConnect(OnlineUtils.GetLocalEndPoint(), OnSocketConnect, null);

        // Disable pausing on focus loss
        Game.GetEngine().bPauseOnLossOfFocus = false;
    }

    private void OnSocketConnect(IAsyncResult result)
    {
        // Finish async connect
        _socket.EndConnect(result);

        Debug.Log($"[Client] Connected to server {_socket.RemoteEndPoint}");

        // Register socket with NetworkManager
        NetworkManager.Instance?.SetServerSocket(_socket);

        // Create new connection object
        _connection = new ClientServerConnection(_socket);

        // Send "join" message to server (includes our local player's NetId)
        var localPawn = (RPawnPlayerCombat)Game.GetPlayerPawn(0);
        var localComponent = localPawn?.GetScriptComponent<NetPlayerComponent>();
        var netId = localComponent?.NetId ?? 0;
        Debug.Log($"[Client] Sending JoinMessage with NetId={netId}");
        var joinMessage = new JoinMessage("Player", netId);
        joinMessage.Send(_socket);
    }
}