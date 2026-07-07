# VSCN Language Pack Updater

`vscnlangpackupdater` is a small client-side code mod that keeps the VSCN Vintage Story Chinese Language Pack up to date from GitHub Releases.

On client startup it requests:

```text
https://api.github.com/repos/vscn-studio/VintageStory-Chinese-Language-Package/releases/latest
```

If the release tag is newer than the loaded `vscnlangpack` version, it downloads the matching `VintageStory-Chinese-Language-Package-<version>.zip` asset into the player's `Mods` directory and asks the player to restart the game.

## Build

Set `VINTAGE_STORY` to the Vintage Story install directory that contains `VintagestoryAPI.dll`, then run. Vintage Story 1.22.x builds currently require a .NET 10 SDK for code mods.

```powershell
dotnet build src/Updater/Updater.csproj -c Release
```

The mod files are copied to:

```text
src/Updater/bin/Release/mod/
```

Zip the contents of that `mod` directory when publishing the updater mod.

## Config

The mod writes `ModConfig/vscnlangpackupdater.json` on first launch.

```json
{
  "enabled": true,
  "checkDelayMilliseconds": 3000,
  "httpTimeoutSeconds": 30,
  "notifyWhenUpToDate": false,
  "notifyOnFailure": true,
  "deleteOldPackages": true,
  "releasesApiUrl": "https://api.github.com/repos/vscn-studio/VintageStory-Chinese-Language-Package/releases/latest",
  "assetFilePrefix": "VintageStory-Chinese-Language-Package-",
  "assetFileSuffix": ".zip"
}
```
