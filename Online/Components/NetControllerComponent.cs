using BmSDK;
using BmSDK.BmGame;
using BmSDK.BmScript;
using Online.Core;
using System.Numerics;

namespace Online.Components;

/// <summary>
/// Syncs PlayerInput values between local and remote players.
/// Instead of forcing controller states directly, we replicate input values
/// and let the controller's state machine handle transitions naturally.
/// </summary>
[ScriptComponent(AutoAttach = true)]
public class NetControllerComponent : ScriptComponent<RPlayerControllerCombat>
{
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

    // Last known input values for change detection
    private float _lastAForward;
    private float _lastAStrafe;
    private byte _lastBCrouchButton;
    private byte _lastBRunButton;
    private Vector3 _lastInputHeading;

    // Last heading received from the network, returned by the InputHeading redirect on remotes
    private Vector3 _remoteInputHeading;

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

        Debug.Log($"[NetControllerComponent] Attached to controller for pawn, NetId={NetId}, IsLocal={IsLocal}");

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

        // Poll for input changes on local controller
        if (IsLocal && NetId != -1)
        {
            PollAndBroadcastInput();
        }

        base.OnTick();
    }

    private void PollAndBroadcastInput()
    {
        var playerInput = Owner.PlayerInput as RPlayerInput;
        if (playerInput == null)
        {
            return;
        }

        var aForward = playerInput.aForward;
        var aStrafe = playerInput.aStrafe;
        var bCrouchButton = playerInput.bCrouchButton;
        var bRunButton = playerInput.bRunButton;
        var inputHeading = Owner.InputHeading(false);

        // Check if any input changed
        bool changed = aForward != _lastAForward ||
                       aStrafe != _lastAStrafe ||
                       bCrouchButton != _lastBCrouchButton ||
                       bRunButton != _lastBRunButton ||
                       inputHeading != _lastInputHeading;

        if (changed)
        {
            _lastAForward = aForward;
            _lastAStrafe = aStrafe;
            _lastBCrouchButton = bCrouchButton;
            _lastBRunButton = bRunButton;
            _lastInputHeading = inputHeading;

            NetworkManager.Instance?.BroadcastPlayerInput(NetId, aForward, aStrafe, bCrouchButton, bRunButton, inputHeading);
        }
    }

    /// <summary>
    /// Called when receiving input values from the network.
    /// Sets the input values directly, letting the controller handle state transitions.
    /// </summary>
    public void ReceivePlayerInput(float aForward, float aStrafe, byte bCrouchButton, byte bRunButton, Vector3 inputHeading)
    {
        if (!IsRemote)
        {
            return;
        }

        var playerInput = Owner.PlayerInput as RPlayerInput;
        if (playerInput == null)
        {
            return;
        }

        playerInput.aForward = aForward;
        playerInput.aStrafe = aStrafe;
        playerInput.bCrouchButton = bCrouchButton;
        playerInput.bRunButton = bRunButton;
        _remoteInputHeading = inputHeading;
    }

    [ComponentRedirect(nameof(RPlayerControllerCombat.InputHeading))]
    Vector3 InputHeading(bool bCanBeZero) => IsRemote ? _remoteInputHeading : Owner.InputHeading(bCanBeZero);

    /// <summary>
    /// Called when receiving an input event from the network. Dispatches it on the remote controller.
    /// </summary>
    public void ReceiveInputEvent(string eventName)
    {
        if (!IsRemote)
        {
            return;
        }

        Owner.ConsoleCommand(eventName);
    }

    // Each redirect calls the original and, for the local player, broadcasts the event name.
    private void ForwardExecEvent(string eventName, Action callOriginal)
    {
        callOriginal();
        if (IsLocal && NetId != -1)
        {
            NetworkManager.Instance?.BroadcastInputEvent(NetId, eventName);
        }
    }

    [ComponentRedirect(nameof(RPlayerControllerCombat.GrabPressed))]
    void GrabPressed() => ForwardExecEvent(nameof(RPlayerControllerCombat.GrabPressed), Owner.GrabPressed);
    [ComponentRedirect(nameof(RPlayerControllerCombat.GrabReleased))]
    void GrabReleased() => ForwardExecEvent(nameof(RPlayerControllerCombat.GrabReleased), Owner.GrabReleased);

    [ComponentRedirect(nameof(RPlayerControllerCombat.CoverPressed))]
    void CoverPressed() => ForwardExecEvent(nameof(RPlayerControllerCombat.CoverPressed), Owner.CoverPressed);
    [ComponentRedirect(nameof(RPlayerControllerCombat.CoverReleased))]
    void CoverReleased() => ForwardExecEvent(nameof(RPlayerControllerCombat.CoverReleased), Owner.CoverReleased);

    [ComponentRedirect(nameof(RPlayerControllerCombat.GadgetPressed))]
    void GadgetPressed() => ForwardExecEvent(nameof(RPlayerControllerCombat.GadgetPressed), Owner.GadgetPressed);
    [ComponentRedirect(nameof(RPlayerControllerCombat.GadgetReleased))]
    void GadgetReleased() => ForwardExecEvent(nameof(RPlayerControllerCombat.GadgetReleased), Owner.GadgetReleased);
    [ComponentRedirect(nameof(RPlayerControllerCombat.StartGadgetMode))]
    void StartGadgetMode() => ForwardExecEvent(nameof(RPlayerControllerCombat.StartGadgetMode), Owner.StartGadgetMode);
    [ComponentRedirect(nameof(RPlayerControllerCombat.EndGadgetMode))]
    void EndGadgetMode() => ForwardExecEvent(nameof(RPlayerControllerCombat.EndGadgetMode), Owner.EndGadgetMode);
    [ComponentRedirect(nameof(RPlayerControllerCombat.CancelGadget))]
    void CancelGadget() => ForwardExecEvent(nameof(RPlayerControllerCombat.CancelGadget), Owner.CancelGadget);
    [ComponentRedirect(nameof(RPlayerControllerCombat.SecondaryFireGadget))]
    void SecondaryFireGadget() => ForwardExecEvent(nameof(RPlayerControllerCombat.SecondaryFireGadget), Owner.SecondaryFireGadget);
    [ComponentRedirect(nameof(RPlayerControllerCombat.SecondaryFireReleased))]
    void SecondaryFireReleased() => ForwardExecEvent(nameof(RPlayerControllerCombat.SecondaryFireReleased), Owner.SecondaryFireReleased);
    [ComponentRedirect(nameof(RPlayerControllerCombat.ReadyGadgetOrCounterPressed))]
    void ReadyGadgetOrCounterPressed() => ForwardExecEvent(nameof(RPlayerControllerCombat.ReadyGadgetOrCounterPressed), Owner.ReadyGadgetOrCounterPressed);

    [ComponentRedirect(nameof(RPlayerControllerCombat.GrapplePressed))]
    void GrapplePressed() => ForwardExecEvent(nameof(RPlayerControllerCombat.GrapplePressed), Owner.GrapplePressed);
    [ComponentRedirect(nameof(RPlayerControllerCombat.GrappleReleased))]
    void GrappleReleased() => ForwardExecEvent(nameof(RPlayerControllerCombat.GrappleReleased), Owner.GrappleReleased);
    [ComponentRedirect(nameof(RPlayerControllerCombat.FireGrapple))]
    void FireGrapple() => ForwardExecEvent(nameof(RPlayerControllerCombat.FireGrapple), Owner.FireGrapple);
    [ComponentRedirect(nameof(RPlayerControllerCombat.AimGrapple))]
    void AimGrapple() => ForwardExecEvent(nameof(RPlayerControllerCombat.AimGrapple), Owner.AimGrapple);
    [ComponentRedirect(nameof(RPlayerControllerCombat.GrappleOrGadgetPressed))]
    void GrappleOrGadgetPressed() => ForwardExecEvent(nameof(RPlayerControllerCombat.GrappleOrGadgetPressed), Owner.GrappleOrGadgetPressed);

    [ComponentRedirect(nameof(RPlayerControllerCombat.StartRun))]
    void StartRun() => ForwardExecEvent(nameof(RPlayerControllerCombat.StartRun), Owner.StartRun);
    [ComponentRedirect(nameof(RPlayerControllerCombat.EndRun))]
    void EndRun() => ForwardExecEvent(nameof(RPlayerControllerCombat.EndRun), Owner.EndRun);

    [ComponentRedirect(nameof(RPlayerControllerCombat.StartStealthMove))]
    void StartStealthMove() => ForwardExecEvent(nameof(RPlayerControllerCombat.StartStealthMove), Owner.StartStealthMove);
    [ComponentRedirect(nameof(RPlayerControllerCombat.EndStealthMove))]
    void EndStealthMove() => ForwardExecEvent(nameof(RPlayerControllerCombat.EndStealthMove), Owner.EndStealthMove);
    [ComponentRedirect(nameof(RPlayerControllerCombat.StealthOrGadgetPressed))]
    void StealthOrGadgetPressed() => ForwardExecEvent(nameof(RPlayerControllerCombat.StealthOrGadgetPressed), Owner.StealthOrGadgetPressed);

    [ComponentRedirect(nameof(RPlayerControllerCombat.TriggerQuickStrike))]
    void TriggerQuickStrike() => ForwardExecEvent(nameof(RPlayerControllerCombat.TriggerQuickStrike), Owner.TriggerQuickStrike);
    [ComponentRedirect(nameof(RPlayerControllerCombat.QuickStrikeReleased))]
    void QuickStrikeReleased() => ForwardExecEvent(nameof(RPlayerControllerCombat.QuickStrikeReleased), Owner.QuickStrikeReleased);

    [ComponentRedirect(nameof(RPlayerControllerCombat.ContextSensitiveAction))]
    void ContextSensitiveAction() => ForwardExecEvent(nameof(RPlayerControllerCombat.ContextSensitiveAction), Owner.ContextSensitiveAction);
    [ComponentRedirect(nameof(RPlayerControllerCombat.PressedContextSensitive))]
    void PressedContextSensitive() => ForwardExecEvent(nameof(RPlayerControllerCombat.PressedContextSensitive), Owner.PressedContextSensitive);
    [ComponentRedirect(nameof(RPlayerControllerCombat.ReleasedContextSensitive))]
    void ReleasedContextSensitive() => ForwardExecEvent(nameof(RPlayerControllerCombat.ReleasedContextSensitive), Owner.ReleasedContextSensitive);

    [ComponentRedirect(nameof(RPlayerControllerCombat.ExitAgilityMode))]
    void ExitAgilityMode() => ForwardExecEvent(nameof(RPlayerControllerCombat.ExitAgilityMode), Owner.ExitAgilityMode);

    [ComponentRedirect(nameof(RPlayerControllerCombat.TriggerBlockBreaker))]
    void TriggerBlockBreaker() => ForwardExecEvent(nameof(RPlayerControllerCombat.TriggerBlockBreaker), Owner.TriggerBlockBreaker);

    [ComponentRedirect(nameof(RPlayerControllerCombat.QuickBatarang))]
    void QuickBatarang() => ForwardExecEvent(nameof(RPlayerControllerCombat.QuickBatarang), Owner.QuickBatarang);
    [ComponentRedirect(nameof(RPlayerControllerCombat.QuickFireBatarang))]
    void QuickFireBatarang() => ForwardExecEvent(nameof(RPlayerControllerCombat.QuickFireBatarang), Owner.QuickFireBatarang);
}
