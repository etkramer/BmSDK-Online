using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;
using BmSDK.Engine;
using BmSDK.Framework;
using Online.Core;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Online.Components;

[ScriptComponent(AutoAttach = true)]
public class NetPlayerComponent : ScriptComponent<RPawnPlayerCombat>
{
    private const double SendRateHz = 20.0;
    private const double SendInterval = 1.0 / SendRateHz;

    public int NetId { get; private set; } = -1;
    public bool IsLocal => Owner == Game.GetPlayerPawn(0);
    public bool IsRemote => !IsLocal;

    private readonly InterpolationBuffer _interpolationBuffer = new();
    private readonly Stopwatch _sendTimer = Stopwatch.StartNew();
    private double _gameTime = 0;

    public NetPlayerComponent()
    {
        NetId = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0);
    }

    public void SetNetId(int netId)
    {
        NetId = netId;
    }

    public override void OnAttach()
    {
        Debug.Log($"[NetPlayerComponent] Attached to {Owner.Name}, IsLocal={IsLocal}, NetId={NetId}");
        base.OnAttach();
    }

    public override void OnTick()
    {
        _gameTime += Game.GetWorldInfo().DeltaSeconds;

        if (IsLocal)
        {
            HandleLocalTick();
        }
        else
        {
            HandleRemoteTick();
        }

        base.OnTick();
    }

    private void HandleLocalTick()
    {
        // Throttle send rate
        if (_sendTimer.Elapsed.TotalSeconds < SendInterval)
        {
            return;
        }
        _sendTimer.Restart();

        // Broadcast position to network
        NetworkManager.Instance?.BroadcastTransform(NetId, Owner.Location, Owner.Rotation);
    }

    private void HandleRemoteTick()
    {
        // Interpolate toward latest received position
        var (position, rotation) = _interpolationBuffer.Interpolate(_gameTime);

        // Only update if we have valid data
        if (position.X != 0 || position.Y != 0 || position.Z != 0)
        {
            Owner.SetLocation(position);
            Owner.SetRotation(rotation);
        }
    }

    public void ReceiveTransform(Vector3 position, Rotator rotation)
    {
        _interpolationBuffer.AddSnapshot(position, rotation, _gameTime);
    }
}
