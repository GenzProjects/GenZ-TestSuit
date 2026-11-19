# Changelog

The detailed history is in §10 of [Documentation~/GenZ-TestSuit.md](Documentation~/GenZ-TestSuit.md).

## [1.0.1] - Unreleased (in development since 2026-11-10)
- Targets **GenZ Ads v1.33** (`AdsScripts-GenzAds`).
- **Auto Apply** now writes the sheet's Taichi 2.0 count into the code when a game has no `RemoteConfigManager` Inspector field (e.g. `{ "custom_imp", 50L }` in `FirebaseManager`). Before, it was checked but never applied.
- **Player Settings, Unity 6:** Minimum API Level 24 or 25 passes (`minSdkUnity6`), and **Show Splash Screen** must be off (`showSplashScreenUnity6`). Older Unity is unchanged.
- **GameAnalytics ad revenue formats** (step 2): the AdMob integration (`GAAdMobIntegration.cs`) must send the same ad format strings as MAX (`GAMaxIntegration.cs`), e.g. `INTER` not `INTERSTITIAL`, `REWARDED` for rewarded, or GameAnalytics shows one format as two. New **Fix GA Ad Formats** button corrects them. Also checks the GA ad network defines and the `GameAnalyticsILRD` subscribe calls.
- **Ad scripts version** (step 4): every script in `AdsScripts-GenzAds` must start with the same `// GenzMonetization-v1.33` header. Mixed versions or a missing header fail; a game from before v1.33 (no headers) or an older version warns. Raise `GenZTestSuitInfo.AdsScriptsVersion` with each GenZ Ads release.
- **AppLovin MAX adapters** (step 2): every adapter in the studio list must be installed (BidMachine, Bigo Ads, Pangle, Meta, DT Exchange, Google AdMob, Google Ad Manager, InMobi, Mintegral, Smaato, Unity Ads, Liftoff/Vungle, Yandex, YSO Network, and the custom networks AdSurge and Helix / Voodoo ADN), IronSource must be removed (adapter, Mediation folder and IronSource / LevelPlay SDK), and any other adapter warns. Lists in the config: `integration.maxAdapters`, `forbiddenAdapters`, `adapterPattern`.
- **No App Open ad after a purchase or review** (step 4): the App Open ad must check `isPausedDuetoAd` (config `appOpenSkipFlags`); purchases and the in-app review must set it before opening the store dialog / review card, and clear it again a moment (real time) after the flow ends, including on failure and early exits. Otherwise an App Open ad shows on return, or a stuck flag skips the next real one. A missing set on the review fails; the rest warns, so v1.32 games still pass.

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
5. Change the `#v…` tag in both install lines of `README.md` to the new version.
6. Commit, tag `v<package version>` (e.g. `git tag v1.1.0`) and push with `git push origin main --tags`. Games update by changing the `#v…` in their git URL.
7. Create the GitHub release for the tag, with this version's changelog entry and the install line as its notes: `gh release create v<package version> --title v<package version> --notes-file <notes.md> --latest`.
