<!DOCTYPE html>
<html lang="en">
<head>
</head>
<body>

<h1>GG1MapChooser</h1>
<p>The <strong>GG1MapChooser</strong> plugin enhances map selection and management for CS2 game servers. It introduces robust controls over map voting, nominations, and automatic map rotation based on player counts and preferences.</p>

<h2>Features</h2>
<ul>
    <li><strong>Map Voting System</strong> - Players can vote on which maps to play next. If no one votes, the plugin chooses a random map from the list.</li>
    <li><strong>Rock The Vote (rtv)</strong> - Players can request to start a vote for a new map during the game.</li>
    <li><strong>Map Nominations</strong> - Players can nominate maps for voting.</li>
    <li><strong>Player Count Thresholds</strong> - Specify minimum and maximum player counts for maps to be included in the vote.</li>
    <li><strong>Map display names</strong> - Maps can have nice display names in the settings file, for example "Mini Circular" instead of "gg_mini_circular" which is the workshop name.</li>
    <li><strong>Map Weights</strong> - Randomly selects maps to vote for the next map with configurable weights. The higher the weight, the more likely the map will be included in the vote list.</li>
    <li><strong>Admin Commands</strong> - Includes commands for admins to start voting, to vote if players want to change the map, to simply change or set the next map .</li>
    <li><strong>Map load at the game end</strong> - If this set in the config, plugin will be responsible for the load of the winning in vote map.</li>
</ul>

<h2>Configuration Files</h2>

<h3>Map Configuration</h3>
<p>Define map settings in <code>csgo/cfg/GGMCmaps.json</code>:</p>
<ul>
    <li>Minimum and maximum player counts for map eligibility <em>(not mandatary parameter)</em>.</li>
    <li>Map weighting (default is <strong>"1"</strong>) <em>(not mandatary parameter)</em>.</li>
    <li>Specify if it is a Workshop or classic map.</li>
    <li>Set the Display name for map if necessary.</li>
    <li>For Workshop maps, set the workshop map ID to use maps without a collection.</li>
</ul>

<h3>Plugin Settings</h3>
<p>Customize plugin behaviour in <code>csgo/addons/counterstrikesharp/configs/plugins/GG1MapChooser/GG1MapChooser.json</code>:</p>
<ul>
    <li><code>RememberPlayedMaps</code> - Number of recent maps to exclude from upcoming votes.</li>
    <li><code>AllowRTV</code> - Allow RTV.</li>
    <li><code>RTVDelay</code> - Time delay at the start of the map during which RTV is disabled.</li>
    <li><code>RTVInterval</code> - Cooldown period after a failed vote.</li>
    <li><code>VotingTime</code> - Duration for players to cast their votes. Can be overridden in console commands.</li>
    <li><code>EndMapVoteWASDMenu</code> - End of Map Vote in WASD menu (navigation by buttons W (up), A (down), S (previous menu), D (select menu item), "Tab" to exit).</li>
     <li><code>AllowNominate</code> - Allow Nomination.</li>
    <li><code>NominationsWASDMenu</code> - Nomination in WASD menu (true) (navigation by buttons W (up), A (down), S (previous menu), D (select menu item), "Tab" to exit) or in Chat Menu (false).</li>
    <li><code>MapsInVote</code> - Number of maps in the voting pool <em>(5 is the recommended value)</em>.</li>
    <li><code>ExtendMapInVote</code> - Set to true to add the "Extend Map" menu item. It increases the "mp_timelimit" variable.</li>
    <li><code>ExtendMapTimeMinutes</code> - Time in minutes to increase the "mp_timelimit" variable.</li>
    <li><code>VotesToWin</code> - Percentage of votes needed to win the vote <em>(0.6 (60%) is the recommended value)</em>.</li>
    <li><code>PrintPlayersChoiceInChat</code> - Print Player's choice to other players in Chat.</li>
    <li><code>ChangeMapAfterWinDraw</code> - Plugin will Change the Map after the end of the game if a map selected after the vote.</li>
    <li><code>ChangeMapAfterVote</code> - Plugin will Change the Map immediately after the vote.</li>
    <li><code>ChangeMapAfterTimeLimit</code> - Plugin will Change the Map after the end of the map (accoring to mp_timelimit).</li>
    <li><code>DelayBeforeChangeSeconds</code> - Delay before Plugin will Change the Map after the events: Win/Draw event (ChangeMapAfterWinDraw); Vote ended (ChangeMapAfterVote)</li>
    <li><code>VoteStartSound</code> - Sound played to players when the map vote starts.</li>
    <li><code>RandomMapOnStart</code> - Enable changing to a random map on server restart.</li>
    <li><code>RandomMapOnStartDelay</code> - Delay in seconds before changing to a random map on server restart.</li>
    <li><code>LastDisconnectedChangeMap</code> - Switch to a random map after the last player disconnects.</li>
    <li><code>WorkshopMapProblemCheck</code> - Checks whether the voted or admin-chosen map is loaded and if not (in case of problems with the workshop map) loads a random map.</li>
    <li><code>VoteDependsOnRoundWins</code> - Set to true if the vote start depends on the number of wins or rounds played.</li>
    <li><code>TriggerRoundsBeforeEnd</code> - Number of rounds before the end of the match to start the vote. 0 - after the win or last round, 1 - one round before the last win, etc.</li>
    <li><code>TriggerRoundsBeforEndVoteAtRoundStart</code> - Vote is triggered in number of rounds before the game end and executed on RoundStart if true and RoundEnd if false.</li>
    <li><code>VoteDependsOnTimeLimit</code> - Set to true if the vote start depends on the time limit to play.</li>
    <li><code>TriggerSecondsBeforEnd</code> - Number of seconds before the end of the map to start the vote. The map time is defined in cvar "mp_timelimit"</li>
