# LunaStatusQuests

LunaStatusQuests is a client + server mod for SPT 3.11 that shows **shared quest progress for all profiles**:
- **Client** (BepInEx plugin) injects a shared status block into:
  - The standard **Tasks** screen.
  - The **trader quest detail** view (configurable).
- **Server** backend exposes `/LunaStatusQuests/statuses` with real‑time quest status for each profile.

## Installation
![Installation guide](https://i.imgur.com/3N6gTe2.gif)

## Client Config Options

All options live in the BepInEx config file for `LunaStatusQuests`:

**Enabled**
- Key: `General.Enabled`
- Default: `true`
- Description: Turn the client mod on/off.

**UpdateIntervalSeconds**
- Key: `General.UpdateIntervalSeconds`
- Default: `60`
- Description: Minimum number of seconds between server fetches. Lower values mean more up‑to‑date data but more traffic.

**ShowDebugLogs**
- Key: `General.ShowDebugLogs`
- Default: `false`
- Description: When enabled, prints extra debug information to the BepInEx log (fetch details, injection reasons, etc.).

**ShowStatusInTrader**
- Key: `General.ShowStatusInTrader`
- Default: `true`
- Description: When enabled, shows the shared status block on the **trader quest detail** UI.

**VisibleProfiles**
- Key: `Profiles.VisibleProfiles`
- Default: `"*"`
- Description: Controls which profile names appear in the shared status list.
- Patterns:
  - Show **all** profiles: `"*"`
  - Show all **except** specific profiles: `"*,-BotName"` or `"*,-Profile1,-Profile2"`
  - Show **only** specific profiles: `"Player1,Player2"`

---

## Debug Menu

The client includes an optional in‑game debug UI for inspecting all known quest statuses:

**Toggle Key**
- Key: `Debug.ToggleKey`
- Default: `F10`
- Description: Opens/closes the debug window.

**Features:**
- Scrollable list of profiles.
- Grouped quests by status (e.g. Available, Started, Completed).
- Search bar for quick filtering by profile name.

---

## Technical Notes

- The client offloads network requests + JSON parsing to a background thread to avoid UI hitches.
- The server caches prerequisite chains and locked reasons to keep responses fast, rebuilding the cache periodically.

---

## About This Project

This is my first SPT mod! While it's fully functional, there's definitely room for optimization in code structure and performance. I'm actively learning and committed to refactoring and improving the codebase as I gain more experience with the SPT ecosystem. Feedback, suggestions, and pull requests are welcome as I continue to iterate.

---

## Credits

- Heavily inspired by the original **SharedQuests** mod for SPT 4.0 by amorijs:  
  [spt-SharedQuests](https://github.com/amorijs/spt-SharedQuests)
