# Unelevated tray app, elevated helper, installer and update check

Date: 2026-10-19
Status: approved in conversation; written for whoever plans and builds it.

## Why

Today BgToggle gets admin rights by registering itself as a scheduled task
with "run with highest privileges" (`AutostartTask.cs`, menu item *System →
Install autostart (admin)*). The whole tray app then runs elevated, which
causes two problems:

1. **Apps it starts run as admin.** `ProcessManager.Launch` uses
   `Process.Start`, so Spotify, Discord etc. inherit the elevated token.
   Drag-and-drop from Explorer breaks and those apps run with admin rights
   they should never have.
2. **It is a silent route to admin.** The elevated process launches
   `launchPath` and runs `shutdownCommand` taken from
   `%APPDATA%\BgToggle\config.json` and `recipes.local.json`. Any program
   running as the user can write those files and get code run as admin
   without a UAC prompt.

Smaller related problems:

- `schtasks /Create` defaults stop the task after 72 hours and don't start
  it on battery, so on a laptop the tray app dies after three days.
- The single-instance mutex (`Program.cs`) is created as `Global\...`. If an
  elevated instance created it, a normal launch may throw
  `UnauthorizedAccessException` instead of showing "already running".
- There is no installer. The owner has only ever run it from a build
  folder. Other people should be able to download and install it from
  GitHub.
- There is no way to learn that a newer version exists.

The owner uses BgToggle mainly on their own PC but wants it to be easy to
install for anyone who finds the GitHub repo.

## What we're building

Four pieces:

1. **The tray app always runs as the user, never elevated.**
2. **A small elevated helper** that does only the few things that really
   need admin, and only for apps listed in the installed recipes.
3. **An installer**, built by CI and attached to GitHub Releases.
4. **An update check** that tells the user when a newer release exists.

## Goals

- The tray app runs at normal (medium) integrity in every supported setup.
  Everything it launches runs as the user.
- Nothing from a user-writable location can make anything run elevated. The
  helper accepts **recipe ids only**: no paths, command lines, service names
  or process names from the client.
- Service-watchdog recipes (Razer Synapse, Logitech G HUB, iCUE, Armoury
  Crate, Adobe CC, …) keep working when the helper is installed. Their
  services are stopped and started, and their elevated UI processes can be
  ended.
- Works for both admin and standard user accounts once installed. Installing
  needs an admin once.
- Installing from GitHub means: download one `BgToggle-Setup-<version>.exe`,
  run it, accept one UAC prompt. No separate .NET install.
- `dotnet run` from the repo keeps working without the helper. Service
  stops then fail and are logged, and the app falls back to killing the
  processes it can.
- Uninstall cleans up: removes the helper, removes leftover tasks, and puts
  back autostart entries BgToggle disabled (best effort).
- The tray tells the user when a newer GitHub release exists.

## Non-goals

- Silent or automatic updates, or downloading the installer for the user.
  The update check only links to the release page.
- Code signing. Releases are unsigned for now, so users will see
  SmartScreen's "unknown publisher" warning. Revisit if it becomes a
  problem.
- winget, Microsoft Store or MSIX packaging.
- ARM64 builds (x64 only for now).
- Several users actively using BgToggle on one machine at the same time.
  It must be *safe* (see session rule below), but it doesn't need to be a
  great experience.

## Anti-goals: a failure even if it "works"

- The tray app, or anything it launches, ending up elevated in any normal
  flow. That includes "launch after install" from the elevated installer.
- The helper accepting anything that names a file, a command, or a
  service/process directly from the client.
- The helper acting on a recipe from `recipes.local.json` or any other
  user-writable file.
- The helper ending processes in another user's session.
- Making the development loop (`dotnet run`, unit tests) need an install or
  admin rights.

## Decisions

Made with the owner in conversation:

- Split the work between an unelevated tray app and an elevated helper.
- The helper only stops/starts services, ends elevated vendor processes,
  and (as discussed) writes machine-wide (HKLM) Run entries — later dropped,
  see "Machine-wide autostart" under Tray app changes. It only acts for apps in
  the `recipes.json` installed next to it.
