# Copilot instructions for And-OneGoLine

## Project snapshot

This repository is an Android-first Unity puzzle game built in Unity 2022.3.62f2. The game flow is a one-scene-per-level draw-a-line puzzle, with persistent managers created during startup and per-level gameplay objects recreated in each scene.

The most important repo-specific facts are already captured in [CLAUDE.md](../CLAUDE.md). Keep that file as the source of truth for project architecture and release/version notes.

## Build, test, and lint commands

There is no checked-in CLI build script, lint configuration, or automated test suite for this project.

- Build: open the Unity project in the Unity Editor and use File > Build Settings > Android to build an APK or AAB.
- Test: there is no repo-managed `dotnet test`, `npm test`, or Unity command-line wrapper in this project. When adding Unity tests, use the Unity Test Runner and filter by test name or assembly name; there is no single project-specific command to run one test from the terminal.
- Lint: none is configured in-repo.
- Generated project files: `*.csproj` and `*.sln` are Unity-generated and should not be edited directly.

## High-level architecture

### Scene flow and progression

- The real startup flow is `Consent` -> `Splash` -> the active level scene.
- `Splash` sets `GameManager.Instance.selectedLevel` and loads the level by name.
- There is no main-menu/level-select scene in the shipped build; the referenced menu paths are effectively legacy and not the active flow.
- Level order is driven by `Assets/Scripts/Common/GameManager.cs` via the `levelNames` array and `GameManager.totalLevels`, not by the scene number alone.
- When levels are added or removed, update all three together: `levelNames`, `totalLevels`, and the Unity Build Settings scene list.

### State and persistence

- `GameManager` is a lazy singleton that stores runtime state and wraps persistence.
- Persistence is entirely `PlayerPrefs` for things such as coins, unlocked levels, hint/skip counts, cosmetics, and ad hoc flags scattered across scripts.
- Persistent managers are created once and survive scene loads through `DontDestroyOnLoad`.
- Per-level systems (`LevelManager`, `UiManager`, player input helpers) are recreated for each level scene.

### Core gameplay implementation

- Gameplay is built around Dreamteck Splines.
- Each level is a set of spline computers and nodes, with `TubeGenerator` rendering the line and `MySplineRef` tracking completion state.
- `PlayerInput` handles pointer input, projects the line, and decides when the player has finished or failed the puzzle.
- `ProjectorLineGenerator` advances the line as the pointer moves and decides which connected uncompleted spline should continue the path.
- Completion is processed by `UiManager.LevelCompleteGenericFunction()`, which updates progression and monetization gating.

### Ads, analytics, and remote config

- Shared monetization/analytics code lives under `Assets/AdsScripts-GenzAds_1.32/`.
- `FirebaseManager` reads remote-config values into static fields and other gameplay/UI systems read those fields directly.
- Rewarded features, hint/skip flows, and ad timing are controlled through this layer.
- Analytics send level events using the pattern `"level_" + (selectedLevel + 1)` and should stay synchronized across both GameAnalytics and AppMetrica.

## Key conventions specific to this repo

- Do not modify third-party SDKs and asset kits under `Assets/` unless the project specifically requires a patch. Examples include Adjust, AppMetrica, Firebase, GameAnalytics, MaxSdk, GoogleMobileAds, Dreamteck Splines, DOTween, NiceVibrations, and other shipped kit folders.
- Keep game progression logic consistent with the existing `GameManager`+`PlayerPrefs` model instead of introducing a new persistence layer for small feature work.
- Treat `Assets/Scenes/*.unity` as authored content, not disposable generated files. If a level is added, removed, or reordered, update both the data layer and the Unity build scene list.
- Prefer working through the existing singleton managers (`GameManager`, `FirebaseManager`, `AdsManager`, etc.) rather than inventing parallel configuration paths.
- Use the Android-first mobile constraints when changing ad timing, in-app flows, or analytics: changes should not assume desktop-only assumptions or a menu-based flow that does not exist in the shipped build.
- Keep `selectedLevel` and progression logic aligned with the 0-based index rules already used in the project; this is easy to break when editing wrap-around logic or level unlocks.
- When changing remote-config-driven behavior, follow the existing static-field convention already used by `FirebaseManager` instead of creating a new config source.

## Working safely in this repo

- Prefer editing gameplay code under `Assets/Scripts/` or related project-owned files rather than generated or third-party directories.
- Keep changes local to the relevant gameplay/monetization flow. This repo is not organized as a large modular service app; most behavior is a coordinated set of managers and scene scripts.
- Validate changes by playing the actual startup flow from `Assets/Scenes/Consent.unity` rather than opening a level scene directly, because persistent managers are created earlier in the flow.

## Source of truth

This file supplements existing repository guidance and should stay aligned with the project-level notes in [CLAUDE.md](../CLAUDE.md).
