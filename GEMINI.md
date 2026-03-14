# Fallout 67

Fallout 67 is a Windows Forms turn-based nuclear strategy game developed in C# (.NET 8.0). It features a multiplayer component powered by Cloudflare Workers and Durable Objects.

## Project Overview

- **Game Client:** A C# WinForms application using `GMap.NET` for interactive map rendering and custom Bézier-based missile animations.
- **Server:** A Cloudflare Worker backend using Durable Objects to manage game rooms (`GameRoom`) and a global leaderboard (`Leaderboard`).
- **Release System:** Uses Velopack for generating Windows installers and delta updates.

## Architecture

### 1. Game Client (`fallout 67/`)
- **UI:** WinForms-based. Main game loop resides in `ControlPanelForm.cs`.
- **Logic:** `GameData.cs` (models), `CombatEngine.cs` (strike resolution), `GameEngine` (static world state).
- **Multiplayer:** `MultiplayerClient.cs` handles WebSocket communication with the Cloudflare Worker.
- **Map:** Powered by `GMap.NET.WinForms`. Coordinate systems map between 0-1 normalized space and real Lat/Lng.

### 2. Server (`server/`)
- **Tech Stack:** TypeScript, Wrangler, Vitest.
- **Durable Objects:**
    - `GameRoom`: Relays game actions between players in a room and manages room state.
    - `Leaderboard`: Persists and retrieves high scores.
- **API Endpoints:**
    - `POST /api/create`: Create a new multiplayer room.
    - `WS /ws?code=XXXX&name=NAME`: Connect to a room via WebSocket.
    - `POST /api/score`: Submit game scores.
    - `GET /api/leaderboard`: Fetch global rankings.

## Building and Running

### C# Game Client
```powershell
# Build the solution
dotnet build "fallout 67.sln"

# Run the game
dotnet run --project "fallout 67/fallout 67.csproj"
```

### Server (Cloudflare Worker)
```bash
cd server

# Install dependencies
npm install

# Start local development server
npm run dev

# Run server tests
npm test

# Deploy to Cloudflare
npm run deploy
```

### Creating a Release
The project uses a PowerShell script to automate the Velopack release process:
```powershell
# Build and package a new version (e.g., 1.2.0)
.\build-release.ps1 -Version 1.2.0
```

## Development Conventions

- **Namespace:** Note that the codebase uses `fallover_67` as a root namespace in many files, while the project file uses `fallout_67`. Maintain consistency with the file you are editing.
- **Global State:** The game relies heavily on static global state in `GameEngine` and `CombatEngine`.
- **Determinism:** Multiplayer relies on a shared random seed sent by the server at game start. AI logic and world events must remain deterministic across clients.
- **Coordinates:** When adding new countries, update both the normalized coordinates in `GameData.cs` and the real Lat/Lng in `ControlPanelForm.cs` (`CorrectCountryCoordinates`).

## Key Dependencies
- **Client:** `GMap.NET.WinForms`, `Velopack`
- **Server:** `wrangler`, `@cloudflare/vitest-pool-workers`
