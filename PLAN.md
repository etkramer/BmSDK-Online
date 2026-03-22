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

#### 1.4 Full Animation Sync 🔲 TODO

**The Challenge**

The game has dozens of animation states beyond walking/running:
- Locomotion (walk, run, sprint, directional blending)
- Gliding, diving, grappling
- Gadget aiming (batarang, REC, explosive gel, etc.)
- Traversal (railing walk, ledge grab, vent crawl, leaping gaps)
- Crouching, crawling
- Combat moves (handled separately in Phase 2)
- Overlays (breathing, weapon hold)

We need a **generic approach** that handles all of these without special-casing each one.

**Current state:**
- Pose changes sync (crouch, glide, combat stance) ✅
- Position/rotation sync ✅
- Most animations do NOT play — remote pawn slides in idle pose

---

### Chosen Approach: Sync Controller Input (Replay player actions)

```
LOCAL CONTROLLER                        REMOTE PAWN
    │                                        │
    ▼                                        ▼
Read input state ────────────────────► Feed to pawn's input system
(direction, buttons, events)            Game logic produces animations
    │                                        │
Position from network ───────────────► Override position (prevent drift)
```

**Concept:** Instead of syncing animation *output*, sync the *input* that causes animations. The game's existing systems handle all the complex animation logic.

**Pros:**
- Much lower bandwidth (inputs are sparse events, not continuous state)
- Game's existing logic handles ALL animation complexity
- Naturally handles every action (grapple, gadgets, traversal, etc.)
- This is how real multiplayer games work
- The game was *designed* for this — `simulated`/`reliable` markers exist for this purpose

**Cons:**
- Need to identify all relevant input state/events
- Remote pawn needs fake input injection mechanism
- Latency means animations start slightly late on remote
- Remote pawn needs similar world context (nearby ledges, etc.)

**What we'd sync:**
| Input Type | Data | Frequency |
|------------|------|-----------|
| Movement direction | Vector2 (stick/WASD) | Continuous (~20Hz) |
| Movement speed | Walk/Run modifier | On change |
| Jump/Glide | Event | On press |
| Grapple | Event + target point | On fire |
| Gadget use | Event + gadget type | On use |
| Crouch | Toggle event | On press |
| Context action | Event (vault, vent, etc.) | On trigger |

**Key insight:** All those `simulated` and `reliable server/client` function markers in the original code exist because the game was designed to sync inputs/events, not animations. We'd be using the architecture as intended.

**Investigation needed:**
- [ ] Where does RPlayerControllerCombat process movement input?
- [ ] Can we inject fake input into a pawn without a real controller?
- [ ] What events trigger grapple, gadgets, context actions?
- [ ] How to handle world context (ledge detection, etc.) on remote?

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
- [x] Walk/run animations

### Milestone 2: "I See You Walking" 🔲 IN PROGRESS
- [x] Walk/run animations sync (via MoveInDirection + controller state)
- [x] Crouch/stand transitions work (via controller StealthMoveMode sync)
- [ ] Glide looks correct

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
│   │   ├── NetPlayerComponent.cs   # [AutoAttach=RPawnPlayerCombat] - transform, locomotion
│   │   ├── NetControllerComponent.cs # [AutoAttach=RPlayerControllerCombat] - controller state sync
│   │   ├── NetCombatComponent.cs   # [AutoAttach=RCombatMove] - combat sync (TODO)
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

### Phase 1.4: Animation Sync Investigation

**Goal**: Determine the best approach for full animation sync, then implement it.

#### Step 1: Investigate Input Sync (Approach B) — ✅ IMPLEMENTED

1. **Explore RPlayerControllerCombat** ✅
   - [x] Find where movement input is processed
     - `PlayerMove()` → `MoveInDirection(newAccel)` where `newAccel = (PlayerInput.aForward * X) + (PlayerInput.aStrafe * Y)`
   - [x] Identify the input variables (direction, speed modifier)
     - `RPlayerInput`: `RawLeftX`, `RawLeftY`, `aForward`, `aStrafe`, button bools
   - [x] Find event handlers for: Jump, Crouch, Grapple, Gadget, ContextAction
     - In `RPlayerInput`: `bCrouchButton`, `aGrappleButton`, `bReadyGadgetButton`, etc.

2. **Key Discovery: Game's built-in network infrastructure** ✅
   - `NetworkMoveVector` exists on RPawnPlayer (line 701)
   - `NetworkTick()` already calls `MoveInDirection(NetworkMoveVector * 0.01)` for remote pawns
   - `DesiredPoseForReplication`, `AimDirectionForReplication` also exist
   - Original netcode was cut before completion — we can use these ourselves!

