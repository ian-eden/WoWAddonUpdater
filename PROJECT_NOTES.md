# WoW Addon Updater: Project Notes

A small Windows app (C#, WinForms, .NET 10) that keeps World of Warcraft addons up to date from the authors' **GitHub releases**. Addons from CurseForge and other sites are handled as manual downloads. Built as a lightweight alternative to the CurseForge app. Made for **WoW Forever** (interface `16001`, folder `_forever_`), but it supports the other game versions through Settings.

- **Repository:** https://github.com/ian-eden/WoWAddonUpdater
- **Latest release:** v0.3.1 (2026-11-10)

*Last updated: 2026-11-10*

> Machine-specific notes (local paths, the developer's own copy and addon list) live in `PROJECT_NOTES.local.md`, which isn't committed.

---

## Folder layout

```
WoWAddonUpdater/
├── PROJECT_NOTES.md        ← this file
├── README.md               ← user-facing instructions (the GitHub front page)
├── WoWAddonUpdater.csproj  ← <Version> is set by release.ps1
├── catalog.json            ← known addons (folder name → GitHub repo); also downloaded live from GitHub
├── app.ico                 ← app icon: flame with an up arrow in its core (exe icon + embedded for windows)
├── build.ps1               ← builds both exes into dist\
├── release.ps1             ← version bump + build + commit + tag + push + GitHub release
├── Program.cs              ← entry point
├── MainForm.cs             ← main window: header, list, toolbar, right-click menu, log, notice bars, all the flows
├── Theme.cs                ← colours (flame orange on dark plum), flat buttons, HeaderPanel, owner-drawn ModernListView, menu renderer
├── SettingsForm.cs         ← AddOns folder, game version, GitHub token, where the list is stored
├── FindAddonsForm.cs       ← "Addons found" tick-list dialog
├── GitHubClient.cs         ← GitHub API: pick the right release/zip for the flavor, downloads
├── Installer.cs            ← safe extract + folder swap with rollback; .toc helpers
├── AddonScanner.cs         ← scans AddOns against the catalog (+ GitHub links in .toc files)
├── ManualPages.cs          ← manual-download addons: CurseForge/other page links, Downloads folder watcher
├── AppUpdater.cs           ← the app updating itself from its own releases
├── AppInfo.cs              ← version, repo owner/name, which release file this build uses
├── ConfigStore.cs          ← settings file location, loading/saving, finding the WoW install
├── Models.cs               ← settings, tracked addons, GitHub and release.json shapes
├── publish\                ← optional local running copy (portable mode). Not in git.
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
- **Junctions/links are never replaced.** Addon developers often link a folder in AddOns to their working copy. Installs refuse to touch a linked folder, and the scanner skips them.
- SavedVariables (in `WTF\`) are never touched.

### Manual-download addons (CurseForge and other sites)
- Pasting a non-GitHub link adds the addon with `Source = "manual"` and a `PageUrl`. CurseForge links are normalised to `https://www.curseforge.com/wow/addons/<slug>`. List key: `page:<url>`, or `folder:<main folder>` for a zip installed without a link.
- The app **can't check these for new versions**: curseforge.com blocks automated requests, and the API needs a key. Status shows "Manual download – right-click to check its page for updates".
- **Right-click → "Check page for updates / download…"** on one: snapshot the Downloads folder (the real location, via `SHGetKnownFolderPath`, so a moved Downloads folder works), open the page, and show a blue "Waiting for …" bar (Choose the zip… / Cancel / "Delete the zip after installing", saved as `DeleteDownloadedZips`). Poll once a second for a new `.zip`. It counts as finished when it isn't 0 bytes, has no Firefox `.part` file, is the same size on two polls, and can be opened exclusively.
- **Update all** skips manual addons, so it doesn't open a browser tab for each one.
- **Add addons ▾ → Install from a zip file…** and dragging a zip onto the window: match the zip's folders to a tracked addon's `InstalledFolders`. A GitHub-tracked match is refused (use Update instead). With no match, a small dialog asks which uninstalled manual addon it is, or adds a new one with an optional page link.
- Installing uses `Installer.StageZipAsync` (extract + read the `.toc` Version/Title, with colour codes stripped), then a confirm on first install or when the folders don't match, then `Installer.CommitAsync` (the same swap/rollback/junction protection). `InstalledTag` = the `.toc` version, `InstalledAsset` = the zip's file name, and the name comes from the `.toc` Title.
- Planned: with a CurseForge API key in Settings, CurseForge manual addons could switch to automatic checks (the slug is already in `PageUrl`).

### Finding installed addons
- `catalog.json` maps main folder names to repos. The app downloads it from `raw.githubusercontent.com/ian-eden/WoWAddonUpdater/main/catalog.json`, with the built-in copy as a fallback. **Adding an addon is just a push to main; no release is needed.** After a push, raw.githubusercontent can take about a minute to serve the new file.
- Module folders that start with a catalog folder name (XLoot_Frame, EllesmereUIBags, ...) count as part of it.
- GitHub links in `.toc` `## X-...` lines are offered unticked. In practice almost no addons have one (only Atlas had a link, and it points to CurseForge).
- Runs automatically when the addon list is empty. Otherwise it's **Add addons ▾ → Find installed addons…**.

### Finding the AddOns folder
In order:
1. The paths in Battle.net's `C:\ProgramData\Battle.net\Agent\product.db`. The file is binary, but the paths are plain text, and this finds installs on other drives.
2. The registry `HKLM\SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft\InstallPath`. It's missing on some PCs.
3. Common folders on every drive, starting with `Program Files (x86)\World of Warcraft`.

### Where settings are stored
- Default: `%AppData%\WoWAddonUpdater\WoWAddonUpdater.json`.
- **Portable mode:** if `WoWAddonUpdater.json` exists next to the exe, that one is used. Anyone who updated from 0.1.x stays in portable mode, so their list wasn't lost.
- An optional GitHub token is stored there in plain text. Without one: 60 API requests/hour per IP, about one per addon per check.

### App self-update
- At startup, on **Check for updates**, and on the automatic check (see below): `GET /repos/ian-eden/WoWAddonUpdater/releases/latest`. If its tag is newer than the running version, a yellow bar offers it.

### Checking while the app stays open
- A 1-minute WinForms timer runs `AutoCheckAsync`. Once `AutoCheckEvery` (3 hours) has passed since `_lastCheck`, it checks the addons and the app (`CheckEverythingAsync`). Comparing times rather than using one long timer catches up after the PC wakes from sleep.
- It's skipped while busy or while a dialog is open. It **only checks**; nothing installs without a click.
- `RefreshTitle()` puts the count in the window title ("– 3 updates available", "– new app version ready"), so it's visible on the taskbar without pop-ups. This is deliberately non-invasive.
- To test without waiting 3 hours: copy the project to a scratch folder, change `AutoCheckEvery` to `FromMinutes(1)` there, and build with `-o`. Don't edit the real file with PowerShell 5's `Get-Content`/`Set-Content`: they read UTF-8 without a BOM as ANSI and garble ▾ – … ›.
- Two release files: **`WoWAddonUpdater.exe`** (standalone/self-contained, ~49 MB, recommended for most people) and **`WoWAddonUpdater-lite.exe`** (~0.4 MB, needs the .NET 10 Desktop Runtime). Each build updates from its own file: `SELF_CONTAINED` is defined in the csproj when `SelfContained=true`, which sets `AppInfo.AssetName`.
- Update = download to `<exe>.new`, check size + GitHub's sha256 `digest` + the `MZ` header, rename the running exe to `.old`, move the new one into place, start it, close. The next start deletes `.old`. If Windows won't allow the rename (e.g. exe in Program Files), it offers to open the releases page instead.
- **Tested for real** (0.1.0 → 0.1.1) on both builds by clicking "Update now" through UI Automation.

---

## Workflow

### Tools
- .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`), Git (`Git.Git`), GitHub CLI (`GitHub.cli`), all available through winget.
- `gh` must be logged in to the account that owns the repo (HTTPS, git credentials included).
- Commit with the GitHub noreply address so no personal email ends up in the public history. `core.safecrlf=false` silences the harmless LF/CRLF warnings.
- A PowerShell window opened before an install won't find the new tools. Open a new one, or refresh `$env:Path`.

### Build and release
- `.\build.ps1` builds both exes into `dist\`.
- `.\release.ps1 -Version X.Y.Z -Notes "..."` does: set `<Version>` → build → `git add -A` + commit → tag `vX.Y.Z` → push → `gh release create` with both exes. The notes are passed to `gh` through a temp file.
- Run the scripts **in the same PowerShell session** (`Set-ExecutionPolicy -Scope Process Bypass; & .\release.ps1 ...`) rather than `powershell -File ...`. Multi-line notes with quote marks don't survive being passed to a new process.
- Version numbers must always go up; the script refuses to reuse a tag. Fixes → 0.2.1, features → 0.3.0.
- A local copy in `publish\` updates itself through the yellow bar after a release (or copy `dist\WoWAddonUpdater-lite.exe` over it while it's closed).
- `release.ps1` commits **everything** (`git add -A`). `.gitignore` excludes bin/obj/publish/dist, `WoWAddonUpdater.json*` and `PROJECT_NOTES.local.md`.

### Testing approach (nothing permanent in the repo)
- Logic tests: a throwaway console project that compiles the non-UI source files (`Models`, `GitHubClient`, `Installer`, `ConfigStore`, `AppInfo`, `AddonScanner`, `ManualPages`, plus `catalog.json` as an embedded resource) and installs into a **fake AddOns folder in a temp directory**, including a junction to prove links are left alone. Never install into a real AddOns folder during testing; only read it.
- UI checks: start a copy of the exe from a temp folder with a portable `WoWAddonUpdater.json` (so it doesn't write to the real `%AppData%`), drive it with `System.Windows.Automation`, and capture windows with `PrintWindow`. Don't capture the screen: that grabs whatever is on top.

---

## Addons tested with WoW Forever

| Addon | Source | Notes |
|---|---|---|
| Atlas | arithmandar/Atlas | |
| Dejunk | moody/Dejunk | |
| EllesmereUI | EllesmereGaming/EllesmereUI | 23 module folders |
| PallyPower | AznamirWoW/PallyPower | Tags end in `-classic`; release.json lists `forever` |
| Questie | Questie/Questie | Zip is ~89 MB (all game versions' databases); installs `Questie` + `QuestieDB` |
| QuestieTrace | Questie/QuestieTrace | A separate Questie add-on, not in the Questie zip. Left out of `catalog.json` because few people use it. |
| SpellCoda | jezzi23/spellcoda | No release.json; Forever confirmed through `## Interface: ... 16001` |
| TellMeWhen | ascott18/TellMeWhen | |
| XLoot | Xuerian/XLoot | |
| DungeonJournal | CurseForge (manual) | Zip `ForeverDungeonJournal-x.y.z.zip`: one folder, `## Interface: 16001`, `## Version`, `## Title: DungeonJournal` |
| Attune | CurseForge (manual) | |
| TitanRecZone | *(Bitbucket, abandoned 2020)* | CurseForge only; works as a manual download |

No GitHub source found for: Leatrix Plus/Maps/Sounds, TradeSkillMaster, Titan Panel + plugins (no `TitanPanel/Titan-Panel` repo), QuestTargets. These can be added as manual downloads.

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
5. **v0.3.0:**
   - **Manual-download addons:**
     - CurseForge and other page links
     - the Downloads watcher
     - Install from a zip file, and drag and drop
   - **New look** (less clutter, more colour):
     - A dark plum header with an ember line, holding the icon, title, game version + AddOns path, and a Settings button.
     - The toolbar has only **Update all** (orange), **Check for updates**, and **Add addons ▾** (Find installed addons / Install from a zip file), plus a hint: "Right-click an addon for more options".
     - Per-addon actions moved to the right-click menu: Update / Reinstall / "Check page for updates / download…" (bold, at the top), open page, open folder, pre-releases, Remove from list. The Delete key removes from the list.
     - The list is owner-drawn: 30 px rows, stripes, an orange selection bar, coloured status dots, and the last column filling the width. The sideways scrollbar is suppressed through WS_HSCROLL in WndProc.
   - **App icon** (`app.ico`; `ApplicationIcon` in the csproj, plus an embedded copy that `AppInfo.Icon` loads for the title bars/taskbar):
     - Drawn in code with System.Drawing: a dark rounded tile, a red→yellow flame, and an up arrow in the core. The ico holds PNG frames at 16–256 px.
     - The generator was a throwaway scratch project, not kept in the repo. Its `Program.cs` would get compiled into the app if it sat in a subfolder.
   - **README:** updated for the new features, and it now gives the correct settings location.

6. **v0.3.1:**
   - A **Find more addons** button in the header, left of Settings. It opens https://www.curseforge.com/wow and has a tooltip saying to paste an addon's link into the box below.
   - Automatic re-check every 3 hours while open. **Check for updates** now also checks for a new app version. Pending updates show in the window title.

## Ideas / not done yet
- **CurseForge API support:** automatic update checks for CurseForge addons.
  - An API key comes from console.curseforge.com. It **must not** be put in the exe or the public repo.
  - Plan: a per-user "CurseForge API key" field in Settings. Without a key, CurseForge addons stay manual downloads.
  - API: `GET /v1/mods/search?gameId=1&slug=<slug>`, then `/v1/mods/{id}/files`. Respect `allowModDistribution` (there's no download URL when it's false).
- **WoWInterface** as another source (it has a public API with no key).
- Grow `catalog.json` as people report addons. Each entry needs a repo that publishes release zips, and its main folder name.
- Maybe: code signing to avoid the SmartScreen warning (costs money; low priority for a small audience).
