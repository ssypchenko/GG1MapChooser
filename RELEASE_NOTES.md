# GG1MapChooser v1.8.0

## Highlights

- Added native Panorama yes/no voting for the admin "change map or not" flow.
- Added map pools with player/admin pool voting and active pool switching.
- Added low-player map pools that are selected automatically when the server population is below the configured threshold.
- Added `wasd`, `chat`, and `both` menu modes for map votes, nominations, and pool votes.
- Added nomination cooldowns so recently nominated maps can be temporarily locked.
- Added a configurable maximum number of map extensions per map.
- Added an optional "No Vote" line and configurable vote count display in WASD menus.
- Improved vote and map-change timer reliability.
- Allowed RTV immediately when `RTVDelayFromStart` is zero or lower.
- Improved map lookup for display names, low-player maps, and workshop map change handling.
- Extended the public WASD menu API with disabled items, post-select actions, menu adapters, and close-by-player support.

## Map Pools

`GGMCmaps.json` now supports both the legacy flat map list and a new pool-based format.

Legacy format is still supported:

```json
{
  "de_mirage": {
    "ws": false,
    "display": "Mirage",
    "mapid": "",
    "minplayers": 0,
    "maxplayers": 0,
    "weight": 1
  }
}
```

New pool-based format:

```json
{
  "Small Maps": {
    "gg_small_map": {
      "ws": false,
      "display": "Small Map",
      "mapid": "",
      "minplayers": 0,
      "maxplayers": 12,
      "weight": 1
    }
  },
  "Large Maps": {
    "gg_large_map": {
      "ws": false,
      "display": "Large Map",
      "mapid": "",
      "minplayers": 10,
      "maxplayers": 0,
      "weight": 1
    }
  }
}
```

If the legacy format is used, the plugin treats it as a single pool named `default`.

When the pool-based format is used, the active pool is selected in this order:

1. The previous active pool, if it still exists.
2. `PoolVoteSettings.DefaultPool`, if configured and found.
3. The first available pool in `GGMCmaps.json`.

The active pool is used by normal map votes, RTV, admin map votes, and random map selection. When an admin switches the active pool manually, the plugin clears pending nominations, admin-selected maps, pending next map state, and temporary vote state.

## Pool Voting

Players can request a vote for the active map pool with:

```text
rtm
!rtm
/rtm
css_rtm
!css_rtm
```

Admins can also start a pool vote from `css_maps` using `Start Pool Vote`.

Pool voting works in two stages:

1. Players first vote to start a pool vote, similar to RTV.
2. Once the configured threshold is reached, the plugin opens a menu where players choose the pool.

The threshold uses `PoolVoteSettings.VotesToWin`. If that value is `0`, the plugin falls back to `VoteSettings.VotesToWin`.

The result rules are:

- A pool wins only when it is the single option with the highest vote count.
- If no one votes, the result is tied, or the current pool wins, the active pool stays unchanged.
- If a different pool wins, it becomes the active pool and the plugin clears pool-sensitive state.
- If a map vote is due while a pool vote is in progress, the map vote is queued and starts after the pool vote finishes.

Pool voting requires at least two pools.

Pool voting also requires exactly one automatic map vote mode to be enabled:

- `TimeLimitSettings.VoteDependsOnTimeLimit = true` and `WinDrawSettings.VoteDependsOnRoundWins = false`, or
- `TimeLimitSettings.VoteDependsOnTimeLimit = false` and `WinDrawSettings.VoteDependsOnRoundWins = true`.

If both are enabled or both are disabled, pool voting is disabled to avoid ambiguous timing.

## Low-Player Pool

Low-player maps are configured separately in `OtherSettings.LowPlayerMaps`. This is not one of the normal map pools and does not appear in pool votes.

Low-player mode becomes active when the number of real players, including spectators, is less than or equal to `OtherSettings.LowPlayerMaxPlayers`.

When low-player mode is active:

- normal map votes use `LowPlayerMaps`;
- RTV uses `LowPlayerMaps`;
- automatic random map changes use `LowPlayerMaps`;
- display name lookup works for low-player maps;
- players are notified that the low-player map pool is being used.

When the player count rises above the threshold, the plugin returns to the normal active pool.

## Menu Modes

The plugin now supports string-based menu modes:

```text
wasd
chat
both
```

They are used by:

- `VoteSettings.EndMapVoteMenuMode`
- `VoteSettings.NominationsMenuMode`
- `PoolVoteSettings.MenuMode`

Mode behaviour:

- `wasd` opens only the WASD HTML menu.
- `chat` or `chatmenu` opens only the CounterStrikeSharp chat menu.
- `both` opens both menu types at the same time; selecting in one closes the other.
- Empty or unknown values fall back to `wasd`.

For the admin "Vote to Change Map or Not" flow, `VoteSettings.YesNoVotePanorama = true` takes priority. When enabled, that yes/no vote uses the Panorama vote UI instead of `EndMapVoteMenuMode`.

## Nomination Cooldown

`VoteSettings.RememberNominatedMaps` adds cooldown support for nominated maps.

After a map vote completes, nominated maps from that vote are stored in a recent nomination cooldown list. While a map is in that list:

- it cannot be nominated again;
- it is shown in nomination menus as a disabled entry with the `map.cooldown` suffix.

The value controls how many recently nominated maps are remembered. `0` disables the feature.

This is a map-count based cooldown, not a time-based cooldown.

## Maximum Map Extend Count

`VoteSettings.MaxExtendMapCount` limits how many times the current map can be extended through the `Extend Map` vote option.

Behaviour:

