# WoW Addon Updater: Project Notes

A small Windows app (C#, WinForms, .NET 10) that keeps World of Warcraft addons up to date from the authors' **GitHub releases**. Built as a lightweight alternative to the CurseForge app. Made for **WoW Forever** (interface `16001`, folder `_forever_`), but it supports the other game versions through Settings.

- **Repository (public):** https://github.com/ian-eden/WoWAddonUpdater
- **Latest release:** v0.2.1 (2026-11-09)
- **Shared with:** a few friends, by link only. It isn't advertised anywhere.

*Last updated: 2026-11-09*

---

## Folder layout

```
D:\GitHub\WoWAddonUpdater\
├── PROJECT_NOTES.md        ← this file
├── README.md               ← what friends see on the GitHub page
├── WoWAddonUpdater.csproj  ← <Version> is set by release.ps1
├── catalog.json            ← known addons (folder name → GitHub repo); also downloaded live from GitHub
├── build.ps1               ← builds both exes into dist\
├── release.ps1             ← version bump + build + commit + tag + push + GitHub release
├── Program.cs              ← entry point
├── MainForm.cs             ← main window: list, buttons, log, app-update bar, find-addons flow
├── SettingsForm.cs         ← AddOns folder, game version, GitHub token, where the list is stored
├── FindAddonsForm.cs       ← "Addons found" tick-list dialog
├── GitHubClient.cs         ← GitHub API: pick the right release/zip for the flavor, downloads
├── Installer.cs            ← safe extract + folder swap with rollback; .toc helpers
├── AddonScanner.cs         ← scans AddOns against the catalog (+ GitHub links in .toc files)
├── AppUpdater.cs           ← the app updating itself from its own releases
├── AppInfo.cs              ← version, repo owner/name, which release file this build uses
├── ConfigStore.cs          ← settings file location, loading/saving, finding the WoW install
├── Models.cs               ← settings, tracked addons, GitHub and release.json shapes
├── publish\                ← Ian's own running copy (lite exe + WoWAddonUpdater.json, portable mode). Not in git.
└── dist\                   ← output of build.ps1. Not in git.
```

---

## How it works

### Picking the right download for an addon
1. Fetch the repo's latest 15 releases from the GitHub API (skips drafts; skips pre-releases unless the addon has "Include pre-releases" ticked).
2. If a release has a **`release.json`** (written by the BigWigs packager, which most addons use), use the zip it lists for the configured flavor (`forever`). This confirms the zip supports WoW Forever.
3. Otherwise guess from the zip names (prefer one naming the flavor, avoid ones naming other flavors). The status then says "(not confirmed for WoW Forever)", **unless** the installed `.toc` declares support: a `*_Forever.toc` file, or `## Interface:` containing a known Forever number. Known numbers are learned from other addons' release.json files and saved in `KnownInterfaces` (seeded with 16001).
4. "Up to date" means the release tag + zip name equal what this app last installed (`InstalledTag`, `InstalledAsset`). Addons it hasn't installed yet show "Not installed by this app yet", and the first **Update all** takes them over.

### Installing
- Download into `Interface\.WoWAddonUpdater-work\<job>\` (next to AddOns, same drive, so folder moves are instant). WoW ignores it. It's deleted afterwards.
- Extract with zip-slip protection. Only top-level folders containing a `.toc` count as addons. One wrapper folder is stepped into if needed.
- Swap: move the old folders (this version's folders + ones from the last install) into a backup, move the new ones in, and **roll back** if anything fails. Folders the new version dropped are removed.
- **Junctions/links are never replaced.** Ian's `AddOns\PyroLooter` is a junction to his dev copy (see the PyroLooter notes). Installs refuse to touch a linked folder, and the scanner skips them.
- SavedVariables (in `WTF\`) are never touched.

### Finding installed addons
- `catalog.json` maps main folder names to repos. The app downloads it from `raw.githubusercontent.com/ian-eden/WoWAddonUpdater/main/catalog.json`, with the built-in copy as a fallback. **Adding an addon is just a push to main; no release is needed.** After a push, raw.githubusercontent can take about a minute to serve the new file.
- Module folders that start with a catalog folder name (XLoot_Frame, EllesmereUIBags, ...) count as part of it.
- GitHub links in `.toc` `## X-...` lines are offered unticked. In practice almost no addons have one (only Atlas had a link, and it points to CurseForge).
- Runs automatically when the addon list is empty. Otherwise it's the "Find installed addons…" button.

### Finding the AddOns folder
In order: (1) the paths in Battle.net's `C:\ProgramData\Battle.net\Agent\product.db` (binary, but the paths are plain text; this is how Ian's `D:/Games/World of Warcraft` is found), (2) the registry `HKLM\SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft\InstallPath` (missing on Ian's PC), (3) common folders on every drive, starting with `Program Files (x86)\World of Warcraft`.

### Where settings are stored
- Default: `%AppData%\WoWAddonUpdater\WoWAddonUpdater.json`.
- **Portable mode:** if `WoWAddonUpdater.json` exists next to the exe, that one is used. Ian's `publish\` copy runs this way. Anyone who updated from 0.1.x also stays in portable mode, so their list wasn't lost.
- An optional GitHub token is stored there in plain text. Without one: 60 API requests/hour per IP, about one per addon per check.

### App self-update
- At startup: `GET /repos/ian-eden/WoWAddonUpdater/releases/latest`. If its tag is newer than the running version, a yellow bar offers it.
- Two release files: **`WoWAddonUpdater.exe`** (standalone/self-contained, ~49 MB, for friends) and **`WoWAddonUpdater-lite.exe`** (~0.2 MB, needs the .NET 10 Desktop Runtime, Ian's copy). Each build updates from its own file: `SELF_CONTAINED` is defined in the csproj when `SelfContained=true`, which sets `AppInfo.AssetName`.
- Update = download to `<exe>.new`, check size + GitHub's sha256 `digest` + the `MZ` header, rename the running exe to `.old`, move the new one into place, start it, close. The next start deletes `.old`. If Windows won't allow the rename (e.g. exe in Program Files), it offers to open the releases page instead.
- **Tested for real** (0.1.0 → 0.1.1) on both builds by clicking "Update now" through UI Automation.

---

## Workflow

### Tools on this PC
- .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`), Git (`Git.Git`), GitHub CLI (`GitHub.cli`), all installed with winget.
- `gh` is logged in as **ian-eden** (HTTPS, git credentials included).
- Repo git identity: `ian-eden` / `59524351+ian-eden@users.noreply.github.com` (the noreply address keeps the real email private). `core.safecrlf=false` is set to silence the harmless LF/CRLF warnings.
- A PowerShell window opened before an install won't find the new tools. Open a new one, or refresh `$env:Path`.

### Build and release
- `.\build.ps1` builds both exes into `dist\`.
- `.\release.ps1 -Version X.Y.Z -Notes "..."` does: set `<Version>` → build → `git add -A` + commit → tag `vX.Y.Z` → push → `gh release create` with both exes. The notes are passed to `gh` through a temp file.
- Run the scripts **in the same PowerShell session** (`Set-ExecutionPolicy -Scope Process Bypass; & .\release.ps1 ...`) rather than `powershell -File ...`. Multi-line notes with quote marks don't survive being passed to a new process.
- Version numbers must always go up; the script refuses to reuse a tag. Fixes → 0.2.1, features → 0.3.0.
- After a release, Ian's `publish\` copy updates itself through the yellow bar (or copy `dist\WoWAddonUpdater-lite.exe` over `publish\WoWAddonUpdater.exe` while it's closed).
- `release.ps1` commits **everything** (`git add -A`). `.gitignore` excludes bin/obj/publish/dist and `WoWAddonUpdater.json*`.

### Testing approach (nothing permanent in the repo)
- Logic tests: a throwaway console project that compiles the non-UI source files (`Models`, `GitHubClient`, `Installer`, `ConfigStore`, `AppInfo`, `AddonScanner`, plus `catalog.json` as an embedded resource) and installs into a **fake AddOns folder in a temp directory**, including a junction to prove links are left alone. Only read the real AddOns folder; never install into it during testing.
- UI checks: start a copy of the exe from a temp folder with a portable `WoWAddonUpdater.json` (so it doesn't write to the real `%AppData%`), drive it with `System.Windows.Automation`, and capture windows with `PrintWindow`. Don't capture the screen: that grabs whatever is on top.

---

## Ian's addons (WoW Forever)

| Addon | GitHub | Notes |
|---|---|---|
| Atlas | arithmandar/Atlas | |
| Dejunk | moody/Dejunk | |
| EllesmereUI | EllesmereGaming/EllesmereUI | 23 module folders |
| PallyPower | AznamirWoW/PallyPower | Tags end in `-classic`; release.json lists `forever` |
| Questie | Questie/Questie | Zip is ~89 MB (all game versions' databases); installs `Questie` + `QuestieDB` |
| SpellCoda | jezzi23/spellcoda | No release.json; Forever confirmed through `## Interface: ... 16001` |
| TellMeWhen | ascott18/TellMeWhen | |
| XLoot | Xuerian/XLoot | |
| TitanRecZone | *(Bitbucket, abandoned 2020)* | Only on CurseForge now; not supported yet |

Also installed but not tracked: Leatrix Plus/Maps/Sounds, TradeSkillMaster, Titan Panel + plugins (no `TitanPanel/Titan-Panel` repo found on GitHub), Attune, QuestTargets. `QuestieTrace` (Questie/QuestieTrace) is a separate Questie add-on, not part of the Questie zip. Ian tracks it, but it's deliberately left out of `catalog.json` because few people use it.

---

## History
1. **v0.1.0:** GitHub addon updates, release.json flavor matching, safe installs, junction protection, app self-update, build/release scripts.
2. **v0.1.1:** test release to prove self-update works (0.1.0 → 0.1.1 on both builds).
3. **v0.2.0:**
   - "Find installed addons" from `catalog.json`, auto-run on first launch
   - WoW detection through Battle.net's product.db, Program Files first
   - settings moved to `%AppData%` with portable mode
   - Settings shows the list's location with an "Open folder" button
   - Forever confirmation through the `.toc` for addons without a release.json
   - release script fixes (git warnings, notes through a file)
4. **v0.2.1:**
   - "Remove" button renamed to "Remove from list", so it's clear it only stops tracking and doesn't delete the addon's files.

## Ideas / not done yet
- **CurseForge support** (for TitanRecZone and CurseForge-only addons). Ian is applying for an API key at console.curseforge.com. The key **must not** be put in the exe or the public repo. Plan: a per-user "CurseForge API key" field in Settings; without a key, CurseForge addons are simply skipped. API: `GET /v1/mods/search?gameId=1&slug=<slug>`, then `/v1/mods/{id}/files`; respect `allowModDistribution` (no download URL when false).
- **WoWInterface** as another source (it has a public API with no key).
- Grow `catalog.json` as friends report addons. Each entry needs: a repo that publishes release zips, and its main folder name.
- Maybe: code signing to avoid the SmartScreen warning (costs money; low priority for friends-only).
