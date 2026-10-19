# Unelevated Tray + Elevated Helper + Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** BgToggle's tray app never runs elevated. A small LocalSystem
helper service does the few admin-only operations, and only for recipe ids
from its own installed `recipes.json`. An Inno Setup installer is built by
CI and published on GitHub Releases, and the tray tells the user when a
newer release exists.

**Architecture:** A new class library, `src/BgToggle.Core`, holds the
recipe model and recipe-file loading, the logger, the service control code
and the whole helper protocol (wire format, allow-list, kill rule, request
handler, pipe server, pipe client). `src/BgToggle.Helper` is a thin Windows
service host around `HelperServer`. The tray (root `BgToggle.csproj`)
reaches the helper through `IHelperClient` and falls back to local
operations when the helper is missing or the app isn't eligible. Packaging
publishes both apps self-contained into one folder (one shared runtime) and
wraps that folder with Inno Setup.

**Tech Stack:** .NET 10 (`net10.0-windows`), WinForms (tray only),
`System.ServiceProcess.ServiceController` 10.0.13 (`ServiceController`,
`ServiceBase`), `System.IO.Pipes` (`NamedPipeServerStreamAcl`), System.Text.Json,
xUnit, Inno Setup 6, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-19-elevation-helper-installer-design.md`

**Branch:** `feat/elevation-helper`, which already contains PRs #1–#3, #4
(.NET 10) and the spec. Base every task on it.

## Global Constraints

- Target framework `net10.0-windows` for every project. The test project
  stays at `tests/BgToggle.Tests`.
- Keep namespace `BgToggle` for code moved into Core, so call sites don't
  change. New helper types also use namespace `BgToggle`.
- Root `BgToggle.csproj` globs every `.cs` file. Add `src/**` and
  `installer/**` to all four existing `Remove` lists, next to `tests/**`.
- The helper accepts **recipe ids only**. It never accepts paths, command
  lines, service names or process names from the client.
- The helper reads recipes **only** from
  `Path.Combine(AppContext.BaseDirectory, "recipes.json")`.
- Fixed names:
  - Service name `BgToggleHelper`, display name `BgToggle Helper`.
  - Pipe name `BgToggle.Helper`, protocol version `1`.
  - Tray Run value name `BgToggle`.
  - Legacy scheduled task `BgToggle`.
- Install folder `{autopf}\BgToggle`. Installer file name
  `BgToggle-Setup-<version>.exe`.
- Helper log dir: `%ProgramData%\BgToggle\logs`. The tray log dir is
  unchanged: `%APPDATA%\BgToggle\logs`.
- Update endpoint:
  `https://api.github.com/repos/emanuellonnberg/bgtoggle-windows/releases/latest`.
  - At most once per 24 h.
  - Never when the running version is `0.0.0`.
  - Config switch `checkForUpdates`, default `true`.
- Version is `0.0.0` for local builds (`Directory.Build.props`). Release
  builds get it from the tag (`v0.2.0` → `-p:Version=0.2.0`).
- README anchor for helper help: `#helper-service`. The link is
  `https://github.com/emanuellonnberg/bgtoggle-windows#helper-service`.
- Code style: file-scoped namespaces, records, short `///` comments that
  explain why, xUnit `[Fact]` tests.
- Build gate for every task:
  - `dotnet build app_toggler.sln -c Debug -warnaserror` with 0 warnings.
  - `dotnet test tests/BgToggle.Tests/BgToggle.Tests.csproj -c Debug` all
    passing.
  - Use PowerShell for `-warnaserror`; Git Bash mangles `/warnaserror`.

## Review Focus

1. **Helper stopped or not installed mid-session.** The tray must fall back
   within about 1 s per call and the status line must say "not installed",
   with no UI freeze. Test: `HelperClientTests.Unavailable_ReturnsQuickly`
   (Task 4).
2. **Garbage on the pipe** (non-JSON, oversized line, client disconnects
   mid-request). The server must reject or ignore it and keep serving the
   next request. Tests: `HelperServerTests.Garbage_ThenValidRequest_StillServed`
   and `ClientDisconnect_ThenValidRequest_StillServed` (Task 4).
3. **Installed `recipes.json` missing or corrupt.** The helper refuses every
   recipe op with a clear error and doesn't crash; `ping` still answers.
   Test: `HelperServerTests.MissingRecipes_RefusesOps_PingWorks` (Task 4).
4. **A process exits between enumeration and kill, or one kill throws.**
   The remaining allowed processes still get killed, and the response lists
   the failure. Test: `HelperRequestHandlerTests.Kill_OneFails_OthersStillKilled`
   (Task 3).
5. **Odd release data from GitHub** (prerelease tag `v0.3.0-beta`,
   non-semver tag `release-1`, HTTP failure, garbage JSON). No notice and
   no crash. Tests: `UpdateCheckTests` (Task 8).

---

## File Structure

| Path | Responsibility |
|---|---|
| `Directory.Build.props` (new) | `<Version>0.0.0</Version>` default for all projects |
| `src/BgToggle.Core/BgToggle.Core.csproj` (new) | Class library. References the ServiceController package |
| `src/BgToggle.Core/Recipe.cs` (moved from `Recipes.cs`) | `ShutdownStrategy`, `AutostartMethod`, `AutostartCandidate`, `Recipe` |
| `src/BgToggle.Core/RecipeFile.cs` (new) | Parse and save a `{ "recipes": [...] }` file. Shared JSON options |
| `src/BgToggle.Core/ExeNames.cs` (moved out of `ProcessSnapshot.cs`) | `ExeNames.Strip` |
| `src/BgToggle.Core/Logger.cs` (moved) | Daily log file, with a settable `LogDir` |
| `src/BgToggle.Core/ServiceManager.cs` (moved) | Stop/start/query services, plus `ProcessId(name)` |
| `src/BgToggle.Core/Helper/HelperProtocol.cs` | Constants, `HelperRequest`, `HelperResponse`, `HelperWire` framing |
| `src/BgToggle.Core/Helper/HelperAllowList.cs` | Recipe id → allowed services and processes |
| `src/BgToggle.Core/Helper/KillRule.cs` | Pure session rule, `RunningProcess` |
| `src/BgToggle.Core/Helper/HelperActions.cs` | `IHelperActions` and `SystemHelperActions` (the real OS calls) |
| `src/BgToggle.Core/Helper/HelperRequestHandler.cs` | Validate a request and run it against an allow-list and actions |
| `src/BgToggle.Core/Helper/HelperServer.cs` | Pipe accept loop, client session lookup, recipe reload |
| `src/BgToggle.Core/Helper/HelperPipeSecurity.cs` | Strict pipe access list |
| `src/BgToggle.Core/Helper/HelperClient.cs` | `IHelperClient`, `HelperClient`, `HelperStatus` |
| `src/BgToggle.Core/Helper/PipeNative.cs` | P/Invoke for pipe client/server PIDs and process sessions |
| `src/BgToggle.Helper/BgToggle.Helper.csproj` + `Program.cs` + `HelperService.cs` (new) | Windows service host, plus a `--console` debug mode |
| `RecipeStore.cs` (modify) | Merge via `RecipeFile`; track which ids are bundled |
| `Recipes.cs` → `ResolvedApp.cs` (rename) | `ResolvedApp` with `RecipeId` and `HelperEligible` |
| `Models.cs` (modify) | Resolver sets eligibility. `Config.CheckForUpdates` |
| `ProcessManager.cs` (modify) | Use the helper for services and kills of eligible apps |
| `AutostartManager.cs` (modify) | HKLM made a no-op. `RestoreAll` |
| `TrayAutostart.cs` (new) | The tray's own HKCU Run value |
| `StartupArgs.cs` (new) | `--first-run`, `--start-at-logon=`, `--exit-after-setup`, `--uninstall-cleanup` |
| `Elevation.cs` (new) | `Elevation.IsElevated()` |
| `UpdateCheck.cs` (new) | Pure parsing and comparison, and the HTTP checker |
| `AppState.cs` (new) | `%APPDATA%\BgToggle\state.json`: last check time, last notified version |
| `Program.cs`, `TrayApp.cs` (modify) | Arg handling, mutex, elevation warning, menu, helper and update wiring |
| `AutostartTask.cs` (delete) | Replaced by `TrayAutostart` and the installer |
| `installer/BgToggle.iss` (new) | Inno Setup script |
| `.github/workflows/release.yml` (new) | Tag → publish → installer → GitHub Release |
| `README.md` (modify) | Install, `#helper-service`, standard-account uninstall note, HKLM autostart removal |

---

### Task 1: Extract `BgToggle.Core` and the version default

**Files:**
- Create: `Directory.Build.props`, `src/BgToggle.Core/BgToggle.Core.csproj`, `src/BgToggle.Core/RecipeFile.cs`
- Move (with `git mv`, content otherwise unchanged except as noted):
  - `Logger.cs` → `src/BgToggle.Core/Logger.cs`
  - `ServiceManager.cs` → `src/BgToggle.Core/ServiceManager.cs`
  - `Recipes.cs` → split. `ShutdownStrategy`, `AutostartCandidate` and `Recipe` go to `src/BgToggle.Core/Recipe.cs`. `ResolvedApp` stays at the root in `ResolvedApp.cs`.
  - `AutostartMethod` enum moves from `Models.cs` → `src/BgToggle.Core/Recipe.cs`
  - `ExeNames` moves from `ProcessSnapshot.cs` → `src/BgToggle.Core/ExeNames.cs`
- Modify: `BgToggle.csproj` (project reference; `src/**` and `installer/**` excludes), `RecipeStore.cs`, `app_toggler.sln`
- Test: `tests/BgToggle.Tests/RecipeFileTests.cs`

**Interfaces:**
- Produces:
  - `public static class RecipeFile { public static JsonSerializerOptions Options { get; } public static List<Recipe> Load(string path); public static void Save(string path, IEnumerable<Recipe> recipes); }`
    - `Load` throws `FileNotFoundException` for a missing file, and `JsonException` for bad JSON.
    - `Options` are the current `RecipeStore.Options`, moved.
  - `Logger.LogDir` becomes `public static string LogDir { get; set; }`, with the same default as today.
  - `public static int? ServiceManager.ProcessId(string serviceName)` returns the service's PID via `QueryServiceStatusEx` on `ServiceController.ServiceHandle`. It returns `null` if the service is missing, stopped or inaccessible.
- `RecipeStore.Options` stays as `=> RecipeFile.Options`, so `RecipeSuggestDialog` keeps compiling.

- [ ] **Step 1: Write the failing tests**

```csharp
public class RecipeFileTests
{
    [Fact]
    public void Load_BundledFile_ParsesRecipes()
    {
        var recipes = RecipeFile.Load(Path.Combine(AppContext.BaseDirectory, "recipes.json"));
        Assert.Contains(recipes, r => r.Id == "razer-synapse" && r.Shutdown == ShutdownStrategy.StopServiceThenKill);
    }

    [Fact]
    public void Load_MissingFile_Throws() =>
        Assert.Throws<FileNotFoundException>(() => RecipeFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));

    [Fact]
    public void ServiceProcessId_RunningService_HasPid() =>
        Assert.True(ServiceManager.ProcessId("RpcSs") > 0);

    [Fact]
    public void ServiceProcessId_MissingService_IsNull() =>
        Assert.Null(ServiceManager.ProcessId("bgtoggle-test-no-such-service"));
}
```

- [ ] **Step 2: Run tests to verify they fail.** Run `dotnet test tests/BgToggle.Tests/BgToggle.Tests.csproj -c Debug`. Expected: compile error, because `RecipeFile` and `ServiceManager.ProcessId` don't exist yet.

- [ ] **Step 3: Create `Directory.Build.props` with `<Version>0.0.0</Version>`.** Then create `BgToggle.Core.csproj`: `net10.0-windows`, `Nullable` and `ImplicitUsings` enabled, `RootNamespace BgToggle`, no WinForms, and the ServiceController 10.0.13 package. Move the root project's package reference there.

- [ ] **Step 4: Do the moves listed under Files.**
  - Root `BgToggle.csproj` gets `<ProjectReference Include="src\BgToggle.Core\BgToggle.Core.csproj" />` and the `src/**` and `installer/**` excludes.
  - `RecipeStore.LoadFile` calls `RecipeFile.Load` inside its existing try/catch; treat a missing file as before.
  - `SaveLocal` uses `RecipeFile.Save`.
  - Add Core to the solution: `dotnet sln app_toggler.sln add src/BgToggle.Core/BgToggle.Core.csproj`.

- [ ] **Step 5: Implement `ServiceManager.ProcessId`.** P/Invoke `QueryServiceStatusEx(SafeHandle, SC_STATUS_PROCESS_INFO = 0, ...)` into a `SERVICE_STATUS_PROCESS` struct and return `dwProcessId` when it's non-zero. Catch everything and return `null`.

- [ ] **Step 6: Run the build gate.** Expected: 0 warnings, and every test passes, including the 4 new ones.

- [ ] **Step 7: Commit.** Message: `Extract BgToggle.Core (recipes, logger, services); default version 0.0.0`.

---

### Task 2: Track bundled recipes; helper eligibility on `ResolvedApp`

**Files:**
- Modify: `RecipeStore.cs`, `ResolvedApp.cs`, `Models.cs` (`AppResolver`)
- Test: `tests/BgToggle.Tests/HelperEligibilityTests.cs`

**Interfaces:**
- Consumes: `RecipeFile.Load`.
- Produces:
  - `public record RecipeSet(IReadOnlyList<Recipe> Recipes, IReadOnlySet<string> BundledIds);`
    - `BundledIds` holds the ids whose effective recipe came from the bundled file. An id overridden by local is **not** in it. Comparison is case-insensitive.
  - `public static RecipeSet RecipeStore.LoadFrom(string bundledPath, string localPath)`. It does no caching and no env expansion; the caller expands.
  - `public static bool RecipeStore.IsBundled(string recipeId)`, backed by the cached set from `Load()`.
  - `ResolvedApp` gets two new trailing parameters: `string? RecipeId = null, bool HelperEligible = false`.
  - `AppResolver.Resolve(AppDefinition app, Recipe? recipe, bool recipeIsBundled = false)`.
    - Sets `RecipeId = recipe?.Id` and `HelperEligible = recipe is not null && recipeIsBundled`.
    - `Resolve(app)` passes `RecipeStore.IsBundled(id)`.

- [ ] **Step 1: Write the failing tests.** The temp-dir fixture writes `bundled.json` and `local.json` using `RecipeFile.Save`.

```csharp
[Fact] public void LoadFrom_BundledOnlyId_IsBundled()            // bundled {a}, local {}       → BundledIds == {a}
[Fact] public void LoadFrom_LocalOverride_IsNotBundled()         // bundled {a}, local {a}      → "a" not in BundledIds; Recipes has local's version
[Fact] public void LoadFrom_LocalOnlyId_IsNotBundled()           // bundled {a}, local {b}      → BundledIds == {a}; Recipes has a and b
[Fact] public void LoadFrom_MissingLocal_Ok()                    // local path absent           → no throw, BundledIds == {a}
[Fact] public void Resolve_BundledRecipe_IsEligible()            // Resolve(app, recipe, true)  → HelperEligible && RecipeId == recipe.Id
[Fact] public void Resolve_LocalRecipe_NotEligible()             // Resolve(app, recipe, false) → !HelperEligible
[Fact] public void Resolve_NoRecipe_NotEligible()                // Resolve(app, null, true)    → !HelperEligible && RecipeId == null
```

- [ ] **Step 2: Run the tests and confirm they fail.** Expected: compile errors.
- [ ] **Step 3: Implement as described in Interfaces.** `Load()` caches a `RecipeSet` and then applies `ExpandPaths` to its recipes, as today.
- [ ] **Step 4: Run the build gate.** Expected: all tests pass.
- [ ] **Step 5: Commit.** Message: `Track bundled recipe ids; mark helper-eligible apps`.

---

### Task 3: Helper protocol, allow-list, kill rule, request handler (pure)

**Files:**
- Create: `src/BgToggle.Core/Helper/HelperProtocol.cs`, `HelperAllowList.cs`, `KillRule.cs`, `HelperActions.cs` (interface only in this task), `HelperRequestHandler.cs`
- Test: `tests/BgToggle.Tests/HelperProtocolTests.cs`, `HelperRequestHandlerTests.cs`

**Interfaces:**
- Produces:

```csharp
public static class HelperProtocol
{
    public const int Version = 1;
    public const string PipeName = "BgToggle.Helper";
    public const string ServiceName = "BgToggleHelper";
    public const string Ping = "ping", StopServices = "stopServices",
                        StartServices = "startServices", KillProcesses = "killProcesses";
    public const int MaxMessageBytes = 64 * 1024;
}
public record HelperRequest(int Protocol, string Op, string? RecipeId = null);
public record HelperResponse(int Protocol, bool Ok, string? Error = null,
                             List<string>? Log = null, string? AppVersion = null);

/// One UTF-8 JSON object per line, camelCase.
public static class HelperWire
{
    public static void Write<T>(Stream s, T message);
    /// null on EOF; throws InvalidDataException on oversize (> MaxMessageBytes) or non-JSON.
    public static T? Read<T>(Stream s) where T : class;
}

public record AllowedRecipe(string Id, IReadOnlyList<string> ServiceNames, IReadOnlyList<string> ProcessNames);
public sealed class HelperAllowList
{
    public HelperAllowList(IEnumerable<Recipe> bundled);   // ServiceNames = serviceNames minus servicesToLeaveAlone (OrdinalIgnoreCase); ProcessNames via ExeNames.Strip
    public static HelperAllowList Empty { get; }
    public bool TryGet(string recipeId, out AllowedRecipe recipe); // case-insensitive id
}

public record RunningProcess(int Pid, int SessionId, string Name);
public static class KillRule
{
    /// (session == client && clientSession != 0 && nameListed) || (session == 0 && servicePids.Contains(pid))
    public static bool MayKill(RunningProcess p, int clientSessionId,
                               IReadOnlySet<string> listedNames, IReadOnlySet<int> recipeServicePids);
}

public interface IHelperActions
{
    void StopServices(IReadOnlyList<string> names, Action<string> log);
    void StartServices(IReadOnlyList<string> names, Action<string> log);
    int? ServiceProcessId(string serviceName);
    IReadOnlyList<RunningProcess> AllProcesses();
    void KillTree(int pid);                       // may throw
}

public sealed class HelperRequestHandler(HelperAllowList allow, IHelperActions actions, string appVersion)
{
    public HelperResponse Handle(HelperRequest request, int clientSessionId);
}
```

- **Handler rules:**
  - A protocol other than `Version` gets `Ok=false`, `Error="protocol mismatch: helper speaks 1"`.
  - `ping` gets `Ok=true` with `AppVersion` set.
  - An unknown op gets `Ok=false`, `Error="unknown op '<op>'"`.
  - A recipe op with a null or unknown id gets `Ok=false`, `Error="recipe '<id>' is not in the installed recipes"`.
  - `killProcesses` builds the candidate list and applies the session rule:
    - Candidates are `AllProcesses()`, filtered by `KillRule.MayKill`.
    - The service PIDs come from `ServiceProcessId` for each allowed service.
    - It calls `KillTree` per candidate. Each exception is caught and added to `Log` as `"PID <pid> (<name>): <message>"`.
    - `Ok` is true unless every kill failed.
  - Every response's `Log` lists what was done.

- [ ] **Step 1: Write the failing tests.** Use a `FakeActions : IHelperActions` that records calls and returns a scripted process list. Put it in `tests/BgToggle.Tests/HelperFakes.cs`; Tasks 4 and 6 add `FakeHelperClient` there as well.

```csharp
// HelperProtocolTests
[Fact] public void Wire_Roundtrip()                     // Write then Read on a MemoryStream → equal record
[Fact] public void Wire_Eof_ReturnsNull()
[Fact] public void Wire_Oversize_Throws()               // 70_000-char line → InvalidDataException
[Fact] public void Wire_NotJson_Throws()                // "hello\n" → InvalidDataException
[Fact] public void AllowList_RemovesLeaveAlone()        // services [A,B], leaveAlone [b] → [A]
[Fact] public void AllowList_UnknownId_False()
[Fact] public void KillRule_ClientSession_ListedName_Allowed()      // session 1, client 1, listed → true
[Fact] public void KillRule_ClientSession_UnlistedName_Denied()
[Fact] public void KillRule_OtherUserSession_Denied()               // session 2, client 1, listed → false
[Fact] public void KillRule_Session0_ServicePid_Allowed()           // session 0, pid in servicePids → true
[Fact] public void KillRule_Session0_ListedNameButNotServicePid_Denied()
[Fact] public void KillRule_ClientInSession0_OnlyServicePids()      // client 0, session 0, listed, pid not service → false

// HelperRequestHandlerTests
[Fact] public void ProtocolMismatch_Refused()           // Protocol 2 → !Ok, Error contains "protocol mismatch"
[Fact] public void Ping_ReturnsVersion()                // AppVersion == "1.2.3"
[Fact] public void UnknownOp_Refused()
[Fact] public void UnknownRecipe_Refused_NoActions()    // FakeActions recorded no calls
[Fact] public void StopServices_UsesAllowListNotRequest() // StopServices called with allow-list names (leave-alone removed)
[Fact] public void Kill_AppliesSessionRule()            // procs: (10,1,"razer"),(11,2,"razer"),(12,0,"RzSvc" pid=service),(13,0,"razer") client 1 → kills {10,12}
[Fact] public void Kill_OneFails_OthersStillKilled()    // KillTree(10) throws → 12 still killed, Ok, Log mentions "PID 10"
```

- [ ] **Step 2: Run the tests and confirm they fail.** Expected: compile errors.
- [ ] **Step 3: Implement the types with the exact signatures above.** For `HelperWire.Read`, read bytes up to `\n` (the cap is `MaxMessageBytes`) and deserialize with `JsonSerializerDefaults.Web`. Map `JsonException` to `InvalidDataException`.
- [ ] **Step 4: Run the build gate.** Expected: all tests pass.
- [ ] **Step 5: Commit.** Message: `Add helper protocol, allow-list, kill rule and request handler`.

---

### Task 4: Pipe server, real OS actions, pipe client

**Files:**
- Create: `src/BgToggle.Core/Helper/HelperServer.cs`, `HelperPipeSecurity.cs`, `HelperClient.cs`, `PipeNative.cs`; fill in `SystemHelperActions` in `HelperActions.cs`
- Test: `tests/BgToggle.Tests/HelperServerTests.cs`, `HelperClientTests.cs`

**Interfaces:**
- Consumes: Task 3 types, `RecipeFile.Load`, `ServiceManager`.
- Produces:

```csharp
public sealed class SystemHelperActions : IHelperActions { }   // ServiceManager.StopMany/StartMany/ProcessId; Process.GetProcesses (SessionId, ProcessName); Process.GetProcessById(pid).Kill(true)

public sealed class HelperServer
{
    /// recipesPath: reloaded when its LastWriteTimeUtc changes (checked on each ping).
    /// Missing/corrupt file → HelperAllowList.Empty + Logger.Error (ops refused, ping works).
    public HelperServer(string pipeName, string recipesPath, IHelperActions actions,
                        string appVersion, PipeSecurity? security);
    public Task RunAsync(CancellationToken ct);   // serial: one instance (MaxServerInstances 1, PipeOptions.FirstPipeInstance | Asynchronous), one request per connection
}

public static class HelperPipeSecurity
{
    public static PipeSecurity Create();   // SYSTEM + BUILTIN\Administrators: FullControl; Interactive (S-1-5-4): ReadWrite; nothing else
}

public enum HelperStatus { Running, NotInstalled, Incompatible }
public interface IHelperClient
{
    HelperStatus Status { get; }                         // cached; re-probed at most every 60 s after a failure
    HelperResponse? Send(string op, string? recipeId);   // null when unavailable/incompatible/timeout; never throws
}
public sealed class HelperClient : IHelperClient
{
    /// expectedServerPid: () => ServiceManager.ProcessId(HelperProtocol.ServiceName) in production;
    /// tests pass () => Environment.ProcessId.
    public HelperClient(string pipeName, Func<int?> expectedServerPid, int connectTimeoutMs = 500);
}
```

- **Server rules:**
  - Get the client PID with `GetNamedPipeClientProcessId` and its session with `ProcessIdToSessionId`. If that fails, refuse the request.
  - A request that fails to read (`InvalidDataException`, IO) is answered with `Ok=false`, `Error="bad request"` where possible, then the connection is closed and the loop continues.
- **Client rules:**
  - If the server PID (`GetNamedPipeServerProcessId`) differs from `expectedServerPid()`, return `null`, set the status to `NotInstalled` and log a warning.
  - If the response `Protocol` differs from `HelperProtocol.Version`, set the status to `Incompatible`.

- [ ] **Step 1: Write the failing tests.**
  - The server tests run `HelperServer` in-process on pipe `"BgToggle.Test." + Guid`.
  - They use the Task 3 `FakeActions` and a temp recipes file with one recipe, `vendor` (services `[VendorSvc]`).
  - `security: null`, apart from the one ACL test.

```csharp
// HelperServerTests
[Fact] public async Task Ping_RoundTrip()                              // client.Send("ping") → Ok, AppVersion
[Fact] public async Task StopServices_KnownRecipe_CallsActions()      // FakeActions.Stopped == ["VendorSvc"]
[Fact] public async Task UnknownRecipe_Refused()
[Fact] public async Task Garbage_ThenValidRequest_StillServed()       // raw NamedPipeClientStream writes "{{{\n"; next client ping Ok
[Fact] public async Task ClientDisconnect_ThenValidRequest_StillServed() // connect + dispose without writing; next ping Ok
[Fact] public async Task MissingRecipes_RefusesOps_PingWorks()        // recipesPath absent → stopServices !Ok, ping Ok
[Fact] public async Task RecipesFileChanged_ReloadedOnPing()          // rewrite file with recipe "other", ping, stopServices("other") Ok
[Fact] public async Task StrictAcl_CurrentUserCanConnect()            // security: HelperPipeSecurity.Create(); ping Ok (skip with message if not in interactive session: check Environment.UserInteractive)

// HelperClientTests
[Fact] public void Unavailable_ReturnsQuickly()                        // no server; Send returns null in < 1500 ms; Status == NotInstalled
[Fact] public async Task WrongServerPid_TreatedAsMissing()              // expectedServerPid: () => -1 → Send null, Status NotInstalled
[Fact] public async Task ProtocolMismatch_Incompatible()               // test server built with a handler stub answering Protocol 99 → Status Incompatible
```

  For `ProtocolMismatch_Incompatible`, add an `internal` constructor overload `HelperServer(string pipeName, Func<HelperRequest, int, HelperResponse> handle, PipeSecurity?)`, exposed with `InternalsVisibleTo("BgToggle.Tests")` on Core.

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement.** Create the server stream with `NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.FirstPipeInstance | PipeOptions.Asynchronous, 0, 0, security)`. Run blocking reads through `Task.Run` within `RunAsync`.
- [ ] **Step 4: Run the build gate.** Expected: all tests pass.
- [ ] **Step 5: Commit.** Message: `Add helper pipe server, client and system actions`.

---

### Task 5: `BgToggle.Helper` Windows service host

**Files:**
- Create: `src/BgToggle.Helper/BgToggle.Helper.csproj`, `Program.cs`, `HelperService.cs`
- Modify: `app_toggler.sln`

**Interfaces:**
- Consumes: `HelperServer`, `SystemHelperActions`, `HelperPipeSecurity`, `Logger`.
- Produces: an executable named `BgToggle.Helper.exe`.
  - `BgToggle.Helper.exe` with no arguments runs as a service (`ServiceBase.Run(new HelperService())`).
  - `BgToggle.Helper.exe --console` runs the same server in the foreground until Ctrl+C, for debugging.
  - Both set `Logger.LogDir = %ProgramData%\BgToggle\logs` and serve `HelperProtocol.PipeName` with `HelperPipeSecurity.Create()` and `recipes.json` from `AppContext.BaseDirectory`.

- [ ] **Step 1: Create the csproj.**
  - `net10.0-windows`, `OutputType Exe`, `AssemblyName BgToggle.Helper`, a reference to Core.
  - Link `..\..\recipes.json` with `CopyToOutputDirectory PreserveNewest`.
  - Add it to the solution.
- [ ] **Step 2: Implement `HelperService : ServiceBase`.**
  - `ServiceName = HelperProtocol.ServiceName`.
  - `OnStart` starts `RunAsync` with a `CancellationTokenSource`. `OnStop` cancels it and waits up to 5 s.
  - Log the start, the stop, and the number of recipes loaded.
- [ ] **Step 3: Run the build gate.** Expected: 0 warnings, all tests pass.
- [ ] **Step 4: Manual smoke test from an elevated PowerShell.**
  1. Run `src\BgToggle.Helper\bin\Debug\net10.0-windows\BgToggle.Helper.exe --console`.
  2. In a normal, non-elevated PowerShell, run `[System.IO.Pipes.NamedPipeClientStream]::new('.', 'BgToggle.Helper')` and connect. Write `{"protocol":1,"op":"ping"}` plus `\n` and read one line.

  Expected: `"ok":true`, and a log file in `%ProgramData%\BgToggle\logs`.
- [ ] **Step 5: Commit.** Message: `Add BgToggle.Helper service host`.

---

### Task 6: Tray uses the helper (services, kills, HKLM no-op, menu marker and status)

**Files:**
- Modify: `ProcessManager.cs`, `AutostartManager.cs`, `TrayApp.cs`
- Test: `tests/BgToggle.Tests/HelperFallbackTests.cs`

**Interfaces:**
- Consumes: `IHelperClient`, `HelperStatus`, `HelperProtocol`, and `ResolvedApp.RecipeId` and `HelperEligible`.
- Produces:
  - `public static IHelperClient? ProcessManager.Helper { get; set; }`.
  - `TrayApp` sets it to `new HelperClient(HelperProtocol.PipeName, () => ServiceManager.ProcessId(HelperProtocol.ServiceName))`.
- **Behavior, as in spec § Tray app changes:**
  - **Stopping services** (`StopServiceOnly`, `StopServiceThenKill`): if the app is eligible and `Helper?.Status == Running`, send `stopServices`. If that returns null or `!Ok`, fall back to `ServiceManager.StopMany`. Ineligible apps go straight to `ServiceManager`.
  - **Starting services** works the same way with `startServices`.
  - **After any `Stop`:** if the app is eligible, the helper is running and `IsRunning(app)` is still true, send `killProcesses`.
  - Write every helper `Log` line to the `log` callback.
  - `AutostartManager.Enable` and `Disable` with `AutostartMethod.RegistryRun` do nothing except `Logger.Warn($"{app.DisplayName}: machine-wide (HKLM) autostart is no longer managed; see README")`.
  - **Apps menu:** add the `(admin)` suffix only when `resolved.RequiresAdmin && ProcessManager.Helper?.Status != HelperStatus.Running`.
  - **System menu:** an (enabled) info item shows *Helper: running*, *Helper: not installed* or *Helper: incompatible version (reinstall)*. Its text is refreshed in the menu's `Opening` handler. Clicking it opens the README `#helper-service` URL.

- [ ] **Step 1: Write the failing tests.**
  - Put the class in `[Collection("ProcessManagerHelper")]` and declare `[CollectionDefinition("ProcessManagerHelper", DisableParallelization = true)]`.
  - Each test sets `ProcessManager.Helper` to a `FakeHelperClient` and resets it to `null` in `Dispose`.

```csharp
[Fact] public void Eligible_HelperRunning_StopUsesHelper()   // StopServiceOnly app, services ["bgtoggle-test-no-such-service"], eligible → fake got ("stopServices","vendor")
[Fact] public void Eligible_HelperMissing_NoHelperCall()     // Status NotInstalled → fake.Calls empty
[Fact] public void NotEligible_NoHelperCall()                // HelperEligible false, Status Running → fake.Calls empty
[Fact] public void Start_Eligible_UsesHelperStartServices()  // ProcessManager.Start → ("startServices","vendor")
[Fact] public void Stop_StillRunning_AsksHelperToKill()      // eligible StopServiceOnly app, services ["RpcSs"] (always running; fake's stopServices does nothing) → IsRunning stays true → fake got ("killProcesses","vendor")
[Fact] public void Stop_Stopped_NoKillRequest()             // eligible StopServiceOnly app, services ["bgtoggle-test-no-such-service"] → not running after stop → no "killProcesses"
[Fact] public void HklmAutostart_IsNoOp_NoThrow()            // AutostartManager.Disable(app with RegistryRun, key "bgtoggle-test-x") does not throw
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement the behavior described above.** Add `private static bool TryHelper(ResolvedApp app, string op, Action<string>? log)` to `ProcessManager`.
- [ ] **Step 4: Run the build gate.** Expected: all tests pass.
- [ ] **Step 5: Commit.** Message: `Route service and stubborn-process operations through the helper`.

---

### Task 7: Tray never elevated; own autostart; setup and uninstall arguments

**Files:**
- Create: `TrayAutostart.cs`, `StartupArgs.cs`, `Elevation.cs`
- Modify: `AutostartManager.cs` (`RestoreAll`), `Program.cs`, `TrayApp.cs`
- Delete: `AutostartTask.cs`
- Test: `tests/BgToggle.Tests/StartupArgsTests.cs`, `TrayAutostartTests.cs`, `RestoreAllTests.cs`

**Interfaces:**
- Produces:

```csharp
public record StartupArgs(bool FirstRun, bool? StartAtLogon, bool ExitAfterSetup, bool UninstallCleanup)
{
    public static StartupArgs Parse(string[] args);   // flags: --first-run, --start-at-logon=on|off, --exit-after-setup, --uninstall-cleanup; unknown args ignored
}
public static class TrayAutostart
{
    public const string ValueName = "BgToggle";
    public static bool IsEnabled() / void Set(bool enabled, string exePath);   // HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    internal static bool IsEnabled(string runKeyPath) / void Set(bool enabled, string exePath, string runKeyPath); // tests
}
public static class Elevation { public static bool IsElevated(); } // WindowsPrincipal(...).IsInRole(Administrator)
// AutostartManager
public static int RestoreAllForCurrentUser();   // HKCU stash → HKCU Run; Startup *.bgtoggle.disabled → original name
internal static int RestoreAll(RegistryKey root, string runKeyPath, string stashKeyPath, string? startupDir); // returns count restored
```

- **Program.Main order:**
  1. Parse the args.
  2. If `UninstallCleanup`:
     - Run `AutostartManager.RestoreAllForCurrentUser()`.
     - Run the HKLM restore (`RestoreAll(Registry.LocalMachine, Run, Stash, null)`, catching errors).
     - Run `TrayAutostart.Set(false, …)`.
     - Exit 0.
  3. If `FirstRun && StartAtLogon is bool b`, run `TrayAutostart.Set(b, Application.ExecutablePath)`.
  4. If `ExitAfterSetup`, exit 0.
  5. Then take the mutex. Catch `UnauthorizedAccessException` from `new Mutex(...)` and treat it as "already running".
- **TrayApp:**
  - If `Elevation.IsElevated()`, log a warning and show the balloon from the spec once: "BgToggle is running as administrator; apps it starts will be too."
  - System menu:
    - Remove *Install/Reinstall/Remove autostart (admin)*.
    - Add a checkable *Start BgToggle at logon* (`TrayAutostart`).
    - Add *Restore all autostart entries*, which calls `RestoreAllForCurrentUser` and shows a balloon "Restored N entries".
    - Keep *Open logs folder*.

- [ ] **Step 1: Write the failing tests.**
  - Registry tests use `HKCU\Software\BgToggleTest\<guid>\Run` and `...\Stash`, and a temp directory as the Startup folder.
  - Delete the test key tree in `Dispose`.

```csharp
[Fact] public void Parse_Setup()       // ["--first-run","--start-at-logon=off","--exit-after-setup"] → (true,false,true,false)
[Fact] public void Parse_Empty()       // [] → (false,null,false,false)
[Fact] public void Parse_BadLogonValue_Null() // "--start-at-logon=maybe" → StartAtLogon null
[Fact] public void TrayAutostart_SetOnOff()    // Set(true,@"C:\x\BgToggle.exe",key) → value == "\"C:\\x\\BgToggle.exe\""; Set(false,…) → IsEnabled false
[Fact] public void RestoreAll_MovesStashToRun_AndRenamesStartupFiles() // stash {A="a.exe"}, startup "foo.lnk.bgtoggle.disabled" → Run has A, stash empty, "foo.lnk" exists, returns 2
[Fact] public void RestoreAll_ExistingRunValue_NotOverwritten()        // Run already has A="new" → stays "new", stash value removed, counted 0
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement. Delete `AutostartTask.cs` and its menu wiring.** Add `<InternalsVisibleTo Include="BgToggle.Tests" />` to `BgToggle.csproj` for the `internal` test overloads.
- [ ] **Step 4: Run the build gate.** Expected: all tests pass.
- [ ] **Step 5: Manual check.**
  1. Build, then run `bin\Debug\net10.0-windows\BgToggle.exe --first-run --start-at-logon=on --exit-after-setup`.
  2. `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v BgToggle` shows the path.
  3. Run it again with `=off`; the value is gone.
- [ ] **Step 6: Commit.** Message: `Never run elevated: own HKCU autostart, setup/uninstall args, restore-all`.

---

### Task 8: Update check

**Files:**
- Create: `UpdateCheck.cs`, `AppState.cs`
- Modify: `Models.cs` (`Config`), `TrayApp.cs`
- Test: `tests/BgToggle.Tests/UpdateCheckTests.cs`

**Interfaces:**
- Produces:

```csharp
public record LatestRelease(string TagName, string HtmlUrl, bool Prerelease, bool Draft);
public static class UpdateCheck
{
    public static LatestRelease? Parse(string json);                 // null on garbage/missing fields
    public static Version? ParseTag(string tag);                     // "v0.3.0"/"0.3.0" → 0.3.0; "v0.3.0-beta", "release-1" → null
    public static bool IsNewer(LatestRelease r, Version current);    // false if Draft/Prerelease/ParseTag null/current == 0.0.0(.0)
    public static bool ShouldCheck(DateTime? lastUtc, DateTime nowUtc); // null or ≥ 24 h ago
}
public record AppState(DateTime? LastUpdateCheckUtc = null, string? LastNotifiedVersion = null);
public static class AppStateStore { public static AppState Load(); public static void Save(AppState s); } // %APPDATA%\BgToggle\state.json; Load never throws (bad file → new AppState())
// Config gains: bool CheckForUpdates = true
```

- **TrayApp wiring:**
  - Run a WinForms timer: the first tick 30 s after start, then hourly.
  - On each tick, check only if `CheckForUpdates && ShouldCheck(state.LastUpdateCheckUtc, now)`.
  - The check runs on `Task.Run` with an `HttpClient` that has a 10 s timeout and `User-Agent: BgToggle/<version>`.
  - Save `LastUpdateCheckUtc` after any completed attempt, including failures, so a bad network doesn't cause retries every hour.
  - If `IsNewer(release, current)`:
    - Add a top-level menu item `Update available: {tag}…` that opens `HtmlUrl`.
    - If `state.LastNotifiedVersion != tag`, show one balloon (clicking it opens `HtmlUrl`) and save the tag.
  - Get the current version from `typeof(Program).Assembly.GetName().Version`.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void Parse_ValidJson()            // {"tag_name":"v0.3.0","html_url":"https://x","prerelease":false,"draft":false}
[Fact] public void Parse_Garbage_Null()         // "<html>rate limited</html>" → null
[Fact] public void ParseTag_Variants()          // "v0.3.0"→0.3.0, "0.3.0"→0.3.0, "v0.3.0-beta"→null, "release-1"→null
[Fact] public void IsNewer_Newer_True()         // tag v0.3.0, current 0.2.0 → true
[Fact] public void IsNewer_SameOrOlder_False()  // v0.2.0 vs 0.2.0; v0.1.0 vs 0.2.0 → false
[Fact] public void IsNewer_DevBuild_False()     // current 0.0.0.0 → false
[Fact] public void IsNewer_Prerelease_False()
[Fact] public void ShouldCheck_Windows()        // null → true; 23 h ago → false; 25 h ago → true
[Fact] public void Config_DefaultsCheckForUpdatesTrue() // JSON without the field → CheckForUpdates true (via ConfigStore.Load on temp path)
```

- [ ] **Step 2: Run the tests and confirm they fail.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the build gate.** Expected: all tests pass.
- [ ] **Step 5: Commit.** Message: `Tell the user when a newer GitHub release exists`.

---

### Task 9: Installer, release workflow, README

**Files:**
- Create: `installer/BgToggle.iss`, `.github/workflows/release.yml`
- Modify: `README.md`, `.gitignore` (add `publish/` and `installer/Output/`)

**Interfaces:**
- Consumes: `BgToggle.exe` and its flags (Task 7), `BgToggle.Helper.exe` (Task 5).
- Produces: `installer/Output/BgToggle-Setup-<version>.exe`.

**Publish command** (the same locally and in CI; both into one folder, so the runtime is shared):

```
dotnet publish BgToggle.csproj -c Release -r win-x64 --self-contained -p:Version=$V -o publish
dotnet publish src/BgToggle.Helper/BgToggle.Helper.csproj -c Release -r win-x64 --self-contained -p:Version=$V -o publish
iscc /DAppVersion=$V installer\BgToggle.iss
```

**`BgToggle.iss` must contain:**
- `[Setup]`:
  - A fixed `AppId` (generate one GUID and keep it forever). `AppName=BgToggle`, `AppVersion={#AppVersion}`.
  - `DefaultDirName={autopf}\BgToggle`, `PrivilegesRequired=admin`.
  - `ArchitecturesAllowed=x64compatible`, `ArchitecturesInstallIn64BitMode=x64compatible`.
  - `CloseApplications=yes`.
  - `OutputBaseFilename=BgToggle-Setup-{#AppVersion}`, `SourceDir=..`, `OutputDir=installer\Output`.
  - `UninstallDisplayIcon={app}\BgToggle.exe`.
- `[Files]`: `publish\*` → `{app}`, recursive. Plus `LICENSE` and `README.md`.
- `[Tasks]`: `startatlogon`, "Start BgToggle when I sign in", checked.
- `[Code]`:
  - `PrepareToInstall` runs `sc.exe stop BgToggleHelper` (ignore the result) and waits up to 10 s for it to stop.
  - `CurStepChanged(ssPostInstall)` runs, all hidden with results logged:
    1. `sc.exe create BgToggleHelper binPath= "\"{app}\BgToggle.Helper.exe\"" start= delayed-auto obj= LocalSystem DisplayName= "BgToggle Helper"`. If that fails because the service exists, run `sc.exe config` with the same arguments instead.
    2. `sc.exe description BgToggleHelper "Lets BgToggle stop and start vendor services listed in its installed recipes."`
    3. `sc.exe failure BgToggleHelper reset= 86400 actions= restart/5000/restart/5000/restart/60000`
    4. `sc.exe start BgToggleHelper`
    5. `schtasks.exe /Delete /TN BgToggle /F`
- `[Run]`:
  - `{app}\BgToggle.exe`, Parameters `--first-run --start-at-logon={code:LogonFlag} --exit-after-setup`, Flags `runasoriginaluser runhidden waituntilterminated`.
  - `{app}\BgToggle.exe`, Description "Launch BgToggle", Flags `runasoriginaluser nowait postinstall skipifsilent`.
- `[UninstallRun]`, in this order, all `runhidden waituntilterminated`:
  1. `taskkill.exe /IM BgToggle.exe /F`
  2. `{app}\BgToggle.exe --uninstall-cleanup`
  3. `sc.exe stop BgToggleHelper`
  4. `sc.exe delete BgToggleHelper`

  Give each a unique `RunOnceId`.

**`release.yml`:**
- Trigger `on: push: tags: ['v*']`, `permissions: contents: write`, `runs-on: windows-latest`.
- Steps:
  1. Checkout and `setup-dotnet` `10.0.x`.
  2. Set `V` from `${GITHUB_REF_NAME#v}`.
  3. Run `dotnet test` (Release).
  4. Publish both apps (commands above).
  5. `choco install innosetup -y --no-progress`, then `iscc`.
  6. `gh release create $GITHUB_REF_NAME installer/Output/BgToggle-Setup-$V.exe --generate-notes` with `GH_TOKEN: ${{ github.token }}`.
- Fail the job if the installer is larger than 150 MB (a size check step).

**README:**
- **Install:** a section with a link to Releases. Mention the SmartScreen "unknown publisher" warning.
- **`## Helper service`** (anchor `helper-service`): what the helper does, that it only acts on installed recipes, that local recipe overrides don't use it, and how to check it (`sc query BgToggleHelper`).
- **Standard accounts:** use *System → Restore all autostart entries* before uninstalling.
- **Machine-wide autostart:** `autostart: registryRun` is no longer managed.
- **"What's still stubbed":** remove the items that are done (hotkeys, scheduled tasks, elevation, logging and confirm dialog, which are listed twice) and keep the rest.
- **Project layout:** update it to include `src/BgToggle.Core` and `src/BgToggle.Helper`.

- [ ] **Step 1: Write `BgToggle.iss`, `release.yml`, and the README and `.gitignore` changes.**
- [ ] **Step 2: Build the installer locally.**
  1. `winget install JRSoftware.InnoSetup` if it isn't present.
  2. Run the publish commands with `V=0.0.1`, then `iscc`.

  Expected: `installer\Output\BgToggle-Setup-0.0.1.exe` exists and is under 150 MB. Report the size.
- [ ] **Step 3: Manual install matrix on this machine** (admin account; the standard-account rows are noted for the owner):
  - **Install:**
    - UAC appears once.
    - `sc query BgToggleHelper` shows RUNNING.
    - The HKCU Run value `BgToggle` exists.
    - The tray starts. Task Manager → Details → Elevated says **No** for BgToggle.exe and for any app it launches.
    - The System menu shows *Helper: running*.
  - **Service recipe:** toggle a service recipe app (e.g. Adobe Creative Cloud, from the owner's config) off and on. Its services stop and start. The tray log shows the helper's log lines.
  - **Upgrade:** install `0.0.2` over `0.0.1`. The service is restarted and the tray is closed and relaunched.
  - **Uninstall:** the service is gone (`sc query` fails), the install folder is gone, and the HKCU Run value is gone. `%APPDATA%\BgToggle` remains.
- [ ] **Step 4: Commit.** Message: `Add Inno Setup installer, release workflow, README install docs`.

---

## Self-review notes

- **Spec coverage:**
  - Tray never elevated, with a warning: Task 7.
  - Helper ops, ids only, and its own `recipes.json`: Tasks 3–5.
  - Session rule including session 0: Task 3.
  - Pipe access list and first instance: Task 4.
  - Server PID check: Task 4.
  - Protocol version, and dev builds using the helper: Tasks 3, 4 and 6 (the client doesn't compare app versions).
  - Recipe reload on ping: Task 4.
  - Eligibility (local override excluded): Tasks 2 and 6.
  - HKLM no-op: Task 6.
  - Menu `(admin)` marker and status line: Task 6.
  - Restore-all and the tray's HKCU autostart: Task 7.
  - Mutex fix: Task 7.
  - `AutostartTask` removed: Task 7.
  - Update check: Task 8.
  - Installer and the legacy task removal: Task 9.
  - Release workflow and shared-runtime publish: Task 9.
  - README: Task 9.
  - Upgrade in place (Restart Manager via `CloseApplications`, service stop in `PrepareToInstall`): Task 9.
- **Not automated, and checked manually instead:** real service install and uninstall, UAC, upgrade, and the standard-account rows of the spec's matrix (Task 9, Step 3).
