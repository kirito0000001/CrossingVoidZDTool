# CrossingVoidZDTool Architecture Migration Map

This file records the baseline framework for the Zero Crossing ZD toolbox. Keep it current before adding feature pages.

## Current Frame

- `ApplicationViewModel`
  - Aggregates module definitions, selected module state, settings, global progress, and feature module ViewModels.
- `SettingsViewModel`
  - Owns project root state, settings persistence, project root migration orchestration, and user-facing settings status.
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
- Keep image decoding, file scans, numbering, naming, CSV/JSON, import/export, and Unreal rules in Services.
- Add pages through the central page-switching path so every page gets the same entrance animation.
- Build and start the app after each meaningful step, then send the step-completion email.

## Next Migration Targets

- Move project-root migration command into `SettingsViewModel` with a command wrapper while leaving the WinUI picker bridge in `MainWindow.Settings.cs`.
- Introduce services for character/action/frame folder layout before implementing imports.
- Create dedicated Views or factories for repeated cards once real character/action cards exist.