</ul>

<h2>Usage</h2>
<ul>
    <li><strong>Voting:</strong> Players can initiate a map vote using <code>!rtv</code> or <code>rtv</code> in chat. The required percentage of votes to start a vote is controlled by the <code>VotesToWin</code> setting.</li>
    <li><strong>Nominating:</strong> Players can nominate a map by typing <code>!nominate &lt;mapname&gt;</code> or simply <code>nominate</code> to bring up a list of eligible maps based on current server conditions.</li>
    <li><strong>Re-Vote:</strong> After the vote has been taken, or if the voting menu has been accidentally closed, players can revote using the "!revote" command in chat.</li>
</ul>

<h2>Setup Examples</h2>
<ul>
    <li>If you call the vote from external plugin you have two options:
        <ul>
            <li>Use "ggmc_mapvote_start" if the external plugin ends the game so the server will change the map after the Win/Draw</li>
            <li>Use "ggmc_mapvote_with_change" command if you want that mapchooser changes the map itself immediately after the vote.</li>
        </ul>
        <p>In that case you need to have "ChangeMapAfterWinDraw": false, "ChangeMapAfterVote": false, "VoteDependsOnRoundWins": false, "VoteDependsOnTimeLimit": false.</p>
    </li>
    <li>Voting Before the End of Multiple Rounds:<br>
        <ul>
            <li>To have a vote before the game ends, set the following configuration:<br>
                <ul><li>"ChangeMapAfterWinDraw": true,</li>
                <li>"ChangeMapAfterVote": false,</li>
                <li>"VoteDependsOnRoundWins": true.</li></ul>
            </li>
            <li>In the "TriggerRoundsBeforeEnd" parameter, specify the number of rounds before the end of the game to trigger the vote (set this value greater than 0).</li>
            <li> Use "TriggerRoundsBeforEndVoteAtRoundStart": true to start the vote at the beginning of the specified round or false to start the vote at the end of that round.</li>
            <li>Triggering the Vote:<br>
                <ul><li>If "TriggerRoundsBeforeEnd" is set to 1:<br>
                    <ul><li>The vote will be triggered at the beginning of the last round.</li>
                        <li>If "TriggerRoundsBeforEndVoteAtRoundStart" is set to false, the vote will start at the end of the last round, right when you see the winners and the standard voting interface appears on the screen.</li>
                    </ul>
                    </li>
                </ul>
            </li>
            <li>Handling Early Round Endings:<br>
                <ul><li>Even if the vote is triggered at the start of the last round, the round may end before the vote finishes.</li>
                <li>To accommodate this, and if your vote is scheduled for the very end of the game, set the mp_endmatch_votenextleveltime cvar to a duration long enough to allow the plugin to complete the vote (VotingTime) and change the map (add an extra 5 seconds).</li>
                </ul>
            </li>
            <li>Note on Post-Game Voting:<br>
                <ul><li>Be aware that once the game finishes, the WASD menu will not be shown, and votes via chat may not be allowed.</li></ul>
            </li>
        </ul>
    </li>
    <li>If you play one long round or just limited time and want to have the vote before tha end of that time (defined in mp_timelimit), you set:<br>"VoteDependsOnTimeLimit": true, "ChangeMapAfterTimeLimit": true,  "ChangeMapAfterWinDraw": false, "ChangeMapAfterVote": false, "VoteDependsOnRoundWins": false. Take attention that "TriggerSecondsBeforEnd" should be more that "VotingTime", otherwise the vote will not have time to finish.</li>
