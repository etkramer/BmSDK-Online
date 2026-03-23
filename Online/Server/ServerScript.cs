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
    public const double TickIntervalSeconds = 1.0 / OnlineUtils.TickRate;

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

    public override void OnUnload()
    {
        NetworkManager.Instance?.DestroyAllRemotePlayers();

        foreach (var client in _clients)
            client.Close();
        _clients.Clear();

        _socket?.Close();
        _socket = null;

        NetworkManager.Shutdown();

        base.OnUnload();
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

        // Clean up disconnected clients
        for (int i = _clients.Count - 1; i >= 0; i--)
        {
            if (!_clients[i].IsConnected)
            {
                HandleClientDisconnect(_clients[i]);
                _clients.RemoveAt(i);
            }
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

    private void HandleClientDisconnect(ServerClientConnection client)
    {
        Debug.Log($"[Server] Client disconnected: \"{client.DisplayName}\" (NetId={client.ClientNetId})");

        NetworkManager.Instance?.RemoveClientSocket(client.Socket);

        var playerComponent = NetworkManager.Instance?.GetRemotePlayer(client.ClientNetId);
        if (playerComponent?.Owner is RPawnPlayerCombat pawn)
        {
            pawn.Controller?.Destroy();
            pawn.Destroy();
        }

        NetworkManager.Instance?.UnregisterRemotePlayer(client.ClientNetId);
    }

    private void StartServer()
    {
        // Initialize NetworkManager as server
        NetworkManager.InitAsServer();

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
        Socket newSocket;
        try
        {
            newSocket = _socket?.EndAccept(result);
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (SocketException)
        {
            return;
        }

        if (newSocket == null) return;

        Debug.Log($"[Server] Client connected: {newSocket.RemoteEndPoint}");

        // Register connected client
        var client = new ServerClientConnection(newSocket, _clients);
        _clients.Add(client);

        // Resume listening
        try
        {
            _socket.BeginAccept(OnSocketAccept, null);
        }
        catch (ObjectDisposedException) { }
    }
}