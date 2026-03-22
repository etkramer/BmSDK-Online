# Batman: Arkham City Online Multiplayer

## Project Goal
Seamless cooperative multiplayer for Batman: Arkham City, where multiple players can explore Arkham City together with synchronized movement, animations, combat, and world interactions.

---

## Architecture Overview

### Why Custom Networking (Not UE3 Replication)

UE3's built-in replication system is present but broken beyond repair:
- **What exists**: `simulated`, `reliable server/client`, `repnotify` markers throughout the codebase
- **What's missing**: Core netcode infrastructure (`NetIndex`, proper `Role`/`RemoteRole` handling)
- **What works**: Can force the game to host/connect, but fundamental replication doesn't function

**Our approach**: Custom TCP networking layer that *calls the same simulated functions* the original netcode would have called. The game was designed for multiplayer (likely for cut co-op content), so the client-side hooks exist — we just need to trigger them ourselves.

### Network Model

```
┌─────────────────┐         ┌─────────────────┐
│   HOST GAME     │◄───────►│   CLIENT GAME   │
│                 │   TCP   │                 │
│  Authoritative  │         │   Simulated     │
│  World State    │         │   Visuals Only  │
└─────────────────┘         └─────────────────┘
```

- **Host**: Runs the "real" game — enemies, AI, physics, pickups, triggers
- **Client**: Renders remote players, plays animations, shows effects (no gameplay authority)
- **Sync Direction**: Host → Client for world state; Client → Host for input/intent

### Component-Based Interception (Key Architecture)

BmSDK provides powerful hooking capabilities via `ScriptComponent`:

```csharp
// AutoAttach: Component automatically attaches to ALL instances of RPawnPlayerCombat
[ScriptComponent(AutoAttach = typeof(RPawnPlayerCombat))]
public class NetPlayerComponent : ScriptComponent<RPawnPlayerCombat>
{
    public int NetId { get; private set; }
    public bool IsLocal => Actor == Game.GetPlayerPawn(0);
    public bool IsRemote => !IsLocal;

    // ComponentRedirect: Intercepts ChangePose() calls on this actor
    [ComponentRedirect]
    public void ChangePose(FName movement, FName weapon, FName idle, ...)
    {
        // Call the original function first (actually change the pose)
        Original.ChangePose(movement, weapon, idle, ...);

        // If this is the LOCAL player, broadcast to network
        if (IsLocal)
        {
            NetworkManager.Broadcast(new PoseChangeMessage(NetId, movement, weapon, idle));
        }
    }
}
```

