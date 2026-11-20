# Free Realms Plugin

Starts Free Realms from the website, like the original SOE browser plugin did.

1. The website's Play Now! button opens `freerealms://play/?site=…&guid=…&ticket=…` (or `freerealms://login/?site=…&session=…` for Customize!).
2. The plugin asks once before trusting a website, then reads the server addresses from `<site>/website/plugin/config`.
3. It downloads the game client from the client manifest (resumable), and on Linux/macOS a pinned, SHA-256 verified Wine build, so players never install Wine themselves.
4. It starts `FreeRealms.exe` (through Wine on Linux/macOS) and reports its progress and "Game Running" to the website on `http://127.0.0.1:20055/status`, answering only the trusted website.

Everything lives in the user's data folder (`%LOCALAPPDATA%\FreeRealmsPlugin`, `~/Library/Application Support/FreeRealmsPlugin`, `~/.local/share/FreeRealmsPlugin`). No administrator rights are needed.

## Commands

| Command | Does |
| --- | --- |
| `FreeRealmsPlugin` | Installs the plugin for the current user and registers `freerealms://` |
| `FreeRealmsPlugin --client <folder>` | Same, and uses an existing client instead of downloading one |
| `FreeRealmsPlugin --uninstall` | Removes the `freerealms://` registration |

## Building

```sh
dotnet publish src/Sanctuary.Plugin -c Release -r win-x64      # FreeRealmsPlugin-windows.exe
dotnet publish src/Sanctuary.Plugin -c Release -r linux-x64    # FreeRealmsPlugin-linux
```

The macOS app has to be assembled on a Mac (it uses `osacompile` and `codesign`):

```sh
dotnet publish src/Sanctuary.Plugin -c Release -r osx-x64 -o out/osx-x64
dotnet publish src/Sanctuary.Plugin -c Release -r osx-arm64 -o out/osx-arm64
sh src/Sanctuary.Plugin/macos/build-app.sh out/osx-x64 out/osx-arm64 out   # FreeRealmsPlugin-macos.zip
```

The `Free Realms Plugin` GitHub workflow (run it manually from the Actions tab) builds all three.

## Serving the downloads

Put the three files in a folder and set `Plugin:DownloadsPath` in the WebAPI settings. Set `Plugin:GatewayAddress`, `Plugin:LoginAddress` and (behind a proxy) `Plugin:PublicSiteUrl` to the addresses players use.
