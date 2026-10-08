# Changelog

All notable changes to **Windows Beats** are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.2.19] - 2026-10-08

### Added
- Microsoft / Windows trademark disclaimer on the website and installer
- Music-note cover placeholders when artwork is missing

### Changed
- Product display name is now **Windows Beats**
- Library dashboard UI simplified (clearer track rows, quieter playlist actions)
- Track list mouse-wheel scrolls normally instead of jumping selection
- Artwork lookup caches misses and prefetches more covers per playlist
- In-app update button waits for startup checks and installs more reliably

### Fixed
- spotDL reporting success when no files were saved
- Update installer relaunch race after silent install

## [2.2.18] - 2026-07-09

### Added
- Playlist accordion dashboard with expandable rows and full-row click targets
- Album artwork in playlist track lists (embedded tags, disk cache, and online lookup)
- Auto-minimize dashboard when clicking outside the panel or focusing another app
- In-app update checks with user-confirmed install from the dashboard
- Global media key support for headset and keyboard transport controls
- Download thumbnails embedded in new MP3 files (`--embed-thumbnail`)

### Changed
- Faster dashboard refresh with debounced playlist and artwork loading
- Improved playlist row hit-testing so the entire header toggles expand/collapse
- Smoother song list interaction: wheel scroll per track, no scroll jump on play
- Startup shows the widget immediately; update check runs in the background

### Fixed
- Update service shutdown and relaunch reliability
- Playlist artwork not loading when opening existing playlists
- Download temp files (`.part`) excluded from track lists
- Various stability and responsiveness improvements across the dashboard

## [2.2.17] and earlier

See [GitHub Releases](https://github.com/Delexoo/beats/releases) for prior version notes.