**Why this is powerful:**
- **No polling** — we intercept function calls at the source
- **Automatic capture** — ANY code path that calls `ChangePose()` gets synced
- **AutoAttach** — works on spawned remote pawns AND the local player
- **Clean separation** — `IsLocal` sends, `IsRemote` receives (and doesn't re-broadcast)

**What we can intercept (non-native UnrealScript functions):**
- `ChangePose()`, `ForceChangePose()` — animation state
- `CapeChangeState()` — cape transitions
- Combat move initialization (if not native)
- Special move triggers
- Gadget usage

**Limitations:**
- Cannot redirect `native` functions (implemented in C++)
- Must be careful to avoid infinite loops (check `IsLocal` before broadcasting)

---

## Implementation Phases

### Phase 1: Foundation (Current → Working Ghost Co-op)

**Goal**: Remote player appears as a fully animated character, not a sliding mesh.

#### 1.1 Core Component Setup ✅ COMPLETE
- [x] Create `NetPlayerComponent` with `[ScriptComponent(AutoAttach = true)]`
- [x] Implement `IsLocal` / `IsRemote` detection
- [x] Verify AutoAttach works on both local player and spawned remote pawns
- [x] Set up NetId assignment (local from GUID, remote from spawn message)

#### 1.2 Transform Sync via Component ✅ COMPLETE
- [x] Implement `InterpolationBuffer` with ring buffer + lerp
- [x] Throttle send rate (20Hz)
- [x] Position and rotation sync working

#### 1.3 Pose State Sync ✅ COMPLETE
- [x] Poll pose state in `OnTick()` (ChangePose is native, can't redirect)
- [x] Send `AnimStateMessage` on pose/physics change
- [x] Apply via `ForceChangePose()` on remote pawns
- [x] Physics state sync via `SetPhysics()`

**Correct property path for reading pose state:**
```csharp
var poseInput = Owner.Anim.PosePlayer.Changes[0].Pose.Input;
var movement = poseInput.MovementStance;
var weapon = poseInput.WeaponStance;
var idle = poseInput.IdleStance;
```

#### 1.4 Locomotion Animation Sync 🔲 TODO

**CRITICAL FINDING: Root Motion Architecture**

The game uses **root motion** — animations drive character movement, not the other way around:
- Animation plays → root bone movement extracted → applied to character position
- We CANNOT set velocity to make walk/run anims play
- We MUST sync the actual locomotion animation being played

**Current state:**
- Pose changes sync (crouch, glide, combat stance) ✅
- Position/rotation sync ✅
- Walk/run animations do NOT play — remote pawn slides in idle pose

**Required approach:**
- Investigate `RAnimUtil_MovementPlayer` — this drives locomotion animation
- Need to sync the movement animation state, not just poses
- Options:
  1. Sync `MovementPlayer` state directly (speed, direction, animation name)
  2. Find the function that triggers locomotion anims and redirect it
  3. Manually call `PlayAnim()` with the correct locomotion animation

**Deliverable**: Remote Batman walks, runs, crouches, glides — looks like a real player.

---

### Phase 2: Visual Combat Sync

**Goal**: Remote player *appears* to fight; enemies only react on host.

#### 2.1 Combat Architecture Analysis
The combat system has multiple intercept points:

```
Player Input
    ↓
RPlayerControllerCombat (handles input, finds targets)
    ↓
RBMCombatManager (orchestrates combat state)
    ↓
RCombatMove subclass spawned (e.g., RCombatMove_BatmanStrike)
    ↓
Animation played via pose system or AnimNodeSlot
```

**Strategy**: Intercept at the `RCombatMove` level — when a move starts, sync it.

#### 2.2 Combat Move Component
```csharp
[ScriptComponent(AutoAttach = typeof(RCombatMove))]
public class NetCombatMoveComponent : ScriptComponent<RCombatMove>
{
    [ComponentRedirect]
    public void OnInitialiseMulticast()  // Already marked "reliable simulated" in game
    {
        Original.OnInitialiseMulticast();

        // Get the owning pawn
        var pawn = GetOwningPawn();
        var netComp = pawn?.GetScriptComponent<NetPlayerComponent>();

        if (netComp?.IsLocal == true)
        {
            NetworkManager.Broadcast(new CombatMoveStartMessage(
                netComp.NetId,
                Actor.Class.Name,           // "RCombatMove_BatmanStrike"
                GetCurrentAnimName(),
                Actor.Location,
                Actor.Rotation
            ));
        }
    }
}
```

#### 2.3 Simpler Alternative: Animation Slot Intercept
If combat moves are too complex, intercept at animation level:

```csharp
// On NetPlayerComponent
[ComponentRedirect]
public void PlayCustomAnim(FName animName, float rate, float blendIn, float blendOut, ...)
{
    Original.PlayCustomAnim(animName, rate, blendIn, blendOut, ...);

    if (IsLocal)
    {
        NetworkManager.Broadcast(new CustomAnimMessage(NetId, animName.ToString(), rate));
    }
}
```

On receive: call `Mesh.FindAnimNode("FullBody").PlayCustomAnim(...)` directly.

#### 2.4 Cape State via Redirect
```csharp
[ComponentRedirect]
public int CapeChangeState(FName newState, float startTime, float playRate, RPhysUtil.ECapeMirroredType mirrored)
{
    var result = Original.CapeChangeState(newState, startTime, playRate, mirrored);

    if (IsLocal)
    {
        NetworkManager.Broadcast(new CapeStateMessage(NetId, newState.ToString(), (byte)mirrored));
    }
    return result;
}
```

#### 2.5 Functions to Investigate for Redirect
| Function | Class | Notes |
|----------|-------|-------|
| `OnInitialiseMulticast()` | RCombatMove | Already multicast-designed |
| `PlayStrike()` | RCombatMove_BatmanStrike | May be native |
| `PlayCustomAnim()` | AnimNodeSlot | Animation playback |
| `CapeChangeState()` | RPawnPlayerBmBase | Cape transitions |

**Deliverable**: Remote Batman visually performs strikes, counters, takedowns.

---

### Phase 3: Synchronized Enemies (Hard Mode)

**Goal**: Both players can fight the same enemies.

#### 3.1 Enemy Authority Model
**Option A — Host Authority (Recommended)**:
- Host owns all enemy AI, health, state
- Client sends "attack intent" to host
- Host validates, applies damage, sends results back
- Client plays hit reactions

**Option B — Distributed (Complex)**:
- Each player owns enemies near them
- Transfer ownership when enemies cross boundaries
- Requires conflict resolution

#### 3.2 Enemy State Sync
For each `RPawnVillain`:
```csharp
public record class EnemyStateMessage(
    int EnemyNetId,
    Vector3 Position,
    Rotator Rotation,
    string BehaviourState,    // Current AI state
    float Health,
    bool IsRagdoll,
    bool IsKnockedOut
) : Message;
```

#### 3.3 Damage Flow (Host Authority)
```
Client presses Strike →
  Client sends StrikeIntentMessage(targetEnemyId, strikeInfo) →
    Host validates (is enemy in range? is player in correct state?) →
      Host applies damage via DamageInfo →
        Host sends DamageResultMessage(enemyId, damageResult, hitReaction) →
          Client plays hit reaction on enemy
```

#### 3.4 Freeflow Combo Considerations
The combo system tracks:
- Current combo count
- Combo multiplier
- Target queue
- Counter windows

Options:
1. **Separate combos**: Each player has independent combo (simpler)
2. **Shared combo**: Combined multiplier (requires careful sync)

#### 3.5 Simultaneous Counter Sync
The game supports multi-enemy counters. The existing infrastructure:
- `SimultaneousCounterFormation` enum
- `ServerStartSimultaneousCounterSimulated()`
- `ClientStartCounterSimulated()`

We can reuse this pattern for syncing counter animations.

**Deliverable**: Both players can punch the same thugs.

---

### Phase 4: World Interaction Sync

#### 4.1 Environmental Traversal
`RSpecialMoveController` handles:
- Ledge grabs
- Vaulting
- Climbing
- Corner cover

Sync the special move type + target position/rotation.

#### 4.2 Interactables
- Doors, vents, destructibles
- Riddler puzzles
- Evidence scanning

Most are Kismet-driven — may need to trigger Kismet remotely.

#### 4.3 Gadgets
Each gadget type needs specific sync:
- **Batarang**: Spawn projectile on all clients
- **Explosive Gel**: Place marker, sync detonation
- **Grapple**: Sync target point, play swing anim
- **Line Launcher**: Sync line endpoints

#### 4.4 Detective Mode
- Sync detective mode on/off
- Highlighted evidence/clues

---

### Phase 5: Polish & Edge Cases

#### 5.1 Cinematics/Cutscenes
- Pause gameplay sync during cutscenes
- Or skip cutscenes for non-host

#### 5.2 Map Streaming
- Arkham City uses seamless world streaming
- Ensure both players are in same area before gameplay sync

#### 5.3 Save/Checkpoint
- Host controls save state
- Client rejoins at host's checkpoint

#### 5.4 Character Selection
- Support Catwoman, Robin, Nightwing (challenge maps)

---

## Technical Deep Dive: Animation System

### The Pose System
Batman's animation is driven by a hierarchical pose system:

```
RAnimNode_Pose (root)
  ├── MovementStance: "Standing" | "Crouching" | "Combat" | ...
  ├── WeaponStance: "Relaxed" | "Armed" | ...
  ├── IdleStance: specific idle variation
  └── Transitions: blended state changes
```

**Key functions**:
- `RPawnCharacter.ChangePose()` — request pose change with transition
- `RPawnCharacter.ForceChangePose()` — immediate pose change
- `RAnimNode_Pose.ChangePose()` — low-level pose application

### Movement Animation (Root Motion)

**CRITICAL: The game uses root motion.** Animations drive movement, not velocity-driven blending.

`RAnimUtil_MovementPlayer` handles locomotion:
- Plays directional movement animations (walk/run/sprint)
- Root bone translation is extracted and applied to pawn position
- Animation choice is based on input direction and speed *request*, not current velocity

**Implication for networking:**
- Cannot just sync velocity and expect correct animations
- Must sync the actual animation being played, OR
- Must sync the movement input/request that triggers the animation
- Remote pawn needs to play the same locomotion animation for natural movement

### Combat Animation
Combat moves override the pose system:
- `RCombatMove` spawns and takes control
- Plays specific attack/counter animations
- Returns control to pose system on exit

### What We Need to Sync
| State | Update Frequency | Size |
|-------|-----------------|------|
| Position | 60Hz | 12 bytes |
| Rotation | 60Hz | 12 bytes |
| Pose (stance names) | On change | ~24 bytes |
| Physics state | On change | 1 byte |
| Combat move | On change | ~32 bytes |
| Cape state | On change | ~8 bytes |

**Estimated bandwidth**: ~2-3 KB/sec per player at 60 tick

---

## Message Protocol

### Message Types (Planned)
```
0x01 - JoinMessage           (existing)
0x02 - ActorMoveMessage      (existing)
0x03 - ActorSpawnMessage     (existing)
0x04 - AnimStateMessage      (Phase 1)
0x05 - CombatMoveMessage     (Phase 2)
0x06 - CombatMoveEndMessage  (Phase 2)
0x07 - EnemyStateMessage     (Phase 3)
0x08 - DamageIntentMessage   (Phase 3)
0x09 - DamageResultMessage   (Phase 3)
0x0A - GadgetUseMessage      (Phase 4)
0x0B - InteractMessage       (Phase 4)
```

### Delta Compression (Future)
For high-frequency updates, send deltas:
- Position delta from last confirmed
- Only send changed stance names
- Bitfield for boolean flags

---

## Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Key functions are `native` (can't redirect) | Medium | High | Fall back to OnTick polling for those functions |
| ComponentRedirect causes infinite loop | Medium | High | Always check `IsLocal` before broadcasting |
| AutoAttach timing issues | Low | Medium | Manual attach fallback in OnEnterGame |
| AnimTree doesn't init on spawned pawn | Medium | High | Use direct `PlayAnim()` fallback |
| Combat moves require PlayerController | Medium | High | Test extensively; may need stub controller |
| Latency causes combat desync | High | Medium | Visual-only combat on client; host authority |
| Cape physics desync | Low | Low | Sync cape state, not physics |
| Memory/perf with multiple pawns | Low | Medium | Profile early; limit particle effects |

### Native vs UnrealScript Function Check
Before relying on redirect, verify each function. In the reference `.uc` files:
- `native function` = C++, **cannot redirect**
- `simulated function` / `function` = UnrealScript, **can redirect**
- `native simulated function` = mixed, **cannot redirect** (native takes precedence)

### CRITICAL FINDING: Core Pose Functions are Native

```
// RPawnCharacter.uc - ALL NATIVE, CANNOT REDIRECT:
native final function ChangePose(...)
native final function ForceChangePose(...)
native final function ChangePoseWeapon(...)
native final function InstantChangePose()
```

**This means we CANNOT intercept pose changes via ComponentRedirect.**

### Revised Strategy: Hybrid Approach

**What we CAN redirect** (simulated functions):
```csharp
simulated function Tick(float DeltaTime)           // Read state each frame
simulated event StartCrouch(float HeightAdjust)    // Crouch state change
simulated event EndCrouch(float HeightAdjust)      // Stand state change
simulated function GetUpFromRagdoll(...)           // Ragdoll recovery
simulated event SetAnimPosition(...)               // Matinee anim control
```

**Hybrid sync approach:**
1. **OnTick polling** for pose state (read `Anim.CurrentPose` or stance names)
2. **ComponentRedirect** for discrete events (crouch, combat moves, gadgets)
3. **Direct state comparison** — only send when state actually changes

```csharp
[ScriptComponent(AutoAttach = true)]
public class NetPlayerComponent : ScriptComponent<RPawnPlayerCombat>
{
    private FName _lastMovementStance;
    private FName _lastWeaponStance;

    // Can't redirect ChangePose (native), so poll in OnTick instead
    public override void OnTick()
    {
        if (IsLocal)
        {
            // Correct path to read current pose state:
            var changes = Owner.Anim.PosePlayer.Changes;
            if (changes.Count > 0)
            {
                var poseInput = changes[0].Pose.Input;
                var currentMovement = poseInput.MovementStance;
                var currentWeapon = poseInput.WeaponStance;

                if (currentMovement != _lastMovementStance || currentWeapon != _lastWeaponStance)
                {
                    _lastMovementStance = currentMovement;
                    _lastWeaponStance = currentWeapon;
                    NetworkManager.Broadcast(new AnimStateMessage(...));
                }
            }
        }
    }
}
```

---

## Testing Milestones

### Milestone 1: "I See You Moving" ✅ COMPLETE
- [x] Remote player visible in world
- [x] Position updates smoothly
- [x] Basic idle animation plays

### Milestone 1.5: "I See You Posing" ✅ COMPLETE
- [x] Crouch/stand poses sync
- [x] Glide pose works
- [x] Gadget poses sync
- [ ] Walk/run animations (requires Phase 1.4 - locomotion sync)

### Milestone 2: "I See You Walking" 🔲 IN PROGRESS
- [ ] Walk/run animations sync (blocked: need locomotion animation sync)
- [x] Crouch/stand transitions work
- [x] Glide looks correct

### Milestone 3: "I See You Fighting" (Phase 2)
- [ ] Strike animations visible
- [ ] Counter animations visible
- [ ] Takedown animations visible
- [ ] Evade/roll animations visible

### Milestone 4: "We Can Fight Together" (Phase 3)
- [ ] Enemies react to both players
- [ ] Damage properly attributed
- [ ] KO state synced

---

## File Structure (Planned)

```
Scripts/
├── Online/
│   ├── Core/
│   │   ├── NetworkManager.cs       # Singleton, connection orchestration
│   │   ├── MessageRouter.cs        # Dispatch incoming messages
│   │   └── InterpolationBuffer.cs  # Smooth movement interpolation
│   │
│   ├── Messages/
│   │   ├── Message.cs              # Base class (existing)
│   │   ├── JoinMessage.cs          # Player join handshake
│   │   ├── TransformMessage.cs     # Position + rotation
│   │   ├── PoseChangeMessage.cs    # Animation stance change
│   │   ├── CombatMoveMessage.cs    # Combat move start/end
│   │   ├── CapeStateMessage.cs     # Cape transitions
│   │   └── ...
│   │
│   ├── Components/                 # THE CORE SYNC SYSTEM
│   │   ├── NetPlayerComponent.cs   # [AutoAttach=RPawnPlayerCombat] - transform, pose, cape
│   │   ├── NetCombatComponent.cs   # [AutoAttach=RCombatMove] - combat sync
│   │   ├── NetEnemyComponent.cs    # [AutoAttach=RPawnVillain] - enemy state (Phase 3)
│   │   └── NetComponent.cs         # Base with NetId (existing)
│   │
│   ├── Server/
│   │   ├── ServerScript.cs         # Host entry point
│   │   ├── ServerConnection.cs     # Per-client connection
│   │   └── WorldAuthority.cs       # Authoritative game state
│   │
│   └── Client/
│       ├── ClientScript.cs         # Client entry point
│       ├── ClientConnection.cs     # Connection to host
│       └── RemotePawnManager.cs    # Spawn/despawn remote pawns
│
└── DebugScript.cs                  # Dev tools
```

**Key insight**: The `Components/` folder is where most sync logic lives. Each component:
- AutoAttaches to relevant actor types
- Uses `[ComponentRedirect]` to intercept function calls
- Checks `IsLocal` to decide send vs. receive behavior

---

## Next Steps (Immediate)

### Option A: Complete Locomotion Sync (Phase 1.4)
To get walk/run animations working:

1. **Investigate `RAnimUtil_MovementPlayer`**
   - [ ] Find how locomotion animations are triggered
   - [ ] Check if there's a function we can redirect (not native)
   - [ ] Understand the movement input → animation flow

2. **Sync locomotion animation state**
   - [ ] Identify what state needs to be synced (anim name? speed? direction?)
   - [ ] Create `LocomotionMessage` with required data
   - [ ] Apply on remote pawn to trigger same animation

3. **Handle root motion on remote**
   - [ ] Remote pawn plays locomotion anim with root motion
   - [ ] Reconcile root motion position with network position (may drift)
   - [ ] Consider: disable root motion on remote, just play anim visually?

### Option B: Skip to Combat Sync (Phase 2)
If locomotion proves too complex, move to combat animations:

1. **Intercept combat moves**
   - [ ] Test `[ComponentRedirect]` on `RCombatMove.OnInitialiseMulticast()`
   - [ ] Create `CombatMoveMessage` with move class + anim data
   - [ ] Spawn/play combat move on remote pawn

2. **Custom animation slot sync**
   - [ ] Find `PlayCustomAnim()` or similar on AnimNodeSlot
   - [ ] Redirect and broadcast animation name + params
   - [ ] Apply on remote via direct anim call

### Current State Summary
- ✅ Transform sync (position, rotation) at 20Hz with interpolation
- ✅ Pose sync (crouch, glide, combat stance, gadget poses)
- ✅ Physics state sync
- ❌ Locomotion animations (walk/run) - remote slides in idle
- ❌ Combat animations (strikes, evades, counters)
- ❌ Custom animations (batarang throw, etc.)

---

## Session Learnings

### 2026-03-22: Phase 1.3 Implementation

**Completed:**
- Pose state sync via polling in `OnTick()`
- `AnimStateMessage` (TypeId=4) for stance + physics sync
- Remote pawns correctly show crouch, glide, gadget poses

**Key Discoveries:**

1. **Correct property path for pose state:**
   ```
   Owner.Anim.PosePlayer.Changes[0].Pose.Input.MovementStance
   ```
   NOT `Owner.Anim.CurrentPose.MovementStance` (doesn't exist)

2. **TArray uses `.Count`, not `.Length`**

3. **Root motion architecture:**
   - Animations drive character movement (root bone → position)
   - Cannot set velocity to trigger walk/run anims
   - Must sync the actual animation being played
   - This affects how we approach locomotion sync

4. **What works vs. what doesn't:**
   - ✅ Pose changes (discrete state changes like crouch/glide)
   - ✅ Position sync (remote moves to correct location)
   - ❌ Locomotion anims (walk/run) — need animation-level sync
   - ❌ Combat/evade anims — these use combat move system or custom anims

**Next session priorities:**
- Investigate `RAnimUtil_MovementPlayer` for locomotion sync
- Or skip to Phase 2 (combat animation sync) if locomotion is too complex

---

## References

### Key SDK Classes
- `BmSDK.BmGame.RPawnCharacter` — base for ChangePose
- `BmSDK.BmGame.RAnimNode_Pose` — animation state machine
- `BmSDK.Engine.AnimNodeSlot` — custom anim playback
- `BmSDK.BmGame.RCombatMove` — combat move base
- `BmSDK.BmGame.RPawnCombat.FDamageInfo` — damage data

### Key Game Classes (Reference)
- `RPawnPlayer.uc:3935` — `ServerPlayRelativeTransition`
- `RPawnPlayerCombat.uc:708` — `ServerApplyDamage`
- `RPlayerControllerCombat.uc:3766` — `ServerStartSimultaneousCounterSimulated`
- `RCombatMove_VillainSimultaneousAttack.uc:326` — `ClientStartCounterSimulated`

### Useful UE3 Concepts
- `simulated` = runs on owning client + server
- `reliable server` = client→server RPC (guaranteed delivery)
- `reliable client` = server→client RPC (guaranteed delivery)
- `unreliable` = UDP-style, can be dropped
