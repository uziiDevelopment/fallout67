# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Fallout 67** is a Windows Forms turn-based nuclear strategy game in C# (.NET 8.0). Players select one of 28 nations and launch weapons at rivals while managing defenses and alliances.

## Build & Run

```bash
# Build
dotnet build "fallout 67.sln"

# Run
dotnet run --project "fallout 67/fallout 67.csproj"
```

Compiled executable: `fallout 67/bin/Debug/net8.0-windows/fallout 67.exe`

No test project exists.

## Architecture

### C# Game (fallout 67/)

| File | Role |
|------|------|
| `Program.cs` | Entry point — shows `LobbyForm`, then launches `ControlPanelForm` |
| `LobbyForm.cs` | Mode selection (SP/MP), MP room create/join, lobby UI, country selection |
| `MainMenuForm.cs` | Legacy country-selection form (no longer used — kept for reference) |
| `GameData.cs` | Data models (`Nation`, `PlayerState`, `TroopMission`) and `GameEngine.InitializeWorld(country, seed?)` |
| `CombatEngine.cs` | Strike logic: `ExecuteCombatTurn`, `ExecuteEnemyStrike`, `ExecuteRemotePlayerStrike`, `ResolveMission` |
| `MultiplayerClient.cs` | WebSocket client wrapping the Cloudflare Worker; fires C# events for each message type |
| `ControlPanelForm.cs` | Main game UI — map rendering, animations, event wiring, MP action send/receive |
| `ShopForm.cs` | Black market upgrade shop |

`Form1.cs` / `Form1.Designer.cs` are unused scaffolding.

### Cloudflare Worker (fallout67/)

Single Durable Object (`GameRoom`) per room code. Deploy with `npm run deploy` from `fallout67/`.
After updating `wrangler.jsonc`, run `npm run cf-typegen` to regenerate `Env` types.

| Endpoint | Purpose |
|---|---|
| `POST /api/create` | Returns a new 6-char room code |
| `WS /ws?code=XXXX&name=NAME` | WebSocket connection to a room |

**Message flow:** Client → `select_country` / `start_game` / `game_action` / `chat`
Server → `welcome` / `player_joined` / `country_selected` / `game_start` / `game_action` / `player_left`

The server only **relays** `game_action` messages; each client runs the game simulation locally using the shared random seed from `game_start`.

### Game Loop

Three timers drive the game inside `ControlPanelForm`:
- **Game timer (1000ms):** AI turns, world events, mission countdowns
- **Radar timer (30ms):** Rotating radar sweep animation
- **Animation timer (16ms):** Missile Bézier curves and explosion effects

The world map image is downloaded at runtime from an external URL (`postimg.cc`).

### Namespace Note

The root namespace in code is `fallover_67` (typo vs. `fallout_67` in the csproj). Both spellings appear — don't "fix" one without updating the other.

## Key Mechanics

- **Weapons:** Standard Nuke (10–30% pop), Tsar Bomba (40–70%), Bio-Plague (35–65%), Orbital Laser (15% + drains nukes/money)
- **Defenses:** Iron Dome (up to 60% block), Bunker (up to 50% block), Vaccine (bio mitigation) — each upgradable 1–4 levels
- **Surrender threshold:** Nation surrenders at ≤40% of max population (scaled by difficulty)
- **Alliances:** 40% random chance per nation pair; allies auto-retaliate when a member is struck
- **Missions:** Troop extractions run 120-second countdown; hostile nations have `5% × difficulty` intercept chance