- The installer puts everything in an admin-only folder (`Program Files`).
- Releases are published to GitHub Releases and built by CI from a version
  tag.
- The bundled .NET runtime is included, so users need no separate install.
- The update check is about once a day and links to the release page. It
  can be turned off in config.
- Recipes saved locally (`recipes.local.json`) still work for everything
  the tray app does on its own. They cannot widen what the helper will do.
  If a local recipe needs a service the installed recipes don't list, the
  helper refuses, the refusal is logged, and the app is killed instead.
  Getting the recipe into the main `recipes.json` and reinstalling is how
  to unlock it.
- The old *Install autostart (admin)* scheduled task is removed by the
  installer. A plain *Start BgToggle at logon* option for the tray app
  replaces it.
- Versions start at `0.x` and come from the git tag (`v0.2.0` → `0.2.0`).

Calls made by the author of this document, which the builder should keep
unless they find a concrete problem:

- **The helper is a Windows service, not a logon scheduled task.** The
  conversation said "logon task". A service is the better fit for what was
  asked:
  - It works for standard-user accounts. A "highest privileges" task only
    elevates for admins.
  - It has no 72-hour or battery defaults to fight.
  - The service manager restarts it if it crashes.
  - It is the usual way to give one narrow privileged operation to normal
    users.

  It runs as LocalSystem, start type *Automatic (Delayed Start)*, with
  recovery set to restart on failure.
- **The installer tool is Inno Setup.** It's free, scriptable and works in
  CI, and it handles Program Files, service registration, a
  "start at logon" checkbox, a launch-after-install option that runs as the
  original (unelevated) user (`runasoriginaluser`), and a clean uninstall.
  MSI tooling (WiX) is heavier than needed. MSIX can't reliably manage
  services and HKLM Run keys.
- **The tray app's own autostart is an HKCU Run value**
  (`BgToggle` → `"<install dir>\BgToggle.exe"`). The installer sets it when
  the checkbox is ticked, and a tray menu toggle turns it on and off. This
  replaces `AutostartTask.cs` and its `schtasks` calls.
- **The update check reads**
  `https://api.github.com/repos/emanuellonnberg/bgtoggle-windows/releases/latest`.
  - It skips drafts and prereleases, which that endpoint does by default.
  - It runs at most once per 24 hours, with the last-check time stored
    outside `config.json` (a small state file next to it is fine).
  - It never checks when the running version is `0.0.0` (a local or dev
    build).
  - Failures are silent apart from a log line.
  - The UI is a tray menu item *Update available: vX.Y.Z…* plus one balloon
    per new version. Clicking either opens the release's `html_url`.
  - The config switch is `checkForUpdates` (default `true`).

## How the pieces fit

```
  user session (medium integrity)              LocalSystem
 ┌────────────────────────────┐   named pipe   ┌──────────────────────────┐
 │ BgToggle.exe (tray)        │ ─────────────▶ │ BgToggle Helper service  │
 │  config.json (user)        │  recipe ids    │  reads ONLY              │
 │  recipes.local.json (user) │  only          │  <install>\recipes.json  │
 │  launches/kills user apps  │ ◀───────────── │  services, service hosts │
 └────────────────────────────┘   results      │  elevated vendor procs   │
                                               └──────────────────────────┘
 <install dir> = C:\Program Files\BgToggle  (admin-only write)
```

### Helper operations

Every request names a **recipe id**. The helper looks that id up in its
**own** copy of `recipes.json`, loaded from the helper executable's
directory. It never uses `%APPDATA%`, the client's copy, or anything sent
over the pipe. If the id is unknown, the request is refused.

| Operation | What the helper does |
|---|---|
| `Ping` | Returns the helper version. Used to detect the helper and check it matches. |
| `StopServices(recipeId)` | Stops the recipe's `serviceNames` minus its `servicesToLeaveAlone`. Same per-service semantics as today's `ServiceManager.StopMany`: skip if already stopped, 10 s wait, continue on error. |
| `StartServices(recipeId)` | The reverse, like `ServiceManager.StartMany`. |
| `KillProcesses(recipeId)` | Kills (with process tree) processes that pass the session rule below and whose name is in the recipe's `processNames`. |

