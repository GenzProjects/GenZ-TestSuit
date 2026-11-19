# GenZ-TestSuit

**Current release: GenZ-TestSuit-v1.0** (2026-10-22), package version `1.0.0`. The window shows it in its title and header, and every copied report starts with it. It's set in `GenZTestSuitInfo.Version` (`GenZTestSuitConfig.cs`). Every script in the package also starts with `// GenZ-TestSuit-v1.0`, and `CHANGELOG.md` records the release. The steps for a new version are in `CHANGELOG.md`.

GenZ-TestSuit is a pre-release test tool for GenZ Unity mobile games. It checks that each game ships with the right ad, in-app and analytics setup. The tool works in any project, because every studio game uses the same `AdsScripts-GenzAds` scripts, with small per-game differences.

It has three parts:

| Part | Status | What it does |
|---|---|---|
| **GenZ-EditorTestSuit** | ✅ Done (v6) | Runs in the Unity Editor. Results are split into four steps, shown as a step bar:<br>**1 · IDs Check:** every ad unit ID, app ID, SDK key and token (Ads object, Adjust, AppMetrica, GameAnalytics, Google Mobile Ads, AppLovin) must be filled in, correctly formatted, free of spaces, and equal to the studio Google Sheet.<br>**2 · SDK Integration:**<br>• SDKs installed, and their Android dependencies resolved into Gradle<br>• `google-services.json` for this package, and Firebase's generated files in sync with it<br>• settings assets in their `Resources` folders, SDK scene objects, required scripts<br>• release switches (test modes off, Adjust Production, GA Send Version on…)<br>• Gradle and manifest setup<br>**3 · Taichi:** Taichi 1.0 (revenue threshold → `custom_ad_impression`) and 2.0 (every N impressions → `count_ad_impression`) switched on, sent from every ad revenue, thresholds read safely from Remote Config, impression count equal to the sheet<br>**4 · Ad Calling Scenarios** (read from the scripts):<br>• MAX-first waterfall with AdMob fallback<br>• never two banners at once<br>• every format keeps loading<br>• every format reports its ad revenue (Adjust, Firebase, AppMetrica)<br>• AdsGapTimer reset on every interstitial and every rewarded grant, either in the SDK callbacks or at every call site<br>• Remote Config sets the cooldown<br>• every Remote Config key has a default<br>• common ad-code bugs |
| **GenZ-ApkTestSuit** | 🧪 v1 (not yet run on a phone) | For developers. Installs the release APK on a phone connected by USB, launches it, records the session while you play, and checks it from logcat. Results are split into five steps:<br>**1 · Build & Device:** the APK is this game's release build (package and version equal Player Settings, not debuggable, target SDK, 64-bit, permissions, release signature), installed and launched<br>**2 · SDK Init:** MAX, AdMob (and each mediation adapter), Firebase, Remote Config, GameAnalytics, Adjust, AppMetrica, UMP, IAP and FCM started without errors<br>**3 · Analytics Events:** Firebase events logged and uploaded (what DebugView shows), GameAnalytics events sent with this build's version, Adjust and AppMetrica events fired and their servers answering<br>**4 · Ads:** each ad format shown, its revenue reported to Adjust, AppMetrica and Firebase, Taichi<br>**5 · Stability:** no crash, ANR or script exception |
| **GenZ-DeepTestSuit** | 🧪 v1 (2026-10-23, not yet run on a phone) | For developers. **Apply Test Environment** switches the project to a test build: SDK logs on (MAX verbose, Adjust Verbose + Sandbox, AppMetrica, GameAnalytics verbose), AdMob test ads on the developer's phone with the real IDs, and a **shake panel** on the phone whose buttons call the game's own ad scripts (AdMob, MAX and the waterfall), so the game's ad code and integration are what gets tested. **Apply Release Environment** puts everything back. The Editor and APK suites fail while the Test Environment is on (§7). |

Open it from **GenZ → Test Suit** in the menu bar. **GenZ → Run Editor Test Suit** opens the window and runs the Editor part immediately. The APK part is the second tab (§6), the Deep part the third (§7).

---

## 1. Installing in another project (plug and play)

The suite is a Unity package, `com.genz.testsuit`, in the private repo `GenzProjects/GenZ-TestSuit`.

1. **Window → Package Manager → + → Add package from git URL…** and enter `https://github.com/GenzProjects/GenZ-TestSuit.git#v1.0.0` (the tag pins the release). Or add `"com.genz.testsuit": "https://github.com/GenzProjects/GenZ-TestSuit.git#v1.0.0"` to `Packages/manifest.json`. The repo is private, so git on that machine must be signed in to GitHub with access to it.
2. Open **GenZ → Test Suit** and run it. The first time, it creates the project's own config, `Assets/GenZ-TestSuit/GenZTestSuitConfig.asset`, already set to the studio sheet link and the `ADs` object name. Commit that asset with the game. The **Game** defaults to this project (the package in Build Settings), so the sheet lookup needs no setup.
3. For the Deep part, **Apply Test Environment** copies the phone panel from the package into `Assets/GenZ-DeepTest/GenZDeepTestPanel.cs` (§7). It's game code because it uses the MAX and AdMob types, so it can't stay in the package. When the package is updated, the next Apply replaces a panel from an older release.
4. For the APK part, the game's `AdsScripts-GenzAds` also needs `GenZLog.cs` and its `[GenZ]` calls (§6). Without them, step 2 of the APK suite fails with "No [GenZ] lines".

To update, change the tag in the URL (e.g. `#v1.1.0`). A project that had the old copied folder `Assets/Editor/GenZ-TestSuit/` deletes it first, but keeps `GenZTestSuitConfig.asset` (with its `.meta`), moved anywhere under `Assets/`: the suite finds the config by type.

How it stays portable:
- **Own editor-only assembly** (`GenZ.TestSuit.Editor.asmdef`). It never goes into a build, and it still compiles when the game's own scripts have compile errors.
- **No compile-time references to the ad scripts.** Components are found by **class name** and fields are read with `SerializedObject`. Private `[SerializeField]` fields work, and a project missing a script still compiles; the check just reports it as missing.
- **Loose name matching.** Field and sheet-column names are compared ignoring case, `_`, `-`, spaces and symbols, so `bannerID`, `BannerId` and `banner_id` all match. Each value also has a list of accepted alternative names (aliases).
- **Config auto-upgrade.** `GenZTestSuitConfig` has a hidden `configVersion`. When the defaults change, the code constant `CurrentVersion` is raised. Older assets then get the new default rules on load and fill an empty object name or sheet link, but keep their SDK Scene setting.
- **Rules are data, not code.** For a project whose scripts differ by those small 1–5% amounts, edit the rules in the config asset (**Edit Rules…**) instead of changing code.
- **The studio sheet link is built in** (`GenZTestSuitConfig.DefaultSheetUrl`). An empty link field falls back to it.

Requirements: Unity 2022.3 or newer (it uses `UnityWebRequest.Result`), and an internet connection for the sheet check. No other packages to install.

## 2. Files

The package root has `package.json` (the package version), `README.md`, `CHANGELOG.md`, this file in `Documentation~/`, and the scripts in `Editor/`:

| File | Role |
|---|---|
| `GenZ.TestSuit.Editor.asmdef` | Editor-only assembly `GenZ.TestSuit.Editor`, namespace `GenZ.TestSuit`. |
| `GenZTestSuitWindow.cs` | Menu items and the window: tabs, settings, run button, the step bar (1 IDs Check · 2 SDK Integration · 3 Taichi · 4 Ad Calling Scenarios), result filter tabs (All / Passed / Failed / Warnings) with ✔ / ✘ / ! icons, Copy Report, Open SDK Scene. |
| `GenZEditorTestSuit.cs` | Runs the checks: finds the scene, finds the Ads object, checks each script and field, compares with the sheet. |
| `GenZSheetReader.cs` | Converts the Google Sheet link to a CSV download, downloads it, parses the CSV, and finds the game by package name. |
| `GenZTestSuitConfig.cs` | `ScriptableObject` with the settings and the rules (scripts, field aliases, sheet column aliases). `DefaultRules()` holds the built-in defaults. |
| `GenZTestReport.cs` | Result model (`Pass / Info / Warn / Fail`, the step it belongs to, an optional file and line) and Markdown export grouped by step. |
| `GenZSourceModel.cs` | A light C# source reader without Roslyn. It removes comments, hides string contents while parsing structure, splits files into classes and methods, and answers "who references this method" and "does this method, or anything it calls, contain X". It has no Unity dependency. |
| `GenZCodeChecks.cs` | The code checks (§4 "Code checks"). It has no UnityEditor dependency, so it can also run from a plain `dotnet` console harness. |
| `GenZIdFixer.cs` | **Auto Apply IDs**: writes the sheet values recorded by the last run into the settings assets and SDK scene components (§4 "Auto Apply IDs"). |
| `GenZTaichiChecks.cs` | The Taichi checks (§4 "Taichi"), part of `GenZCodeChecks` (partial class). No UnityEditor dependency. |
| `GenZIntegrationChecks.cs` | The SDK file checks (§4 "SDK files"): Firebase config, SDKs installed and resolved, Gradle and manifest. It only reads files, so it can also run from the console harness. |
| `GenZApkChecks.cs` | The APK suite's checks (§6): reads `aapt2 dump badging`, `apksigner` and logcat text, including the `[GenZ]` lines. No UnityEditor dependency, so the console harness can run it on a saved log. |
| `GenZApkTestSuit.cs` | The APK suite's device side: finds `adb`, `aapt2` and `apksigner` in Unity's Android SDK, installs, prepares the log, launches, reads the log, saves it. |
| `GenZPlayerSettingsChecks.cs` | Step 2's Android Player Settings checks. |
| `GenZSigning.cs` | The Android keystore: Publishing Settings, the sheet's `KeyStore` column, the certificate read with keytool; the APK suite's expected signer. |
| `GenZDeepTestSuit.cs` | GenZ-DeepTestSuit (§7): reads and applies the Test / Release Environment, writes the phone panel's settings file, the release guard (Editor suite step 2), Firebase DebugView on the phone, and a build hook that warns about test builds. |
| `GenZDeepTestSuitWindow.cs` | The window's third tab (a `partial` of `GenZTestSuitWindow`). |
| `Templates/GenZDeepTestPanel.cs.txt` | The phone panel's source (§7). Apply Test Environment copies it into the game. |
| `CHANGELOG.md` (package root) | The release name, its contents, and how to release a new version. |

In the game's project (not in the package): `Assets/GenZ-TestSuit/GenZTestSuitConfig.asset`, created automatically, holds this project's settings and rules. The APK suite needs **`GenZLog.cs` in `AdsScripts-GenzAds`** and its calls in the ad scripts (§6), and the Deep suite installs **`Assets/GenZ-DeepTest/GenZDeepTestPanel.cs`** (§7). That script is game code (not editor-only), but all of it sits inside `#if GENZ_DEEP_TEST`, so release builds compile nothing from it.

## 3. Window settings

| Setting | Meaning | Default |
|---|---|---|
| SDK Scene | Scene that holds the Ads object. | Empty = auto-detect from the first enabled build scenes, preferring names containing Consent, Splash, Loading, Init or Boot. |
| Ads Object Name | Name of the Ads GameObject, ignoring case. If no object has this name, the suite auto-detects it and shows a warning (see §4). | `ADs` |
| Google Sheet Link | Sheet with every game's IDs. | The studio sheet (an empty field also uses it). |
| Verify Against Sheet | Turns the sheet comparison off or on. | On |
| Run Scene & Asset Checks | Steps 1 and 2: the Ads object's scripts and IDs, the SDK scene objects, the settings assets. (Field `runIdChecks`.) | On |
| Run SDK File Checks | Step 2: the SDK file checks in §4. (Field `runIntegrationChecks`.) | On |
| Run Code Checks | Step 2's required-scripts check and all of step 4. | On |
| Run Taichi Checks | Step 3. (Field `taichi.enabled`.) | On |
| Game | The game whose sheet row the project is checked against. It's a searchable list of every game in the sheet (type part of a name or package), shared with the APK tab. **Load Games** downloads the sheet again. Picking another game than the Build Settings package gives a ! warning in Environment, because the build would still ship with the Player Settings package. | This project (Build Settings package) |

Buttons: **Edit Rules…** selects the config asset in the Inspector. **Reset Rules** restores `DefaultRules()`.

## 4. What GenZ-EditorTestSuit checks

### The four steps
Every check belongs to one step. A section can hold checks of more than one step. For example, the **Adjust** section shows its App Token under step 1, and "Object in scene" and "Environment is Production" under step 2.

| Step | What goes there |
|---|---|
| **1 · IDs Check** | The Environment section (package, sheet, SDK scene, Ads object), and every rule field whose kind is an ID: `AdMobUnitId`, `AdMobAppId`, `MaxSdkKey`, `MaxUnitId`, `AdjustToken`, `GameAnalyticsGameKey`, `GameAnalyticsSecretKey`, `AppMetricaApiKey`, `InAppList`, `Text`. |
| **2 · SDK Integration** | Everything else from the rules: script attached, object in scene / active / prefab, settings asset found in the right folder, and the `MustBeFalse` / `MustBeTrue` / `Expected` / `NumberAboveZero` fields. Also the SDK file checks below, the required-scripts code check, and compile errors. |
| **3 · Taichi** | The Taichi switches and both versions' logic (see "Taichi" below). |
| **4 · Ad Calling Scenarios** | All code checks except required scripts. |

