# Microsoft Store package (MSIX)

Work in progress: a first package to try on a PC, before anything is submitted to the Store.

- `AppxManifest.xml`: the template; `tools/build-msix.ps1` fills it in and packs it.
- Run **MSIX (Store package)** from the Actions tab (or `./tools/build-msix.ps1 -Sign` on Windows with the
  Windows SDK). Without an identity it builds a test package signed with a throw-away certificate, and prints
  how to install it.

## To check on a real PC, in the packaged app

- The Windows taskbar is hidden and restored (also with Ctrl+Alt+Shift+B and after a crash: the guard process).
- The dock, the menu bar and the tray icons work.
- Start with Windows: `AutoStart` writes the `Run` key and a scheduled task; in a package the registry is
  virtualized and Windows never reads that key at sign-in, so this needs a `windows.startupTask` in the manifest
  and the `StartupTask` API before a Store release.
- Uninstalling the package does not run `--uninstall`; the guard restores the taskbar when the dock is stopped.

## For the Store submission

- `Identity Name`, `Publisher` and `PublisherDisplayName` come from Partner Center (Product management >
  Product identity); pass them to the workflow or the script. The Store signs the package.
- `runFullTrust` is a restricted capability: Partner Center asks why the app needs it.
- The package is self-contained (the Store package cannot rely on the .NET runtime being installed).
- Certification policy 10.2 (security, and consent before changing the user's Windows experience) is the
  likeliest objection: the listing should say plainly that the app replaces the Windows taskbar.