- `0` means unlimited extensions.
- The option is shown only when `VoteSettings.ExtendMapInVote = true`.
- If the limit has been reached, `Extend Map` is not added to the vote menu.
- The counter resets with the map state.
- The extension length still comes from `VoteSettings.ExtendMapTimeMinutes`.

## Immediate RTV When Delay Is Zero

If `RTVSettings.AllowRTV = true` and `RTVSettings.RTVDelayFromStart <= 0`, RTV is now available immediately after the match starts.

This only affects the initial RTV delay. After an RTV starts, `RTVSettings.IntervalBetweenRTV` is still used as the cooldown before another RTV can be started.

## New Configuration Parameters

### VoteSettings

`RememberNominatedMaps`

Default: `0`

Number of recently nominated maps to keep in the cooldown list. Maps in this list cannot be nominated again. `0` disables nomination cooldown.

`NominationsMenuMode`

Default: `""` (falls back to `wasd`)

Nomination menu mode. Accepted values: `wasd`, `chat`, `both`.

`EndMapVoteMenuMode`

Default: `""` (falls back to `wasd`)

End-of-map vote menu mode. Accepted values: `wasd`, `chat`, `both`.

`YesNoVotePanorama`

Default: `true`

Enables native Panorama yes/no voting for the admin "Vote to Change Map or Not" flow.

`PanoramaSFUIString`

Default: `"#SFUI_Vote_None"`

SFUI string used by the Panorama yes/no vote.

`MaxExtendMapCount`

Default: `0`

Maximum number of times the current map can be extended. `0` means unlimited.

### PoolVoteSettings

New configuration section for map pool voting.

`Enable`

Default: `false`

Enables player/admin pool voting.

`DefaultPool`

Default: `""`

Preferred active pool name when the mapcycle is loaded. If empty or not found, the first available pool is used.

`DelayFromStart`

Default: `90`

Initial delay in seconds before pool voting is available.

`IntervalBetweenVotes`

Default: `120`

Cooldown in seconds after a pool vote starts before another pool vote can be started.

`VotesToWin`

Default: `0.0`

Required player vote ratio to start a pool vote. If `0`, `VoteSettings.VotesToWin` is used.

`VotingTime`

Default: `20`

Time in seconds for players to choose a pool after the pool vote menu opens.

`BlockBeforeMapVoteSeconds`

Default: `30`

In time-limit mode, blocks pool voting when the scheduled map vote is this many seconds away or closer.

`BlockBeforeMapVoteRounds`

Default: `1`

In round-wins mode, blocks pool voting when the scheduled map vote is this many rounds away or closer.

`MenuMode`

Default: `""` (falls back to `wasd`)

Pool vote menu mode. Accepted values: `wasd`, `chat`, `both`.

### MenuSettings

`DisplayVotesCount`

Default: `true`

Shows vote counters next to options in WASD vote menus.

### OtherSettings

`LowPlayerMaxPlayers`

Default: `8`

Maximum real player count, including spectators, for low-player mode.

`LowPlayerMaps`

Default: `{}`

Map list used while low-player mode is active. Entries use the same map format as normal maps: `ws`, `display`, `mapid`, `minplayers`, `maxplayers`, `weight`.

## Configuration Example

```json
{
  "VoteSettings": {
    "RememberNominatedMaps": 3,
    "NominationsMenuMode": "both",
    "EndMapVoteMenuMode": "wasd",
    "YesNoVotePanorama": true,
    "PanoramaSFUIString": "#SFUI_Vote_None",
    "MaxExtendMapCount": 2
  },
  "PoolVoteSettings": {
    "Enable": true,
    "DefaultPool": "Small Maps",
    "DelayFromStart": 90,
    "IntervalBetweenVotes": 120,
    "VotesToWin": 0.6,
    "VotingTime": 20,
    "BlockBeforeMapVoteSeconds": 30,
    "BlockBeforeMapVoteRounds": 1,
    "MenuMode": "both"
  },
  "MenuSettings": {
    "DisplayVotesCount": true
  },
  "OtherSettings": {
    "LowPlayerMaxPlayers": 8,
    "LowPlayerMaps": {
      "gg_small_map": {
        "ws": false,
        "display": "Small Map",
        "mapid": "",
        "minplayers": 0,
        "maxplayers": 8,
        "weight": 1
      }
    }
  }
}
```

## Suggested Server Testing

- Start with a pool-based `GGMCmaps.json` containing at least two pools.
- Set `PoolVoteSettings.Enable = true`.
- Use `rtm` / `!rtm` / `css_rtm` and confirm that a pool vote starts after the configured vote threshold.
- Confirm that ties and votes for the current pool keep the existing active pool.
- Switch pools from `css_maps` and confirm that nominations and pending selected maps are cleared.
- Set `LowPlayerMaxPlayers` above the current online count and confirm RTV/map vote uses `LowPlayerMaps`.
- Test `NominationsMenuMode`, `EndMapVoteMenuMode`, and `PoolVoteSettings.MenuMode` with `wasd`, `chat`, and `both`.
- Set `RememberNominatedMaps` above `0`, nominate a map, complete a vote, and confirm the map cannot be nominated again until it leaves cooldown.
- Set `MaxExtendMapCount = 1`, enable `ExtendMapInVote`, extend once, and confirm the extend option is not available again on the same map.
- Set `RTVDelayFromStart = 0` and confirm RTV is available immediately after the match starts.

## Compatibility Notes

- `CounterStrikeSharp.API` remains pinned to `1.0.367` for reproducible builds.
- Legacy `GGMCmaps.json` format is still supported; pool-based format is optional.
- The active `ggmcAPI` project is the sibling `../ggmcAPI/ggmcAPI.csproj`, not the excluded nested copy under `GG1MapChooser/ggmcAPI`.
