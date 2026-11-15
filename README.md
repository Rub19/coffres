# Coffres

Counts the chests you open in Fortnite, from the replays the game saves after each match. In French, English and Spanish.

- **Web**: https://coffres.ethone.dev (Chrome or Edge on a PC). The analysis engine runs in the browser (WebAssembly), nothing is uploaded.
- **Windows app**: `Coffres.exe` from the site. It reads the replays folder directly, counts in the background, shows an in-game overlay, a notification area icon, notifications and updates itself.

Not affiliated with Epic Games.

## Credits

The replay analysis is [Fortnite Chest Counter](https://github.com/LED-N/Fortnite-ChestCounter) by LED-N (MIT), included as the `vendor` submodule, with the [FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor) reader (MIT) and OozSharp (GPL v3). Coffres is therefore licensed under the GPL v3: see `LICENSE` and `LICENSES.txt`.

## Layout

| Path | What |
|---|---|
| `vendor/` | LED-N/Fortnite-ChestCounter (submodule) |
| `patches/browser-aes.patch` | Managed AES-ECB for the browser (WebAssembly .NET has no AES); applied by `build.sh` |
| `wasm/` | WebAssembly build of the engine (`Summary.cs` is shared with the app) |
| `site/` | The whole interface (`index.html`), the web worker, licenses |
| `app/` | `Coffres.exe`: local server on `localhost:47821`, Edge app window, overlay, tray icon, updater |

## Build

Needs the .NET 10 SDK and Git Bash.

```bash
git clone --recursive https://github.com/Rub19/coffres
bash build.sh
dotnet publish app/Coffres.csproj -c Release -o dist
```

`bash release.sh "notes fr" "notes en" "notes es"` builds both, writes `site/version.json` (used by the app's updater) and deploys with Wrangler.
