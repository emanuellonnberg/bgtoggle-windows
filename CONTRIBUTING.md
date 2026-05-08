# Contributing

Thanks for considering a contribution. The most valuable thing this project can grow is its **recipe library** — the per-app shutdown knowledge in [`recipes.json`](recipes.json). A bad recipe wastes a user's day; a good one saves it. This guide focuses on getting recipes right.

## Recipe contributions

A recipe captures "how to actually shut down this app." See [docs/RECIPES.md](docs/RECIPES.md) for the full schema. Quick rules:

1. **Identify the bucket first.** Every tray app falls into one of:
   - **graceful-cooperator** — honors WM_CLOSE → `"shutdown": "graceful"`
   - **tray-trap** — hides on WM_CLOSE → `"shutdown": "killTree"`
   - **command-driven** — has a documented CLI shutdown (OneDrive `/shutdown`, Steam `-shutdown`) → `"shutdown": "command"`
   - **service-watchdog** — UI plus a service that respawns it (Razer, Adobe, Logitech G HUB) → `"shutdown": "stopServiceThenKill"`
   - **service-only** — no UI process → `"shutdown": "stopServiceOnly"`

   If you can't tell, run the app, close it via the X button, and check Task Manager. If processes are still running after a few seconds, it's tray-trap or has a service. If a service respawns the UI within ~10s, it's a service-watchdog.

2. **List _all_ relevant process names.** Helpers (Discord's helper, Steam's `steamwebhelper`, Adobe's `node`/`CCXProcess`/etc.) belong in `processNames`. Run the app and look at Task Manager → Details, sorted by name.

3. **Mark driver-tied services in `servicesToLeaveAlone`.** This is the most important field for vendor utilities. NVIDIA's `NvContainerLocalSystem`, AMD's `AMD External Events Utility`, ASUS's `atkexComSvc` — these support the display driver / motherboard hardware. Stopping them is hostile. List them so future recipe authors don't accidentally promote them to `serviceNames`.

4. **Use `%ENVVAR%` paths, not absolute ones.** `%LOCALAPPDATA%`, `%APPDATA%`, `%PROGRAMFILES%`, `%PROGRAMFILES(X86)%`. The launcher expands them at load time.

5. **Set `requiresAdmin: true` whenever `serviceNames` is non-empty.** The UI uses this to surface an "(admin)" hint.

6. **Use `notes` to call out non-obvious behavior** — e.g. "force-kill corrupts the download manifest", "stop service drops the VPN tunnel", "tray-trap (hides on WM_CLOSE)". Notes show up as menu tooltips.

### Submitting a recipe

1. Add or edit the recipe block in `recipes.json`. Keep entries roughly alphabetical by `id` within their grouping.
2. Test locally: `dotnet build && dotnet run`, click **Scan running processes…**, verify your recipe is detected and that **Apply Profile** with your app excluded actually stops it cleanly (and doesn't kill anything driver-tied).
3. Open a PR. Include in the description:
   - **App version + Windows version** you tested on
   - **Bucket** you placed it in and why
   - **What got killed and what stayed running** (Task Manager screenshot or process list)
   - **Any side effects** (browser session lost, VPN dropped, sync interrupted, etc.)

If a recipe is wrong on someone's machine (different install path, vendor changed service names in an update, app went tray-trap when it used to be graceful), open an issue with the same info — don't overwrite without context.

## Code contributions

- Build: `dotnet build`
- Target: .NET 8, WinForms, Windows-only.
- No external dependencies beyond `System.ServiceProcess.ServiceController`.
- Keep PRs scoped: one feature or one bug per PR. Recipe additions can batch.
- No test suite yet. Manual smoke test against your own tray apps before opening a PR.

## Code of conduct

Be civil, be specific, don't open issues like "doesn't work". If a recipe behaves wrong on your machine, paste the process list and what happened, not your feelings about Razer.
