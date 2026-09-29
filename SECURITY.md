# Security policy

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please **do not open a public issue**. Use GitHub's private reporting instead:
[Report a vulnerability](https://github.com/lupanostefano/Gravitone/security/advisories/new).

Include what an attacker could do, the steps to reproduce and the version. You will get an answer within a week; a fix
for a confirmed problem is released as soon as it is ready, and you are credited unless you prefer otherwise.

## What Gravitone does on your PC

So you can judge the risk for yourself:

- It runs as your user, never elevated. The installer installs for your account only.
- It makes **no network connections**. The only download is the installer's optional fetch of the .NET 9 Desktop
  Runtime from Microsoft (`aka.ms`), when it is missing.
- It writes to `%APPDATA%\Gravitone` (settings and log), to `HKCU\...\Run`, `HKCU\...\RunOnce` and a scheduled task
  (start with Windows, taskbar safety net), and to Explorer's taskbar auto-hide state, which it restores.
- It installs a low-level keyboard hook only for Win + 1 … 9 (setting `NumberShortcuts`) and never records keys.