### Finding the scene and the Ads object
1. **Candidate scenes:** the configured SDK Scene; otherwise the first enabled build scenes with Consent/Splash-like names, plus build scenes 0 and 1.
2. Each candidate is opened **additively and read-only**, then closed without saving. A scene that is already open is read as-is, so unsaved changes are included.
3. **Ads object:** an object with the configured name (default `ADs`, ignoring case, so `Ads` and `ADS` match too) wins, even if it's inactive. If none has that name, every GameObject is scored: **+10 for each rule script on it** and **+5 if its name looks like `Ads`, `Ad`, `AdsManager`** and so on. The highest score wins, and the result shows a ! warning saying the name wasn't found. The report shows the object's hierarchy path, how it was found, and how many SDK scripts are on it.

### Scripts that must be on the Ads object
Each script counts as found if it's on the object itself (✔). If it's only on a child object, that's a warning (!). If it's missing entirely, that's a failure (✘). A Missing Script on the object is mentioned, since it usually means compile errors.

| # | Script (accepted class names) | Fields checked |
|---|---|---|
| 1 | `AdsIds` / `AdIds` | **In-App IDs** (`InAppIds[].Id`): at least one non-empty ID. Warns if an ID isn't a valid Google Play product ID (lowercase `a-z 0-9 . _`) or appears twice. |
| 2 | `AdMob` / `Admob` | **Banner ID** (`bannerID`), **Interstitial ID** (`interstitialID`), **Rewarded ID** (`rewardID`), and **`isTestIdOn` must be false** (only checked if the field exists). |
| 3 | `AdsManager` | **MAX SDK Key** (`maxSdkKey`), **Interstitial** (`InterstitialAdUnitId_Android`), **Banner** (`BannerAdUnitId`), **Rewarded** (`RewardedAdUnitId_Android`), **MREC** (`MRecAdUnitId`). The `_IOS` names are also accepted, because the field names change with the build target. |
| 4 | `AppOpen_Code` | **App Open ID** (`AppOpenId`). |
| 5 | `FirebaseManager` | Presence only. |
| 6 | `RemoteConfigManager` | Optional. **Taichi 1.0 default revenue** and **Taichi 2.0 default impressions** (Inspector) must be > 0. |
| 7 | `Analytics` | Presence only. |
| 8 | `unityInApp` | Presence only. |

`BigBanner` isn't checked: the MREC is shown with `AdsManager.ShowMRECBanner` / `HideMRECBanner`.

### Value validation (without the sheet)
| Kind | ✘ Fail | ! Warn | ✔ Pass |
|---|---|---|---|
| AdMob unit ID | Empty; a Google sample/test ID (`ca-app-pub-3940256099942544…`); an AdMob **App** ID containing `~` | Doesn't match `ca-app-pub-<16 digits>/<10 digits>` | Correct format |
| MAX unit ID | Empty | Not 16 hex characters | Correct format |
| MAX SDK key | Empty | Not 86 characters of `A-Z a-z 0-9 _ -` | Correct format |
| AdMob App ID | Empty; Google sample App ID; an ad unit ID containing `/` | Doesn't match `ca-app-pub-<16 digits>~<10 digits>` | Correct format |
| Adjust token | Empty | Not 12 lowercase letters/digits | Correct format |
| AppMetrica API key | Empty | Not a GUID (8-4-4-4-12 hex) | Correct format |
| GA game key / secret key | Empty; Android platform not added | Not 32 / 40 hex characters | Correct format |
| In-app list | No IDs; an ID with a space | Invalid Play ID, duplicate | One or more IDs |
| Must be false / Must be true | Wrong value, or the field isn't a bool | — | Right value |
| Expected (e.g. Adjust environment) | Value ≠ the expected value (enums compared by name) | — | Equal |

- **Whitespace (all IDs):** any space, tab or line break in a stored ID is a ✘ failure, even at the start or end, where it's easy to miss. The value is checked as stored, before trimming. Sheet values *are* trimmed, because some sheet cells end with a tab.
- **Duplicates:** one ad unit ID used in two places (for example the same ID as banner and interstitial) gives a warning on the second one.
- **Cross-checks (`sameAs`):** a field can be required to equal another field already read in the same run. Defaults: AppLovin `sdkKey` = `AdsManager` MAX SDK Key, and AppLovin `adMobAndroidAppId` = Google Mobile Ads Android App ID.

AdMob's default field values are Google's sample IDs, so a fresh `AdMob` component that was never filled in **fails**, which is intended.

### Other SDK objects in the same scene
These are searched in the SDK scene by name, ignoring case. If no object has the name, the suite looks for one carrying the script and shows a warning. An object found in another candidate scene also gives a warning.

| Object (names accepted) | Script | Checks |
|---|---|---|
| `Adjust` | `Adjust` | Should be a prefab instance (warning if not). **App Token** (`appToken`): Adjust format (12 lowercase letters/digits), no spaces, equal to the sheet's `Adjust Token`. **Environment** (`environment`) must be **Production**. |
| `GameAnalytics` | `GameAnalytics` | Exists, should be a prefab instance, and has the script. |
| `AdsGapTimer` / `AdGapTimer` / `AdsTimer` | `AdsGapTimer` | Exists in the SDK scene. **Default cooldown** (`timeLeft`) must be **> 0**. This is the value used until Remote Config overrides it. |
| `UmpManager` / `UMPManager` / `UMP` | `UmpManager` | **Active**: the object must be SetActive true, and so must its parents. **Test Mode** (`isTestMode`) must be **false**. |
| `AppMetrica` | `AppMetrica` | Should be a prefab instance. **API Key** (`ApiKey`): GUID format (8-4-4-4-12 hex), no spaces, equal to the sheet's `AppMatrica` column (compared ignoring case). |

### Settings assets
These are found by file name **and** ScriptableObject class, and must sit in the expected folder. The SDKs load them with `Resources.Load`, so one in the wrong place fails. If there are several copies in the right folder, that's a warning.

| Asset (class) | Must be under | Checks |
|---|---|---|
| `Settings.asset` (GameAnalytics `Settings`) | `Resources/GameAnalytics` | **Android platform added** (`Platforms` contains Android). **Game Key** and **Secret Key** for Android: the `gameKey`/`secretKey` entry at the Android index. Game key is 32 hex characters and secret key 40; no spaces; both equal to the sheet's `GA Game Key` / `GA Secret Key`. **Send Version** (`UsePlayerSettingsBuildNumber`, Advanced Settings → "Send Version* (Android, iOS) as build number") must be **ON**. |
| `GoogleMobileAdsSettings.asset` | `Resources` (normally `GoogleMobileAds/Resources`) | **Android App ID** (`adMobAndroidAppId`): `ca-app-pub-<16>~<10>` format, not Google's sample App ID, not an ad unit ID, no spaces, equal to the sheet's `Admob AppID`. **Optimize Initialization** and **Optimize Ad Loading** must be **ON**. |
| `AppLovinSettings.asset` (Menu **AppLovin → Integration Manager**) | `Resources` (normally `MaxSdk/Resources`) | **SDK Key** (`sdkKey`): equal to the sheet's key **and** to `AdsManager.maxSdkKey`. **AdMob Android App ID** (`adMobAndroidAppId`): App ID format, equal to the sheet's `Admob AppID` **and** to the Google Mobile Ads Settings Android App ID. |

### Google Sheet verification
- **Default sheet:** the studio IDs sheet (`DefaultSheetUrl` in `GenZTestSuitConfig.cs`): https://docs.google.com/spreadsheets/d/1kr1zIgQJ0fujexkbj_xgfrUb_hJv-RbllTtqTIQ7O4Y/edit?usp=sharing
- **Link formats:** the normal share link (`…/spreadsheets/d/<id>/edit#gid=<tab>`), a published link (`…/d/e/<id>/pubhtml`), or a direct CSV link. The `gid` in the link selects the tab.
- **Access:** the sheet must be **"Anyone with the link: Viewer"** or **published to the web**. A private sheet returns a Google login page, and the suite reports exactly that.
- **Package name:** the **Game** picked in Settings; by default this project's package from Build Settings.

#### Layout of the studio sheet (as of 2026-10-23)
The studio spreadsheet (`GenZTestSuitConfig.DefaultSheetUrl`) has five tabs: `IOS Games ID's`, `IOS MAX Ids`, `Android Games ID's`, `Android Max ID's` and `Stors`. The suite reads the two Android ones, listed in the config's **Sheet Tabs** (`sheetTabs`):

| Tab (gid) | Content | Headers used by the suite |
|---|---|---|
| **Android Games ID's** (0): header row 1, one row per game | AdMob IDs, Adjust, GameAnalytics, AppMetrica, Taichi 2.0 | `Package Name`, `App Open`, `admob banner`, `admob int`, `admob rewarded`, `Admob AppID`, `GA Game Key`, `GA Secret Key`, `Adjust Token`, `AppMatrica`, `Taichi 2.0 Default Values` (present but not checked: `admob Big-banner`, `admob native`, `unity id`, `InApp Key`) |
| **Android Max ID's** (1078150226): header row 2, one row per game | AppLovin MAX ad unit IDs | `Package Name`, `Int`, `Rewarded`, `Banner`, `MRec` |
| **Android Max ID's**, cell **B1** | The single MAX SDK key for the whole AppLovin account | No header; found by pattern (see below) |

**How the tabs are read:** the suite reads the spreadsheet's tab list from its public HTML view and matches each **Sheet Tabs** name, ignoring case, spaces and symbols. It downloads each matched tab as CSV and stacks them, with an empty row between, so each table keeps its own header. A listed tab the spreadsheet doesn't have is skipped. The stored gid is used only when the tab list can't be read. If no tab is found, or the list is empty, the tab in the link is read, as before. The Environment section's **Google Sheet** row names the tabs it read. Cells are trimmed, so a stray tab or newline after an ID in the sheet doesn't matter.

**Picking the right IDs (AdMob vs MAX):**
- The suite finds **every** cell equal to the package name. Each one belongs to a table, and that table's header row is the nearest row above with a `Package Name` header in the same column.
- Only the **contiguous header block** around that column is read: the header cells to the left and right, stopping at the first empty header. So the games table never sees MAX headers, and the reverse (on separate tabs or side by side on one tab).
- Both tables' values are merged. Shared headers like `Game Name` keep the first table's value.
- Column matching is **exact**, ignoring case, spaces and symbols. That's why the MAX rule's `Banner` matches only the MAX `Banner` column and never `admob banner`. There's no "contains" matching, on purpose.
- **MAX SDK key:** if no column such as `MAX SDK Key` exists, the suite searches the whole sheet for the one distinct cell matching the SDK-key pattern (86 characters of `A-Z a-z 0-9 _ -`). If there are zero or several such cells, it reports a warning instead of guessing.
- **In-app IDs aren't in the sheet.** `InApp Key` is the Google Play licensing public key, not product IDs. In-app IDs are only checked in the project (at least one, valid, no duplicates). If you add a product-ID column to the sheet later, put its header in the AdsIds rule's `sheetColumns`.

#### Default field → sheet column mapping
| Field (script) | Sheet columns tried, in order |
|---|---|
| App Open ID (`AppOpen_Code.AppOpenId`) | `App Open`, `AdMob App Open`, `App Open ID` |
| AdMob Banner (`AdMob.bannerID`) | `admob banner`, `AdMob Banner ID` |
| AdMob Interstitial (`AdMob.interstitialID`) | `admob int`, `AdMob Interstitial`, `AdMob Inter` |
| AdMob Rewarded (`AdMob.rewardID`) | `admob rewarded`, `AdMob Reward` |
| MAX SDK Key (`AdsManager.maxSdkKey`) | `MAX SDK Key`, `Applovin SDK Key`, `SDK Key`, then the pattern search |
| MAX Interstitial (`AdsManager.InterstitialAdUnitId_Android`) | `Int`, `MAX Int`, `MAX Interstitial` |
| MAX Banner (`AdsManager.BannerAdUnitId`) | `Banner`, `MAX Banner` |
| MAX Rewarded (`AdsManager.RewardedAdUnitId_Android`) | `Rewarded`, `MAX Rewarded` |
| MAX MREC (`AdsManager.MRecAdUnitId`) | `MRec`, `MAX MREC` |
| In-App IDs (`AdsIds.InAppIds`) | none (not in the sheet) |
| Adjust App Token (`Adjust.appToken`) | `Adjust Token`, `Adjust App Token` |
| AppMetrica API Key (`AppMetrica.ApiKey`) | `AppMatrica`, `AppMetrica`, `AppMetrica Key`, `AppMetrica API Key` |
| GA Game Key / Secret Key (GameAnalytics `Settings`, Android entry) | `GA Game Key` / `GA Secret Key` (also `GameAnalytics Game Key` / `GameAnalytics Secret Key`) |
| AdMob App ID (`GoogleMobileAdsSettings.adMobAndroidAppId` and `AppLovinSettings.adMobAndroidAppId`) | `Admob AppID`, `AdMob App ID` |
| AppLovin SDK Key (`AppLovinSettings.sdkKey`) | same as MAX SDK Key above |

