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

### Addons that aren't on GitHub (CurseForge etc.)

1. Paste the addon's page link, e.g. `https://www.curseforge.com/wow/addons/dungeonjournal`, and press Enter.
2. Right-click it and choose **Check page for updates / download…**. Its page opens in your browser.
3. Click **Download** on the page. As soon as the zip lands in your Downloads folder, the app installs it.

The app can't see when these addons get a new version, so check their page now and then. You can also install a zip you already have: use **Add addons ▾ → Install from a zip file…**, or drag the zip onto the window.

Right-click any addon for more: update or reinstall it, open its page or folder, or remove it from the list.

Good to know:
- A GitHub addon only works here if its author attaches a `.zip` to their GitHub releases. Most popular addons do.
- Your addon settings (in WTF\SavedVariables) aren't touched.
- If an update fails partway, the old version is put back.
- Your list is saved in `%AppData%\WoWAddonUpdater\WoWAddonUpdater.json` (Settings shows where, with an "Open folder" button).
- The app updates itself too: when there's a new version, a yellow bar offers it.

## For developers

Requires the .NET 10 SDK.

- `.\build.ps1` builds both versions into `dist\` (standalone, and a small "lite" exe that needs the .NET 10 Desktop Runtime).
- `.\release.ps1 -Version 1.2.3 -Notes "What changed"` bumps the version, builds, commits, tags, pushes and publishes the GitHub release.
