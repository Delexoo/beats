# Changelog

All notable changes to **Beats** are documented here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