There is **no HKLM autostart operation** (author's call, see "Machine-wide
autostart" below).

Each response carries ok/failed plus a list of human-readable log lines.
The tray app writes those into its own log, so nothing extra is needed to
debug the helper.

How the helper must protect itself:

- **Session rule (author's call).** Get the client's process id from the
  pipe (`GetNamedPipeClientProcessId`) and its session
  (`ProcessIdToSessionId`). `KillProcesses` may kill a process only if
  either:
  - it is in the **client's session** and its name is in the recipe's
    `processNames`; or
  - it is in **session 0** and its PID is the process of one of the
    recipe's own `serviceNames` (minus leave-alone), as reported by the
    service manager (`QueryServiceStatusEx`).

  The second case exists because several bundled recipes (`CorsairService`,
  `Razer Synapse Service`, `MSI.CentralServer`, `ArmouryCrate.Service`,
  VPN services, AnyDesk) list their own service host in `processNames`.
  The old elevated tray killed those when a service stop timed out. Write
  this rule as a pure function over (process session, process id, client
  session, set of allowed service PIDs) so it can be unit tested. Nothing
  else in session 0 and nothing in another user's session is ever touched.
- **Pipe access:** the pipe's access list allows SYSTEM and Administrators
  full control, and Interactive users (`S-1-5-4`) read/write. Nobody else.
- **Pipe hijacking:** the helper creates the pipe with
  `FILE_FLAG_FIRST_PIPE_INSTANCE`. Before trusting a response, the tray app
  checks that the pipe server's process id
  (`GetNamedPipeServerProcessId`) is the process id the service manager
  reports for the helper service. If it isn't, the tray treats the helper
  as missing and logs a warning.
- **Protocol version, separate from the app version (author's call).** Every
  request and response carries an integer `protocol` field (start at 1).
  The helper refuses requests with a protocol it doesn't speak. `Ping`
  returns both the protocol and the app version. The tray uses the helper
  whenever the protocol matches, **including from a dev build**, so the
  owner can test service recipes with `dotnet run` against the installed
  helper. When the protocol doesn't match, the tray treats the helper as
  missing and the status line says *Helper: incompatible version
  (reinstall)*.
- **Recipes are read at service start, and again on a `Ping` if the file's
  timestamp changed.** That way an upgrade via the installer is picked up
  without restarting the service. Either behavior is acceptable, as long
  as the file read is always the one in the helper's directory.
- Any local user can ask the helper to stop or start a listed vendor
  service, or to end a listed vendor process in their own session. This is
  an accepted risk: it's all the helper can do, the list is controlled by
  an admin, and it is equivalent to what BgToggle exists to do.

### Tray app changes

- **Never elevated.**
  - The manifest stays `asInvoker`.
  - On startup, if the process token is elevated, log a warning and show a
    one-time balloon: "BgToggle is running as administrator; apps it
    starts will be too." Apps are not launched elevated silently.
  - The installer's *Launch BgToggle* step must use `runasoriginaluser` so
    this doesn't happen in the normal flow.
- **Helper client.** A `HelperClient` with a short connect timeout (about
  500 ms) and a cached "helper available" flag. Re-check after a failure,
  with a backoff of about a minute.
- **When the helper is used:**
  - **Which apps are helper-eligible (author's call):** the app has a
    `recipeId` **and** that recipe was loaded from the bundled
    `recipes.json`, not from `recipes.local.json`. If a local recipe
    overrides a bundled one with the same id, the tray never uses the helper
    for it, because the helper would act on the bundled definition and not
    on what the user sees. Not eligible means the services are tried
    locally through `ServiceManager` (fails and logs unless the user happens
    to be elevated) and the app's processes are killed locally. This needs
    `RecipeStore` to record each recipe's source (e.g. a `Source` value or a
    separate "is bundled" lookup). This is also how "local recipes can't
    widen the helper" shows up in practice: there is no way to send the
    helper anything but a bundled id.
  - `StopServiceOnly` / `StopServiceThenKill` stop and start services
    through the helper when it's available and the app is eligible.
    Otherwise they use `ServiceManager` directly, as today.
  - Killing processes tries locally first. For processes the local kill
    can't end ("access denied"), and only for eligible apps, ask the helper
    to `KillProcesses`. When a service stop fails or times out, also ask the
    helper to `KillProcesses` so the session-0 rule can clean up the
    service host.
  - Apps without a `recipeId` (legacy entries) never use the helper.
- **Machine-wide autostart (author's call).** `autostart: registryRun`
  (HKLM) only worked because the whole tray was elevated. No bundled recipe
  uses it, and the owner's config doesn't either. It is dropped from what
  works: the tray logs a warning and leaves the HKLM value alone (no
  stash, no delete). The README documents this. If a recipe ever needs it,
  a helper operation scoped to that recipe's `autostartHints` can be added
  then. `registryRunUser` and `startupFolder` keep working locally.
- **The "requires admin" marker** in the Apps menu (`(admin)`) should only
  show when the helper is *not* available. When it is, admin rights aren't
  something the user needs to think about.
- **System menu:**
  - Remove *Install / Reinstall / Remove autostart (admin)*.
  - Add a checkable *Start BgToggle at logon* item (HKCU Run).
  - Add a status line, *Helper: running* or *Helper: not installed*, that
    links to the README section explaining it.
- **Single instance:** creating the mutex must not crash. Catch
  `UnauthorizedAccessException` and treat it as "already running". The
  `Global\BgToggle.SingleInstance.<user>` name can stay.
- **Update check:** as described under Decisions. Runs on a background
  thread so it never blocks the UI thread.
- `AutostartTask.cs` is deleted. A config with no use for it needs no
  migration, since the setting lived in Task Scheduler, not in config.

### Installer (Inno Setup)

- Installs to `{autopf}\BgToggle`: tray exe, helper exe, bundled runtime,
  `recipes.json`, license and README.
- Admin install (`PrivilegesRequired=admin`), x64 only.
- **Registers the helper service:** name `BgToggleHelper`, display name
  "BgToggle Helper", LocalSystem, delayed automatic start, restart on
  failure. Starts it at the end of install.
- **Removes the legacy scheduled task** named `BgToggle`, if present.
- **Optional tasks:** *Start BgToggle when I sign in* (checked by default)
  and *Launch BgToggle now*.
  - **Who gets the autostart (author's call).** The installer itself must
    not write HKCU: when a standard user installs by typing an admin's
    password, the elevated installer's HKCU belongs to the admin. Instead
    the installer runs the tray **as the original user** (`runasoriginaluser`)
    with `--first-run --start-at-logon=<on|off>`. The tray writes its own
    HKCU Run value, and exits right away if *Launch now* was unticked.
    `runasoriginaluser` runs as whoever started Setup, which is the
    standard user in that case.
- **Upgrade in place:** installing a newer version over an older one stops
  the tray app and the service, replaces files, and restarts the service.
  Closing the tray app can use Inno's Restart Manager support, or the
  helper/tray can expose a close command; builder's call.
- **Uninstall:**
  - Stops the tray app.
  - Puts back autostart entries BgToggle disabled, best effort:
    - `HKLM\Software\BgToggle\Stash` values back into HKLM Run, left over
      from the old elevated era.
    - For the **profile the uninstaller runs under**: HKCU Run values from
      `HKCU\Software\BgToggle\Stash`, `*.bgtoggle.disabled` files in its
      Startup folder, and the tray's own HKCU Run value.
  - Stops and deletes the service, then removes the install folder.
  - Leaves `%APPDATA%\BgToggle` (config, logs) in place.
  - **Standard accounts (author's call).** Inno can't run uninstall steps as
    the original user. So when a standard user uninstalls with an admin's
    password, HKCU cleanup lands on the admin's profile and that user's own
    entries aren't restored. This is accepted and out of scope. The tray
    gets a *System → Restore all autostart entries* item that does the
    per-user restore on demand, and the README tells standard users to use
    it before uninstalling.

### Release pipeline

- **New workflow, triggered by tags matching `v*`:**
  1. Build and test as the existing CI does.
  2. Publish tray and helper for `win-x64`, self-contained.
  3. Build the installer.
  4. Create a GitHub Release with `BgToggle-Setup-<version>.exe` attached.
- The version comes from the tag and is passed as `-p:Version=...`, so the
  assembly version matches what the update check compares against.
- **Keep the download reasonable.** Publishing two separate self-contained
  single-file exes would ship two copies of the runtime. Publish so the
  runtime is shared: for example both projects self-contained, not single
  file, into the same folder, or one exe with a `--service` mode. Builder's
  call, but the installer should be in the tens of MB, not well over
  100 MB.
- The existing `build.yml` keeps running on pushes and PRs.

## Constraints and context

- Target is **.NET 10** (`net10.0-windows`); PR #4 moves the repo there.
  Fixes in PRs #1–#3 (config robustness, graceful shutdown,
  `ProcessManager.Start` restarting services, background profile apply,
  `ProcessSnapshot`) are assumed merged. Build on top of them.
- `BgToggle.csproj` sits at the repo root and globs every `.cs` file below
  it. It already excludes `tests/**`. Any new project folder (helper,
  shared library) must be excluded the same way, or the root project will
  try to compile it.
- `recipes.json` is copied next to the build output today
  (`CopyToOutputDirectory`). The helper needs it next to its own exe too.
- Recipe JSON loading (`RecipeStore`, `Recipe` record,
  `JsonSerializerOptions`) should be shared between tray and helper rather
  than copied. The helper must not use `RecipeStore`'s `%APPDATA%` merge,
  only the bundled file.
- Code style: file-scoped namespaces, records, short XML doc comments that
  explain *why*, xUnit tests in `tests/BgToggle.Tests`. Match it.

## Left to the builder

- The pipe wire format. Length-prefixed or newline-delimited JSON, one
  request per connection, is the expected shape; any simple, versioned
  format is fine. The pipe name (e.g. `BgToggle.Helper`) is also the
  builder's choice.
- Project layout. Suggested: `src/BgToggle.Core` (recipes, models,
  helpers), `src/BgToggle.Helper` (service), with the tray app at the root
  as today or moved under `src/`, whichever keeps the diff readable.
- Service hosting: plain `ServiceBase`, or
  `Microsoft.Extensions.Hosting.WindowsServices`.
- Inno Setup details: wizard pages, icons, how CI installs Inno.
- How the tray reads the service's process id (`QueryServiceStatusEx` via
  P/Invoke is fine).
- Exact wording of menu items and messages, within the intent above.

## Testing expectations

**Unit tests (no admin, no install needed):**

- The helper's allow-list: a recipe id resolves to service names minus
  leave-alone; unknown ids are refused; ids present only in a local file
  are refused.
- Tray-side eligibility: a bundled recipe is eligible; a local-only recipe
  and a local override of a bundled id are not; a legacy app without
  `recipeId` is not.
- The protocol-version check: a matching protocol is used even when app
  versions differ; a mismatch reads as "helper missing".
- Request parsing and validation: unknown operations and malformed
  payloads are refused cleanly.
- The session rule as a pure function: client-session process with a
  listed name is allowed; session-0 process allowed only if its PID is one
  of the recipe's service PIDs; another user's session is never allowed.
- Version comparison and parsing of the update-check response, including
  "no newer", "newer", "prerelease tag", "garbage JSON" and "0.0.0 dev
  build".
- Helper-client fallback: with no helper, the stop and start paths fall
  back exactly as described above.
- A pipe round trip in-process: a server on a test pipe name with a
  test-only recipe file, and the real client against it.

**Manual matrix before the first release:**

- Fresh install on an admin account and on a standard account. On the
  standard account, *Start BgToggle when I sign in* must land on the
  standard user, not the admin whose password was typed.
- Upgrade from one build to a newer one.
- Uninstall: autostart entries come back, the service and task are gone.
- Razer- or iCUE-style recipe off and on from a standard account.
- Launched apps are not elevated: Task Manager → Details → "Elevated"
  column says No.
- Dev run (`dotnet run`) with no helper installed.
- The update notice appears when a newer release exists, and not for a dev
  build.
