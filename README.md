# GenZ Test Suit

The GenZ studio's pre-release checker for Android games built on `AdsScripts-GenzAds`. It runs in the Unity Editor from **GenZ → Test Suit** and has three tabs:

- **GenZ-EditorTestSuit**: ad IDs against the studio sheet (with Auto Apply), SDK integration and Player Settings, Taichi, and the ad-calling code.
- **GenZ-ApkTestSuit**: installs the release APK on a USB phone and checks SDK init, analytics events, ads and stability from logcat.
- **GenZ-DeepTestSuit**: switches the project between a Test and a Release Environment and adds a shake panel on the phone that calls the game's own ad scripts.

## Install

The repo is public: no GitHub login is needed.

**Window → Package Manager → + → Add package from git URL…**

```
https://github.com/GenzProjects/GenZ-TestSuit.git#v1.0.1
```

or in `Packages/manifest.json`:

```json
"com.genz.testsuit": "https://github.com/GenzProjects/GenZ-TestSuit.git#v1.0.1"
```

The tag above is the latest release. Keep the `#v…` tag so every game gets the same release. To update, change the tag. Every release and its changes are under [Releases](https://github.com/GenzProjects/GenZ-TestSuit/releases/latest).

The first run creates `Assets/GenZ-TestSuit/GenZTestSuitConfig.asset` in your project; commit it. Apply Test Environment copies the phone panel to `Assets/GenZ-DeepTest/`.

**Coming from the copied folder?** Delete `Assets/Editor/GenZ-TestSuit/` but keep `GenZTestSuitConfig.asset` and its `.meta` (move them to `Assets/GenZ-TestSuit/`).

Requires Unity 2022.3 or newer. The APK suite also needs `GenZLog.cs` and its `[GenZ]` calls in the game's `AdsScripts-GenzAds`.

Full documentation: [Documentation~/GenZ-TestSuit.md](Documentation~/GenZ-TestSuit.md).
