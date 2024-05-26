<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <style>
        body {
            font-family: Arial, sans-serif;
            line-height: 1.6;
        }
        h1, h2, h3 {
            color: #333;
        }
        code {
            background-color: #f4f4f4;
            padding: 2px 4px;
            border-radius: 4px;
        }
        pre {
            background-color: #f4f4f4;
            padding: 10px;
            border-radius: 4px;
            overflow-x: auto;
        }
    </style>
</head>
<body>

<h1>GG1MapChooser</h1>
<p>The <strong>GG1MapChooser</strong> plugin enhances map selection and management for CS2 game servers. It introduces robust controls over map voting, nominations, and automatic map rotation based on player counts and preferences.</p>

<h2>Features</h2>
<ul>
    <li><strong>Random Map Selection</strong> - Automatically selects the next map with configurable weights.</li>
    <li><strong>Map Voting System</strong> - Players can vote on which maps to play next. If no one votes, the plugin chooses a random map from the list to vote.</li>
    <li><strong>Map Nominations</strong> - Players can nominate maps for voting.</li>
    <li><strong>Player Count Thresholds</strong> - Specify minimum and maximum player counts for maps to be included in the vote.</li>
    <li><strong>Command Controls</strong> - Includes commands like rtv (rock the vote) and nominate for direct player interaction.</li>
    <li><strong>Admin commands</strong> - Include commands for admins to start voting or change the map.</li>
</ul>

<h2>Configuration Files</h2>

<h3>Map Configuration</h3>
<p>Define map settings in <code>csgo/cfg/GGMCmaps.json</code>:</p>
<ul>
    <li>Minimum and maximum player counts for map eligibility.</li>
    <li>Map weighting (default is <strong>"1"</strong>).</li>
    <li>Is it Workshop or classic map.</li>
</ul>

<h3>Plugin Settings</h3>
<p>Customize plugin behaviour in <code>csgo/addons/counterstrikesharp/configs/plugins/GG1MapChooser/GG1MapChooser.json</code>:</p>
<ul>
    <li><code>RememberPlayedMaps</code> - Number of recent maps to exclude from upcoming votes.</li>
    <li><code>RTVDelay</code> - Time delay at the start of the map during which rtv is disabled.</li>
    <li><code>RTVInterval</code> - Cooldown period after a failed vote.</li>
    <li><code>VotingTime</code> - Duration for players to cast their votes.</li>
    <li><code>MapsInVote</code> - Number of maps in the voting pool <em>(5 is recommended value)</em>.</li>
    <li><code>VotesToWin</code> - Percentage of votes needed to win the vote.</li>
    <li><code>RandomMapOnStart</code> - Enable changing to a random map on server restart.</li>
    <li><code>LastDisconnectedChangeMap</code> - Switch to a random map after the last player disconnects.</li>
</ul>

<h2>Usage</h2>
<ul>
    <li><strong>Voting:</strong> Players can initiate a map vote using <code>!rtv</code> or <code>rtv</code> in chat. The required percentage of votes to start a vote is controlled by the <code>VotesToWin</code> setting.</li>
    <li><strong>Nominating:</strong> Players can nominate a map by typing <code>!nominate &lt;mapname&gt;</code> or simply <code>nominate</code> to bring up a list of eligible maps based on current server conditions.</li>
</ul>

<h2>Admin Commands</h2>
<ul>
    <li><strong>Map Change</strong> - Use <code>css_maps</code> or <code>!maps</code> to change the map manually or start a vote with standard or custom selections.</li>
    <li><strong>Quick Map Selection</strong> - Use <code>ggmap &lt;partofmapname&gt;</code> helps find and switch to a map quickly by using a partial name match.</li>
</ul>

<h3>External Controls:</h3>
<ul>
    <li><code>ggmc_mapvote_start [time]</code> - Trigger a map vote externally with an optional time parameter.</li>
    <li><code>ggmc_auto_mapchange</code> - Automatically change a map to random map.</li>
    <li><code>ggmc_nortv</code> - Disable the rtv command temporarily to maintain game continuity.</li>
</ul>

<h2>Disclaimer</h2>
<p>The plugin is provided <strong>"as-is"</strong> and fulfills the specific requirements it was designed for. While I am not planning further major updates, I welcome suggestions that might benefit a broader user base, which could lead to additional features.</p>

<h2>Credits</h2>
<p>Thank you <a href="https://forums.alliedmods.net/showthread.php?t=134190">UMC Mapchooser</a> for the main ideas.</p>

<h2>Donations</h2>
<a href="https://www.paypal.com/cgi-bin/webscr?cmd=_s-xclick&hosted_button_id=APGJ8MXWRDX94">
  <img src="https://www.paypalobjects.com/en_GB/i/btn/btn_donate_SM.gif" alt="Donate with PayPal" />
</a>
</body>
</html>
