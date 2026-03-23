# Batman: Arkham City Online Multiplayer Mod

This is a C# mod adding online multiplayer to Batman: Arkham City using the BmSDK modding framework.

## Project Structure

- `Scripts/` — Main mod source code (C#)
- `Scripts/Online/` — Networking implementation (TCP sockets)
- `Scripts/PLAN.md` — Detailed implementation roadmap and technical design

## Key References

- **UnrealScript source**: `C:\Users\elitk\Desktop\Batman2` — Decompiled game scripts. Use this to understand how game systems work (animation, combat, AI, etc.)
- **BmSDK docs**: `E:\Games\BatmanArkhamCity\Binaries\Win32\sdk\BmSDK.xml` — API documentation for the modding SDK
- **Project file**: `E:\Games\BatmanArkhamCity\BmGame\ScriptsDev\ScriptsDev.csproj`

## Architecture Summary

- UE3's built-in replication is broken (NetIndex removed), so we use custom TCP networking
- The game has `simulated`, `reliable server/client` markers — designed for multiplayer that was cut
- Our approach: call the same `simulated` functions the original netcode would have. Prefer simulating controller input over performing actions on the pawn.
- Host runs authoritative game state; clients render remote players visually

## Key Classes

| Game Class | Purpose |
|------------|---------|
| `RPawnPlayerCombat` | Player pawn (Batman/Catwoman/etc) |
| `RCombatMove` | Base class for all combat moves |
| `RPawnCombat.FDamageInfo` | Damage data structure |

## Current State

See `PLAN.md` for detailed phases. Currently working toward Phase 1 (ghost co-op with animation sync).

## Running

- Host: Press `[` in-game to start server
- Client: Press `]` to connect to localhost:8888
- Debug: Press `Enter` to load test level
