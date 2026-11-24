# GenZ Test Suit

The GenZ studio's pre-release checker for Android games built on `AdsScripts-GenzAds`. It runs in the Unity Editor from **GenZ → Test Suit** and has three tabs:

- **GenZ-EditorTestSuit**: ad IDs against the studio sheet (with Auto Apply), SDK integration and Player Settings, Taichi, and the ad-calling code.
- **GenZ-ApkTestSuit**: installs the release APK on a USB phone and checks SDK init, analytics events, ads and stability from logcat.
- **GenZ-DeepTestSuit**: switches the project between a Test and a Release Environment and adds a shake panel on the phone that calls the game's own ad scripts.

## Latest release: [GenZ-TestSuit-v1.0.1](https://github.com/GenzProjects/GenZ-TestSuit/releases/latest) (2026-11-20)

## Install

No GitHub login needed. In Unity: **Window → Package Manager → + → Add package from git URL…**

```
https://github.com/GenzProjects/GenZ-TestSuit.git#v1.0.1
```

or in `Packages/manifest.json`:

```json
"com.genz.testsuit": "https://github.com/GenzProjects/GenZ-TestSuit.git#v1.0.1"
```

Keep the `#v…` tag so every game gets the same release. To update, change the tag to the newest one under [Releases](https://github.com/GenzProjects/GenZ-TestSuit/releases).

The first run creates `Assets/GenZ-TestSuit/GenZTestSuitConfig.asset` in your project; commit it. Apply Test Environment copies the phone panel to `Assets/GenZ-DeepTest/`.

Requires Unity 2022.3 or newer. The APK suite also needs `GenZLog.cs` and its `[GenZ]` calls in the game's `AdsScripts-GenzAds`.

Full documentation: [GenZ-TestSuit.md](https://github.com/GenzProjects/GenZ-TestSuit/blob/v1.0.1/Documentation~/GenZ-TestSuit.md) (in the release tag).

## About this branch

The package's code is in the release tags (`v1.0.0`, `v1.0.1`), which the install link above uses. The `main` and `development` branches hold the studio's host test project (a stripped game with the SDKs and `AdsScripts-GenzAds`) that the suite is developed and tested against.
