<!-- Thanks for the PR. For recipe changes please fill in the fields below. -->

## Summary

<!-- One or two sentences. -->

## Recipe changes (delete if N/A)

- **App + version tested:**
- **Windows version:**
- **Bucket / `shutdown` strategy:** <!-- graceful / killTree / command / stopServiceOnly / stopServiceThenKill -->
- **Process list before / after apply:**
  - Before:
  - After:
- **Side effects observed:** <!-- driver flicker, sync interrupted, VPN dropped, etc. -->

## Test plan

- [ ] `dotnet build` is clean (CI will also enforce this)
- [ ] Smoke-tested locally: scan detects the app, applying a profile that excludes it actually stops it without taking down driver-tied services