3. **Prototype minimal input sync** ✅ DONE
   - [x] Sync movement direction via `InputHeading()` → `MoveInDirection()`
   - [x] Extended `ActorMoveMessage` with `MoveDirection` field
   - [x] Remote pawn calls `MoveInDirection(_lastMoveDirection)`
   - [x] Position correction to prevent drift (blend toward network position)

### After Animation Sync: Phase 2 (Combat Sync)
Combat moves may need special handling via `RCombatMove` interception, or may work automatically if input sync covers attack/counter inputs.

### Current State Summary
- ✅ Transform sync (position, rotation) at 20Hz with interpolation
- ✅ Physics state sync
- ✅ Locomotion sync via `MoveInDirection()` (walk/run animations work)
- ✅ Controller state sync (WalkingMode, RunningMode, StealthMoveMode, etc.)
- ❌ Combat animations (strikes, evades, counters)
- ❌ Custom animations (batarang throw, etc.)

---

## Session Learnings

### 2026-03-22: Animation Sync Strategy Discussion

**The Problem:**
- Game has dozens of animation states: locomotion, gliding, grappling, gadgets, traversal, etc.
- `MoveTo()` is for AI pathing, not player movement — wrong approach
- Need a generic solution that handles ALL animations without special-casing each

**Two Candidate Approaches Identified:**

**Chosen Approach: Sync Controller Input**
- Sync the player's input (direction, buttons, events) instead of animation output
- Remote pawn processes inputs → game logic produces correct animations
- Pro: Lower bandwidth, handles all actions automatically, game designed for this
- Con: Need to inject fake input, slight latency on animation start

**Key Insight:**
The `simulated` and `reliable server/client` markers throughout the codebase exist because the game was designed to sync inputs/events. We'd be using the architecture as originally intended.

**What to sync:**
- Movement direction (Vector2) — continuous
- Movement speed modifier — on change
- Events: Jump, Crouch, Grapple(target), UseGadget, ContextAction

**Hybrid strategy:**
- Position always from network (authoritative)
- Input sync for initiating actions
- Animation sync as fallback for edge cases

**Next step:** Investigate RPlayerControllerCombat to understand input processing.

---

### 2026-03-22: Locomotion Sync via Input Direction (Approach B) ✅

**Solution:** Sync movement input direction, call `MoveInDirection()` on remote pawn.

**Key details:**
- `HasMovementInput()` check required — `InputHeading()` returns camera direction when no input!
- `MoveInDirection()` handles both animation AND pawn facing — don't override rotation
- Position correction (blend toward network pos) prevents root motion drift

**Implementation:**
- Local: `controller.HasMovementInput() ? Owner.InputHeading() : Vector3.Zero`
- Remote: `Owner.MoveInDirection(_lastMoveDirection)` every tick
- Extended `ActorMoveMessage` with `MoveDirection` field

---

### 2026-03-22: Controller State Sync ✅

**Goal:** Sync controller states (WalkingMode, RunningMode, StealthMoveMode) so remote pawns run/crouch correctly.

**Architecture:**
- Remote pawns now have their own `RPlayerControllerCombat` (spawned and possessed during `HandleActorSpawn`)
- `NetControllerComponent` auto-attaches to all `RPlayerControllerCombat` instances
- Uses `[ComponentRedirect]` on `BeginState` to intercept state changes
- When local controller enters a new state, broadcasts `ControllerStateMessage`
- Remote controller receives and calls `GotoState(stateName)`

**Key States Synced:**
- `WalkingMode` — Normal walking (calls `SetWalkSpeed()`)
- `RunningMode` — Running (calls `SetRunSpeed()`)
- `StealthMoveMode` — Crouching (calls `SetStealthSpeed()`, changes pose to 'Crouching')

**Why this approach:**
- The controller's state machine handles ALL state-specific logic (speed, pose, camera, etc.)
- We just sync the state name, and the game does the rest
- Much cleaner than syncing individual properties

**Files Added/Modified:**
- `NetControllerComponent.cs` — New component for controller state sync
- `ControllerStateMessage` — New message type (TypeId=4)
- `NetworkManager.cs` — Added controller spawning and state handling
- `Connection.cs`, `ClientServerConnection.cs`, `ServerClientConnection.cs` — Message routing

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
