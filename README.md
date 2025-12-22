## LunaStatusQuests

LunaStatusQuests is a client + server mod for SPT 3.11 that shows **shared quest progress for all profiles**:

- **Client** (BepInEx plugin) injects a shared status block into:
  - The standard **Tasks** screen.
  - The **trader quest detail** view (configurable).
- **Server** backend exposes `/LunaStatusQuests/statuses` with real‑time quest status for each profile.

---

## Installation

- **Client DLL** → `BepInEx/plugins/LunaStatusQuests/LunaStatusQuests.dll`
- **Server mod** → normal SPT server mod folder (with `LunaStatusQuestsBackend.ts` compiled/bundled as per your usual workflow).

Reboot the server and the client after installing.

---

## Client config options

All options live in the BepInEx config file for `LunaStatusQuests`:

- **Enabled**
  - Key: `General.Enabled`
  - Default: `true`
  - Description: Turn the client mod on/off.

- **UpdateIntervalSeconds**
  - Key: `General.UpdateIntervalSeconds`
  - Default: `60`
  - Description: Minimum number of seconds between server fetches. Lower values mean more up‑to‑date data but more traffic.

- **ShowDebugLogs**
  - Key: `General.ShowDebugLogs`
  - Default: `false`
  - Description: When enabled, prints extra debug information to the BepInEx log (fetch details, injection reasons, etc.).

- **ShowStatusInTrader**
  - Key: `General.ShowStatusInTrader`
  - Default: `true`
  - Description: When enabled, shows the shared status block on the **trader quest detail** UI.

- **VisibleProfiles**
  - Key: `Profiles.VisibleProfiles`
  - Default: `"*"`
  - Description: Controls which profile names appear in the shared status list.
  - Patterns:
    - Show **all** profiles:
      - `"*"`
    - Show all **except** specific profiles:
      - `"*,-BotName"`
      - `"*,-Profile1,-Profile2"`
    - Show **only** specific profiles:
      - `"Player1,Player2"`

---

## Debug menu

The client includes an optional in‑game debug UI for inspecting all known quest statuses:

- **Toggle key**
  - Key: `Debug.ToggleKey`
  - Default: `F10`
  - Description: Opens/closes the debug window.

Features:

- Scrollable list of profiles.
- Grouped quests by status (e.g. Available, Started, Completed).
- Search bar for quick filtering by profile name.

---

## Notes

- The client offloads network requests + JSON parsing to a background thread to avoid UI hitches.
- The server caches prerequisite chains and locked reasons to keep responses fast, rebuilding the cache periodically.

## Credits

- Heavily inspired by the original **SharedQuests** mod for SPT 4.0 by amorijs:  
  [spt-SharedQuests](https://github.com/amorijs/spt-SharedQuests)


