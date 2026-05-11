<!DOCTYPE html>
<html lang="en">
<head>
</head>
<body>

<h1>GG1MapChooser</h1>
<p><strong>GG1MapChooser</strong> is a CounterStrikeSharp map chooser for CS2 servers. It manages map votes, RTV, nominations, map pools, low-player map lists, admin map changes, automatic map rotation, next-map handling, and Discord reporting.</p>

<h2>Features</h2>
<ul>
    <li><strong>Map Voting System</strong> - Players can vote for the next map. If no valid vote is cast, the plugin chooses a random valid map.</li>
    <li><strong>Rock The Vote (RTV)</strong> - Players can request an immediate map vote during the game.</li>
    <li><strong>Immediate RTV Option</strong> - If <code>RTVDelayFromStart</code> is set to <code>0</code> or below, RTV is available immediately after the match starts.</li>
    <li><strong>Map Nominations</strong> - Players can nominate maps for upcoming votes. Each player can have one active nomination.</li>
    <li><strong>Nomination Cooldown</strong> - Recently nominated maps can be temporarily blocked from being nominated again.</li>
    <li><strong>Map Pools</strong> - Maps can be grouped into named pools. The active pool is used by RTV, map votes, admin votes, and automatic map changes.</li>
    <li><strong>Pool Voting</strong> - Players can vote to start a pool vote and then choose the active pool.</li>
    <li><strong>Low-Player Pool</strong> - A separate low-player map list can be used automatically when the server population is below the configured threshold.</li>
    <li><strong>Menu Modes</strong> - Map votes, nominations, and pool votes can use <code>wasd</code>, <code>chat</code>, or <code>both</code> menu modes.</li>
    <li><strong>Panorama Yes/No Vote</strong> - The admin "Vote to Change Map or Not" flow can use the native CS2 Panorama vote UI.</li>
    <li><strong>Player Count Thresholds</strong> - Maps can define minimum and maximum player counts.</li>
    <li><strong>Map Display Names</strong> - Maps can have friendly names shown in menus and messages.</li>
    <li><strong>Map Weights</strong> - Random map selection can be weighted per map.</li>
    <li><strong>Extend Map Limit</strong> - The number of map extensions per map can be limited.</li>
    <li><strong>No Vote Line</strong> - Optional "No Vote" line can be added to vote menus to reduce accidental map selections.</li>
    <li><strong>WASD Vote Counters</strong> - WASD vote menus can show live vote counts next to options.</li>
    <li><strong>Admin Controls</strong> - Admins can change maps, start votes, start yes/no votes, set the next map, switch pools, and start pool votes.</li>
    <li><strong>External Controls</strong> - Server commands allow other plugins or configs to start map votes and trigger selected map changes.</li>
    <li><strong>Discord Logging</strong> - Loaded and voted maps can be reported to Discord with a configurable message template.</li>
    <li><strong>Timer Reliability Improvements</strong> - Vote and map-change timer handling has been improved to avoid blocked or mistimed votes.</li>
</ul>

<h2>Configuration Files</h2>

<h3>Map Configuration</h3>
<p>Define maps in <code>csgo/cfg/GGMCmaps.json</code>. Each map entry supports:</p>
<ul>
    <li><code>ws</code> - <code>true</code> for Workshop maps, <code>false</code> for classic maps.</li>
    <li><code>display</code> - Friendly display name shown in menus and chat.</li>
    <li><code>mapid</code> - Workshop map ID. Required for Workshop maps outside the server collection.</li>
    <li><code>minplayers</code> - Minimum player count required for the map. <code>0</code> disables the lower limit.</li>
    <li><code>maxplayers</code> - Maximum player count allowed for the map. <code>0</code> disables the upper limit.</li>
    <li><code>weight</code> - Random selection weight. Higher values increase the chance of being selected. <code>0</code> disables random selection. <code>-1</code> uses <code>OtherSettings.DefaultMapWeight</code>.</li>
</ul>

<h4>Legacy Map List Format</h4>
<p>The legacy flat format is still supported. When this format is used, the plugin treats the whole map list as one pool named <code>default</code>.</p>
<pre><code>{
  "de_mirage": {
    "ws": false,
    "display": "Mirage",
    "mapid": "",
    "minplayers": 0,
    "maxplayers": 0,
    "weight": 1
  }
}
</code></pre>

