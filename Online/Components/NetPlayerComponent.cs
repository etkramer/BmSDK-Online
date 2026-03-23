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

    // Animation state tracking (for change detection)
    private FName _lastMovementStance;
    private FName _lastWeaponStance;
    private FName _lastIdleStance;
    private EPhysics _lastPhysics;

    // Input sync for locomotion
    private Vector3 _lastMoveDirection;
    private Rotator _lastControllerRotation;

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
        // Interpolate toward latest received position
        var (position, rotation) = _interpolationBuffer.Interpolate(_gameTime);

        // Only update if we have valid data
        if (position.X != 0 || position.Y != 0 || position.Z != 0)
        {
            // Always call MoveInDirection - it handles both movement animation AND facing
            // Zero vector = stop moving, non-zero = walk/run in that direction
            Owner.MoveInDirection(_lastMoveDirection);

            // Apply the sender's look direction to the remote controller
            if (Owner.Controller is RPlayerControllerCombat remoteController)
            {
                remoteController.SetRotation(_lastControllerRotation);
            }

            // Correct position to prevent drift from root motion
            var currentPos = Owner.Location;
            var posDiff = position - currentPos;

            if (posDiff.LengthSquared() > 10000) // ~100 units - snap
            {
                Owner.Location = position;
                Owner.Rotation = rotation;
            }
            else
            {
                Owner.Location = position;
            }
        }
    }

    public void ReceiveTransform(Vector3 position, Rotator rotation, Vector3 moveDirection, Rotator controllerRotation)
    {
        _interpolationBuffer.AddSnapshot(position, rotation, _gameTime);
        _lastMoveDirection = moveDirection;
        _lastControllerRotation = controllerRotation;
    }
}
