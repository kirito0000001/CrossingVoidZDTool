# CrossingVoidZDTool Architecture Migration Map

This file records the baseline framework for the Zero Crossing ZD toolbox. Keep it current before adding feature pages.

## Current Frame

- `ApplicationViewModel`
  - Aggregates module definitions, selected module state, settings, global progress, and feature module ViewModels.
- `SettingsViewModel`
  - Owns project root state, settings persistence, project root migration orchestration, auxiliary display settings, log filters, setting undo stack, and user-facing settings status.
- `GlobalProgressViewModel`
  - Owns bottom progress visibility, title, detail, percent, elapsed text, and indeterminate state.
- `CharacterDeskViewModel`
  - Owns St1 design draft state, character cards, current/last edited character, draft text, autosave status, and reference-image list.
- `ActionFramesViewModel`
  - Owns action-frame import and frame-processing state as features are added.
- `LineArtViewModel`
  - Owns line-art processing state as features are added.
- `UnrealSyncViewModel`
  - Owns St3 character info state, validation notice state, material status, and delayed save orchestration.
- `SequenceFramesViewModel`
  - Owns St5 action sections, selected preview/management section, per-action FPS settings, frame cursor state, and sequence-frame toolbox JSON persistence events.

`MainWindow.xaml.cs` is now the composition root and shell startup bridge. UI bridge code is split into:

- `MainWindow.Navigation.cs`: `NavigationView` selection, page switching, and entrance animation.
- `MainWindow.Settings.cs`: project root folder picker, migration progress bridge, and project-root help dialog.
- `MainWindow.Progress.cs`: bottom progress host animation, cancellation, elapsed timer, and ring geometry.
- `MainWindow.Logging.cs`: log output bridge, auxiliary display refresh, log help dialog, and log panel actions.
- `MainWindow.CharacterInfo.cs`: St3 bridge for delayed save, text focus behavior, code rename picker-free UI events, and metadata display-name synchronization.
- `MainWindow.SequenceFrames.cs`: St5 bridge for pickers, popup/viewer hosting, drag/drop forwarding, and preview canvas input. Playback cache remains a temporary window bridge and should move into a preview helper or ViewModel-owned state when the preview surface is extracted.

## Module Categories

- `Character`: character desk, character metadata, action ownership.
- `CharacterDraft`: St1 design notes, reference images, current production character state.
- `Frame`: screenshot sequence import, frame ordering, crop/output preparation.
- `ImageProcessing`: line-art extraction, preview, masks, generated outputs.
- `Integration`: Unreal sync and external process coordination.
- `Settings`: project root and toolbox preferences.
- `Shell`: title bar, navigation, dialogs, and progress host.

## Rules For Future Feature Work

- Start every feature by adding or extending a ViewModel and Service.
- Keep raw `MainWindow` event handlers as small bridges.
- Long operations must report through `GlobalProgressViewModel` and accept cancellation.
- User-visible operations should write to the bottom log through `AppendLog(...)`; log output must respect the settings filters.
- Setting changes that are simple preferences should be undoable through the settings undo stack.
- Treat workspace files as the source of truth whenever practical. Existing JSON, text, image folders, action/frame folders, and material folders should be read back into Services/ViewModels instead of relying only on in-memory state.
- When users or external tools change files inside the workspace folder, refresh paths should re-scan the affected files and synchronize visible state before editing, exporting, or syncing.
- Keep last-edited module state centralized through module definitions. Step pages must persist their module key when durable edits are saved, and Continue must read the saved module tag rather than transient window state.
- Cross-page character identity fields must be synchronized after saves: `tool/ZDToolboxData.json` owns detailed St3 data, while `tool/character.json` mirrors the display name/code/completion fields needed by character cards and Continue.
- Delayed-save modules must flush before window close or navigation decisions that depend on their data.
- Keep image decoding, file scans, numbering, naming, CSV/JSON, import/export, and Unreal rules in Services.
- Add pages through the central page-switching path so every page gets the same entrance animation.
- Build and start the app after each meaningful step, keep the final launched app instance open for debugging, then send the step-completion QQ email.

## Next Migration Targets

- Move project-root migration command into `SettingsViewModel` with a command wrapper while leaving the WinUI picker bridge in `MainWindow.Settings.cs`.
- Introduce services for character/action/frame folder layout before implementing imports.
- `CharacterWorkspaceService` now owns character-card folder creation and St1 reference files. St1 draft text is user-authored data and must persist through `tool/ZDToolboxData.json` under `Draft`, not through a text cache or window-only state.
- Create dedicated Views or factories for repeated cards once real character/action cards exist.
- When adding preferences for future pages, register them in `AppSettings`, `SettingsViewModel`, the settings undo stack behind `Ctrl+Z`, and the settings page.
