# Recipe schema reference

Recipes live in [`recipes.json`](../recipes.json), bundled next to the binary. The launcher loads them at startup and on **Reload config**.

## File layout

```jsonc
{
  "recipes": [
    { "id": "spotify", "displayName": "Spotify", "processNames": ["Spotify"], ... },
    { "id": "discord", ... }
  ]
}
```

Top-level `_comment` and `$schema` are ignored. `ReadCommentHandling = Skip` and `AllowTrailingCommas = true` are enabled, so `// comments` and trailing commas are tolerated — but please keep the file machine-clean for tooling.

## Recipe fields

| Field | Type | Required | Description |
|---|---|---|---|
| `id` | string | yes | Stable identifier. Lowercase, kebab-case (`razer-synapse`, `adobe-creative-cloud`). Used as the `recipeId` reference in user configs and as the `AppDefinition.Id` when scanned. |
| `displayName` | string | yes | Human-readable name shown in the tray menu and scan dialog. |
| `processNames` | string[] | yes | One or more process names (case-insensitive, with or without `.exe`). Match is by `Process.GetProcessesByName`. Include all helpers and watchdog processes that should die together. |
| `defaultLaunchPath` | string | no | Full path to launch the app. Supports `%ENVVAR%` expansion (e.g. `%LOCALAPPDATA%\Discord\Update.exe`). Skip if the app cannot be programmatically started. |
| `defaultLaunchArgs` | string | no | Default arguments for the launch command. Discord uses `--processStart Discord.exe`; OneDrive uses `/background`. |
| `shutdown` | enum | yes | One of `graceful`, `killTree`, `command`, `stopServiceOnly`, `stopServiceThenKill`. See below. |
| `shutdownCommand` | string | only with `command` | CLI argument to invoke on the launch exe for clean shutdown. OneDrive: `/shutdown`. Steam: `-shutdown`. The launcher waits up to 5 s for processes to exit, then falls back to `killTree` as a safety net. |
| `serviceNames` | string[] | only with `stopServiceOnly` / `stopServiceThenKill` | Windows service names (the registry key name, **not** the display name). Stopped in order with a 10 s wait per service. Stopping requires admin. |
| `servicesToLeaveAlone` | string[] | no | Driver-tied services we should never touch even if a future contributor sees the name and assumes it belongs in `serviceNames`. Document them here so the next person doesn't break a display driver. |
| `autostartHints` | object[] | no | Suggested autostart locations. Each hint: `{ "method": "...", "key": "..." }` where `method` is `registryRunUser`, `registryRun`, or `startupFolder`. The first hint is used by the resolver when the user's `AppDefinition` doesn't override `autostart`. |
| `notes` | string | no | Free-text caveats. Shown as a tooltip on the per-app tray menu item. Use this for "force-kill corrupts X", "service stop drops Y", "tray-trap on WM_CLOSE". |
| `requiresAdmin` | bool | no, default `false` | Set to `true` whenever `serviceNames` is non-empty. The UI flags the menu entry with `(admin)`. |

## Shutdown strategies

### `graceful`

WM_CLOSE → wait `gracefulTimeoutMs` (from the user's `AppDefinition`, default 3000) → kill tree if still alive.

Use for apps that exit cleanly when their main window is closed: Dropbox, Greenshot, f.lux, Rainmeter, HWiNFO.

### `killTree`

`Process.Kill(entireProcessTree: true)` for every matched process. No graceful step.

Use for:
- **Tray-trap apps** that hide instead of exiting: Discord, Slack, Teams, Telegram, EarTrumpet, Notion.
- **Multi-process apps** where the parent doesn't take its helpers down: Spotify, Steam helpers, Battle.net, Riot Client, PowerToys.
- **Apps that prompt on graceful close** when you want it skipped: qBittorrent.

### `command`

Launches the app's exe with `shutdownCommand` as the argument, waits up to 5 s for the matched processes to exit, falls back to `killTree` if anything remains.

Use only when the vendor documents a CLI shutdown:
- OneDrive: `OneDrive.exe /shutdown` — sync-aware exit.
- Steam: `steam.exe -shutdown` — saves download manifest.

If you're not sure the CLI exists, don't guess — use `graceful` or `killTree`.

### `stopServiceOnly`

Stops every service in `serviceNames`. No process kill. Use when the app has no UI process to speak of (pure background service).

### `stopServiceThenKill`

Stops services first (which kills the watchdog), then kill-trees the UI processes. The order matters: if you kill the UI first, the watchdog respawns it.

Use for:
- **Vendor utilities with respawning watchdogs**: Razer Synapse, Logitech G HUB, Adobe Creative Cloud, Corsair iCUE, Armoury Crate, MSI Center, SteelSeries GG.
- **Remote-access tools** where the service handles incoming connections: AnyDesk, TeamViewer.
- **VPN clients** where the service holds the tunnel: NordVPN, ProtonVPN, ExpressVPN.

## Common mistakes

- **Listing a driver service in `serviceNames`.** Stopping `NvContainerLocalSystem` will hang the display. Put driver-tied services in `servicesToLeaveAlone` instead.
- **Forgetting helper processes.** If `processNames: ["Discord"]` but Discord also runs `DiscordHelper.exe`, the helpers leak. Check Task Manager → Details after a kill.
- **Hard-coding paths.** `C:\Program Files\...` breaks on machines with the user folder elsewhere or a non-default install location. Use `%PROGRAMFILES%`.
- **`shutdownCommand` without `command` strategy.** Ignored. The dispatcher only reads it for `command`.
- **Service display name vs registry name.** `services.msc` shows display names ("Razer Synapse Service"); the API needs the service key name. They're often the same but not always — check `sc query | findstr <name>` if a stop fails with "service not found".

## Resolver behavior (how recipes meet user config)

A user's [`AppDefinition`](../Models.cs) may set `recipeId` plus optional overrides. [`AppResolver.Resolve`](../Models.cs) merges them:

| Field | Source if AppDefinition sets it | Otherwise |
|---|---|---|
| `processNames` | always recipe (AppDef has no equivalent) | recipe |
| `LaunchPath` | AppDefinition wins | `recipe.defaultLaunchPath` |
| `LaunchArgs` | AppDefinition wins | `recipe.defaultLaunchArgs` |
| `Shutdown` strategy | always recipe (AppDef has the legacy `KillMethod` for recipe-less entries) | derived from legacy `KillMethod` if no recipe |
| `ServiceNames` | always recipe | empty if no recipe |
| `Autostart` | AppDefinition wins (when not `None`) | first entry in `recipe.autostartHints` |
| `RequiresAdmin` | always recipe | `false` |
| `Notes` | always recipe | `null` |

This means: ship recipes that are correct for the default install, and let users override `LaunchPath` / `Autostart` / `LaunchArgs` for their machine. Don't ship machine-specific paths in recipes.
