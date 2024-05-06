# GG1MapChooser
The **GG1MapChooser** plugin enhances map selection and management for CS2 game servers. It introduces robust controls over map voting, nominations, and automatic map rotation based on player counts and preferences.

## Features
- **Random Map Selection** - Automatically selects the next map with configurable weights.
- **Map Voting System** - Players can vote on which maps to play next. If no one votes, the plugin chooses a random map from the list to vote.
- **Map Nominations** - Players can nominate maps for voting.
- **Player Count Thresholds** - Specify minimum and maximum player counts for maps to be included in the vote.
- **Command Controls** - Includes commands like rtv (rock the vote) and nominate for direct player interaction.
- **Admin commands** - Include commands for admins to start voting or change the map.

## Configuration Files

### Map Configuration
Define map settings in `csgo/cfg/GGMCmaps.json`:
- Minimum and maximum player counts for map eligibility.
- Map weighting (default is **"1"**).
- Is it Workshop or classic map.

###  Plugin Settings
Customize plugin behaviour in `csgo/addons/counterstrikesharp/configs/plugins/GG1MapChooser/GG1MapChooser.json`:
- `RememberPlayedMaps` - Number of recent maps to exclude from upcoming votes.
- `RTVDelay` - Time delay at the start of the map during which rtv is disabled.
- `RTVInterval` - Cooldown period after a failed vote.
- `VotingTime` - Duration for players to cast their votes.
- `MapsInVote` - Number of maps in the voting pool *(5 is recommendedf value)*.
- `VotesToWin` - Percentage of votes needed to win the vote.
- `RandomMapOnStart` - Enable changing to a random map on server restart.
- `LastDisconnectedChangeMap` - Switch to a random map after the last player disconnects.

## Usage
- **Voting:** Players can initiate a map vote using `!rtv` or `rtv` in chat. The required percentage of votes to start a vote is controlled by the `VotesToWin` setting.
- **Nominating:** Players can nominate a map by typing `!nominate <mapname>` or simply `nominate` to bring up a list of eligible maps based on current server conditions.

## Admin Commands
- **Map Change** - Use `css_maps` or `!maps` to change the map manually or start a vote with standard or custom selections.
- **Quick Map Selection** - Use `ggmap <partofmapname>` helps find and switch to a map quickly by using a partial name match.

### External Controls:
- `ggmc_mapvote_start [time]` - Trigger a map vote externally with an optional time parameter.
- `ggmc_auto_mapchange` - Automatically change a map to random map.
- `ggmc_nortv` - Disable the rtv command temporarily to maintain game continuity.

## Disclaimer
The plugin is provided **"as-is"** and fulfills the specific requirements it was designed for. While I am not planning further major updates, I welcome suggestions that might benefit a broader user base, which could lead to additional features.

## Credits
Thank you for [UMC Mapchooser](https://forums.alliedmods.net/showthread.php?t=134190) for the main ideas.
