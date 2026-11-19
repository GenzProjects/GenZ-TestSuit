# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

The **host/test project for the GenZ Test Suit** (`com.genz.testsuit`), the studio's pre-release checker for Android games built on `AdsScripts-GenzAds`. Remote `GenzProjects/GenZ-TestSuit`, branches `main` and `development`.

It is a stripped copy of the game "One Go Line Puzzle" (package `com.gps.onego.line.puzzle`, `bundleVersion` 2.71 / `AndroidBundleVersionCode` 41, Unity **6000.3.12f1** per `ProjectSettings/ProjectVersion.txt`). Only what the suite checks is kept: the SDKs, `Assets/AdsScripts-GenzAds/` (GenZ Ads, version in its `Release.txt`), and the scenes Consent → Splash → `Level_0`, `Level_3`, `Level_1`. Gameplay code (`GameManager`, `UiManager`, level order, splines input) is **not** in this repo.

### Versions in development (since 2026-11-10)

All current work targets **GenZ Ads v1.33** and **GenZ Test Suit 1.0.1** together:
- Ads changes go in `Assets/AdsScripts-GenzAds/` and are listed under the v1.33 heading of its `Release.txt` (released: v1.32).
- Suite changes go in the package and are listed under `## [1.0.1] - Unreleased` in `CHANGELOG.md` and the 1.0.1 entry of §10 in `Documentation~/GenZ-TestSuit.md` (released: 1.0.0 / GenZ-TestSuit-v1.0).
- The release-time markers stay at 1.0 until the release steps below are run: `GenZTestSuitInfo.Version`, the `// GenZ-TestSuit-v…` first lines, `package.json`, and the README's `#v…` tag (that tag must point to a tag that exists). At release they become `1.0.1` / `GenZ-TestSuit-v1.0.1`.
- When a suite check depends on something new in v1.33 (a new `GenZLog` line, Remote Config key, ad script method), keep it working on v1.32 games too (plug and play), or make it a warning rather than a failure.

### Where the suite's code lives

The suite is **not** in `Assets/`. It is a local UPM package in a sibling folder, its own git checkout (same `GenZ-TestSuit` remote, branch `main`, tags `v1.0.0`…):

```
E:/GenzGitProjects/GenzTestSuitFolder/
├─ GenzTestSuit/          ← this repo (host project)
└─ com.genz.testsuit/     ← the package: Editor/*.cs, Editor/Templates/, Documentation~/, CHANGELOG.md, README.md, package.json
```

`Packages/manifest.json` links it with `"com.genz.testsuit": "file:../../com.genz.testsuit"`; games install it by git URL (`…/GenZ-TestSuit.git#v1.0.0`). Edits to the suite go in that package folder and are committed there, not here. This repo only holds what the package creates in a project:
- `Assets/GenZ-TestSuit/GenZTestSuitConfig.asset`: the per-project config (rules, sheet tabs, deep-test settings). Committed.
- `Assets/GenZ-DeepTest/`: the phone panel `GenZDeepTestPanel.cs`, copied from the package's `Editor/Templates/GenZDeepTestPanel.cs.txt` by Apply Test Environment, plus `Resources/GenZDeepTest.json` and `link.xml` while the Test Environment is on.

Full docs: `com.genz.testsuit/Documentation~/GenZ-TestSuit.md`. Keep it updated whenever the suite changes.

## Building / running

- No CLI build, test suite or linter. Builds come from the Unity Editor (File > Build Settings, Android). Play from `Assets/Scenes/Consent.unity` (build index 0); persistent managers (ads, Firebase) are created in Consent/Splash.
- Android defines: `DREAMTECK_SPLINES;gameanalytics_max_enabled;gameanalytics_admob_enabled;MOREMOUNTAINS_NICEVIBRATIONS` (`GENZ_DEEP_TEST` is added/removed by the Deep tab).
- Menus: `GenZ/Test Suit`, `GenZ/Run Editor Test Suit` (package); `GenZ/Firebase/StartADB|StopADB` (`Assets/Editor/FirebaseADB.cs`); `XCompression/Compress Textures|Audio Compress` (`Assets/Editor/GameCompressor.cs`).
- `*.csproj`/`*.sln` are Unity-generated; don't edit them. `GenZ.TestSuit.Editor.csproj` lists the package's files by absolute path.
- The `com.coplaydev.unity-mcp` package (MCP for Unity) is installed. With the Editor open and its bridge running, use MCP tools to inspect scenes/GameObjects, read the console and enter play mode instead of editing `.unity`/`.prefab` YAML by hand.

