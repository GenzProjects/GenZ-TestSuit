# Changelog

The detailed history is in §10 of [Documentation~/GenZ-TestSuit.md](Documentation~/GenZ-TestSuit.md).

## [1.0.0] - GenZ-TestSuit-v1.0 (2026-10-22; packaged 2026-10-29)
- **GenZ-EditorTestSuit** (first tab), four steps:
  1. IDs Check: every ad unit ID, app ID, SDK key and token against the studio Google Sheet, with Auto Apply and Auto Correct; keystore against the sheet.
  2. SDK Integration: SDK scene objects and settings assets, SDK files and Gradle setup, Android signing, and the Android Player Settings (Auto Graphics API off, no Vulkan, ES3.1 off, ES3.1+AEP and ES3.2 on, IL2CPP, ARMv7 + ARM64 only, minimum API 24, target API 36).
  3. Taichi: Taichi 1.0 / 2.0 switches, Inspector defaults and their revenue events.
  4. Ad Calling Scenarios: the ad code read statically (waterfall, vendor switch, banners, reloads, revenue, AdsGapTimer, Remote Config defaults).
- **GenZ-ApkTestSuit** (second tab, first version; not yet run on a phone): Build & Device, SDK Init, Analytics Events, Ads, Stability, read from logcat on a release build. Needs `GenZLog.cs` and its `[GenZ]` lines in the game's `AdsScripts-GenzAds`.
- **GenZ-DeepTestSuit** (third tab, first version; not yet run on a phone): Test / Release Environment and the phone's shake panel.
- Distributed as the Unity package `com.genz.testsuit` (git URL). The config is created in `Assets/GenZ-TestSuit/`, and the phone panel is installed from the package by Apply Test Environment.

## Releasing a new version
1. Raise `GenZTestSuitInfo.Version` in `Editor/GenZTestSuitConfig.cs`. The window title, its header and copied reports use it.
2. Change the first line of every `.cs` file in `Editor/` and of `Editor/Templates/GenZDeepTestPanel.cs.txt` to `// GenZ-TestSuit-v<version>`. The panel's first line is how projects know their installed panel is out of date.
3. Raise `version` in `package.json` (e.g. `1.1.0`).
4. Add an entry at the top of this file and of the changelog in `Documentation~/GenZ-TestSuit.md`.
5. Commit, tag `v<package version>` (e.g. `git tag v1.1.0`) and push with `git push origin main --tags`. Games update by changing the `#v…` in their git URL.
