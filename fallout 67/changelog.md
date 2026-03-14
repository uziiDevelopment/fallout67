# Changelog - Fallout 67 Tactical Update

## [1.2.0-Alpha] - 2026-03-14

### ⚓ Submarine & Strategic Assets
- **New Asset: Industrial Complex**
    - Generates passive treasury income every 10 seconds based on Industry Level.
    - Serves as the tech requirement for advanced naval warfare.
- **New Asset: Nuclear Submarine**
    - Stealth platform for precision nuclear strikes.
    - Spawns in the **Arctic Sector** for strategic deployment.
    - Features custom registry naming upon commission.
- **Smart Navigation (Auto-Pilot)**
    - Implemented grid-based A* pathfinding for submarines.
    - Subs now automatically route around continents and islands through international waters.
    - Travel speed increased by **900%** for better tactical responsiveness.
- **"Dead Hand" Protocol**
    - Active submarines now automatically retaliate against attackers if the national soil is struck.
    - Integrated with visual missile animations and multiplayer synchronization.
- **"Last Stand" Insurance**
    - Players no longer lose immediately when their population reaches zero.
    - Survival is guaranteed as long as at least one active submarine remains in the fleet.
- **AI Submarine Hunting**
    - AI nations now recognize "Last Stand" scenarios.
    - Hostile nations will perform ocean sweeps with tactical nukes to hunt remaining player assets.

### 🛡️ AI & Diplomacy Balancing
- **Escalation Management**
    - AI retaliation is now probability-based, scaling with a nation's `AngerLevel`.
    - Calm nations are less likely to trigger immediate counter-strikes.
- **Survival Instinct**
    - Nations with critical population loss (<15%) now prioritize survival and are significantly less likely to escalate conflicts further.
- **Alliance Capping**
    - Limited the number of allies that can intervene in a single conflict to prevent "Nuke Rain" cascades.
- **Mobilization Delays**
    - AI strikes now feature randomized mobilization delays (2-5s) to make world events feel more organic and less like instant chain reactions.
- **Neutral Mediation**
    - Shared allies are now 90% more likely to remain neutral in conflicts between two of their friends.

### 🖥️ UI & Visual Experience
- **Shop Redesign**
    - Removed side-bar filters to maximize tactical screen space.
    - Fixed Z-ordering for header/footer elements to prevent overlapping.
    - Refactored [BuildUI](file:///d:/Development/%5BRUST%5D%20Development/Vanilla/fallout67/fallout%2067/ShopForm.cs#69-115) for stable docking and layout management.
- **Notification System (Glassmorphism)**
    - Implemented high-fidelity toast notifications for world events and status updates.
    - Added smart deduplication to prevent log spam for repeated events.
- **Tactical Alerts**
    - Added animated **Incoming Strike Warning** with rotating glows and nuke alarms.
    - Integrated "Submarine Count" into the main player statistics panel.
- **Terminal Polish**
    - Added solid background to [TerminalBox](file:///d:/Development/%5BRUST%5D%20Development/Vanilla/fallout67/fallout%2067/ShopForm.cs#288-343) to fix transparency artifacts.
    - Implemented "Blink" tech animation for incoming log messages.

### 🐛 Bug Fixes & Refactoring
- Fixed [RefreshData](file:///d:/Development/%5BRUST%5D%20Development/Vanilla/fallout67/fallout%2067/ControlPanelForm.cs#479-500) protection level issues to allow cross-form updates.
- Added tactical borders to [SubmarineControlForm](file:///d:/Development/%5BRUST%5D%20Development/Vanilla/fallout67/fallout%2067/SubmarineControlForm.cs#10-116) for visual consistency.
- Standardized coordinate mapping between normalized 0-1 space and Lat/Lng grid.
- Enforced multiplayer authority for unprovoked AI events (Host-authoritative).
- Added [MapUtility](file:///d:/Development/%5BRUST%5D%20Development/Vanilla/fallout67/fallout%2067/GameData.cs#237-320) class for centralized pathfinding and terrain validation.
