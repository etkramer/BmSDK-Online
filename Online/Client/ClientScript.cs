using System.Net.Sockets;
using BmSDK.BmGame;
using BmSDK.BmScript;
using BmSDK.Engine;

namespace Online.Client;

[Script]
public class ClientScript : Script
{
    private Socket _socket = null;
    private ClientServerConnection _connection = null;

    // TEMP
    public static RPawnPlayerBm HostPawn = null;

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

        // Check for new messages
        lock (_connection.MessageQueue)
        {
            _connection.ProcessMessages();
        }

        base.OnTick();
    }

    // TEMP
    private static void SpawnHostPawn()
    {
        var gameInfo = Game.GetGameInfo();
        var playerStart = gameInfo.FindPlayerStart(Game.GetPlayerController(0));

        HostPawn = Game.SpawnActor<RPawnPlayerBm>(playerStart.Location, playerStart.Rotation);
    }

    private void StartClient()
    {
        // Create client socket
        _socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        // Connect to host (non-blocking)
        Debug.Log("Connecting to server...");
        _socket.BeginConnect(OnlineUtils.GetLocalEndPoint(), OnSocketConnect, null);

        // Disable pausing on focus loss
        Game.GetEngine().bPauseOnLossOfFocus = false;

        // TEMP
        SpawnHostPawn();
    }

    private void OnSocketConnect(IAsyncResult result)
    {
        // Finish async connect
        _socket.EndConnect(result);

        Debug.Log($"Connected to server {_socket.RemoteEndPoint}");

        // Create new connection object
        _connection = new ClientServerConnection(_socket);

        // Send "join" message to server
        var joinMessage = new JoinMessage("SomeUser");
        joinMessage.Send(_socket);
    }
}