#### Auto Apply IDs
There are two buttons:

| Button | Where | What it fixes |
|---|---|---|
| **Auto Correct N Wrong IDs** (orange) | Under the step bar, and next to Apply All in Settings, whenever the last run found wrong IDs | Exactly the IDs that run found **empty, different from the sheet, or padded with spaces**, and that the sheet has a value for. No new download; its tooltip and the bar list which ones. |
| **Auto Apply All IDs from Sheet** (blue) | Always in Settings, whether or not the suite has run | Downloads the sheet again, re-checks every ID, and writes everything that differs. |

The bar under the step bar says "**N wrong IDs**", names them, and has both buttons on the right (Apply All, then Correct). Those rows also say "Auto Apply can set it".

**Auto Apply All** runs the suite first, with the ID checks and the sheet comparison switched on for that run even if they're off in Settings (in memory only; the setting isn't saved). Then it applies what differs. If the sheet can't be read, or every ID already matches, it says so and changes nothing.

1. Click either button. A dialog lists every change (`field: current → sheet value`; SDK keys and GA secret keys shortened). Nothing changes until you press **Apply**.
2. Each value is written with `SerializedObject` into the exact field the check read:
   - settings assets (GameAnalytics `Settings` Android entry, `GoogleMobileAdsSettings`, `AppLovinSettings`) are saved;
   - components in the SDK scene (AdMob, AdsManager, AppOpen_Code, Adjust, AppMetrica…) are found by scene, hierarchy path and class name. A scene that wasn't open is opened, saved and closed. A scene you already have open is only marked modified, so your own unsaved edits are never saved for you; save it yourself. On a prefab instance, the value is kept as a scene override (where the suite reads it).
   - the **Taichi 2.0 default impressions** (step 3) goes into `RemoteConfigManager`'s Inspector field when the game has one. A game without it (older ad scripts) keeps the count in code, so Auto Apply writes the sheet's number into the script, at the spot the check compared: the key's Remote Config default (`{ "custom_imp", 50L }` → `30L`, the suffix is kept), or, when that default names a field, the field's starting value. Only a plain number is written; the line must still be in the file exactly once, as the check read it, or it's reported as changed ("run the suite again"). The encoding and line endings are kept, and the script is reimported, so Unity recompiles. The dialog lists the scripts it edits.
3. The suite runs again, and a summary shows how many were applied, any that couldn't be, and how many still differ. Every change is also logged in the Console.

Not covered: IDs with no sheet value (missing column or empty cell), in-app product IDs (not in the sheet), and switches such as Test Mode (step 2). It's blocked in Play mode. Since the sheet is the source of truth, a wrong value **in the sheet** is applied as is; check the dialog.

#### Comparison results
- A value equal to the sheet adds "Matches sheet". A different value is ✘ "Sheet expects …".
- A column that can't be found, or an empty sheet cell, is a ! warning. For example, a game that has no row in the MAX table gives warnings on the four MAX IDs.
- MAX IDs are compared ignoring case; AdMob IDs must match exactly. Values are trimmed first, because some sheet cells end with a tab.
- The Environment row "Google Sheet" lists every header found for this package, which helps when correcting column aliases.
- If the package isn't in any table, the result is ✘ "Package … was not found in the sheet".

### SDK files (step 2)
These only read files under `Assets` and `ProjectSettings`; no scene is opened.

| Section | Check | Fail / warn when |
|---|---|---|
| **SDK · Firebase config** | `google-services.json` | The file must exist anywhere under `Assets` and belong to this game: its Android apps (`client[].client_info.android_client_info.package_name`) must include the Build Settings package. ✘ Missing; ✘ not a valid Firebase Android config; ✘ no Android app for this package, so it belongs to another game. With several copies, each gets its own row (a wrong copy fails even if another is right), plus a ! "Only one google-services.json" warning. **Open** jumps to the `package_name` line. |
| | Generated Android config | `Plugins/Android/FirebaseApp.androidlib/res/values/google-services.xml` is what the build uses. ✘ Its `project_id` or `google_app_id` differs from `google-services.json` (a stale copy from another project); ! not generated yet. Fix: reimport `google-services.json`. |
| | Editor config (StreamingAssets) | ! `google-services-desktop.json` belongs to another Firebase project (Play mode only). |
| | Firebase SDK modules | ✘ A DLL in `firebaseModules` (default `Firebase.App`, `Firebase.Analytics`, `Firebase.RemoteConfig`) isn't installed. |
| **SDK · Installed and resolved** | `<SDK> installed` | ✘ A folder in `requiredSdks` (Firebase, GameAnalytics, AppMetrica, Adjust, MaxSdk, GoogleMobileAds) is missing. |
| | `<SDK> resolved` | For every `*Dependencies.xml` (grouped by SDK folder; each MAX adapter separately): ✘ an `androidPackage` spec isn't in `mainTemplate.gradle`, so Force Resolve wasn't run after adding/updating the SDK; ! the Gradle line attributed to that file has a different version. ℹ when Custom Main Gradle Template is off (nothing to read). |
| **SDK · AppLovin MAX adapters (mediation networks)** | `<Network> adapter` | Adapters are matched by Android package (`group:artifact`, version ignored), read from every `*Dependencies.xml` under `Assets` (XML comments ignored), `mainTemplate.gradle`, adapter `.aar`/`.jar` files in `Plugins/Android` and the `MaxSdk/Mediation` folders. ✘ An adapter in `integration.maxAdapters` is missing: the 14 Integration Manager adapters (BidMachine, Bigo Ads, Pangle, Meta, DT Exchange, Google AdMob, Google Ad Manager, InMobi, Mintegral, Smaato, Unity Ads, Liftoff/Vungle, Yandex, YSO Network) and the custom networks AdSurge (`com.adsurge.sdk:adapter-for-max`, `Assets/Editor/AdSurgeDependencies.xml`) and Helix / Voodoo ADN (`io.adn:adn-applovin-adapter`, `Assets/Editor/HelixDependencies.xml`). ✘ It's only left in Gradle / Plugins/Android (its Dependencies.xml is gone, so the next Force Resolve drops it). ℹ when MAX isn't installed. |
| | All studio adapters installed | ✘ Any of the above is missing (lists them). |
| | `<Network> removed` | ✘ A network in `integration.forbiddenAdapters` is still anywhere above: IronSource's MAX adapter (or its `MaxSdk/Mediation/IronSource` folder), the IronSource SDK (`com.ironsource.sdk`) or IronSource / LevelPlay (`com.unity3d.ads-mediation`). |
| | Unlisted adapter · `<package>` | ! An ad network adapter (matching `integration.adapterPattern`: `com.applovin.mediation:*-adapter`, or a name with `adapter-for-max` / `applovin-adapter` / `max-adapter`) is in neither list. Remove it, or add it to `maxAdapters` if the studio now uses it. |
| **SDK · Android build setup** | AndroidX + Jetifier | ✘ `gradleTemplate.properties` lacks `android.useAndroidX=true` or `android.enableJetifier=true` (only when the custom properties template is on). |
| | aapt2 override | ✘ `android.aapt2FromMavenOverride` points to a file that doesn't exist; ! it points into another Unity version's folder than `ProjectVersion.txt`. |
| | Manifest not debuggable | ✘ The custom main manifest has `android:debuggable="true"`. |
| **SDK · GameAnalytics ad revenue formats (MAX = AdMob)** | Define `gameanalytics_max_enabled` / `gameanalytics_admob_enabled` | ✘ Not in the Android Scripting Define Symbols: GameAnalytics compiles that network's ad revenue (ILRD) code out. |
| | GA format · Interstitial / Rewarded / Banner / App Open | GameAnalytics groups ad revenue by the `adunit_format` string each integration sends. `GAAdMobIntegration.cs` must send the same string as `GAMaxIntegration.cs` for each format (this project: `INTER`, `REWARDED`, `BANNER`, `APP_OPEN`); otherwise GA shows one format as two. ✘ Interstitial or Rewarded differs (e.g. AdMob `"INTERSTITIAL"`, MAX `"INTER"`); ! Banner or App Open differs; ! the string couldn't be read. Without `GAMaxIntegration.cs`, AdMob is compared with `integration.gaAdFormatFallback`. **Fix GA Ad Formats** (below). |
| | GA ← MAX revenue / GA ← AdMob Interstitial, Rewarded revenue | ! No `GameAnalyticsILRD.SubscribeMaxImpressions()` call, or no `SubscribeAdMobImpressions(…)` for the interstitial / rewarded ad (the ad variable's type is read from its declaration in the same script; ℹ when it can't be resolved), in the game's scripts outside `Assets/GameAnalytics`. |

**Fix GA Ad Formats** (orange, under the step bar whenever a GA format row differs): replaces each wrong string in `GAAdMobIntegration.cs` with MAX's, on the exact line the check read (refused if that line changed since the run), after a confirmation listing every change, then re-runs the suite. The file is in the GameAnalytics SDK folder, so updating or reimporting GameAnalytics brings the old strings back: run the suite after every GA update.

The **settings assets** checks above (GameAnalytics `Settings` under `Resources/GameAnalytics`, `GoogleMobileAdsSettings`, `AppLovinSettings`) also show in step 2.

### Player Settings (Android) (step 2)
Read from Player Settings (`GenZPlayerSettingsChecks.cs`), with the **Run SDK File Checks** toggle. Every check fails when the setting differs, and says where to change it.

| Check | Must be (default) |
|---|---|
| Auto Graphics API | Off |
| Graphics APIs | No Vulkan (`forbiddenGraphicsApis`). Also fails while Auto Graphics API is on, since Unity then picks the APIs itself. |
| Require ES3.1 | Off |
| Require ES3.1+AEP | On |
| Require ES3.2 | On |
| Scripting Backend | IL2CPP |
| Target Architectures | Exactly ARMv7 and ARM64 (missing ones say "Tick", extra ones "Untick") |
| Minimum API Level | Android 7.0 (API 24). On **Unity 6** (6000.x) also Android 7.1 (API 25): `minSdkUnity6`, default 24 or 25. |
| Target API Level | Android 16 (API 36). "Automatic (highest installed)" fails. |
| Show Splash Screen | **Unity 6 only**: Off (`showSplashScreenUnity6`), Player Settings → Splash Image → Show Splash Screen. Not checked on older Unity. |

The Unity version is read from the running Editor (`Application.unityVersion`). The expected values are in the config asset under `playerSettings` (`AndroidPlayerRules`), so a game with other needs changes them there. The two Unity 6 fields are new with their defaults, so existing config assets get them without a version bump. This project passes all of them (2026-10-22).

The **Open** button on these rows opens the file: scripts at the line, other files (json, xml, gradle) in their default app.

### Android signing (keystore) (steps 1 and 2)
`GenZSigning.cs`, settings in the config's `signing`. It runs only when the build target is Android; on another target it's reported as info.

