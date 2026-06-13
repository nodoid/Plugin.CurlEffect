# Changelog

All notable changes to **Plugin.CurlEffect** are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.5] - 2026-06-13

### Fixed
- **Page-curl drag now works on Android (and iOS).** The drag is driven from `SKCanvasView`'s
  touch events on touch platforms; `PointerGestureRecognizer`, used previously, only reports
  mouse/stylus pointers there and never delivered finger touch, so a drag never started a curl on
  a real device. Desktop (Windows / Mac Catalyst) continues to use the pointer recognizer.
- **Android system back-gesture no longer hijacks an edge page-turn.** On gesture-navigation
  devices a swipe from the screen edge is the system "back" gesture, which overlapped a backward
  curl (grab the left edge, drag inward) and could close the app. The control now registers its
  bounds as a system-gesture exclusion zone (API 29+).

### Added
- **`EdgeInset`** bindable property (also on `ICurlView`). Holds the touch-sensitive curl edge
  away from the physical screen edge, covering the cases the platform's 200dp gesture-exclusion
  cap can't reach. Defaults to `0`; the sample uses `24`.

## [1.0.4]

### Changed
- Include the `net10.0-windows` target in the NuGet package.
- Fixed the project URL and wired README content into the package release notes.
