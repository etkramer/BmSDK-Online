using System.Numerics;
using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;
using BmSDK.Engine;
using BmSDK.Framework;
using Online.Core;
using Stopwatch = System.Diagnostics.Stopwatch;
using EPhysics = BmSDK.Engine.Actor.EPhysics;

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
        Debug.Log($"[NetPlayerComponent] Attached to {Owner.Name}, NetId={NetId}");
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
        // Throttle transform send rate
        if (_sendTimer.Elapsed.TotalSeconds < SendInterval)
        {
            return;
        }
        _sendTimer.Restart();

        // Get movement input direction (what the player is pressing)
        // Check if there's actual input first, otherwise return zero
        var controller = Owner.Controller as RPlayerControllerCombat;
        var moveDirection = controller?.HasMovementInput() == true
            ? Owner.InputHeading()
            : Vector3.Zero;

        // Broadcast position, movement input, and look direction to network
        var controllerRotation = controller?.Rotation ?? default;
        NetworkManager.Instance?.BroadcastTransform(NetId, Owner.Location, Owner.Rotation, moveDirection, controllerRotation);
    }

    private void HandleRemoteTick()
    {
        if (!_interpolationBuffer.HasData)
        {
            return;
        }

        // All four values are buffered together so they stay in sync
        var (position, rotation, controllerRotation, moveDirection) = _interpolationBuffer.Interpolate(_gameTime);

        // Apply the sender's look direction to the remote controller
        if (Owner.Controller is RPlayerControllerCombat remoteController)
        {
            remoteController.SetRotation(controllerRotation);
        }

        Owner.MoveInDirection(moveDirection);

        // Position correction: use a dead-zone so we don't fight physics-driven
        // movement (gliding, climbing, falling) on every frame.
        var posDiff = position - Owner.Location;
        float driftSq = posDiff.LengthSquared();

        if (driftSq > 10000) // >~100 units: hard snap, also fix rotation
        {
            Owner.Location = position;
            Owner.Rotation = rotation;
        }
        else if (driftSq > 25) // >~5 units: correct drift
        {
            Owner.Location = position;
        }
        // else: within tolerance — let physics/animation own the position
    }

    public void ReceiveTransform(Vector3 position, Rotator rotation, Vector3 moveDirection, Rotator controllerRotation)
    {
        _interpolationBuffer.AddSnapshot(position, rotation, controllerRotation, moveDirection, _gameTime);
    }
}