| Check | Step | ✘ Fail / ! Warn |
|---|---|---|
| Custom keystore | 2 | ✘ Off in Publishing Settings: builds would be signed with Unity's debug key, which Google Play rejects |
| Keystore file | 2 | ✘ The file in Publishing Settings isn't on this computer (the build fails) |
| Key alias | 2 | ✘ None selected |
| **Keystore = sheet** | 1 | ✘ The sheet's `KeyStore` value (e.g. `gamestoplay`) matches neither the keystore file name (without extension) nor the key alias, ignoring case and symbols: the wrong keystore for this game. ! The cell is empty. ℹ No such column, or no sheet. |
| Certificate | 2 | ℹ The key's SHA-256 and owner, read with `keytool` from Unity's JDK. A JKS keystore lists them without a password; a PKCS12 keystore needs the password in Publishing Settings (! when it can't be read). |

The sheet columns tried are `signing.sheetColumns` (`KeyStore`, `Keystore Name`, `Keystore Alias`, `Signing Key`). On 2026-10-23 only One Go Line's cell is filled (`gamestoplay`, = `D:/keystore/GamesToPlay.keystore`, alias `gamestoplay`).

### Taichi (step 3)
Taichi sends extra Firebase revenue events that Google Ads can bid on. The studio code is in `FirebaseManager` (`#region TaicheCodes`):
- **Taichi 1.0** (`CheckTheRevenue`): adds up each ad's revenue (saved in PlayerPrefs); when the total reaches the Remote Config value `custom_rev` (USD), it logs `custom_ad_impression` with value + currency and starts again from 0.
- **Taichi 2.0** (`CheckTheImpressionForRevenue`): counts impressions and their revenue; every `custom_imp` impressions it logs `count_ad_impression` with value + currency and resets.
- Each is switched on by a bool on `FirebaseManager` (`Taiche1_0`, `Taiche2_0`), read from the SDK scene.

| Section | Check | Fail / warn when |
|---|---|---|
| **Taichi · Switches** | `Taichi 1.0 / 2.0 switched on` | ✘ off while required (`requireTaichi2` is on by default); ℹ off while optional (`requireTaichi1` is off by default, and the version's logic isn't checked); ! the switch field or the SDK scene couldn't be read. ✘ both off. |
| **Taichi 1.0 / 2.0** | Sends "`<event>`" | ✘ No ad/analytics script logs the event. |
| | Event has value + currency | ✘ `ParameterValue` / `ParameterCurrency` (or `"value"` / `"currency"`) missing. |
| | Respects its switch | ! The method doesn't check its switch. |
| | Called on every ad revenue | ✘ A format's paid handler (the same 8 as the revenue check) doesn't call it, directly or 2 levels down. |
| | Progress saved between sessions | ! Nothing is written to PlayerPrefs. |
| | Reset after sending | ! Nothing is set back to 0 after the event. |
| | Threshold from Remote Config "`<key>`" | ✘ The key is never read in `remoteConfigClasses`; ! it's read into a field the Taichi method doesn't use (one assignment hop is followed, e.g. `custom_rev` → `revenue_maxValue_St` → `revenue_maxValue`). |
| | Read safely on every phone | ✘ `Parse` / `TryParse` without `CultureInfo.InvariantCulture`: on a comma-decimal phone "0.05" becomes 5 or throws; ! `Parse` without `Try` throws on a missing key. |
| | Safe if "`<key>`" is missing | ! No Remote Config default and no `> 0` check, so a missing key makes the threshold 0 and the event fires on every ad / impression. |
| | Impression count = sheet (2.0) | ✘ The Remote Config default, or else the field's initial value (`impressionValue_ForRevenue = 30`), differs from the sheet's `Taichi 2.0 Default Values` for this game. |

On this project (2026-10-14): 1.0 is off (optional, ℹ). 2.0 is on and fully wired, and 30 matches the sheet. One ! warning: `custom_imp` has no Remote Config default. If 1.0 is switched on, it fails "Read safely": `revenue_maxValue = double.Parse(revenue_maxValue_St)` has no InvariantCulture (`FirebaseManager.cs:212`).

### Code checks
Code checks read the C# scripts directly; nothing is compiled or played. Scanned: every `.cs` under `Assets`, except Editor folders and the third-party folders in `excludeFolders`. **Ad scripts** are the classes in `adScripts` plus the gap timer. Every other scanned class counts as **game code**, and its calls into the ad scripts are the *call sites*.

The checks anchor on **SDK API calls**, not on method names, so a project where `ShowInterstitial` is called `ShowInter` still works. The anchors include `MaxSdk.ShowInterstitial`, `MaxSdk.IsRewardedAdReady`, `MaxSdkCallbacks.Banner.OnAdLoadedEvent`, `InterstitialAd.Load`, `AppOpenAd.Load`, `new BannerView` and `.OnAdFullScreenContentClosed`. Wrapper methods are followed through the call graph, up to two levels. Every finding has an **Open** button that jumps to the file and line.

| Section | Check | Fail / warn when |
|---|---|---|
| **Scripts** | Required scripts exist | Any class in `requiredScripts` is missing: AdMob, AdsIds, AdsManager, Analytics, AppmetricaAnalytics, AppOpen_Code, FirebaseManager, GA_Aanalytics, InAppCalling, InAppReviewManager, UmpManager, unityInApp, AdsGapTimer. |
| | Compile errors | `EditorUtility.scriptCompilationFailed` is true (reported in Environment). |
| **Ad scripts version (GenzMonetization)** (step 4) | Same version in every script | Since GenZ Ads v1.33 every `.cs` in the ad scripts folder (the folder of `AdsManager`, else a folder named `AdsScripts…`; Editor subfolders skipped) starts with `// GenzMonetization-v1.33`. ✘ Scripts state different versions or some have no header (one row per odd script, opening it): a script was copied from another release. ! No script has the header (ad scripts from before v1.33). |
| | Expected version | ! The scripts' version is older than `GenZTestSuitInfo.AdsScriptsVersion` (1.33; per project `codeChecks.adsScriptsVersion`); ℹ newer (update the suite). Versions compare part by part as integers. |
| | Release.txt | ! The newest `v…` in the folder's `Release.txt` differs from the scripts' version. |
| **Ad waterfall** | Interstitial / Rewarded: MAX first, AdMob fallback | ✘ No method checks MAX readiness before showing (directly, or through a helper like `bool IsMaxInterstitialReady() { return … MaxSdk.IsInterstitialReady(…); }`); ✘ no AdMob fallback in that method; ✘ the AdMob call comes before the MAX call. |
| | …vendor switch (`useSdkVendorSwitch`) | Only when a field in `vendorSwitchFields` is declared in the ad scripts; a project without it gets only the MAX-first checks. The switch picks the network order: 0 = MAX first every time, 1 = MAX / AdMob one by one, 2 = MAX, MAX, AdMob. Looked for: an `if (<AdMob turn>) { … }` in the show method, before MAX is tried, whose condition reads the switch directly or through a helper (e.g. `IsAdmobTurn(count)`). ! the switch isn't used to pick the network (every value behaves like 0); ! no `case 1:` / `case 2:` or `switch == 1` / `== 2`; ! the switch is read but no AdMob-first branch is found. That branch is left out of the MAX-first check below it. |
| | …AdMob turn — AdMob first, MAX fallback | ✘ The AdMob-turn branch (or the method it calls) never tries MAX, so there's no ad when AdMob has none ready. |
| | …AdMob turn — MAX fallback only when ready | ✘ MAX is shown on AdMob's turn without first checking it's initialized and has an ad ready. |
| | …AdMob fallback when MAX isn't initialized | ✘ An `if (!…Init…) return;` sits before the fallback. While MAX is still initializing, or failed to, neither network shows the ad. |
| | App Open is AdMob only | ✘ No `AppOpenAd.Load`; ! MAX App Open is also used. |
| | MREC is MAX only | ✘ No `MaxSdk.CreateMRec`; ! an AdMob `MediumRectangle` is also used. |
| | App Open skipped after a fullscreen ad | ! The App Open show method doesn't check any flag that the interstitial/rewarded show code sets to `true` (here: `isPausedDuetoAd`). |
| **Banner** | Both banners requested | ✘ `MaxSdk.CreateBanner` or AdMob `new BannerView` is missing. |
| | MAX banner loaded → AdMob banner hidden | ✘ The `Banner.OnAdLoadedEvent` handler doesn't hide the AdMob banner. The flag it sets, e.g. `isBannerShowing`, is remembered for the next two checks. |
| | MAX banner failed → AdMob banner shown | ! The `Banner.OnAdLoadFailedEvent` handler doesn't show the AdMob banner. |
| | AdMob banner loaded while MAX's is showing | ✘ There's no `OnBannerAdLoaded` handler that hides the banner or checks the MAX flag. An AdMob `BannerView` shows itself when it loads, so a late AdMob load puts two banners on screen. |
| | `<method>` checks MAX banner | ✘ A method that calls AdMob `banner.Show()` doesn't check the MAX flag. |
| **Every format keeps loading** | MAX Interstitial / Rewarded | ✘ The `OnAdHiddenEvent` handler doesn't reload; ✘ the `OnAdLoadFailedEvent` handler doesn't retry; ! the `OnAdDisplayFailedEvent` handler doesn't reload. |
| | MAX Banner / MREC | ✘ Never created or never shown. They refresh by themselves once created. |
| | AdMob Interstitial / Rewarded / App Open | ✘ The `OnAdFullScreenContentClosed` handler doesn't reload; ! the `OnAdFullScreenContentFailed` handler is missing or doesn't reload; the load callback's `error != null` branch doesn't retry (✘ if nothing else ever reloads, ! if some public method reloads on demand). |
| | AdMob App Open: reload when missing/expired | ✘ The show method returns without loading when the ad is missing or older than 4 h. |
| | AdMob Banner: retry after failed load | ! The `OnBannerAdLoadFailed` handler doesn't request a new banner. |
| | `<method>`: null check | ✘ An AdMob ad object (`InterstitialAd`, `RewardedAd`, `AppOpenAd`, `BannerView` field) is used with `.` without a null check in that method. It's null after a failed load, the empty catch swallows the exception, and nothing reloads. Skipped for private helpers whose callers all create the object first. |
| **Ad revenue (paid) events** | `<format>`: revenue reported | For every format the game uses (MAX Interstitial / Rewarded / Banner / MREC via `MaxSdkCallbacks.<Format>.OnAdRevenuePaidEvent`; AdMob Interstitial / Rewarded / App Open / Banner via `.OnAdPaid`), the handler, or a method it calls (2 levels), must report to every entry in `revenueSinks`: Adjust (`Adjust.TrackAdRevenue`), Firebase (`"ad_impression"`) and AppMetrica (`ReportRevenue_*` / `ReportAdRevenue`). ✘ no handler, or it reports to none; ! it misses some. AdMob handlers are found where the ad is loaded/created, in a method taking the ad type, or in a helper the loader calls. |
| **AdsGapTimer reset** | `<format> · <network>: reset in SDK callback` | Either way is accepted: each network resets the timer in its own callback, **or** every call site resets it. Checked per network the game uses:<br>• Interstitial, MAX: the `Interstitial.OnAdDisplayedEvent` / `OnAdHiddenEvent` handler<br>• Interstitial, AdMob: the `InterstitialAd`'s `OnAdFullScreenContentOpened` / `Closed` handler<br>• Rewarded, MAX: the `Rewarded.OnAdReceivedRewardEvent` handler<br>• Rewarded, AdMob: the `rewardedAd.Show(reward => …)` reward callback<br>A public show method that resets it itself also covers the networks it shows. A reset one call down (in the ad scripts) counts. ✔ when it resets. When **every** network of a format does, the call-site rows below are skipped. Otherwise the networks that don't are ℹ, and the call sites are checked. A failing call site then names the network that doesn't reset in its callback. |
| | Interstitial · `<method>` | Only when not every network resets in its callback. Every game method that calls an interstitial entry point (any public ad-script method that reaches `MaxSdk.ShowInterstitial` or AdMob `interstitial.Show()`) must reset the timer: in the same method, in a helper it calls, or in **every live caller**, followed up to 4 levels. ✘ shows the uncovered chain, e.g. `ShowAdWithWait ← SwitchScene ← OnClickX`. ! means it's reset but not gated by `canShowAd`. ℹ means it's never called (dead code). |
| | Rewarded · `<method>` | Only when not every network resets in its callback. For each `ShowRewardedAd(callback)` call, the callback (method or lambda) must reset the timer, directly or through a method it calls. ✘ otherwise. The timer must reset when the reward is **granted**, so the callback is the place. |
| **Rewarded: reward goes to the right callback** | `<method>` (direct AdMob call) | Its own section, because it's a reward bug, not a timer one. It's checked however the timer is reset. ✘ Game code calls the AdMob rewarded method directly (e.g. `AdMob._instance.ShowRewardAd()`), and that method grants through a shared delegate (`AdsManager._rewarded`) that the caller never set. The reward goes to the previous ad's callback, or nowhere, and the timer isn't reset. ✔ "Every rewarded call sets its callback" when there's none. |
| **Remote Config → AdsGapTimer** | Remote Config sets the cooldown | ✘ No assignment `AdsGapTimer…timeLeft = …` in `remoteConfigClasses`; ! the value doesn't come from `GetValue("key")`. The key is reported, e.g. `adtimer`. |
| **Remote Config defaults** | `SetDefaultsAsync` is called; one row per key read with `GetValue("key")` (any scanned script, comments ignored) | ! no `SetDefaultsAsync`; ! a key read without an entry in the defaults list (it reads as 0 / false / empty when missing from the console, e.g. `adtimer` 0 = no cooldown). The row shows the default value. A default nothing reads is listed as info. |
| | Applied after fetch + activate | ! The assigning method isn't called from code using `ActivateAsync` / `FetchAndActivateAsync`. |
| | Safe if "key" is missing | ! The key has no Remote Config default and the assignment has no `> 0` guard, so a missing key sets the cooldown to 0. |
| | Nothing before it can throw | ! A `.Parse(` earlier in the same method can throw and skip the update. |
| | Also changed in | ℹ Other code that assigns the timer, e.g. `UiManager` setting `adtimertwo` after level 5. |
| **No App Open ad after a purchase or review** (step 4) | App Open checks the skip flag | ✘ A method of the App Open class (the one calling `AppOpenAd.Load`) calls `.Show(` without reading a flag in `appOpenSkipFlags` (default `isPausedDuetoAd`). |
| | `<method>`: sets the flag before the purchase | ! A method calling `PurchaseProduct` / `InitiatePurchase` doesn't set the flag to `true` before the call: an App Open ad shows when the player returns from the store dialog. A warning, not a failure, so older games still pass. |
| | `<handler>`: clears the flag | Checked for the purchase-succeeded (`OnPurchasePending` / `ProcessPurchase` / `OnPurchaseConfirmed`) and purchase-failed (`OnPurchaseFailed`) handlers of that class. ! Never set back to `false`: the store dialog is an overlay, so the game often doesn't go to background, `AppOpen_Code` never clears the flag and the next real App Open ad is skipped. ! Cleared at once (no `WaitForSeconds…` / `Invoke` on the way): a late return-to-foreground event still shows the ad. ! Cleared after a game-time delay (`WaitForSeconds` / `Invoke`): never cleared while the game is paused (`timeScale` 0). Passes with `WaitForSecondsRealtime`, e.g. 2 s (GenZ Ads v1.33). |
| | `<method>`: sets the flag before the review | ✘ The method calling `LaunchReviewFlow` (or a method calling it) doesn't set the flag to `true` first. |
| | `<method>`: clears the flag after the review | ! Same three cases as the purchase, for the code after `LaunchReviewFlow` (Google's quota often shows no card at all). |
| | `<method>`: clears the flag on early exits | ! A `yield break` / `return;` after the flag is set (e.g. the review request failed) doesn't clear it in its own block. |
| **Other logic** | AdMob events on Unity's main thread | ✘ `MobileAds.RaiseAdEventsOnUnityMainThread = true` is never set. AdMob callbacks then run on a background thread: the reward grant, and the reloads on close. Unity calls made there throw, the empty catch hides it, and the reward or reload is lost. |
| | Invoke / StartCoroutine by name | ✘ `Invoke("X")` or `StartCoroutine("X")` names a method that doesn't exist in the class, so it silently never runs. |
| | `<field>()` null check | ! A callback delegate is invoked as `field()` without a null check. |
| | Empty catch blocks | ℹ How many `catch { }` blocks the ad scripts have. They hide every runtime error above. |

**How "live" call paths work:** a call path counts only if it starts somewhere that can actually run: a method referenced by other code, a method wired to a UI/animation event in any scene or prefab (`m_MethodName`, scanned once per run), or a Unity message (`Start`, `Update`, …). Calls that pass a literal `false` (e.g. `SwitchScene(name, false)`) are skipped as "no ad on this path".

#### Baseline: first run on One Go Line Puzzle (2026-10-09)
For reference, these are the code findings on this project before any fixes:

| ✘ / ! | Finding | Where |
|---|---|---|
| ✘ | Rewarded returns before the AdMob fallback when MAX isn't initialized | `AdsManager.ShowRewardedAd` (`if (!isMaxInitialized) return;`) |
| ✘ | AdMob banner has no `OnBannerAdLoaded` guard, so both banners can show | `AdMob.RequestBanner` / `ListenToAdEvents` |
| ✘ | App Open: a failed load is never retried; a missing/expired ad is never reloaded | `AppOpen_Code.LoadOpenApp`, `ShowAppOpenAdIfAvailable` |
| ✘ | `rewardedAd` used without a null check | `AdMob.ShowRewardAd`, `AdMob.HasRewardedAvaiable` |
| ✘ | Reward callback doesn't reset AdsGapTimer | `RewItemsLock.VideoWatches` |
| ✘ | AdMob rewarded called directly without setting the reward callback (7 scripts, `MaxAdStop == 1` branch) | `Rew*.CallRewardedVideoAd` / `OnClick*Button` |
| ✘ | `RaiseAdEventsOnUnityMainThread` not set | `AdMob.Start` |
| ! | No default for `adtimer`, so a missing key means a 0 s cooldown | `FirebaseManager.GetRemoteData` |
| ! | No reload after a failed show (MAX rewarded, AdMob interstitial and rewarded); no retry for a failed AdMob rewarded or banner load | `AdsManager`, `AdMob` |
| ! | `_rewarded()` invoked without a null check | `AdsManager.OnRewardedAdReceivedRewardEvent`, `AdMob.ShowRewardAd` |

These checks were validated by applying the matching fixes to a copy of the scripts: every fixed item turned ✔ and nothing else changed.

The v5 checks on this project (2026-10-14): all 8 revenue handlers report to Adjust, Firebase and AppMetrica (✔); the Firebase files, SDKs and resolved dependencies all pass; one ! warning: `gradleTemplate.properties` has `android.aapt2FromMavenOverride` pointing to Unity **2022.3.24f1** while the project uses 2022.3.62f2.

### Result statuses
✔ green = pass · ✘ red = fail · ! amber = warning (check it, but it doesn't block) · i blue = information. Sections that fully pass start collapsed, so the failures stay in view.

Above the results is the **step bar**: **1 IDs Check ─── 2 SDK Integration ─── 3 Taichi ─── 4 Ad Calling Scenarios**. **Click a step** to list only its checks: the step gets a soft blue tile and a blue ring, the other steps dim, and a **Showing: Step N · name ✕** chip appears next to the filter tabs. Click the same step again, or the chip, to list every step. The status tabs still filter within the step. Each circle and step name has its step's worst colour, and the line under the name says the same in words:
- green **✔ Completed**: every check passed (no failure, no warning). The circle keeps its number and gets a small tick badge (dark green disc, white outline, white tick) on its lower-right edge, plus a soft green halo; the connector between two completed steps turns green;
- amber **Minor issues · 1 warning**: warnings only;
- red **Errors · 15 failed · 8 warnings**: at least one failure;
- grey **Not run**: its checks are turned off.

Under the step bar is a row of **filter tabs**: **All · ✔ Passed · ✘ Failed · ! Warnings**. Their counts are always for the whole run, all steps together, even while a step is selected. Each tab shows its count in a bold, rounded pill in the category's colour; the pill turns grey when the count is 0. The selected tab's name is bold. **All** shows every section as before, including the i information rows. The other tabs show only the checks with that status, still grouped under their section titles, with every section expanded. A tab with no matching checks says so. The chosen tab stays selected when you run the suite again. **Copy Report** always copies the full report, all four steps (one heading per step), whichever tab is selected.

Long check names wrap onto more lines instead of being cut, and hovering a name shows it in full. The name column can be resized by dragging the thin divider to its right. The width is saved in EditorPrefs (`GenZ.TestSuit.LabelColumnWidth`), so it stays the same across sessions. Multi-line values, such as a quoted code snippet, get their full height. Rows use fixed column positions, so every value and message starts at the same x and wraps within the window, with the **Open** button aligned on the right. **Copy Report** puts a Markdown table on the clipboard, ready to paste into a chat, a ticket or claude.ai.

The last results **stay in the window when scripts recompile** (editing any script reloads the editor's scripts). The report, the selected tab and step, the collapsed sections and the scroll position are saved before the reload and restored after it, and a note says which run they're from. They are not re-checked: run the suite again to see the effect of your edit. Open, Copy Report, Open SDK Scene and Auto Apply keep working on the restored results.

## 5. Adapting to a project (the 1–5% differences)
Select the config asset (**Edit Rules…**). Under **Rules**, each entry is a `ScriptRule` with:
- `label`: the section title. It's also the first part of `sameAs` keys.
- `target`:
  - `AdsObject`: a component on the Ads object.
  - `SceneObject`: a GameObject found by name in the SDK scene.
  - `Asset`: a ScriptableObject settings asset.
- `scriptNames`: accepted class names. For `Asset`, these are the asset's ScriptableObject class names.
- `required`: if off, a missing script, object or asset is a warning instead of a failure.
- **SceneObject only:**
  - `objectNames`: GameObject names, ignoring case.
  - `mustBeActive`: the object must be SetActive true.
  - `expectPrefab`: warn if the object isn't a prefab instance.
- **Asset only:**
  - `assetNames`: file names without `.asset`.
  - `assetFolder`: a folder fragment the path must contain, e.g. `Resources/GameAnalytics`.
- `fields`: a list of `FieldRule`, each with:
  - `label`
  - `fieldNames`: serialized field aliases. Top-level fields only, `[HideInInspector]` ones included.
  - `kind`: `AdMobUnitId`, `AdMobAppId`, `MaxSdkKey`, `MaxUnitId`, `AdjustToken`, `GameAnalyticsGameKey`, `GameAnalyticsSecretKey`, `AppMetricaApiKey`, `InAppList`, `MustBeFalse`, `MustBeTrue`, `Expected`, `NumberAboveZero` or `Text`. The kind also sets the step: ID kinds go to step 1, the rest to step 2.
  - `required`
  - `sheetColumns`: sheet header aliases.
  - `expectedValue`: for `Expected`, e.g. `Production`.
  - `platformListField` + `platformName`: for per-platform lists, e.g. `Platforms` + `Android`. The value is the list element at the index where that platform appears.
  - `sameAs`: `<Rule label>/<Field label>` of a field this one must equal. That field's rule must come **earlier** in the list.

Examples:
- A project renamed `bannerID` to `admobBanner`: add `admobBanner` to that rule's `fieldNames`.
- Your sheet calls the column `Admob_Banner_Unit`: add it to `sheetColumns`. Matching is exact, so every new header spelling has to be listed.
- A new ID to check, for example a native ad ID: add a `FieldRule` with kind `AdMobUnitId`.
- A project's consent object is called `ConsentManager`: add it to the UmpManager rule's `objectNames`.

If the defaults should change for **every** project, edit `GenZTestSuitConfig.DefaultRules()`. Existing config assets keep their own rules until you press **Reset Rules**.

**Code check settings** (`codeChecks` in the config asset):

| Field | Default | Meaning |
|---|---|---|
| `enabled` | true | The same as the **Run Code Checks** toggle. |
| `adScripts` | the 13 studio ad/analytics classes (+ `Admob`) | Where ad logic is looked for. Calls from any other class are game call sites. |
| `requiredScripts` | the 13 + `AdsGapTimer` (`A\|B` = either name) | Must exist in the project. |
| `gapTimerClass` | `AdsGapTimer` | The cooldown class. |
| `resetMethods` | `ResetTimer` | Calls that count as a reset. |
| `timerFields` | `timeLeft` | The field Remote Config must set. |
| `gateFields` | `canShowAd` | The flag that gates interstitials. |
| `remoteConfigClasses` | `FirebaseManager` | Where the Remote Config assignment must be. |
| `vendorSwitchFields` | `useSdkVendorSwitch` | Int field that picks the Interstitial/Rewarded network order. Not declared in a project = only MAX first is checked. |
| `revenueSinks` | Adjust, Firebase, AppMetrica | `Name\|regex` entries. Every revenue handler must reach each regex (string contents included). Remove an entry if a game doesn't use that analytics. |
| `excludeFolders` | SDK and kit folders | Not scanned. Add a project's own third-party folders here. |
| `appOpenSkipFlags` | `isPausedDuetoAd` | Flag that makes the App Open ad skip one return to the game. Purchases and the in-app review must set it and clear it again after their flow. Empty = `isPausedDuetoAd`. |

**SDK file check settings** (`integration` in the config asset):

| Field | Default | Meaning |
|---|---|---|
| `requiredSdks` | Firebase, GameAnalytics, AppMetrica, Adjust, AppLovin MAX, Google Mobile Ads | `Label\|folder under Assets` of each SDK that must be installed. |
| `firebaseModules` | `Firebase.App`, `Firebase.Analytics`, `Firebase.RemoteConfig` | Firebase DLLs that must be installed. |

**Taichi settings** (`taichi` in the config asset):

| Field | Default | Meaning |
|---|---|---|
| `enabled` | true | The **Run Taichi Checks** toggle. |
| `managerClasses` | `FirebaseManager` | Component holding the switches (Ads object first, then any object in the SDK scene). |
| `requireTaichi1` / `requireTaichi2` | false / true | A required version that's switched off fails; an optional one is only reported. |
| `taichi1Flags` / `taichi2Flags` | `Taiche1_0`… / `Taiche2_0`… | Bool field names of the switches. |
| `taichi1Event` / `taichi2Event` | `custom_ad_impression` / `count_ad_impression` | Firebase events. |
| `taichi1Key` / `taichi2Key` | `custom_rev` / `custom_imp` | Remote Config keys. |
| `taichi2SheetColumns` | `Taichi 2.0 Default Values`, … | Sheet column with this game's impression count. |

## 6. GenZ-ApkTestSuit (release APK on a phone)

For the developer, before a release: connect a phone by USB with USB debugging on, then open the **GenZ-ApkTestSuit** tab. It tests the real release build; no debug build is needed. Any studio game can be tested from any project.

### Flow
**Firebase DebugView: ▶ Start ADB / ■ Stop ADB** (under the phone list, in this tab and the Deep tab) do what **GenZ → Firebase → StartADB / StopADB** do: `adb shell setprop debug.firebase.analytics.app <package>` (or `.none.`). Start also sets the `FA` / `FA-SVC` log tags to VERBOSE. Unlike the menu, they use Unity's adb on the **selected phone** (the menu uses the `adb` on PATH, which fails with several phones attached) and the **selected game's** package (the Deep tab: this project). Restart the game after Start. A test session turns DebugView on by itself; **Stop ADB** is disabled while a session runs, because the session reads Firebase's debug log.

1. **Settings:**
   - **Google Sheet Link**: the same studio sheet as the Editor part (shared setting).
   - **Game**: every game in the sheet (all its `Package Name` columns, with the game's name; the sheet's row numbers are dropped and the list is sorted by name). This project comes first. It's a searchable list, like Add Component: click it and type part of a game name or package. **Load Games** downloads the sheet again; it's loaded once per editor session automatically.
     - For this project, the version from Player Settings is expected. For another game, the version is shown, not compared.
     - The sheet's `Taichi 2.0 Default Values` for the game is compared with the `custom_imp` the game gets from Remote Config (step 4).
   - **Build**:
     - **Install an APK**: the **APK** defaults to the newest `.apk` in the project folder (**Browse…** picks another). **Fresh Install** uninstalls first, so the game starts as a new player (consent form, first-open events). It's also needed when the installed app has another signature.
     - **Already installed on the phone**: no APK needed; the suite reads the installed game (`dumpsys package`) and opens it itself. **Fresh Start** clears its data first (`pm clear`). The signature can't be checked without a file (ℹ).
   - **Phones**: every attached phone, refreshed every 5 seconds in the background. Each row shows the model, Android version, serial and state: **Ready**, "Accept the USB debugging prompt on the phone" (unauthorized) or offline. For ready phones it also shows whether the selected game is installed, and which version. Only ready phones can be picked; the first ready one is picked by default.
2. **▶ Install & Start APK Test** / **▶ Start Test on the Installed Game** (blocks with a progress bar):
   - with an APK: reads it with `aapt2 dump badging` and checks the signature with `apksigner`, run with Unity's JDK (step 1), then installs it with `adb install -r -d` and reads the installed versionCode;
   - with the installed game: reads it with `dumpsys package` (step 1); fails if it isn't installed on that phone;
   - enlarges the phone's log buffer (`logcat -G`, `logcatBufferMb`) and clears it;
   - turns on Firebase debug logging: the same as **GenZ → Firebase → StartADB**, plus the `FA`/`FA-SVC` log tags, so every Firebase event and upload appears in logcat (DebugView keeps working too);
   - force-stops and cold-starts the game with `am start -W`, which gives the start time. Without an APK, the launcher activity comes from `cmd package resolve-activity`.
3. **Play on the phone:** finish a level, let an interstitial show, watch a rewarded ad, send the game to the background and back for App Open. The results update every 4 seconds (a background `logcat -d` of the needed tags). While recording, missing evidence shows as ℹ "Waiting…", not as a failure.
4. **■ Finish & Check:**
   - reads every log buffer (main, system, crash, events);
   - saves it to `Logs/GenZ-ApkTestSuit/<package>_<version>-<code>_<time>.txt` (**Open Log**);
   - turns Firebase debug logging off (the same as StopADB);
   - shows the final results.

   **Cancel** stops without checking.

Nothing runs on the PC between those calls, so a script reload in the middle of a session doesn't break it. The window keeps the session in serialized fields and polls again after the reload.

### The `[GenZ]` log lines
A release build has the SDKs' own logs mostly off. So the shared ad scripts write one line for each fact the suite needs, through `GenZLog` (`Assets/AdsScripts-GenzAds_*/GenZLog.cs`). Unity's `Debug.Log` reaches logcat (tag `Unity`) in release builds:

```
[GenZ] <sdk>|<what>|<OK|FAIL|CALL|INFO>|<detail>
```

Only what's safe to expose is logged: SDK names, states, error codes and messages, ad formats, event names and Remote Config ad values. It **never** logs keys, tokens, revenue, or device / advertising / user IDs.

| Line | Written in | Means |
|---|---|---|
| `MAX\|init` | `AdsManager` `OnSdkInitializedEvent` | `IsSuccessfullyInitialized` |
| `AdMob\|init`, `AdMob\|adapter` | `AdMob.Start`, `MobileAds.Initialize` callback | Initialized, then one line per mediation adapter: class name, Ready or not, description |
| `Firebase\|init` | `FirebaseManager`, `CheckAndFixDependenciesAsync` | `DependencyStatus` |
| `RemoteConfig\|init`, `RemoteConfig\|values` | `FirebaseManager.FetchComplete` / `GetRemoteData` | Fetched and activated, or the failure reason; then `adtimer`, `adtimertwo`, `custom_imp`, `useSdkVendorSwitch` |
| `GameAnalytics\|init` | `Analytics.Awake` | `GameAnalytics.Initialized`, and the build GA tags events with (`Application.version` when Send Version is on) |
| `Adjust\|init`, `AppMetrica\|init` | `GenZLog.ProbeServers`, started from `Analytics.Start` | The SDK's server answered. Adjust hands out an adid only after it tracked the first session; AppMetrica gives a device ID only after its startup server answered. It waits up to 60 s and never logs the IDs. |
| `UMP\|init`, `UMP\|form` | `UmpManager` callbacks | Consent status, or the `FormError` code and message |
| `IAP\|init`, `IAP\|products` | `unityInApp` | Store connected and products fetched, or the failure |
| `FCM\|init` | `FirebaseMessagingManager` | A token arrived (not the token itself) |
| `Ads\|impression` | every revenue handler (4 MAX, AdMob, App Open) | `<MAX or AdMob> <format>` was shown and paid |
| `Adjust\|ad_revenue` | right after each `Adjust.TrackAdRevenue` | Revenue sent to Adjust |
| `AppMetrica\|ad_revenue`, `AppMetrica\|event` | `AppmetricaAnalytics` | Revenue or an event sent to AppMetrica (event type and parameters) |

Firebase and GameAnalytics are also checked from **their own SDK logs**. Firebase logs through the debug property above. GameAnalytics logs when **Info Log Build** is on in GA Settings (it is in this project).

### What it checks
| Step | Section | Check | Fail / warn when |
|---|---|---|---|
| **1 · Build & Device** | APK file / Installed game | Package | ✘ Not the selected game |
| | | Version | ✘ Not equal to Player Settings (an old build). Only for this project; another game's version is ℹ. |
| | | Not debuggable | ✘ A Development Build |
| | | Target SDK | ✘ Below `minTargetSdk` (36, the studio's target API level) |
| | | 64-bit | ✘ No `arm64-v8a` |
| | | Permissions | ✘ One of `requiredPermissions` (INTERNET, ACCESS_NETWORK_STATE, AD_ID) is missing from the merged manifest |
| | | Signing | ✘ Unsigned, or signed with the debug key. ✔ Shows the certificate and its SHA-256. ! apksigner couldn't run. For **Already installed on the phone**, the game's base APK is copied off the phone (`adb pull` into `Temp/GenZ-ApkTestSuit`) and checked the same way. |
| | | Signed with the right keystore | ✘ The APK's signer certificate (SHA-256) isn't the release key's. The release key is this project's Publishing Settings keystore, or, for another studio game, the keystore its sheet `KeyStore` value names, looked for next to this project's keystore (`.keystore`, `.jks`: by file name, else by key alias). ℹ Not compared when that keystore is unknown or unreadable. |
| | Device | Installed, Launched | ✘ The install failed (with a hint for signature, storage or ABI errors), or the phone has another versionCode. ! The launch wasn't confirmed. |
| **2 · SDK Init** | SDK initialization | each of `requiredSdks` / `optionalSdks` | ✘ A `FAIL` line, or (on Finish) no `init` line. ! It failed first and started later. Optional SDKs only warn. ✘ No `[GenZ]` line at all: the ad scripts are older than GenZLog. |
| | AdMob mediation adapters | each adapter | ! Not ready, with AdMob's description |
| **3 · Analytics Events** | Firebase | Events logged, Sends "`ad_impression`" (`firebaseEvents`), Uploaded | ✘ No event (Firebase didn't start, or the debug property wasn't set). ! A listed event wasn't logged. ✘ Uploads failed and none succeeded. ! No upload seen. |
| | GameAnalytics | Build sent = APK version | ✘ The build GA sends isn't the APK's versionName (Send Version off, or a wrong Build field) |
| | | Build in GA events | ✘ A GA event (verbose log) carries another build |
| | | Events sent, No GA errors | ! GA wrote nothing (Info Log Build off), or no line matched `gaEventPattern`. ✘ GA error or warning lines. |
| | Adjust | Server answered, Ad revenue sent | ✘ No adid within 60 s. ✘ Fewer revenue calls than impressions. ! Adjust error lines. |
| | AppMetrica | Server answered, Events sent, Ad revenue sent | ✘ No device ID within 60 s (the error is shown). ! No event. ✘ Fewer revenue calls than impressions. |
| **4 · Ads** | Ad impressions | each network + format seen; each of `adFormats` | ! A format wasn't shown in the session. ! Fewer Firebase `ad_impression` events than impressions. |
| | Taichi | 2.0 impression count = sheet | ✘ The `custom_imp` from Remote Config differs from the sheet's `Taichi 2.0 Default Values` for the game. ! No Remote Config values logged to compare. |
| | | Sends "`count_ad_impression`" / "`custom_ad_impression`" | ✔ When seen. ℹ How many impressions (`custom_imp`) 2.0 needs. |
| **5 · Stability** | Crashes and errors | No Java crash, No native crash, No ANR | ✘ `FATAL EXCEPTION` in the game's process, `Fatal signal` or a tombstone, `am_anr` or "ANR in" |
| | | No script exceptions | ! Unity `…Exception` lines (the first three are shown) |

**Settings** (`apk` in the config asset). It's a new field, so existing configs get the defaults without a version bump.

| Field | Default | Meaning |
|---|---|---|
| `requiredSdks` | MAX, AdMob, Firebase, RemoteConfig, GameAnalytics, Adjust, AppMetrica, UMP | Must report `<sdk>\|init\|OK`. |
| `optionalSdks` | IAP, FCM | Only warned about. |
| `firebaseEvents` | `ad_impression` | Must be logged in a session. |
| `adFormats` | Banner, Interstitial, Rewarded, AppOpen | A full session shows each one (matched against the end of the `Ads\|impression` detail). |
| `requiredPermissions` | INTERNET, ACCESS_NETWORK_STATE, AD_ID | Must be in the merged manifest. |
| `minTargetSdk` | 36 | The studio's target API level (Android 16). |
| `logcatBufferMb` | 16 | Log buffer size asked of the phone. |
| `firebaseEventPattern`, `firebaseUploadOkPattern`, `firebaseUploadFailPattern` | Firebase's debug-log wording | Regex. |
| `gaTag`, `gaEventPattern`, `gaErrorPattern`, `gaBuildPattern` | `GameAnalytics`, … | Regex for GA's own log lines. **Confirm on the first device run**: GA's exact wording hasn't been seen yet. |
| `adjustTag` | `Adjust` | Tag of Adjust's own log lines (only errors are read). |

### Tools
`adb`, `aapt2` (from the newest build-tools) and `apksigner` come from Unity's Android SDK (External Tools). They're read by reflection, because the asmdef can't reference the Android module. The fallback is the bundled `PlaybackEngines/AndroidPlayer/SDK` and `OpenJDK`. Nothing to install.

### Tested so far (2026-10-22)
- The session checks, on sample logcat files through the console harness: a good session, a broken one (crash, ANR, failed upload, failed Remote Config, missing Adjust revenue) and an empty live log.
- Step 1 on real `aapt2` and `apksigner` output (another studio APK: package and version correctly fail).
- The ad scripts with the GenZLog calls compile (`Assembly-CSharp` built outside Unity), and the Editor suite's code checks give the same results as before.
- **Not yet run on a phone.** The first real run should confirm GameAnalytics' and Firebase's log wording and the AdMob adapter lines. Adjust the patterns in the config if needed.

## 7. GenZ-DeepTestSuit (Test / Release Environment and the phone's ad panel)

The third tab. Apart from Auto Apply IDs, it's the only part of the suite that writes to the project. It never edits the ad scripts: every SDK setting is a field on an SDK object or settings asset, found with the config rules and written with `SerializedObject`. The phone panel is a separate script that talks to the SDKs directly.

### The two buttons
Each shows every change and asks first. SDK scenes the tool opens itself are saved; a scene you have open is only marked as modified (save it yourself), as with Auto Apply IDs.

| Setting | Test | Release | Where (config rule) |
|---|---|---|---|
| Adjust environment | `Sandbox` | `Production` | `Adjust` |
| Adjust log level | `Verbose` | `Info` | `Adjust` |
| AppMetrica logs | `true` | `false` | `AppMetrica` |
| GameAnalytics verbose log | `true` | `false` | `GameAnalytics Settings` |
| GameAnalytics info log | `true` | `true` | `GameAnalytics Settings` (the APK suite reads it) |
| UMP consent test mode | `true` | `false` | `UmpManager (AdMob consent)`. **Off by default**: it only works on the device IDs listed in `UmpManager`. |
| Scripting define `GENZ_DEEP_TEST` (Android) | added | removed | Player Settings. Compiles the phone panel in. Changed last, because it recompiles. |
| `Assets/GenZ-DeepTest/Resources/GenZDeepTest.json` and `Assets/GenZ-DeepTest/link.xml` | written | deleted | The panel's rows (which game methods each button calls), the watched fields and switches; `link.xml` keeps those ad classes from being stripped. Every `Class.Member` is checked against the game's scripts when it's written: a missing one is listed in the Apply summary. |

The tab shows each setting's current value (T = test, R = release, = = same in both, ! = not found) and the overall state: **TEST ENVIRONMENT**, **RELEASE ENVIRONMENT** or **MIXED**. The settings are `deepTest.settings` in the config asset (label, rule, field names, test value, release value), so a game with other names only needs config edits.

### The phone panel (`Assets/GenZ-DeepTest/GenZDeepTestPanel.cs`)
**Every ad button calls the game's own ad scripts.** The panel never loads or shows an ad itself, so what's tested is the game's ad code and SDK integration. The scripts aren't changed: methods are called by name with reflection (any visibility), and the names are data in the config (`deepTest.actions`), so another game only edits names.

| Row | Load | Show (Hide) | Notes |
|---|---|---|---|
| Interstitial (game waterfall) | – | `AdsManager.ShowInterstitial` | The game's own order: MAX first, AdMob fallback, vendor switch. |
| Rewarded (game waterfall) | – | `AdsManager.ShowRewardedAd(callback)` | The panel passes its callback: "✔ reward callback reached (game code)". |
| MAX Interstitial | `AdsManager.RequestInter` | `AdsManager.ShowMaxInterstitial` | Show only when `IsMaxInterstitialReady`. |
| MAX Rewarded | `AdsManager.RequestVideo` | `AdsManager.ShowRewardedAd` | Only when `IsMaxRewardedReady`, with `useSdkVendorSwitch` = 0 just for that call, so the game's own method takes its MAX path. |
| MAX Banner | – | `AdsManager.ShowBanner` (`HideBanner`) | Hides the AdMob banner first (`AdMob.bannerHide`). |
| MAX MREC | – | `AdsManager.ShowMRECBanner` (`HideMRECBanner`) | |
| AdMob Interstitial | `AdMob.RequestInterstitial` | `AdMob.ShowInterstialAd` | Only when `HasAdmobInterstialAvaible`. |
| AdMob Rewarded | `AdMob.RequesRewardAd` | `AdMob.ShowRewardAd` | Only when `HasRewardedAvaiable`. `AdsManager._rewarded` is set to the panel's callback first, because `ShowRewardAd` calls it. |
| AdMob Banner | `AdMob.RequestBanner` | `AdMob.bannerShow` (`bannerHide`) | Hides MAX's banner first; `isBannerShowing` = false just for the call (otherwise `bannerShow` hides itself). |
| AdMob App Open | `AppOpen_Code.LoadOpenApp` | `AppOpen_Code.ShowAppOpenAdIfAvailable` | `AdMob.isPausedDuetoAd` = false just for the call (otherwise it skips the show). |

How a row works (`DeepAction`): `load`, `show`, `hide` are `Class.Method`. `ready` is a bool method or field, asked **only when Show is pressed**, because the game's readiness methods also request an ad. With `requireReady`, Show does nothing while it's false, so a waterfall method can't fall through to the other network. `before` lists calls made first. `showFields` (`Class.field=value`) are set only while Show runs and put back right after. `rewardField` points a delegate field at the panel's callback. A parameter of a delegate type (the reward callback) gets the panel's callback; any other parameter gets its default. The object is the class's static `instance` / `_instance` / `Instance`, else the object in the scene.

The panel shows each call's result. An exception thrown by the game's code is shown as "the game's code threw …", which is a bug to fix in the ad script. The top line shows the `deepTest.watch` fields live (default: AdsGapTimer `canShowAd`, `time`, `timeLeft`; `useSdkVendorSwitch`; `isBannerShowing`; `isPausedDuetoAd`). The log also lists what MAX did after the game's call (loaded, shown, closed, failed, with the network). It only listens to MAX's callbacks; the game's own handlers run as usual. The panel also has the **MAX Mediation Debugger** and **AdMob Ad Inspector** (SDK debug tools; they don't show ads).

**It only exists in a Test Environment build, behind two keys that only Apply Test Environment sets.** The whole script is inside `#if GENZ_DEEP_TEST`, so without the define the build contains no panel code at all (checked: the compiled DLL has no panel class). With the define but without `Resources/GenZDeepTest.json` (for example a define added by hand), it only logs `[GenZ] DeepTest|build|FAIL|…` and does nothing else: no panel and no SDK debug switches.

At start, before any game script's `Awake` (`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`, so no scene object is needed), it sets the SDK debug switches:
- `MaxSdk.SetVerboseLogging(true)` and `MaxSdk.SetCreativeDebuggerEnabled(true)`, before the game starts MAX;
- the phone as an **AdMob test device** (`RequestConfiguration.TestDeviceIds` = the upper-case MD5 of the Android ID). The game's own AdMob requests keep their real ad unit IDs, but Google serves test ads, so tapping them is safe. The game's `isTestIdOn` is left alone;
- logs `[GenZ] DeepTest|build|INFO|Test Environment build`. The APK suite fails on that line (see below).

**Shake** the phone (default 2 g, `deepTest.shakeThreshold`) to open the panel. Shake again or press Close to hide it; in the Editor, F9 toggles it. Rows without a Hide button are full-screen ads, so pressing their Show also closes the panel. Its log lines use the GenZLog format and never contain IDs, device IDs or revenue values.

### Test on a phone
The same **Build** choice and **phone list** as the APK tab (shared with it): **Install an APK** (Browse… / Newest, Fresh Install) or **Already installed on the phone** (Fresh Start clears its data).
- **▶ Install & Start Test** / **▶ Start Test on the Installed Game** (`GenZDeepTestSuit.StartSession`) installs the APK if one is picked, clears the log, turns on Firebase DebugView and cold-starts the game. Unlike the APK tab, it runs no release-build checks, because a Test Environment build isn't a release build.
- While it runs, the tab reads the phone's Unity log every 4 s in the background and shows what happened. It says whether this is a Test Environment build (the panel's `[GenZ] DeepTest|build` line; after 20 s without it: "No test panel in this build"). It also shows the AdMob test-device state, which SDKs started (`[GenZ] <sdk>|init`), every panel button result (✔ / ✘ / •, newest first: what was called, the reward callback reached, or the exception the game's code threw), and Unity exceptions and crashes.
- **■ Stop & Save Log** saves the whole log under `Logs/GenZ-DeepTestSuit/`, turns Firebase DebugView off and keeps the last results with an **Open Log** button. **Cancel** only stops.
- The session state is in serialized window fields, so a script reload doesn't break it. It can't start while an APK tab session is running.
- **Firebase DebugView: ▶ Start ADB / ■ Stop ADB** are also there, for a game started by hand (see §6).

### Release guards
- Editor suite, step 2: the section **Release Environment (GenZ-DeepTestSuit)** fails while the define is set or the panel's settings file exists. The other release values are already checked by their rules (Adjust Production, UMP test mode off).
- APK suite, step 1: **Not a Test Environment build** fails when the log has a `[GenZ] DeepTest` line.
- A build hook (`GenZDeepTestBuildGuard`) warns in the Console when an Android build is made with the define, and rewrites the panel's IDs so they match the project. It also warns when the settings file is left over without the define.

## 8. Known limits / ideas for later
- Field lookup covers top-level fields of each component only. Fields nested inside a custom serializable class aren't searched; the in-app array elements are the exception.
- `GameObject.Find` in **Open SDK Scene** can only select an active Ads object.
- The sheet's `admob Big-banner`, `admob native` and `unity id` columns aren't checked yet.
- "Resolved" only checks `mainTemplate.gradle`. With Custom Main Gradle Template off, the resolver's output isn't checked.
- Only the **Android** entries are checked (GA keys, AdMob App ID). The iOS fields are ignored.
- The sheet download blocks the editor, with a cancelable progress bar and a 30 s timeout.
- **Code checks are static and pattern-based, not a compiler.** They follow calls by method **name**: two classes with a method of the same name can merge paths. They can't see calls made through reflection, `SendMessage`, or a UI event set up at runtime. Lambdas are followed when they're subscribed with `+=` or passed as the rewarded callback. A finding means "this pattern is missing", so read the linked line before changing code.
- "No logical errors" can't be proven by static analysis. The suite checks the specific ad-flow rules above plus common ad-code bugs. Runtime behaviour belongs to GenZ-ApkTestSuit.

- **Deep suite:** while the panel is open, touches still reach the game too (it doesn't block game input). The shake threshold may need tuning per phone. Not yet run on a phone.

- **APK suite:** it checks what the session showed, so a format that wasn't shown is only a warning. An AAB can't be installed (build an APK). One phone at a time. AdMob unit IDs aren't verified at run time; the developer runs the Editor suite's ID checks first.

## 9. Roadmap
- **GenZ-ApkTestSuit:** confirm the log patterns on a phone, then grow the checklist with the team (it's meant to keep growing). A separate tool for QA, without Unity, comes later.

## 10. Changelog
- **1.0.1, in development (since 2026-11-10).** Targets GenZ Ads v1.33. Entries for this version go here until it's released.
  - **Taichi 2.0 default in code is auto-applied.** A game without `RemoteConfigManager` (e.g. older `FirebaseManager` with `{ "custom_imp", 50L }`) had its count checked against the sheet but Auto Apply had no Inspector field to write, so nothing changed. Now the check records where the number is written (`TaichiInput.taichi2Code*`), the fix goes into `IdFix.codeFile/codeLine/codeContext/codeValue`, and `GenZIdFixer` replaces the number in the script (§4 "Auto Apply IDs"). Games with the Inspector field work as before.
  - **Minimum API Level on Unity 6:** API 24 or 25 passes (`playerSettings.minSdkUnity6`); older Unity still needs 24.
  - **Show Splash Screen off on Unity 6** (new step 2 check, `playerSettings.showSplashScreenUnity6`).
  - **GameAnalytics ad revenue formats** (new step 2 section, §4 "SDK files"): `GAAdMobIntegration.cs` must send the same `adunit_format` as `GAMaxIntegration.cs` (`INTER`, `REWARDED`, …), otherwise GA splits one ad format in two; Interstitial/Rewarded fail, Banner/App Open warn. Also the `gameanalytics_max_enabled`/`gameanalytics_admob_enabled` defines and the `GameAnalyticsILRD.Subscribe…` calls. New orange **Fix GA Ad Formats** button sets AdMob's strings to MAX's (`TestReport.gaFormatFixes`, `GenZIdFixer.ApplyOnLines`). New config field `integration.gaAdFormatFallback` (no version bump: existing configs get the default).
  - **Ad scripts version** (new step 4 section, §4 "Code checks"): every script in `AdsScripts-GenzAds` must start with the same `// GenzMonetization-v<version>` header (added in GenZ Ads v1.33). Mixed or missing headers fail; no headers at all (pre-v1.33 game) or an older version warns; `Release.txt` must match. Expected version `GenZTestSuitInfo.AdsScriptsVersion` (`1.33`; raise it with each GenZ Ads release), per project `codeChecks.adsScriptsVersion`.
  - **AppLovin MAX adapters** (new step 2 section, §4 "SDK files"): every adapter in the studio list (`integration.maxAdapters`: the 14 Integration Manager networks plus the custom networks AdSurge and Helix / Voodoo ADN) must be installed, else ✘; IronSource (MAX adapter, its Mediation folder, the IronSource / LevelPlay SDK; `integration.forbiddenAdapters`) must be gone, else ✘; any other adapter (`integration.adapterPattern`) is a !. Read from the `*Dependencies.xml` files, `mainTemplate.gradle`, `Plugins/Android` libraries and `MaxSdk/Mediation`.
  - **No App Open ad after a purchase or review** (new step 4 section, §4 "Code checks"): the App Open ad checks the skip flag (`codeChecks.appOpenSkipFlags`, default `isPausedDuetoAd`); purchases (`PurchaseProduct` / `InitiatePurchase`) and the in-app review (`LaunchReviewFlow`) set it first, then clear it again after the flow ends, after a short real-time delay (`WaitForSecondsRealtime`), on success, failure and early exits. A missing set on the review fails; everything new in GenZ Ads v1.33 (set before a purchase, the clears, the delay) warns, so v1.32 games still pass. The SDK's own classes (those declaring `LaunchReviewFlow` / `PurchaseProduct`) are skipped.
- **Unreleased (2026-10-29): Unity package.** The suite is now the package `com.genz.testsuit` in `GenzProjects/GenZ-TestSuit`, installed by git URL (§1) instead of copying the folder. A new config is created in `Assets/GenZ-TestSuit/` (the package folder is read-only). The phone panel ships in the package and Apply Test Environment installs or updates it in `Assets/GenZ-DeepTest/`. `RELEASE.md` became `CHANGELOG.md`.
- **Unreleased (2026-10-23): Start ADB / Stop ADB buttons** in the APK and Deep tabs (Firebase DebugView, as in GenZ → Firebase), for the selected phone and game.
- **Unreleased (2026-10-23): Keystore checks.** New section **Android signing (keystore)** in the Editor suite: custom keystore on, file present, alias set (step 2), and **Keystore = sheet** against the sheet's new `KeyStore` column (step 1). The certificate's SHA-256 is read with `keytool`. The APK suite checks **Signed with the right keystore** (the APK's signer = that certificate), now also for a game already installed on the phone (its APK is pulled with adb). New `signing` config field (no version bump).
- **Unreleased (2026-10-23): Deferred reloads.** A failed-show or close handler that only sets a flag (e.g. `interRetryPending = true`, because SDK callbacks can arrive off the main thread) now passes when another ad-script method checks that flag and then loads (`Update` → `Invoke(nameof(RequestInterstitial))`). Before, only a direct load counted for shows; failed loads already accepted the flag. The AdMob banner's retry check accepts the same flag route. One Go Line: AdMob interstitial and rewarded now retry a failed load and reload after a failed show, the AdMob banner retries a failed load (`interRetryPending` / `rewardRetryPending` / `bannerRetryPending`), and `_rewarded` is null-checked. Taichi plus code checks: **0 failures, 0 warnings**.
- **Unreleased (2026-10-23): No BigBanner rule.** The optional `BigBanner` rule (presence only) is removed from the defaults, so a game without the script gets no warning. **Config v10**.
- **Unreleased (2026-10-23): Auto Correct Wrong IDs.** A second, orange button, **Auto Correct N Wrong IDs**, fixes only the IDs the last run found wrong, without downloading the sheet again. It's under the step bar (which now lists the wrong IDs) and next to **Auto Apply All IDs from Sheet** in Settings.
- **Unreleased (2026-10-23): New studio sheet with tabs.** The default link is the new spreadsheet; a config still on the old link is moved to it. New **Sheet Tabs** setting (default `Android Games ID's` and `Android Max ID's`): the tabs are found by name, downloaded and combined, so the games table and the MAX table are read from their own tabs. **Config v9**.
- **Unreleased (2026-10-23): Taichi defaults in the Inspector.** `RemoteConfigManager` is a component on the ADs object with `taichi1DefaultRevenue` and `taichi2DefaultImpressions` serialized fields. The Editor suite reads them from the SDK scene (`TaichiSettings.defaultClasses`, `taichi1DefaultFields`, `taichi2DefaultFields`). Each Taichi section shows its **Default in the Inspector**, and **Impression count = sheet** compares the Inspector value (not the code's) with the sheet. When they differ, **Auto Apply All IDs from Sheet** writes the sheet's count into the field (Auto Apply now writes numbers as well as strings). New optional rule **RemoteConfigManager (Taichi defaults)**: both values > 0. Numbers in check values are written with a `.` on every Editor language. **Config v8**.
- **Unreleased (2026-10-23): Remote Config in its own class.** One Go Line moved every Remote Config key, default and read into `RemoteConfigManager` (`AdsScripts-GenzAds`); `FirebaseManager` only calls `RemoteConfigManager.Defaults()` and `LoadValues()`. The suite follows it: the Remote Config classes are `remoteConfigClasses` (now `FirebaseManager`, `RemoteConfigManager`) **plus any class that calls `GetValue("…")`**, so an old config asset still finds it. A default written as a field name (`{ "custom_imp", impressionValue_ForRevenue }`) resolves to the field's starting value for the Taichi 2.0 sheet check and the defaults rows. `BigBanner` is no longer required (the MREC can go through `AdsManager.ShowMRECBanner`), and the Deep panel's MAX MREC row calls `AdsManager`. **Config v7**: on load, older config assets get the new default rules, check settings and Deep panel rows (scene, sheet link and Ads object name are kept). Taichi plus code checks: 0 failures, 7 warnings.
- **Unreleased (2026-10-23): Remote Config defaults check** (step 4, section **Code · Remote Config defaults**): every key the game reads with `GetValue("key")` must be in the `SetDefaultsAsync` list. One Go Line's empty list was filled in `FirebaseManager.cs` with the values the code already starts with (`adtimer` and `adtimertwo` 20, `custom_rev` 0.1, `custom_imp` 30 = the sheet's Taichi 2.0 value, …). Taichi plus code checks: 0 failures, 7 warnings (before: 9).
- **Unreleased (2026-10-23): Taichi threshold check.** It now follows the fix it recommends: `double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out field)` counts as reading the Remote Config value into `field` (before, only `field = …Parse(text)` was followed, so the fixed code was reported as "read but not used"). "Safe if missing" also passes for a fallback such as `if (!TryParse(…, out x) || x <= 0) x = 0.1;`. One Go Line's `custom_rev` was fixed this way in `FirebaseManager.cs`: before, 1 failure and 10 warnings; after, 0 failures and 9 warnings.
- **Unreleased (2026-10-23): GenZ-DeepTestSuit, first version** (§7). It adds the third tab with **Apply Test Environment** and **Apply Release Environment**, the phone's shake panel (`Assets/GenZ-DeepTest/`; its buttons call the game's own AdsManager / AdMob / AppOpen_Code methods), **Test on a phone** (install an APK or use the installed game, start, live results of the panel's buttons, Stop & Save Log) with Firebase DebugView buttons, and release guards in the Editor suite (step 2) and the APK suite (step 1). New `deepTest` config field (a new field, so the config version stays at v6). It's part of v1.0 until the release name is raised (`RELEASE.md`).
- **GenZ-TestSuit-v1.0 (2026-10-22):** first named release. It contains everything below: the Editor suite (steps 1–4 and the Player Settings checks) and the first APK suite. The v1–v7.3 entries below were development iterations before the release name. The config asset's own `configVersion` (v6) is separate: it only tracks default-rule upgrades.
- **v7.3 (2026-10-22):** The Editor tab has the same searchable **Game** list as the APK tab (one selection for both). The Editor suite checks the project against the picked game's sheet row, and warns when that isn't the Build Settings package.
- **v7.2 (2026-10-22):** New step 2 section **Player Settings (Android)**: Auto Graphics API off, no Vulkan, Require ES3.1 off, ES3.1+AEP on, ES3.2 on, IL2CPP, only ARMv7 + ARM64, minimum API 24 (Android 7.0), target API 36 (Android 16). New `playerSettings` config field (no version bump).
- **v7.1 (2026-10-22):** APK suite: pick the **game from the sheet** (any studio game, this project first), test a game **already installed** on the phone without an APK (the suite opens it itself), and a **phone list** with each phone's state and the game's installed version, refreshed in the background. The sheet's Taichi 2.0 count is checked against Remote Config.
- **v7 (2026-10-22):** **GenZ-ApkTestSuit, first version** (§6). It installs the release APK on a USB-connected phone, records the session, and checks Build & Device, SDK Init, Analytics Events (Firebase, GameAnalytics, Adjust, AppMetrica), Ads and Stability from logcat. It needs `GenZLog.cs` and its `[GenZ]` lines in `AdsScripts-GenzAds`. New `apk` settings in the config (a new field, so the config version stays at v6). The window keeps each tab's results separately.
- **v6.4 (2026-10-16):** The ad waterfall check understands the **vendor switch** (`useSdkVendorSwitch`: 0 = MAX first, 1 = MAX / AdMob one by one, 2 = two MAX then one AdMob). On AdMob's turn, AdMob must be tried first and MAX only after its readiness check. MAX readiness in a helper method is now recognised, which fixes the false "No method checks MAX … readiness" failure. Projects without the switch are checked as before. New `vendorSwitchFields` setting (a new field, so the config version stays at v6).
- **v6.3 (2026-10-16):** The AdsGapTimer reset is accepted in either place: in the SDK callbacks (MAX interstitial displayed/hidden, AdMob interstitial opened/closed, MAX reward received, AdMob reward callback) or at every call site. It's checked per network: an interstitial passes only when both MAX and AdMob reset (before, MAX alone was enough). The direct AdMob rewarded check moved to its own section, **Rewarded: reward goes to the right callback**, and runs either way. Code-check only; the config version stays at v6.
- **v6.2 (2026-10-16):** The results survive a script reload: editing a script no longer clears the window. This is a window-only change, so the config version stays at v6.
- **v6.1 (2026-10-14):** Click a step in the step bar to list only its checks (click again, or the chip's ✕, to list every step). Tab counts stay for the whole run.
- **v6 (2026-10-14):**
  - New step **3 · Taichi**: both switches, and for each version that's on (or required): the event, value + currency, the switch check, called from every revenue handler, saved and reset, the Remote Config threshold, safe parsing (InvariantCulture), a missing-key default, and the 2.0 impression count against the sheet. **Run Taichi Checks** toggle. Ad Calling Scenarios is now step 4.
  - Completed steps (no failure, no warning) keep their number and get a tick badge and a green halo; connectors between completed steps turn green.
  - The config upgrades to v6 automatically (rules and settings reset to the new defaults).
- **v5.1 (2026-10-14):**
  - **Auto Apply All IDs from Sheet** button: writes the sheet value into every ID that's empty, wrong or padded with spaces, after a confirmation dialog, then re-runs the suite. It's always in Settings, and also appears under the step bar when the last run found differences.
  - Wider Settings labels, so "Run Scene & Asset Checks" isn't cut.
  - `google-services.json`: every copy is checked against the Build Settings package (one row per copy), with a warning when there's more than one.
- **v5 (2026-10-14):**
  - **Step bar**: every check belongs to 1 · IDs Check, 2 · SDK Integration or 3 · Ad Calling Scenarios. The bar above the results shows each step's state: Completed, Minor issues or Errors. It doesn't filter; the filter tabs and the list cover all steps. Copy Report is grouped by step.
  - New **SDK file checks** (step 2): `google-services.json` for this package, generated Firebase config in sync, Firebase modules, required SDKs installed, every SDK's Android dependencies resolved into `mainTemplate.gradle`, AndroidX/Jetifier, the aapt2 override path, manifest not debuggable. New **Run SDK File Checks** toggle; "Run ID Checks" is now labelled **Run Scene & Asset Checks**.
  - New **ad revenue** code check (step 3): every format's paid event reports to Adjust, Firebase and AppMetrica (`revenueSinks`).
  - New scene rule **AppMetrica**: API key format and the sheet's `AppMatrica` column.
  - **Open** works on any file, not only scripts.
  - The config upgrades to v5 automatically (rules and code-check settings reset to the new defaults).
- **v4.1 (2026-10-14):** The passed / failed / warnings counts are now clickable filter tabs (All, Passed, Failed, Warnings), each with its count in a coloured pill. Check names wrap instead of being cut, the name column can be resized by dragging, and multi-line values no longer overlap the next row. This is a window-only change, so the config version stays at v4.
- **v4 (2026-10-09):**
  - New **code checks**:
    - Ad waterfall, and the fallback when MAX isn't initialized.
    - App Open is AdMob only, MREC is MAX only, and App Open is skipped after a fullscreen ad.
    - Banner exclusivity.
    - Reload and retry for every format, plus null-safety of AdMob ad objects.
    - AdsGapTimer reset at every interstitial call site, following live call paths through callers, UI events and Unity messages.
    - AdsGapTimer reset in every rewarded callback; direct AdMob rewarded calls with no callback set.
    - Remote Config sets the cooldown, after activation, with a default or guard, and nothing before it can throw.
    - AdMob main-thread events, string `Invoke` targets, delegate null checks, empty catches.
  - New scene rule: the AdsGapTimer object exists with `timeLeft > 0`.
  - **Run ID Checks** / **Run Code Checks** toggles.
  - **Open** button on findings, and `file:line` in the Markdown report.
  - Compile errors are reported.
  - The config upgrades to v4 automatically.
- **v3 (2026-10-09):**
  - New rule targets: **SceneObject** (Adjust, GameAnalytics, UmpManager) and **Asset** (the GameAnalytics, Google Mobile Ads and AppLovin settings).
  - New checks:
    - Adjust token, and the Production environment.
    - GA Android game and secret keys, and Send Version ON.
    - UmpManager active, with Test Mode OFF.
    - Google Mobile Ads Android App ID, with Optimize Initialization and Optimize Ad Loading ON.
    - AppLovin SDK key and AdMob App ID, each cross-checked against the sheet and against the other component.
  - A strict whitespace check on every ID.
  - Field lookup now includes `[HideInInspector]` fields.
  - The config upgrades to v3 automatically (rules reset to the new defaults).
  - Menu moved to **GenZ → Test Suit**.
- **v2 (2026-10-09):**
  - The studio sheet link is now the built-in default.
  - The Ads Object Name defaults to `ADs`, with an auto-detect fallback that shows a warning.
  - The package name is always read from Build Settings; the override field is removed.
  - Sheet reading now handles the studio layout: the AdMob and MAX tables are read separately by their header blocks, column matching is exact, and the MAX SDK key is found by pattern.
  - In-app IDs are no longer looked up in the sheet.
  - Older config assets upgrade automatically.
  - Verified against the live sheet: every ID in this project's Splash `Ads` object matches.
- **v1 (2026-10-09):** GenZ-EditorTestSuit: scene and Ads object auto-detection, script-presence checks, ID format checks, Google Sheet verification by package name (row and column layouts), Markdown report.
