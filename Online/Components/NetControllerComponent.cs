using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;
using Online.Core;

namespace Online.Components;

/// <summary>
/// Syncs PlayerController state changes (WalkingMode, RunningMode, StealthMoveMode, etc.)
/// between local and remote players via polling.
/// </summary>
[ScriptComponent(AutoAttach = true)]
public class NetControllerComponent : ScriptComponent<RPlayerControllerCombat>
{
    // States that are safe to sync - others may crash due to missing subsystems
    private static readonly HashSet<string> SyncableStates =
    [
        "WalkingMode",
        "RunningMode",
        "StealthMoveMode",
    ];

    public int NetId { get; private set; } = -1;

    /// <summary>
    /// True if this controller belongs to the local player.
    /// </summary>
    public bool IsLocal
    {
        get
        {
            if (Game.GetGameViewportClient().FindPlayerByControllerId(0)?.Actor?.Pawn == null)
            {
                return true;
            }

            return Owner.Pawn == Game.GetPlayerPawn(0);
        }
    }

    /// <summary>
    /// True if this controller belongs to a remote player.
    /// </summary>
    public bool IsRemote => !IsLocal;

    private FName _lastStateName;

    public void SetNetId(int netId)
    {
        NetId = netId;
    }

    public override void OnAttach()
    {
        // Get NetId from the associated pawn's NetPlayerComponent
        var pawn = Owner.Pawn as RPawnPlayerCombat;
        var playerComponent = pawn?.GetScriptComponent<NetPlayerComponent>();
        if (playerComponent != null)
        {
            NetId = playerComponent.NetId;
        }

        _lastStateName = Owner.GetStateName();
        Debug.Log($"[NetControllerComponent] Attached to controller for pawn, NetId={NetId}, IsLocal={IsLocal}, State={_lastStateName}");

        base.OnAttach();
    }

    public override void OnTick()
    {
        // Try to acquire NetId if we don't have one yet
        if (NetId == -1)
        {
            var pawn = Owner.Pawn as RPawnPlayerCombat;
            var playerComponent = pawn?.GetScriptComponent<NetPlayerComponent>();
            if (playerComponent != null && playerComponent.NetId != -1)
            {
                NetId = playerComponent.NetId;
                Debug.Log($"[NetControllerComponent] Acquired NetId={NetId} for {(IsLocal ? "local" : "remote")} controller");
            }
        }

        // Poll for state changes on local controller
        if (IsLocal && NetId != -1)
        {
            var currentState = Owner.GetStateName();
            if (currentState != _lastStateName)
            {
                _lastStateName = currentState;

                // Only broadcast states that are safe to sync
                if (SyncableStates.Contains(currentState.ToString()))
                {
                    NetworkManager.Instance?.BroadcastControllerState(NetId, currentState);
                }
            }
        }

        base.OnTick();
    }

    /// <summary>
    /// Called when receiving a state change from the network.
    /// </summary>
    public void ReceiveStateChange(FName newStateName)
    {
        if (!IsRemote)
        {
            return;
        }

        // Only apply states that are safe to sync
        var stateStr = newStateName.ToString();
        if (!SyncableStates.Contains(stateStr))
        {
            return;
        }

        var currentState = Owner.GetStateName();
        if (currentState == newStateName)
        {
            return;
        }

        Owner.GotoState(newStateName);
    }
}