### Compile-checking outside Unity

- The suite: `dotnet build` an SDK-style csproj over `com.genz.testsuit/Editor/*.cs`, referencing DLLs in `C:/Program Files/Unity/Hub/Editor/6000.3.12f1/Editor/Data/Managed/UnityEngine/` (CoreModule, TextRenderingModule, IMGUIModule, UnityWebRequestModule, SharedInternalsModule, JSONSerializeModule, UnityEditor.CoreModule).
- Running the code checks headless: a console harness compiles `GenZSourceModel.cs`, `GenZCodeChecks.cs`, `GenZIntegrationChecks.cs`, `GenZTaichiChecks.cs`, `GenZTestReport.cs`, `GenZTestSuitConfig.cs` against CoreModule + UnityEditor.CoreModule, sets the working directory to this project's root, and calls `GenZCodeChecks.Run(report, new CodeCheckSettings())` and `GenZIntegrationChecks.Run(report, "<package>", new IntegrationSettings())`. Add `GenZApkChecks.cs` to run APK checks on a saved logcat: `GenZApkChecks.CheckSession(report, GenZApkChecks.ParseLogcat(lines), apkInfo, new ApkSettings(), new TaichiSettings(), final: true)`.
- The game's scripts (e.g. after editing ad scripts): build an SDK-style csproj from `Assembly-CSharp.csproj`'s Compile items, HintPaths and DefineConstants (`DisableImplicitFrameworkReferences`, plus `Library/ScriptAssemblies` DLLs of its ProjectReferences), adding any new `.cs` Unity hasn't put in the csproj yet.
- The phone panel alone: `DefineConstants` `GENZ_DEEP_TEST;UNITY_ANDROID`, the CoreModule, IMGUIModule, InputLegacyModule, AndroidJNIModule and JSONSerializeModule DLLs, `Library/ScriptAssemblies/MaxSdk.Scripts.dll`, and `Assets/GoogleMobileAds/GoogleMobileAds{,.Core,.Common}.dll`.

## Releasing the suite

Release name **GenZ-TestSuit-v1.0** (package `1.0.0`), from `GenZTestSuitInfo.Version` in `Editor/GenZTestSuitConfig.cs`; shown in the window title, header and copied reports. The steps are in the package's `CHANGELOG.md`:
1. Raise `GenZTestSuitInfo.Version`.
2. Change the first line of every `Editor/*.cs` and of `Editor/Templates/GenZDeepTestPanel.cs.txt` to `// GenZ-TestSuit-v<version>` (projects detect an outdated panel by that line). New scripts must start with it too.
3. Raise `version` in `package.json`.
4. Add entries to `CHANGELOG.md` and the changelog (§10) of `Documentation~/GenZ-TestSuit.md`.
5. Update the `#v…` tag in both install lines of `README.md`.
6. Commit, `git tag v<package version>`, `git push origin main --tags`, then `gh release create v<ver> --title v<ver> --notes-file <notes.md> --latest`.

The release name is separate from the config asset's `configVersion` (`GenZTestSuitConfig.CurrentVersion`): raise that when config defaults change so existing config assets upgrade.

## Suite architecture

It must stay **plug and play** across all studio games, which share `AdsScripts-GenzAds` with small per-game differences: it's one editor-only asmdef (`GenZ.TestSuit.Editor`) that never references the ad script types at compile time. Components are matched by class name, fields read via `SerializedObject`, and per-project name differences go in the config asset's rules, not in code.

Three tabs in one window (`GenZTestSuitWindow.cs`; the Deep tab is a `partial` in `GenZDeepTestSuitWindow.cs`). It's for developers; a separate QA tool comes later.

