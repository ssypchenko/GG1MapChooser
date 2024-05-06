### **GG1MapChooser**

The **GG1MapChooser** plugin enhances map selection and management for CS2 game servers. It introduces robust controls over map voting, nominations, and automatic map rotation based on player counts and preferences.

## **Features**

Random Map Selection: Automatically selects the next map with configurable weights.
Map Voting System: Players can vote on which maps to play next. If no one votes, the plugin chooses a random map from the list to vote.
Map Nominations: Players can nominate maps for voting.
Player Count Thresholds: Specify minimum and maximum player counts for maps to be included in the vote.
Command Controls: Includes commands like rtv (rock the vote) and nominate for direct player interaction.
Admin commands: Include commands for admins to start voting or change the map.

## **Configuration Files**

Map Configuration: Define map settings in csgo/cfg/GGMCmaps.json:
Minimum and maximum player counts for map eligibility.
Map weighting (default is "1").
Is it Workshop or classic map

**Plugin Settings:** Customize plugin behaviour in csgo/addons/counterstrikesharp/configs/plugins/GG1MapChooser/GG1MapChooser.json:
_RememberPlayedMaps:_ Number of recent maps to exclude from upcoming votes.
_RTVDelay:_ Time delay at the start of the map during which rtv is disabled.
_RTVInterval:_ Cooldown period after a failed vote.
_VotingTime:_ Duration for players to cast their votes.
_MapsInVote:_ Number of maps in the voting pool (5 is recommendedf value).
_VotesToWin:_ Percentage of votes needed to win the vote.
_RandomMapOnStart:_ Enable changing to a random map on server restart.
_LastDisconnectedChangeMap:_ Switch to a random map after the last player disconnects.

## **Usage**

_Voting:_ Players can initiate a map vote using !rtv or rtv in chat. The required percentage of votes to start a vote is controlled by the VotesToWin setting.

_Nominating:_ Players can nominate a map by typing !nominate <mapname> or simply nominate to bring up a list of eligible maps based on current server conditions.

**Admin Commands**

_Map Change:_ Use css_maps (or !maps) to change the map manually or start a vote with standard or custom selections.

_Quick Map Selection:_ ggmap <partofmapname> helps find and switch to a map quickly by using a partial name match.

_External Controls:_
ggmc_mapvote_start [time]: Trigger a map vote externally with an optional time parameter.
ggmc_auto_mapchange: Automatically change a map to random map.
ggmc_nortv: Disable the rtv command temporarily to maintain game continuity.

## **Translation**

Available in English and Russian. You can add your own translations by sending me "lang".json file

## **Disclaimer**

The plugin is provided "as-is" and fulfills the specific requirements it was designed for. While I am not planning further major updates, I welcome suggestions that might benefit a broader user base, which could lead to additional features.

## **Credits** 

Thank you for UMC Mapchooser for the main ideas
https://forums.alliedmods.net/showthread.php?t=134190
