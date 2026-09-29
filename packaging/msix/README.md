# Microsoft Store package (MSIX)

Work in progress: a first package to try on a PC, before anything is submitted to the Store.

- `AppxManifest.xml`: the template; `tools/build-msix.ps1` fills it in and packs it.
- Run **MSIX (Store package)** from the Actions tab (or `./tools/build-msix.ps1 -Sign` on Windows with the
  Windows SDK). Without an identity it builds a test package signed with a throw-away certificate, and prints
  how to install it.

## To check on a real PC, in the packaged app

- The Windows taskbar is hidden and restored (also with Ctrl+Alt+Shift+B and after a crash: the guard process).
- The dock, the menu bar and the tray icons work.
- Start with Windows: in a package the `Run` key is virtualized and Windows never reads it, so the manifest
  has a `windows.startupTask` (on by default) and `AutoStart` does nothing when packaged (`AppPackage.IsPackaged`).
  Windows starts it at the next sign-in, after the app has been opened once. The "Start with Windows" setting
  does not switch it yet (that needs the `StartupTask` API); the user can turn it off in Settings > Apps > Startup.
- Uninstalling the package does not run `--uninstall`; the guard restores the taskbar when the dock is stopped.

## For the Store submission

- `Identity Name`, `Publisher` and `PublisherDisplayName` come from Partner Center (Product management >
  Product identity) and are built into `tools/build-msix.ps1 -Store` (the workflow's "store" input).
  The Store signs the package, so it is not signed here.
- `runFullTrust` is a restricted capability: Partner Center asks why the app needs it.
- The package is self-contained (the Store package cannot rely on the .NET runtime being installed).
- Certification policy 10.2 (security, and consent before changing the user's Windows experience) is the
  likeliest objection: the listing should say plainly that the app replaces the Windows taskbar.
