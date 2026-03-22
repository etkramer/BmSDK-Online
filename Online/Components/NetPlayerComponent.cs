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
        // Poll animation state every tick (only sends on change)
        PollAndSendAnimState();

        // Throttle transform send rate
        if (_sendTimer.Elapsed.TotalSeconds < SendInterval)
        {
            return;
        }
        _sendTimer.Restart();

        // Broadcast position to network
        NetworkManager.Instance?.BroadcastTransform(NetId, Owner.Location, Owner.Rotation);
    }

    private void PollAndSendAnimState()
    {
        // Read current pose from RAnimNode_Pose
        var anim = Owner.Anim;
        if (anim == null) return;

        var changes = anim.PosePlayer.Changes;
        if (changes.Count == 0) return;

        // Get the current (head) stance change's pose input
        var poseInput = changes[0].Pose.Input;
        var currentMovement = poseInput.MovementStance;
        var currentWeapon = poseInput.WeaponStance;
        var currentIdle = poseInput.IdleStance;
        var currentPhysics = Owner.Physics;

        // Only send if something changed
        bool changed = currentMovement != _lastMovementStance
                    || currentWeapon != _lastWeaponStance
                    || currentIdle != _lastIdleStance
                    || currentPhysics != _lastPhysics;

        if (changed)
        {
            _lastMovementStance = currentMovement;
            _lastWeaponStance = currentWeapon;
            _lastIdleStance = currentIdle;
            _lastPhysics = currentPhysics;

            NetworkManager.Instance?.BroadcastAnimState(
                NetId,
                currentMovement.ToString(),
                currentWeapon.ToString(),
                currentIdle.ToString(),
                (byte)currentPhysics
            );
        }
    }

    private void HandleRemoteTick()
    {
        // Interpolate toward latest received position
        var (position, rotation) = _interpolationBuffer.Interpolate(_gameTime);

        // Only update if we have valid data
        if (position.X != 0 || position.Y != 0 || position.Z != 0)
        {
            Owner.Location = position;
            Owner.Rotation = rotation;
        }
    }

    public void ReceiveTransform(Vector3 position, Rotator rotation)
    {
        _interpolationBuffer.AddSnapshot(position, rotation, _gameTime);
    }

    public void ReceiveAnimState(string movementStance, string weaponStance, string idleStance, byte physics)
    {
        // Apply pose change to remote pawn
        var movement = new FName(movementStance);
        var weapon = new FName(weaponStance);
        var idle = new FName(idleStance);

        // ForceChangePose applies immediately without transition
        Owner.ForceChangePose(movement, weapon, idle);

        // Apply physics state
        Owner.SetPhysics((EPhysics)physics);
    }
}
