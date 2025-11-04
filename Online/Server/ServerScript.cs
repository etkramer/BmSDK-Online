using System;
using System.Net.Sockets;
using System.Numerics;
using BmSDK.BmGame;
using BmSDK.Engine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Online.Server;

[Script]
public class ServerScript : Script
{
    public const double TickRate = 60;
    public const double TickIntervalSeconds = 1.0 / TickRate;

    private Socket _socket = null;
    private readonly List<ServerClientConnection> _clients = [];

    private readonly Stopwatch _limiterWatch = Stopwatch.StartNew();

    public override void OnKeyDown(Keys key)
    {
        if (key == Keys.OemOpenBrackets)
        {
            StartServer();
        }

        base.OnKeyDown(key);
    }

    public override void OnEnterGame()
    {
        // Add net components to various objects we want synced
        var hostPawn = Game.GetPlayerPawn(0);
        var hostPawnNet = hostPawn.AttachScriptComponent<NetComponent>();

        base.OnEnterGame();
    }

    public override void OnTick()
    {
        // Do nothing if not listening
        if (_socket == null)
        {
            return;
        }

        // Limit server tickrate
        if (_limiterWatch.Elapsed.TotalSeconds >= TickIntervalSeconds)
        {
            _limiterWatch.Restart();
        }
        else
        {
            return;
        }

        // Check for new messages
        foreach (var client in _clients)
        {
            lock (client.MessageQueue)
            {
                client.ProcessMessages();
            }
        }

        // Update clients with new world state
        foreach (var client in _clients)
        {
            var hostPawn = Game.GetPlayerPawn(0);
            var hostPawnNet = hostPawn.GetScriptComponent<NetComponent>();

            // Send new pawn location to client
            var message = new ActorMoveMessage(hostPawnNet.NetId, hostPawn.Location, hostPawn.Rotation);
            message.Send(client.Socket);
        }

        base.OnTick();
    }

    private void StartServer()
    {
        // Find host
        var hostEndPoint = OnlineUtils.GetLocalEndPoint();

        // Create socket to listen on
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Bind(OnlineUtils.GetLocalEndPoint());
        _socket.Listen(10);

        // Begin accepting connections
        _socket.BeginAccept(OnSocketAccept, null);

        // Disable pausing on focus loss
        Game.GetWorldInfo().NetMode = WorldInfo.ENetMode.NM_ListenServer;
        Game.GetEngine().bPauseOnLossOfFocus = false;

        Debug.Log($"Listening on port {hostEndPoint.Port}");
    }

    private void OnSocketAccept(IAsyncResult result)
    {
        // Finish async accept
        var newSocket = _socket.EndAccept(result);

        Debug.Log($"Connected to client {newSocket.RemoteEndPoint}");

        // Register connected client
        var client = new ServerClientConnection(newSocket);
        _clients.Add(client);

        // Resume listening
        _socket.BeginAccept(OnSocketAccept, null);
    }
}