### GenZ-EditorTestSuit (done)

Every `CheckResult` has a `TestStage`; the step bar shows each step green/amber/red, clicking a step filters to its checks (`stepFilter`). New checks must set their step: `report.Section(title, stage)` gives the default, and `section.stage` can be changed before an `Add`.
1. **IDs Check** (`Ids`): Environment section plus every ID-kind rule field, compared with the studio Google Sheet row matched by package name. Any whitespace in a stored ID fails. Also **Keystore = sheet**: the sheet's `KeyStore` column must equal the keystore file name or key alias, ignoring case and symbols.
2. **SDK Integration** (`Integration`): presence of scripts/objects/assets, switch fields (`MustBeFalse`, `MustBeTrue`, `Expected`, `NumberAboveZero`), compile errors, SDK files (`GenZIntegrationChecks.cs`: `google-services.json` for the package and its generated XML in sync, `*Dependencies.xml` resolved into `mainTemplate.gradle`, AndroidX/Jetifier, aapt2 override path, non-debuggable manifest), Android Player Settings (`GenZPlayerSettingsChecks.cs`: Auto Graphics API off, no Vulkan, ES3.1 off, ES3.1+AEP and ES3.2 on, IL2CPP, exactly ARMv7+ARM64, min API 24 (24 or 25 on Unity 6, `minSdkUnity6`), target API 36, and on Unity 6 only Show Splash Screen off; the Unity version is read at run time from `Application.unityVersion`) and signing (`GenZSigning.cs`: custom keystore, file present, alias set; SHA-256 read with Unity's JDK `keytool`).
3. **Taichi** (`GenZTaichiChecks.cs`, partial of `GenZCodeChecks`): `Taiche1_0`/`Taiche2_0` switches on `FirebaseManager`, Inspector defaults on `RemoteConfigManager`, each version's Firebase event (`custom_ad_impression`/`count_ad_impression`) from all 8 revenue handlers, PlayerPrefs progress, Remote Config thresholds (`custom_rev`/`custom_imp`) parsed with InvariantCulture, and the 2.0 count equal to the sheet's `Taichi 2.0 Default Values`.
4. **Ad Calling Scenarios** (`AdCalling`): all other code checks.

Rule targets: `AdsObject` (the 8 scripts on the Ads object in the SDK scene, default name `ADs`, case-insensitive), `SceneObject` (Adjust prefab with Production environment, AppMetrica `ApiKey` vs sheet column `AppMatrica`, GameAnalytics, AdsGapTimer, UmpManager active with `isTestMode` false), `Asset` (GameAnalytics `Settings.asset` by `Platforms` index with Send Version on; `GoogleMobileAdsSettings`; `AppLovinSettings` cross-checked with `sameAs`).

**Auto Apply / Auto Correct** (`GenZIdFixer.cs`): each run records an `IdFix` (asset, or scene + hierarchy path + class, plus property path) for every ID differing from a non-empty sheet cell. *Auto Apply All IDs from Sheet* re-downloads and applies; *Auto Correct N Wrong IDs* applies the last run's list. Both confirm, save assets and scenes they opened (already-open scenes are only dirtied), then re-run. The Taichi 2.0 count goes into `RemoteConfigManager`'s Inspector field; a game without one (older ad scripts) gets it written into the script instead (`IdFix.codeFile/codeContext/codeValue`, `GenZIdFixer.SetInCode`: the number on the exact line the check read, suffix kept, refused if that line changed).

**Code checks** (`GenZCodeChecks.cs` on `GenZSourceModel.cs`, a Roslyn-free C# reader; no UnityEditor dependency). They anchor on SDK API calls, not method names, and verify:
- MAX first, AdMob fallback for interstitial/rewarded; App Open AdMob only; MREC MAX only; the two banners never shown together.
- Vendor switch (`vendorSwitchFields`, default `useSdkVendorSwitch` on `AdsManager`; 0 = MAX first, 1 = alternate, 2 = MAX, MAX, AdMob), only when the field is declared. On AdMob's turn (`if (IsAdmobTurn(count)) {…}`) AdMob goes first and that branch is cut out of the MAX-first check. MAX readiness may be in a `return … MaxSdk.Is…Ready(…)` helper.
- Every format reloads after close and retries after a failed load. A handler that only sets a flag counts when another ad-script method checks the flag and loads (e.g. `Update` → `Invoke(nameof(RequestInterstitial))`).
- Every paid event (MAX `OnAdRevenuePaidEvent`, AdMob `OnAdPaid`) reports to Adjust, Firebase `ad_impression` and AppMetrica (`revenueSinks`).
- `AdsGapTimer.ResetTimer()` for every interstitial and rewarded grant, per network: in SDK callbacks, or else on every call path.
- Remote Config sets `AdsGapTimer.timeLeft` (`adtimer`), and every `GetValue("key")` has a `SetDefaultsAsync` default (section **Remote Config defaults**). Remote Config classes = `remoteConfigClasses` plus any class calling `GetValue("…")`; a default written as a field name resolves to its starting value.
- No App Open ad after a purchase or review (`CheckAppOpenSkip`): App Open reads `appOpenSkipFlags` (`isPausedDuetoAd`); the `PurchaseProduct`/`InitiatePurchase` caller and the `LaunchReviewFlow` caller set it, then clear it after a `WaitForSecondsRealtime` delay on success, failure and early exits (v1.33; only a missing review set fails).
- Common bugs: AdMob callbacks off the main thread, unchecked nulls, wrong `Invoke("name")`.

Current state of this project's ad scripts: Taichi + code checks give 0 failures and 0 warnings (AdMob retries go through `interRetryPending`/`rewardRetryPending`/`bannerRetryPending` and `AdMob.Update`; `_rewarded?.Invoke()`). The SDK file checks warn that `Assets/Plugins/Android/gradleTemplate.properties` points `android.aapt2FromMavenOverride` at Unity 2022.3.24f1.

### GenZ-ApkTestSuit (first version, not yet run on a phone)

`GenZApkTestSuit.cs` (device side) and `GenZApkChecks.cs` (checks); docs §6. Tests the **release** APK, never a debug build.
- The game is picked from the studio sheet (`GenZSheetReader.ListGames`, every `Package Name` column); only this project's version is compared with Player Settings. The sheet's Taichi 2.0 count is checked against the `custom_imp` from Remote Config.
- APK optional: "Already installed on the phone" reads it with `dumpsys package` (`ApkInfo.FromDumpsys`). Signer SHA-256 via `apksigner` must equal the expected certificate (`ExpectedSigning`); for another game, its sheet keystore is looked for next to this project's keystore; for an installed game the base APK is pulled with adb.
- Phone list shows every attached phone, its state and the selected game's installed version, refreshed every 5 s. Start/Stop ADB buttons (`DrawFirebaseAdb`) do what the `GenZ/Firebase` menu does on the selected phone.
- Session: install, enable Firebase debug logging, cold-start, read logcat every 4 s; Finish saves the log under `Logs/GenZ-ApkTestSuit/`. Stages `ApkBuild`, `SdkInit`, `Events`, `Ads`, `Stability` (`TestReport.ApkStages`; reports with `apk = true` are kept separately from Editor reports).
- No process runs between polls, so script reloads don't break a session; state lives in the window's `[SerializeField]` fields.
- Evidence comes mainly from `[GenZ] <sdk>|<what>|<OK|FAIL|CALL|INFO>|<detail>` lines written by `GenZLog.cs` in the ad scripts, plus Firebase debug log and GameAnalytics Info Log Build output (regexes in the config's `apk` settings, still to confirm on a device). `GenZLog` must never log keys, tokens, revenue, or device/advertising/user IDs; `GenZLog.ProbeServers` detects Adjust/AppMetrica server answers without logging the ID.

### GenZ-DeepTestSuit (unreleased, not yet run on a phone)

`GenZDeepTestSuit.cs`; docs §7.
- **Apply Test / Release Environment** switch SDK settings in the config's `deepTest.settings` via `SerializedObject` (Adjust Sandbox+Verbose ↔ Production+Info, AppMetrica `Logs`, GameAnalytics `VerboseLogBuild`, optionally UMP `isTestMode`), add/remove the `GENZ_DEEP_TEST` define, and write/delete `Assets/GenZ-DeepTest/Resources/GenZDeepTest.json` and `link.xml`. Every `Class.Member` is checked against the game's types when written.
- The phone panel is entirely inside `#if GENZ_DEEP_TEST` and also needs the JSON. It boots with `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)`, turns on MAX verbose logging and the creative debugger, registers the phone as an AdMob test device, and opens on shake (F9 in Editor).
- **Panel ad buttons must call the game's own ad scripts, never the SDKs directly** (it tests the integration): AdMob rows → `AdMob.*`, MAX rows → `AdsManager.*`, waterfall rows → `AdsManager.ShowInterstitial`/`ShowRewardedAd`, by name via reflection from the config's `deepTest.actions` (`DeepAction`). The ad scripts themselves must not be edited for it.
- Test on a phone: shares the APK tab's build choice and phone list; `StartSession` installs, clears logcat, enables DebugView, launches; reads the log every 4 s (`[GenZ] DeepTest|<row>|…`). Stop & Save Log writes to `Logs/GenZ-DeepTestSuit/`.
- Release guards: Editor step 2 and APK step 1 fail on a test build, and a preprocess-build hook warns.

### Studio sheet

Built-in link `GenZTestSuitConfig.DefaultSheetUrl`. Tabs from the config's `sheetTabs` (default `Android Games ID's` gid 0, `Android Max ID's` gid 1078150226); `GenZSheetReader.Download` matches names against `/htmlview` (`ParseTabList`), downloads each as CSV and stacks them (`Stack`).
- Games tab (header row 1): `App Open`, `admob banner`, `admob int`, `admob rewarded`, `Admob AppID`, `GA Game Key`, `GA Secret Key`, `Adjust Token`, `AppMatrica`, `Taichi 2.0 Default Values`, `KeyStore`.
- MAX tab (header row 2): `Int`, `Rewarded`, `Banner`, `MRec`; the MAX SDK key is the unlabeled cell B1, found by pattern.
- Column matching is exact on purpose (`Banner` never matches `admob banner`). `InApp Key` is the Play licensing key, not product IDs.
- The **Game** list (shared by both tabs) defaults to this project's package; picking another game is a warning in Environment.

### Report persistence

The window keeps the last results across script reloads via `JsonUtility` and `[SerializeField]` view state. So `GenZTestReport.cs` classes must stay `[Serializable]` with plain public fields (no `readonly`, no `DateTime`). Strings come back as `""`, not `null`: test with `string.IsNullOrEmpty`.

## Game-side code checked by the suite

- `Assets/AdsScripts-GenzAds/` is the studio's shared ads/analytics layer (MAX + AdMob, App Open, UMP, Firebase Remote Config, GameAnalytics/AppMetrica/Adjust, Unity IAP, in-app review). Keep `GenZLog` calls when updating these scripts.
- `RemoteConfigManager.cs` is the only per-game Remote Config file: a MonoBehaviour on the ADs object with static fields whose starting values are also the defaults. `FirebaseManager` (identical across games) passes `RemoteConfigManager.Defaults()` to `SetDefaultsAsync` and calls `RemoteConfigManager.LoadValues(rc)` after each fetch + activate. A new key needs a field, a `Defaults()` entry and a `LoadValues()` read. The Taichi defaults (`taichi1DefaultRevenue` → `custom_rev`, `taichi2DefaultImpressions` → `custom_imp`, must equal the sheet) are Inspector fields copied into the statics in `Awake`. `LoadValues` writes the `[GenZ] RemoteConfig|values|…custom_imp=…` line the APK suite reads; keep its format.
- Other third-party folders under `Assets/` (Adjust, AppMetrica, Firebase, GameAnalytics, MaxSdk, GoogleMobileAds, Dreamteck, DOTween, NiceVibrations, …) shouldn't be modified.