</ul>

<h2>Admin Commands</h2>
<ul>
    <li><strong>Map Change:</strong> Use <code>css_maps</code> or <code>!maps</code> to open a menu with options: simply change the map (manually choose or automatic selection); start a vote with automatic or custom selections; start a vote to see if players agree to change the map; set the next map.</li>
    <li><strong>Quick Map Selection:</strong> Use <code>ggmap &lt;partofmapname&gt;</code> to quickly find and switch to a map using a partial name match.</li>
</ul>

<h3>External Controls:</h3>
<ul>
    <li><code>ggmc_mapvote_start [time]</code> - Trigger a map vote externally with an optional time parameter. This command starts the vote with automatic map selection and sets the "nextlevel" cvar to the winning map. This command can only be used with maps in the map list (GGMCmaps.json) from the collection or classic maps. In GunGame, we use it in the gungame.mapvote.cfg file to start the vote after the win or at a specified number of levels before the win.</li>
    <li><code>ggmc_mapvote_with_change [time]</code> - Trigger a map vote externally with an optional time parameter and immediate map change after the vote ends. This command can be used with set workshop map IDs in the map list (GGMCmaps.json).</li>
    <li><code>ggmc_auto_mapchange</code> - Automatically change to a random map.</li>
    <li><code>ggmc_nortv</code> - Temporarily disable the RTV command to maintain game continuity.</li>
</ul>

<h3>Other Commands:</h3>
<ul>
    <li><code>reloadmaps</code> - Command to reload maps file. It reloads every map start, but you can reload it manually if necessary.</li>
    <li><code>setnextmap</code> - Command to set the next map without voting.</li>
</ul>

<h2>Important Notes About Usage</h2>
<ul>
    <li>If the server has an assigned maps collection (<code>+host_workshop_collection [collection number]</code>), the map list in GGMCmaps.json can have map names for the workshop maps from the assigned collection without "mapid".</li>
    <li>If maps are not from the collection, please fill in the "mapid" parameter with the map number in the map list.</li>
    <li>If a map ID is set for the workshop map, the plugin will use it to change the map. If the map ID is not set, the plugin will try to change the map by the name, but if it is not included in the collection assigned to the server - nothing will happen. Workshop maps that are not from the collection cannot be used to set "nextlevel" when using the "ggmc_mapvote_start" command.</li>
    <li>Even if a map ID is set for the workshop map, it is important to have the name the maps exactly the same as it is in the Workshop. Otherwise, you won't be able to use the WorkshopMapProblemCheck feature to check if the requested map is loaded or if it is missing in the workshop, because the map owner can delete the map at any time.</li>
</ul>

<h2>Disclaimer</h2>
<p>The plugin is provided <strong>"as-is"</strong> and fulfills the specific requirements it was designed for. While I am not planning further major updates, I welcome suggestions that might benefit a broader user base, which could lead to additional features.</p>

<h2>Credits</h2>
<p>Thank you to <a href="https://forums.alliedmods.net/showthread.php?t=134190">UMC Mapchooser</a> for the main ideas.</p>
<p>Thanks to:
<ul>
<li>crashzk for the Portuguese translation,</li>
<li>YuYueCraft for Chinese translation.</li></ul></p>

<h2>Donations</h2>
<a href="https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=APGJ8MXWRDX94">
  <img src="https://www.paypalobjects.com/en_GB/i/btn/btn_donate_SM.gif" alt="Donate with PayPal" />
</a>
</body>
</html>