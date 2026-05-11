# GG1MapChooser v1.8.0

## Highlights

- Added native Panorama yes/no voting for the admin "change map or not" flow.
- Added map pools with player/admin pool voting and active pool switching.
- Added low-player map pools that are selected automatically when the server population is below the configured threshold.
- Added `wasd`, `chat`, and `both` menu modes for map votes, nominations, and pool votes.
- Added nomination cooldowns so recently nominated maps can be temporarily locked.
- Added a configurable maximum number of map extensions per map.
- Added an optional "No Vote" line and configurable vote count display in WASD menus.
- Extended the public WASD menu API with disabled items, post-select actions, menu adapters, and close-by-player support.

## Fixes

- Fixed stale `voteTimer` state that could block later votes.
- Allowed RTV immediately when `RTVDelayFromStart` is zero or lower.
- Avoided starting the time-limit map-change timer too early when the vote is delayed to round start or round end.
- Improved map lookup for display names, low-player maps, and workshop map change handling.

## Compatibility Notes

- `CounterStrikeSharp.API` remains pinned to `1.0.367` for reproducible builds.
- Legacy `GGMCmaps.json` format is still supported; pool-based format is optional.
- The active `ggmcAPI` project is the sibling `../ggmcAPI/ggmcAPI.csproj`, not the excluded nested copy under `GG1MapChooser/ggmcAPI`.
