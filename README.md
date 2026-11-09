# WoW Addon Updater

A small Windows app that keeps your World of Warcraft addons up to date from the authors' GitHub releases. One window, no background services, no account.

Made for **WoW Forever**, but it also works for Retail and the Classic versions (choose in Settings).

## Getting it

Download **WoWAddonUpdater.exe** from the [latest release](https://github.com/ian-eden/WoWAddonUpdater/releases/latest) and put it in any folder. That's it; nothing to install.

Windows may say *"Windows protected your PC"* the first time, because the app isn't signed. Click **More info → Run anyway**.

## Using it

1. Open the app. It finds your AddOns folder by itself (or set it in **Settings**).
2. Find the addon's GitHub page. CurseForge pages usually link to it under "Source".
3. Paste the link, e.g. `https://github.com/Xuerian/XLoot`, and press Enter.
4. Click **Update all**.

After that, open the app now and then: it checks for updates when it starts, and **Update all** installs only what changed. Type `/reload` in game (or restart WoW) to load them.

Good to know:
- An addon only works here if its author attaches a `.zip` to their GitHub releases. Most popular addons do.
- Your addon settings (in WTF\SavedVariables) aren't touched.
- If an update fails partway, the old version is put back.
- Your list is saved in `WoWAddonUpdater.json` next to the exe. Keep the two together.
- The app updates itself too: when there's a new version, a yellow bar offers it.

## For developers

Requires the .NET 10 SDK.

- `.\build.ps1` builds both versions into `dist\` (standalone, and a small "lite" exe that needs the .NET 10 Desktop Runtime).
- `.\release.ps1 -Version 1.2.3 -Notes "What changed"` bumps the version, builds, commits, tags, pushes and publishes the GitHub release.
