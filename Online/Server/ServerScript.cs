using System;
using System.Net.Sockets;
using BmSDK.BmGame;
using BmSDK.Engine;
using Online.Components;
using Online.Core;
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

        // Check for new messages from clients
        foreach (var client in _clients)
        {
            lock (client.MessageQueue)
            {
                client.ProcessMessages();
            }
        }

        // Transform sync is now handled by NetPlayerComponent.OnTick()

        base.OnTick();
    }

    private void StartServer()
    {
        // Initialize NetworkManager as server
        NetworkManager.InitAsServer();

        // Attach NetPlayerComponent to host pawn
        var hostPawn = Game.GetPlayerPawn(0);
        hostPawn.AttachScriptComponent<NetPlayerComponent>();
        Debug.Log($"[Server] Attached NetPlayerComponent to host pawn");

        // Find host
        var hostEndPoint = OnlineUtils.GetLocalEndPoint();

        // Create socket to listen on
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.Bind(OnlineUtils.GetLocalEndPoint());
        _socket.Listen(10);

        // Begin accepting connections
        _socket.BeginAccept(OnSocketAccept, null);

        // Disable pausing on focus loss
        Game.GetEngine().bPauseOnLossOfFocus = false;

        Debug.Log($"[Server] Listening on port {hostEndPoint.Port}");
    }

    private void OnSocketAccept(IAsyncResult result)
    {
        // Finish async accept
        var newSocket = _socket.EndAccept(result);

        Debug.Log($"[Server] Client connected: {newSocket.RemoteEndPoint}");

        // Register connected client (socket added to NetworkManager after join handshake)
        var client = new ServerClientConnection(newSocket, _clients);
        _clients.Add(client);

        // Resume listening
        _socket.BeginAccept(OnSocketAccept, null);
    }
}