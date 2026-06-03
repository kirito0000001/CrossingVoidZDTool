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
  - Owns the character workbench landing state.
- `ActionFramesViewModel`
  - Owns action-frame import and frame-processing state as features are added.
- `LineArtViewModel`
  - Owns line-art processing state as features are added.
- `UnrealSyncViewModel`
  - Owns Unreal sync status and busy state as sync features are added.

`MainWindow.xaml.cs` is now the composition root and shell startup bridge. UI bridge code is split into:

- `MainWindow.Navigation.cs`: `NavigationView` selection, page switching, and entrance animation.
- `MainWindow.Settings.cs`: project root folder picker, migration progress bridge, and project-root help dialog.
- `MainWindow.Progress.cs`: bottom progress host animation, cancellation, elapsed timer, and ring geometry.
- `MainWindow.Logging.cs`: log output bridge, auxiliary display refresh, log help dialog, and log panel actions.

## Module Categories

- `Character`: character desk, character metadata, action ownership.
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
- Keep image decoding, file scans, numbering, naming, CSV/JSON, import/export, and Unreal rules in Services.
- Add pages through the central page-switching path so every page gets the same entrance animation.
- Build and start the app after each meaningful step, then send the step-completion email.

## Next Migration Targets

- Move project-root migration command into `SettingsViewModel` with a command wrapper while leaving the WinUI picker bridge in `MainWindow.Settings.cs`.
- Introduce services for character/action/frame folder layout before implementing imports.
- Create dedicated Views or factories for repeated cards once real character/action cards exist.
- When adding preferences for future pages, register them in `AppSettings`, `SettingsViewModel`, the settings undo stack behind `Ctrl+Z`, and the settings page.
