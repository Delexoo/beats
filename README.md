# Beats

A clean, frameless desktop pill-widget for Windows that plays your local music and downloads tracks from almost any site (YouTube, Spotify, SoundCloud, Instagram, TikTok, etc.) as MP3.

## Download

**Latest installer:** [Beats-Setup-x64.exe](https://github.com/Delexoo/beats/releases/latest/download/Beats-Setup-x64.exe)

## Features

- **Pill widget**: dark rounded bar with blue circular buttons — Settings, Loop, Previous, Play/Pause, Next.
- **Always on top, draggable**, no taskbar entry. Drag the pill anywhere on screen.
- **CTRL+\\ hotkey** slides the widget off the nearest screen edge and back in on the next press.
- **Top-of-screen toggle**: a tiny chevron at the top center of your primary monitor — click to hide / show the widget.
- **Loop current song** — toggle on/off with the loop button.
- **Folder-based playlists** — each playlist is a folder under `My Music\Beats`, so all your songs stay portable.
- **Paste any URL → MP3**: powered by `yt-dlp` + `ffmpeg` (auto-downloaded on first run). Spotify links use **spotDL** for tracks, albums, and playlists.
- **Create / rename / delete playlists**, add tracks from disk, remove tracks.

## Requirements

- Windows 10 / 11 (x64)
- .NET 8 Desktop Runtime (or build from source with the .NET 8 SDK)

## Build & Run

```powershell
dotnet restore
dotnet build -c Release
dotnet run --project MusicWidget -c Release
```

On first launch the app will silently download `yt-dlp.exe` (~10 MB) and `ffmpeg.exe` (~80 MB) into `%APPDATA%\Beats\tools`. The first Spotify download also fetches `spotdl.exe` (~42 MB). After that, downloads are local and instant.

## Build installer

Requires [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
powershell -ExecutionPolicy Bypass -File Installer\Build-Installer.ps1
```

Output: `Installer\Output\Beats-Setup-x64.exe` (also copied to `website\downloads\` for local preview).

## GitHub setup

1. Create a new repo on GitHub (e.g. `beats`).
2. Repo: [github.com/Delexoo/beats](https://github.com/Delexoo/beats)
3. Push this project:

```powershell
git init
git add -A
git commit -m "Initial commit: Beats app, installer, and landing page"
git branch -M main
git remote add origin https://github.com/Delexoo/beats.git
git push -u origin main
```

4. Publish a release (builds the installer automatically):

```powershell
git tag v2.2.0
git push origin v2.2.0
```

Or run **Actions → Release → Run workflow** from the GitHub UI.

Each release uploads `Beats-Setup-x64.exe` under a stable filename. The website Download button points at `releases/latest/download/Beats-Setup-x64.exe`, so visitors always get the newest build.

### GitHub Pages (optional)

In repo **Settings → Pages**, set source to the `/website` folder on `main`. Your landing page will be live at `https://delexoo.github.io/beats/`.

## Hotkeys

| Action | Shortcut |
|---|---|
| Slide widget off / back in | `CTRL` + `\` |

## File layout

```
%APPDATA%\Beats\
  settings.json
  tools\yt-dlp.exe
  tools\ffmpeg.exe
  tools\spotdl.exe

%USERPROFILE%\Music\Beats\   (default; configurable in settings)
  <Playlist 1>\
    *.mp3
```

## License

MIT — see [LICENSE](LICENSE).
