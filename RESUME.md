# RESUME

Hand-off notes for picking up wgetui in a new conversation. Written only when the user asks for it; it is a
snapshot, not a spec: AGENTS.md, STRUCTURE.md and ROADMAP.md stay the source of truth, and the code wins over
this file. Last written: 2026-11-18, at `a77ffe0` (version 0.1.0, no git remote yet, nothing pushed).

## How the user works (keep this consistent)

- **Talk to the user in Italian**, always, including when relaying agent reports. Code, comments, commits and
  docs stay in English (AGENTS.md).
- **Plans first, then wait for every answer.** End a proposal with numbered questions and a recommendation; don't
  start (or launch an agent) until each one has an explicit answer (AGENTS.md workflow rule). When the user
  answers with something ambiguous, ask, don't guess.
- **UI changes get a mockup first** when they add new UI: a Design canvas artifact in the app's current style,
  approved before implementing. Approved mockups are not committed; their link and a short description go in
  ROADMAP.md when the feature is planned for later.
- **Big work goes to one background agent** with a full written plan (goal, steps with one commit each, hard
  constraints, how to verify, short report). Agents send a 2-4 line progress update to "main" after each step;
  relay each update to the user in Italian with the decisions the agent took, then verify with `git log`,
  `make check` and the agent's screenshots before reporting done.
- **No real side effects from Claude or agents**: never install, upgrade, uninstall or enable automatic updates
  for real; never trigger UAC. Live checks: Release build, `--force-no-tray --data-dir <empty scratch folder>`,
  stop the instance by PID. The user runs the real operations and reports back.
- **Experiments (styling, layout) are not committed** until the user picks the version to keep; a rejected one
  can be parked in `git stash` with a descriptive message.
- **Don't touch the user's `make dev`.** `make dev`/`make run` run `make stop` first, which only kills Debug
  instances of this repo (e.g. one left in the tray).
- The shared guidelines template lives at `C:\Users\jacopo\Development\GitHub\jacopofilonzi\agents-guideline`
  (remote `jacopofilonzi/agents-guideline`); commit/push there only when asked.

## Product decisions not obvious from the code

- Look: dark Fluent (FluentAvalonia), Mica, lilac accent, blue (i) button, source badges (winget blue, msstore,
  Local amber). App icon "A" (package with arrow, tile `#5B4BDB`); tray glyph monochrome with a fixed state dot:
  admin orange > failure red > running green > updates blue.
- Default window 1360×760 so the nav stays expanded with the queue panel open.
- Queue panel: always present, collapsed by default; expands by itself only when an operation is queued on an
  empty history (also after "Clear"); otherwise the user's choice wins for the session.
- Row buttons ((i), automatic update) are always visible (hover-only was tried and dropped).
- Agreements: only installs can show the agreements modal (one per request); upgrades never prompt.
- First-run "notify new updates?" dialog: Esc counts as No.
- Installed page hides only what Windows won't let you uninstall (SignatureKind System, the undocumented
  `IsPackageFamilyInUninstallBlocklist` export, plus App Installer and Windows Security as a fallback list). The
  user accepted the undocumented API: a wrong guess only shows a row whose uninstall fails.
- Automatic updates: run only after the startup and hourly checks (not manual Refresh); an open app is postponed
  with one toast per version unless forced (per app or globally); postponed/done/failed toasts always show.
- Relaunch of apps closed by the installer applies to upgrades and to installing another version (downgrade).
- Distribution plan: MSI (WiX) letting the user choose per-machine or per-user; `make publish` keeps producing the
  runnable exe + activation files. Signing not required for now. Publishing to winget-pkgs comes later.
- In-app self-update (later): sidebar item above Settings, two clicks (download, then "Restart to update"),
  approved mockup https://claude.ai/artifact/5SZinW8Wtm4cYdCU7B2Nc2.

## Waiting on the user

Real-world checks not done yet:
- automatic update on a closed app (updates at the next check), on an open one (postponed toast once), forced;
- relaunch of a tray app (e.g. NetBird) after an upgrade or a downgrade;
- clicking a "new updates" toast opens the Updates page;
- light theme of the new automatic-update colours.

## Next steps (proposed, not started)

- The "Later" chain: put the repo on GitHub (public/private, name and license still to decide) → CI running
  `make check` → MSI → in-app self-update → winget-pkgs.
- Or the only "Planned" item: another package manager (Chocolatey first), following "Adding a package manager"
  in AGENTS.md.

## Useful references

- Mockups (approved, not committed): icon concepts https://claude.ai/artifact/2fEuJjpyZayweTY6AyXpNw, details popup
  https://claude.ai/artifact/UxnToiwwcUw9MR3xCjZDeY, automatic updates
  https://claude.ai/artifact/QVncZpwzFk6Pd8dGnMFAjQ.
- Real logs: `%LOCALAPPDATA%\wgetui\logs\wgetui.log`; settings: `%APPDATA%\wgetui\settings.json`.