<h4>Pool-Based Map List Format</h4>
<p>Maps can now be grouped into named pools. The active pool is used by normal map votes, RTV, admin map votes, and automatic random map changes.</p>
<pre><code>{
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
</code></pre>

<p>When a pool-based map list is used, the active pool is selected in this order:</p>
<ol>
    <li>The previous active pool, if it still exists.</li>
    <li><code>PoolVoteSettings.DefaultPool</code>, if configured and found.</li>
    <li>The first available pool in <code>GGMCmaps.json</code>.</li>
</ol>

<p>When an admin switches the active pool manually, the plugin clears pending nominations, admin-selected maps, pending next map state, and temporary vote state.</p>

<h3>Plugin Settings</h3>
<p>Customise plugin behaviour in <code>csgo/addons/counterstrikesharp/configs/plugins/GG1MapChooser/GG1MapChooser.json</code>.</p>
<p>The config is divided into these sections:</p>
<ul>
    <li><code>VoteSettings</code> - map vote, nomination, menu, no-vote, extend, and Panorama yes/no vote settings.</li>
    <li><code>RTVSettings</code> - Rock The Vote settings.</li>
    <li><code>PoolVoteSettings</code> - map pool voting settings.</li>
    <li><code>WinDrawSettings</code> - round/win based vote timing.</li>
    <li><code>TimeLimitSettings</code> - time-limit based vote timing.</li>
    <li><code>DiscordSettings</code> - Discord webhook reporting.</li>
    <li><code>MenuSettings</code> - WASD menu behaviour.</li>
    <li><code>OtherSettings</code> - additional behaviour such as sounds, map-change delay, low-player maps, and default map weight.</li>
</ul>

<h4>VoteSettings</h4>
<ul>
    <li><code>RememberPlayedMaps</code> - Number of recently played maps excluded from upcoming votes.</li>
    <li><code>RememberNominatedMaps</code> - Number of recently nominated maps kept in nomination cooldown. <code>0</code> disables cooldown.</li>
    <li><code>MapsInVote</code> - Number of maps shown in a vote.</li>
    <li><code>VotesToWin</code> - Vote ratio required to win or start threshold-based actions. Example: <code>0.6</code> means 60%.</li>
    <li><code>AllowNominate</code> - Allows players to nominate maps.</li>
    <li><code>NominationsMenuMode</code> - Nomination menu mode: <code>wasd</code>, <code>chat</code>, or <code>both</code>. Empty values fall back to <code>wasd</code>.</li>
    <li><code>EndMapVoteMenuMode</code> - End-of-map vote menu mode: <code>wasd</code>, <code>chat</code>, or <code>both</code>. Empty values fall back to <code>wasd</code>.</li>
    <li><code>YesNoVotePanorama</code> - Enables native Panorama yes/no voting for the admin "Vote to Change Map or Not" flow.</li>
    <li><code>PanoramaSFUIString</code> - SFUI string used by the Panorama yes/no vote.</li>
    <li><code>VotingTime</code> - Duration in seconds for map votes.</li>
    <li><code>ExtendMapInVote</code> - Adds the "Extend Map" option to map votes.</li>
    <li><code>ExtendMapTimeMinutes</code> - Number of minutes added when "Extend Map" wins.</li>
    <li><code>MaxExtendMapCount</code> - Maximum number of times the current map can be extended. <code>0</code> means unlimited.</li>
    <li><code>ChangeMapAfterVote</code> - Changes the map after vote completion when the vote is not already configured as an immediate change.</li>
    <li><code>SpectatorsCanVote</code> - Allows or blocks spectator voting.</li>
    <li><code>IncludeNoVote</code> - Adds a "No Vote" option to vote menus.</li>
</ul>

<h4>RTVSettings</h4>
<ul>
    <li><code>AllowRTV</code> - Enables Rock The Vote.</li>
    <li><code>RTVDelayFromStart</code> - Initial delay in seconds before RTV is allowed. If this is <code>0</code> or below, RTV is available immediately after the match starts.</li>
    <li><code>IntervalBetweenRTV</code> - Cooldown in seconds after an RTV starts before another RTV can start.</li>
    <li><code>NoRTVafterRoundsPlayed</code> - Blocks RTV after this many rounds have been played. <code>0</code> disables this restriction.</li>
</ul>

<h4>PoolVoteSettings</h4>
<ul>
    <li><code>Enable</code> - Enables player/admin pool voting.</li>
    <li><code>DefaultPool</code> - Preferred active pool name when the mapcycle is loaded. If empty or not found, the first available pool is used.</li>
    <li><code>DelayFromStart</code> - Initial delay in seconds before pool voting is available.</li>
    <li><code>IntervalBetweenVotes</code> - Cooldown in seconds after a pool vote starts before another pool vote can start.</li>
    <li><code>VotesToWin</code> - Required player vote ratio to start a pool vote. If <code>0</code>, <code>VoteSettings.VotesToWin</code> is used.</li>
    <li><code>VotingTime</code> - Time in seconds for players to choose a pool after the pool vote menu opens.</li>
    <li><code>BlockBeforeMapVoteSeconds</code> - In time-limit mode, blocks pool voting when the scheduled map vote is this many seconds away or closer.</li>
    <li><code>BlockBeforeMapVoteRounds</code> - In round-wins mode, blocks pool voting when the scheduled map vote is this many rounds away or closer.</li>
    <li><code>MenuMode</code> - Pool vote menu mode: <code>wasd</code>, <code>chat</code>, or <code>both</code>. Empty values fall back to <code>wasd</code>.</li>
</ul>

<h4>WinDrawSettings</h4>
<ul>
    <li><code>VoteDependsOnRoundWins</code> - Enables round/win based vote timing.</li>
    <li><code>TriggerRoundsBeforeEnd</code> - Number of rounds before the end of the match to start the vote.</li>
    <li><code>TriggerRoundsBeforeEndVoteAtRoundStart</code> - Starts the scheduled vote on round start when true, or round end when false.</li>
    <li><code>TriggerVoteAtRoundStartSecondsFromStart</code> - Delay in seconds from round start before starting the vote.</li>
    <li><code>ChangeMapAfterWinDraw</code> - Changes the map after the win/draw event if a next map has been selected.</li>
</ul>

<h4>TimeLimitSettings</h4>
<ul>
    <li><code>VoteDependsOnTimeLimit</code> - Enables time-limit based vote timing using <code>mp_timelimit</code>.</li>
    <li><code>TriggerSecondsBeforeEnd</code> - Number of seconds before the time limit ends to start the vote.</li>
    <li><code>ChangeMapAfterTimeLimit</code> - Changes to the selected map when the time limit expires.</li>
    <li><code>VoteNextRoundStartAfterTrigger</code> - Starts the vote at the next round start after the time-limit trigger.</li>
    <li><code>VoteRoundEndAfterTrigger</code> - Starts the vote at round end after the time-limit trigger.</li>
    <li><code>MinutesExtendTimeLimitToRoundEnd</code> - Temporarily extends the time limit so the delayed round-start or round-end vote can happen.</li>
</ul>

<h4>DiscordSettings</h4>
<ul>
    <li><code>DiscordWebhook</code> - Discord webhook URL used for map reports.</li>
    <li><code>DiscordMessageMapStart</code> - Reports map starts to Discord.</li>
    <li><code>DiscordMessageAfterVote</code> - Reports map vote results to Discord.</li>
    <li><code>PictureExtension</code> - File extension used when building map image URLs for Discord embeds.</li>
</ul>

<h4>MenuSettings</h4>
<ul>
    <li><code>DisplayVotesCount</code> - Shows vote counters next to options in WASD vote menus.</li>
    <li><code>SoundInMenu</code> - Enables menu navigation sounds.</li>
    <li><code>FreezePlayerInMenu</code> - Freezes regular players while they use WASD menus.</li>
    <li><code>FreezeAdminInMenu</code> - Freezes admins while they use WASD admin menus.</li>
    <li><code>FreezeMode</code> - Freeze method used by the menu.</li>
    <li><code>ScrollUp</code>, <code>ScrollDown</code>, <code>Choose</code>, <code>Back</code>, <code>Exit</code> - Button bindings for WASD menus.</li>
</ul>

<h4>OtherSettings</h4>
<ul>
    <li><code>PrintPlayersChoiceInChat</code> - Prints player choices to everyone instead of only to the player.</li>
    <li><code>PrintNextMapForAll</code> - Prints <code>nextmap</code> output to all players.</li>
    <li><code>DelayBeforeChangeSeconds</code> - Delay before changing the map after configured vote/end events.</li>
    <li><code>VoteStartSound</code> - Sound played when a map vote starts.</li>
    <li><code>RandomMapOnStart</code> - Changes to a random map after server start.</li>
    <li><code>RandomMapOnStartDelay</code> - Delay before random map change after server start.</li>
    <li><code>LastDisconnectedChangeMap</code> - Changes to a random map after the last player disconnects.</li>
    <li><code>WorkshopMapProblemCheck</code> - Detects workshop map load failures and retries/falls back to random map handling.</li>
    <li><code>TvStopRecord</code> - Stops TV recording before map changes.</li>
    <li><code>DefaultMapWeight</code> - Default random selection weight for maps with <code>weight = -1</code>.</li>
    <li><code>LowPlayerMaxPlayers</code> - Maximum real player count, including spectators, for low-player mode.</li>
    <li><code>LowPlayerMaps</code> - Map list used while low-player mode is active. Entries use the same format as normal map entries.</li>
</ul>

<h2>How New Features Work</h2>

<h3>Pool Voting</h3>
<p>Players can request a pool vote with <code>rtm</code>, <code>!rtm</code>, <code>/rtm</code>, <code>css_rtm</code>, or <code>!css_rtm</code>. Admins can also start a pool vote from <code>css_maps</code>.</p>
<p>Pool voting has two stages:</p>
<ol>
    <li>Players first vote to start a pool vote, similar to RTV.</li>
    <li>When the threshold is reached, the plugin opens a menu where players choose the pool.</li>
</ol>
<p>A pool wins only if it is the single option with the highest vote count. If no one votes, the result is tied, or the current pool wins, the active pool stays unchanged. If a different pool wins, it becomes the active pool and pool-sensitive state is cleared.</p>
<p>If a map vote is due while a pool vote is in progress, the map vote is queued and starts after the pool vote finishes.</p>
<p>Pool voting requires at least two pools and exactly one automatic map vote mode: either time-limit based voting or round/win based voting. If both are enabled or both are disabled, pool voting is disabled to avoid ambiguous timing.</p>

<h3>Low-Player Pool</h3>
<p>Low-player maps are configured in <code>OtherSettings.LowPlayerMaps</code>. This list is separate from normal map pools and does not appear in pool votes.</p>
<p>Low-player mode is active when the number of real players, including spectators, is less than or equal to <code>OtherSettings.LowPlayerMaxPlayers</code>.</p>
<p>When active, normal map votes, RTV, and automatic random map changes use <code>LowPlayerMaps</code>. When the player count rises above the threshold, the plugin returns to the normal active pool.</p>

<h3>Menu Modes</h3>
<p><code>wasd</code> opens only the WASD HTML menu. <code>chat</code> or <code>chatmenu</code> opens only the CounterStrikeSharp chat menu. <code>both</code> opens both menu types at the same time; selecting in one closes the other. Empty or unknown values fall back to <code>wasd</code>.</p>
<p>The admin "Vote to Change Map or Not" flow uses Panorama voting when <code>VoteSettings.YesNoVotePanorama</code> is true. In that case, <code>EndMapVoteMenuMode</code> does not apply to that yes/no vote.</p>

<h3>Nomination Cooldown</h3>
<p>When <code>VoteSettings.RememberNominatedMaps</code> is greater than <code>0</code>, nominated maps from completed votes are added to a recent nomination cooldown list. Maps in this list cannot be nominated again and are shown as disabled entries in nomination menus.</p>
<p>This is a map-count based cooldown, not a time-based cooldown.</p>

<h3>Maximum Map Extend Count</h3>
<p><code>VoteSettings.MaxExtendMapCount</code> limits how many times the current map can be extended through the <code>Extend Map</code> vote option. <code>0</code> means unlimited. The counter resets with the map state.</p>

<h3>Immediate RTV</h3>
<p>If <code>RTVSettings.AllowRTV</code> is true and <code>RTVSettings.RTVDelayFromStart</code> is <code>0</code> or below, RTV is available immediately after the match starts. After an RTV starts, <code>RTVSettings.IntervalBetweenRTV</code> is still used as the cooldown before another RTV can start.</p>

<h2>Configuration Example</h2>
<pre><code>{
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
</code></pre>

<h3>Discord Message Configuration</h3>
<p>Define the text you want to display in <code>csgo/addons/counterstrikesharp/configs/plugins/GG1MapChooser/NextMapMessage.json</code>:</p>
<ul>
    <li>The config file is created automatically if it does not exist.</li>
    <li>You can modify or localise the text, for example: <code>content = "Next map: "</code>.</li>
    <li>To display map pictures, set the image folder URL in the message template.</li>
    <li>Map images must follow the naming convention <code>&lt;mapname&gt;.&lt;PictureExtension&gt;</code>.</li>
</ul>

<h2>Usage</h2>
<ul>
    <li><strong>RTV:</strong> Players can use <code>rtv</code>, <code>!rtv</code>, or <code>/rtv</code>.</li>
    <li><strong>Pool Voting:</strong> Players can use <code>rtm</code>, <code>!rtm</code>, <code>/rtm</code>, <code>css_rtm</code>, or <code>!css_rtm</code>.</li>
    <li><strong>Nominating:</strong> Players can use <code>nominate</code>, <code>!nominate &lt;mapname&gt;</code>, or the <code>yd</code> alias.</li>
    <li><strong>Revote:</strong> Players can use <code>revote</code> to reopen the active vote menu and change their vote.</li>
    <li><strong>Next Map:</strong> Players can use <code>nextmap</code> to see the selected next map.</li>
    <li><strong>Time Left:</strong> Players can use <code>timeleft</code> to see time or round information, depending on the active vote mode.</li>
</ul>

<h2>Setup Examples</h2>
<ul>
    <li>If another plugin ends the game and only needs GG1MapChooser to select the next map, use <code>ggmc_mapvote_start</code>.</li>
    <li>If another plugin needs GG1MapChooser to select and immediately change to the winning map, use <code>ggmc_mapvote_with_change</code>.</li>
    <li>For round/win based voting, enable <code>WinDrawSettings.VoteDependsOnRoundWins</code>, configure <code>TriggerRoundsBeforeEnd</code>, and decide whether the vote should start on round start or round end.</li>
    <li>For time-limit based voting, enable <code>TimeLimitSettings.VoteDependsOnTimeLimit</code> and make sure <code>TriggerSecondsBeforeEnd</code> leaves enough time for <code>VoteSettings.VotingTime</code>.</li>
    <li>For pool voting, use a pool-based <code>GGMCmaps.json</code> with at least two pools and enable <code>PoolVoteSettings.Enable</code>.</li>
    <li>For low-player maps, fill <code>OtherSettings.LowPlayerMaps</code> and set <code>OtherSettings.LowPlayerMaxPlayers</code> to the desired threshold.</li>
</ul>

<h2>Plugin Commands</h2>
<h3>Player Commands</h3>
<ul>
    <li><code>rtv</code> - Rock The Vote.</li>
    <li><code>rtm</code> / <code>css_rtm</code> - Request a map pool vote.</li>
    <li><code>nominate</code> - Open nomination menu.</li>
    <li><code>nominate &lt;mapname&gt;</code> - Nominate a specific map.</li>
    <li><code>yd</code> - Nomination alias.</li>
    <li><code>revote</code> - Reopen the active vote menu.</li>
    <li><code>nextmap</code> - Show the selected next map.</li>
    <li><code>timeleft</code> - Show remaining time or round information.</li>
</ul>

<h3>Admin Commands</h3>
<ul>
    <li><code>css_maps</code> / <code>!maps</code> - Opens the admin map menu.</li>
    <li><code>ggmap &lt;partofmapname&gt;</code> - Quickly find and change to a map by partial name.</li>
    <li><code>ggmap &lt;exactmapname&gt;</code> - Server-side exact map change command for scripts/plugins.</li>
    <li><code>setnextmap</code> - Set the next map without starting a vote.</li>
</ul>

<h3>External Controls</h3>
<ul>
    <li><code>ggmc_mapvote_start [time]</code> - Starts a map vote externally and sets <code>nextlevel</code> to the winning map.</li>
    <li><code>ggmc_mapvote_with_change [time]</code> - Starts a map vote externally and changes map after the vote ends.</li>
    <li><code>ggmc_auto_mapchange</code> - Changes to a random valid map.</li>
    <li><code>ggmc_nortv</code> - Temporarily disables RTV.</li>
    <li><code>ggmc_change_nextmap</code> - Immediately changes to the previously selected next map.</li>
    <li><code>reloadmaps</code> - Reloads <code>GGMCmaps.json</code>.</li>
</ul>

<h2>Suggested Server Testing</h2>
<ul>
    <li>Start with a pool-based <code>GGMCmaps.json</code> containing at least two pools.</li>
    <li>Set <code>PoolVoteSettings.Enable = true</code>.</li>
    <li>Use <code>rtm</code>, <code>!rtm</code>, or <code>css_rtm</code> and confirm that a pool vote starts after the configured vote threshold.</li>
    <li>Confirm that ties and votes for the current pool keep the existing active pool.</li>
    <li>Switch pools from <code>css_maps</code> and confirm that nominations and pending selected maps are cleared.</li>
    <li>Set <code>LowPlayerMaxPlayers</code> above the current online count and confirm RTV/map vote uses <code>LowPlayerMaps</code>.</li>
    <li>Test <code>NominationsMenuMode</code>, <code>EndMapVoteMenuMode</code>, and <code>PoolVoteSettings.MenuMode</code> with <code>wasd</code>, <code>chat</code>, and <code>both</code>.</li>
    <li>Set <code>RememberNominatedMaps</code> above <code>0</code>, nominate a map, complete a vote, and confirm the map cannot be nominated again until it leaves cooldown.</li>
    <li>Set <code>MaxExtendMapCount = 1</code>, enable <code>ExtendMapInVote</code>, extend once, and confirm the extend option is not available again on the same map.</li>
    <li>Set <code>RTVDelayFromStart = 0</code> and confirm RTV is available immediately after the match starts.</li>
</ul>

<h2>Important Notes About Usage</h2>
<ul>
    <li>If the server has an assigned Workshop collection (<code>+host_workshop_collection [collection number]</code>), map names from that collection can be used without <code>mapid</code>.</li>
    <li>If Workshop maps are not from the assigned collection, fill in <code>mapid</code>.</li>
    <li>If <code>mapid</code> is missing for a Workshop map outside the collection, the server might not be able to change to it by name.</li>
    <li>Workshop map names should match the actual Workshop names exactly, especially when <code>WorkshopMapProblemCheck</code> is enabled.</li>
    <li>Pool voting requires at least two pools.</li>
    <li>Pool voting must happen before the next map vote. It can be blocked by <code>BlockBeforeMapVoteSeconds</code> or <code>BlockBeforeMapVoteRounds</code>.</li>
    <li>If both <code>VoteDependsOnTimeLimit</code> and <code>VoteDependsOnRoundWins</code> are enabled, or both are disabled, pool voting is disabled.</li>
    <li>Low-player maps are separate from normal pools and do not appear in pool votes.</li>
</ul>

<h2>Plugin Compilation</h2>
<p>To compile the plugin, download or reference the API project from <a href="https://github.com/ssypchenko/ggmcAPI">ggmcAPI</a>.</p>

<h2>Plugin APIs</h2>
<p>The plugin exposes these capabilities to other plugins.</p>
<ul>
    <li>Maps API:
        <ul>
            <li><code>public bool GGMC_IsVoteInProgress();</code> - returns whether a map vote is currently in progress.</li>
        </ul>
    </li>
    <li>WASD Menu API:
        <ul>
            <li><code>public IWasdMenu CreateMenu(string title = "", bool freezePlayer = true, bool displayOptionsCount = false);</code> - creates a WASD menu.</li>
            <li><code>public void OpenMainMenu(CCSPlayerController? player, IWasdMenu? menu);</code> - opens a menu as the main menu.</li>
            <li><code>public void OpenSubMenu(CCSPlayerController? player, IWasdMenu? menu);</code> - opens a menu as a submenu.</li>
            <li><code>public void CloseActiveMenu(IWasdMenu? menu);</code> - closes an active menu object.</li>
            <li><code>public void CloseActiveMenu(CCSPlayerController? player);</code> - closes the player's active menu.</li>
            <li><code>public BaseMenu MenuByType(string menuType, string title, BasePlugin plugin);</code> - creates an adapter menu by type name.</li>
            <li><code>public BaseMenu MenuByType(Type menuType, string title, BasePlugin plugin);</code> - creates an adapter menu by type.</li>
        </ul>
    </li>
</ul>

<h2>Disclaimer</h2>
<p>The plugin is provided <strong>"as-is"</strong> and fulfils the specific requirements it was designed for. Suggestions that benefit a broader user base are welcome.</p>

<h2>Credits</h2>
<p>Thank you to <a href="https://forums.alliedmods.net/showthread.php?t=134190">UMC Mapchooser</a> for the main ideas.</p>
<p>Special thanks to <a href="https://github.com/T3Marius/T3Menu-API">T3Marius</a> for WASD menu ideas and design.</p>
<p>Thanks to:</p>
<ul>
    <li>crashzk for the Portuguese translation.</li>
    <li>YuYueCraft for the Chinese translation.</li>
</ul>

<h2>Donations</h2>
<a href="https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=APGJ8MXWRDX94">
  <img src="https://www.paypalobjects.com/en_GB/i/btn/btn_donate_SM.gif" alt="Donate with PayPal" />
</a>
</body>
</html>
