using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Text;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Listeners;
using MapChooserAPI;

namespace MapChooser;

public class MapChooser : BasePlugin, IPluginConfig<MCConfig>
{
    public override string ModuleName => "GG1_MapChooser";
    public override string ModuleVersion => "v1.5.0";
    public override string ModuleAuthor => "Sergey";
    public override string ModuleDescription => "Map chooser, voting, rtv, nominate, etc.";
    public MCCoreAPI MCCoreAPI { get; set; } = null!;
    public static PluginCapability<MCIAPI> MCAPICapability { get; } = new("ggmc:api");
    public static PluginCapability<IWasdMenuManager> WasdMenuManagerCapability = new ("ggmc:wasdmanager");
    public readonly IStringLocalizer<MapChooser> _localizer;
    public MaxRoundsManager roundsManager;
    public TimerManager timeManager;
    public WebhookService webhookService;
    public string WebhookNextMapMessagePath = "";
    public MapChooser (IStringLocalizer<MapChooser> localizer)
    {
        _localizer = localizer;
        roundsManager = new(this);
        timeManager = new(this);
        webhookService = new(this);
        wASDMenu = new(this);
    }
    public MCConfig Config { get; set; } = new();
    public void OnConfigParsed (MCConfig config)
    { 
        Config = config;
        if (Config.VoteSettings.MapsInVote < 1)
        {
            Config.VoteSettings.MapsInVote = 5;
            Logger.LogInformation("Set MapsInVote to 5 on plugin load because of an error in config.");
        }
        if (Config.WinDrawSettings.ChangeMapAfterWinDraw && Config.VoteSettings.ChangeMapAfterVote)
        {
            Logger.LogWarning("ChangeMapAfterWinDraw may not work because ChangeMapAfterVote set true");
        }
        roundsManager.InitialiseMap();
//        roundsManager.CheckConfig();
        if (Config.TimeLimitSettings.VoteDependsOnTimeLimit)
        {
            var TimeLimit = ConVar.Find("mp_timelimit");
            var TimeLimitValue = TimeLimit?.GetPrimitiveValue<float>() ?? 0;
            if (TimeLimitValue < 1)
            {
                Logger.LogError($"VoteDependsOnTimeLimit set true, but cvar mp_timelimit set less than 1. Plugin can't work correctly with these settings.");
            }
            if (Config.TimeLimitSettings.TriggerSecondsBeforeEnd < Config.VoteSettings.VotingTime)
            {
                Config.TimeLimitSettings.TriggerSecondsBeforeEnd = Config.VoteSettings.VotingTime + 1;
                Logger.LogInformation($"VoteDependsOnTimeLimit: TriggerSecondsBeforeEnd updates to {Config.VoteSettings.VotingTime + 1} which is minimum value for VotingTime {Config.VoteSettings.VotingTime} in config.");
            }
            if (Config.TimeLimitSettings.TriggerSecondsBeforeEnd < (TimeLimitValue * 60))
            {
                Logger.LogError($"VoteDependsOnTimeLimit set true, but TriggerSecondsBeforEnd is more than cvar mp_timelimit value. Plugin can't work correctly with these settings.");
            }
        }
    }
    public bool mapChangedOnStart = false; 
    private Random random = new();
    private string mapsFilePath = "";
    private Dictionary<string, MapInfo>Maps_from_List = new Dictionary<string, MapInfo>();
    private Dictionary<string, string> DisplayNameToKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private List<string> maplist = new();
    private List<string> nominatedMaps = new();
    private List<string> mapsToVote = new List<string>();
    public static Dictionary<int, string> votePlayers = new Dictionary<int, string>();
    private int mapstotal = 0;
    private bool canVote { get; set; } = false;
    private bool canRtv { get; set; } = false;
    private int rtv_can_start { get; set; } = 0;
    private int rtv_need_more { get; set; }
    private Timer? rtvTimer = null;
    private int rtvRestartProblems = 0;
    private bool _runVoteRoundEnd = false;
    private Timer? changeRequested = null;
    private Timer? voteEndChange = null;
    private Timer? voteEndTimer = null;
    private int RestartProblems = 0;
    private bool Restart = true;
    public bool IsVoteInProgress { get; set; } = false;
    private Timer? _timeLimitTimer = null;
    private Timer? _timeLimitMapChangeTimer = null;
    private bool _timeLimitVoteRoundStart = false;
    public ChatMenu? GlobalChatMenu { get; set; } = null;
    public IWasdMenu? GlobalWASDMenu { get; set; } = null;
    public WASDMenu wASDMenu{ get; set; }
    private Dictionary<string, int> optionCounts = new Dictionary<string, int>();
    private Timer? voteTimer = null;
//    public string EmergencyMap { get; set; } = "";
    private Player[] players = new Player[65];
    int timeToVote = 20;
    int VotesCounter = 0;
    private int _votedMap;
    private string NoMapcycle = "[GGMC] Could not load mapcycle! Change Map will not work";
    private string? _selectedMap;
    private string? _roundEndMap;
    private List<string> _playedMaps = new List<string>();
    public string MapToChange = ""; 
    public bool MapIsChanging = false;
    private readonly object _timerLock = new object();
    public override void Load(bool hotReload) 
    {
        Logger.LogInformation($"{ModuleVersion}");
        MCCoreAPI = new MCCoreAPI(this);
        if (MCCoreAPI != null)
        {
            Capabilities.RegisterPluginCapability(MCAPICapability, () => MCCoreAPI);
            Logger.LogInformation("GGMC API registered");
        }
        else
        {
            Logger.LogWarning("GGMC API not registered");
        }
        var wasdMenuManager = new WasdManager();
        if (wasdMenuManager != null)
        {
            Capabilities.RegisterPluginCapability(WasdMenuManagerCapability, () => wasdMenuManager);
            Logger.LogInformation("GGMC WASD API registered");
        }
        else
        {
            Logger.LogWarning("GGMC WASD API not registered");
        }

        WebhookNextMapMessagePath = Server.GameDirectory + "/csgo/addons/counterstrikesharp/configs/plugins/GG1MapChooser/NextMapMessage.json";
        EnsureNextMapMessageFileExists(WebhookNextMapMessagePath);

        mapsFilePath = Server.GameDirectory + "/csgo/cfg/GGMCmaps.json";

        if (!File.Exists(mapsFilePath))
        {
            Console.WriteLine("[GGMC] GGMCmaps.json not found!");
            Logger.LogError("[GGMC] GGMCmaps.json not found!");
            return;         
        }
        canVote = ReloadMapcycle();
        if (!canVote)
        {
            Console.WriteLine(NoMapcycle);
            Logger.LogError(NoMapcycle);
            return;
        }
        RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
        RegisterEventHandler<EventRoundEnd>(EventRoundEndHandler);
        RegisterEventHandler<EventRoundAnnounceLastRoundHalf>(EventRoundAnnounceLastRoundHalfHandler);
        RegisterEventHandler<EventRoundAnnounceMatchStart>(EventRoundAnnounceMatchStartHandler);
        RegisterEventHandler<EventRoundAnnounceWarmup>(EventRoundAnnounceWarmupHandler);
        RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
        
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RegisterListener<Listeners.OnClientDisconnectPost>(OnClientDisconnect);

        wASDMenu.Load(hotReload);

        if (hotReload)
        {
            mapChangedOnStart = true;
            var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var pl in playerEntities)
                {
                    if (players[pl.Slot] != null)
                        players[pl.Slot] = null!;
                    players[pl.Slot] = new Player
                    {
                        putInServer = true
                    };
                }
            }
        }
/*        AddTimer(2.0f, () => {
            GameRules = GetGameRules();
        }, TimerFlags.STOP_ON_MAPCHANGE); */
    }
    public override void Unload(bool hotReload)
    {
        RemoveListener<Listeners.OnClientDisconnectPost>(OnClientDisconnect);
        RemoveListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RemoveListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RemoveListener<Listeners.OnMapStart>(OnMapStart);
        RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
        DeregisterEventHandler<EventRoundStart>(EventRoundStartHandler);
        DeregisterEventHandler<EventRoundEnd>(EventRoundEndHandler);
        DeregisterEventHandler<EventRoundAnnounceLastRoundHalf>(EventRoundAnnounceLastRoundHalfHandler);
        DeregisterEventHandler<EventRoundAnnounceMatchStart>(EventRoundAnnounceMatchStartHandler);
        DeregisterEventHandler<EventRoundAnnounceWarmup>(EventRoundAnnounceWarmupHandler);
        DeregisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);

        wASDMenu.Unload(hotReload);
    }
    private void OnClientDisconnect(int slot)
    {
        if (canVote)
        {
            if (players[slot] != null)
            {
                if (players[slot].putInServer)
                {
                    var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
                    if (playerEntities != null && playerEntities.Any())
                    {
                        if (players[slot].HasProposedMaps())
                        {
                            nominatedMaps.Remove(players[slot].ProposedMaps);
                        }
                        if (Config.RTVSettings.AllowRTV && IsRTVThreshold(false))  // если достаточно голосов - запускаем голосование
                        {
                            Logger.LogInformation($"Start rtv because of disconnected player and rtv threshold");
                            StartRTV();
                        }
                    }
                    else
                    {   
                        ResetData("On last client disconnect");

                        if (Config.OtherSettings.LastDisconnectedChangeMap && changeRequested == null && !Restart)
                        {
                            Logger.LogInformation($"Requested map change on last disconnected player");
                            changeRequested = AddTimer(60.0f, Timer_ChangeMapOnEmpty, TimerFlags.STOP_ON_MAPCHANGE);
                        }
                    }
                }
                players[slot] = null!;
            }
        }
    }
    private void OnClientPutInServer(int slot)
    {
        var p = Utilities.GetPlayerFromSlot(slot);
        if (p != null && p.IsValid && slot < 65 && !p.IsBot && !p.IsHLTV)
        {
            if (players[slot] == null)
            {
                players[slot] = new Player ();
            }
            players[slot].putInServer = true;
        }
    }
    private void OnClientAuthorized(int slot, SteamID id)
    {
        var p = Utilities.GetPlayerFromSlot(slot);
        if (p != null && p.IsValid && slot < 65 && !p.IsBot && !p.IsHLTV)
        {
            if (players[slot] == null)
            {
                players[slot] = new Player ();
            }
            mapChangedOnStart = true;
            if (changeRequested != null)
            {
                timeManager.EnqueueOperation(async () => 
                {
                    bool success = await KillTimer(changeRequested);
                    if (success)
                    {
                        changeRequested = null;
                    }
                });
            }
        }
    }
    private void OnMapStart(string name)
    {
        Logger.LogInformation(name + " loaded");
        if (Config.DiscordSettings.DiscordWebhook != "" && Config.DiscordSettings.DiscordMessageMapStart)
            _ = webhookService.SendWebhookMessage(name, GetDisplayName(name));
        ResetData("On Map Start");
//        KillAllTimers();
//        OnMapEnd(); //kill all timers just in case
        canVote = ReloadMapcycle();
        if (canVote)
        {
            if (Config.OtherSettings.WorkshopMapProblemCheck && MapToChange != "" && !string.Equals(MapToChange, name, StringComparison.OrdinalIgnoreCase)) // case when the server loaded the map different from requested in case the collection is broken, so we need to restart the server to fix the collection
            {
                if (++RestartProblems < 4 && canVote)
                {
                    Logger.LogError($"The problem with changed map. Instead of {MapToChange} loaded {name}. Try to change again");
                    GGMCDoAutoMapChange();
                    return;
                }
                else
                {
                    Logger.LogError($"The problem with changed map more then 3 times. Instead of {MapToChange} loaded {name}. Restart server");
                    Server.ExecuteCommand("sv_cheats 1; restart");
                    return;
                }
            }
            _selectedMap = null;
            _roundEndMap = null;
            MapToChange = "";
            RestartProblems = 0;
            
            if (Config.VoteSettings.RememberPlayedMaps > 0)
            {
                if (_playedMaps.Count > 0)
                {   
                    if ( _playedMaps.Count >= Config.VoteSettings.RememberPlayedMaps)
                    {
                        _playedMaps.RemoveAt(0);
                    }
                }
                if (!_playedMaps.Contains(name.ToLower()))
                    _playedMaps.Add(name.ToLower());

                string excludedMaps = string.Join(", ", _playedMaps);

                Logger.LogInformation($"Played maps: {excludedMaps}");
            }

            if (Config.OtherSettings.RandomMapOnStart && !mapChangedOnStart && changeRequested == null)
            {
                Logger.LogInformation($"OnMapStart Requested map change after the server restarted");
                changeRequested = AddTimer((float)Config.OtherSettings.RandomMapOnStartDelay, Timer_ChangeMapOnEmpty, TimerFlags.STOP_ON_MAPCHANGE);
                if (changeRequested == null)
                {
                    Console.WriteLine("*************** Could not create timer for map change on server start");
                    Logger.LogError("Could not create timer for map change on server start");
                }
            }
            if (Config.RTVSettings.AllowRTV && Config.RTVSettings.RTVDelayFromStart > 0)
            {
                MakeRTVTimer (Config.RTVSettings.RTVDelayFromStart);
                Logger.LogInformation("RTV timer started");
            }
            if (Config.TimeLimitSettings.VoteDependsOnTimeLimit)
            {
                var TimeLimit = ConVar.Find("mp_timelimit");
                var TimeLimitValue = TimeLimit?.GetPrimitiveValue<float>() ?? 0;

                if (((int)TimeLimitValue * 60) <= Config.TimeLimitSettings.TriggerSecondsBeforeEnd)
                {
                    Logger.LogError($"Vote Depends On TimeLimit can't be started: map time limit is {TimeLimitValue} min and vote start trigger is {Config.TimeLimitSettings.TriggerSecondsBeforeEnd} seconds before end");
                }
                else if (Config.TimeLimitSettings.TriggerSecondsBeforeEnd < Config.VoteSettings.VotingTime)
                {
                    Logger.LogError($"Vote Depends On TimeLimit can't be started: Vote should be finished before the end of the map, but vote start trigger is {Config.TimeLimitSettings.TriggerSecondsBeforeEnd} seconds and Vote time is {Config.VoteSettings.VotingTime}");
                }
                else
                {
                    float timerTime = (float)((TimeLimitValue * 60) - Config.TimeLimitSettings.TriggerSecondsBeforeEnd);
                    Logger.LogInformation($"MapStart: Vote timer started for {timerTime} seconds, {Config.TimeLimitSettings.TriggerSecondsBeforeEnd} seconds before end.");
                    StartOrRestartTimeLimitTimer(timerTime, TimeLimitTimerHandle);
                }
            }
        }
        else
        {
            Console.WriteLine(NoMapcycle);
            Logger.LogError(NoMapcycle);
        }
        Logger.LogInformation("OnMapstart finished");
    }
    private void OnMapEnd()
    {
        Logger.LogInformation("OnMapEnd executed");
        changeRequested = null;
        _timeLimitTimer = null;
        _timeLimitMapChangeTimer = null;
        rtvTimer = null;
        voteEndTimer = null;
    }
    public HookResult EventRoundAnnounceWarmupHandler(EventRoundAnnounceWarmup @event, GameEventInfo info)
    {
        if (@event is null)
            return HookResult.Continue;

        Logger.LogInformation("EventRoundAnnounceWarmupHandler");
        return HookResult.Continue;
    } 
    public HookResult EventRoundAnnounceLastRoundHalfHandler(EventRoundAnnounceLastRoundHalf @event, GameEventInfo info)
    {
        if (@event is null)
            return HookResult.Continue;

        roundsManager.LastBeforeHalf = true;
        return HookResult.Continue;
    } 
    public HookResult EventRoundAnnounceMatchStartHandler(EventRoundAnnounceMatchStart @event, GameEventInfo info)
    {
        if (@event is null)
            return HookResult.Continue;

        Logger.LogInformation("EventRoundAnnounceMatchStartHandler");
        roundsManager.ClearRounds();
        return HookResult.Continue;
    }
    public HookResult EventRoundStartHandler(EventRoundStart @event, GameEventInfo info)
    {
        Restart = false;
        MapIsChanging = false;
        if (canVote)
        {
            roundsManager.UpdateMaxRoundsValue();
            roundsManager.CheckConfig();
            if (!IsVoteInProgress && !roundsManager.WarmupRunning && roundsManager.CheckMaxRounds() && !_runVoteRoundEnd)
            {
                Logger.LogInformation("Time to vote because of CheckMaxRounds");
                if (Config.WinDrawSettings.TriggerRoundsBeforeEndVoteAtRoundStart)
                {
                    if (Config.WinDrawSettings.TriggerVoteAtRoundStartSecondsFromStart > 0)
                    {
                        AddTimer((float)Config.WinDrawSettings.TriggerVoteAtRoundStartSecondsFromStart, () => {
                            Logger.LogInformation("Vote started");
                            StartVote();
                        }, TimerFlags.STOP_ON_MAPCHANGE);
                    }
                    else
                    {
                        Logger.LogInformation("Vote started");
                        StartVote();
                    }
                }
                else
                {
                    _runVoteRoundEnd = true; //StartVote will be called at round end
                    Logger.LogInformation("Vote will be started at the Round End");
                }
            }
            else if (_timeLimitVoteRoundStart)
            {
                Logger.LogInformation("Vote started from TimeLimit at Round Start");
                StartVote();
            }
            else
            {
                Logger.LogInformation($"Round start, canVote {(canVote ? "True" : "False")}, Warmup {(roundsManager.WarmupRunning ? "True" : "False")} ");
            }
        }
        else
        {
            Logger.LogError("Can't vote");
        }
        return HookResult.Continue;
    }
    public HookResult EventRoundEndHandler(EventRoundEnd @event, GameEventInfo info)
    {
        Restart = true;
        if (@event is null)
            return HookResult.Continue;
        CsTeam? winner = Enum.IsDefined(typeof(CsTeam), (byte)@event.Winner) ? (CsTeam)@event.Winner : null;
        if (winner is not null)
            roundsManager.RoundWin(winner.Value);

        if (roundsManager.LastBeforeHalf)
            roundsManager.SwapScores();

        roundsManager.LastBeforeHalf = false;
        if (_runVoteRoundEnd)
        {
            Logger.LogInformation("Vote started at the Round End");
            _runVoteRoundEnd = false;
            StartVote();
        }
        return HookResult.Continue;
    }
    public HookResult EventCsWinPanelMatchHandler(EventCsWinPanelMatch @event, GameEventInfo info)  
    {
        if (canVote && Config.WinDrawSettings.ChangeMapAfterWinDraw)
        {
            if (!string.IsNullOrEmpty(_roundEndMap))
            {
                string mapNameToChange = _roundEndMap;
                var delay = Config.OtherSettings.DelayBeforeChangeSeconds - 5.0f;
                if (delay < 1)
                    delay = 1.0f;
                Logger.LogInformation($"EventCsWinPanelMatch: plugin is responsible for map change, delay for {delay} seconds before the change of map.");
                
                // ********************** maybe add check if the timer started...
                AddTimer(delay, () =>
                {
                    if (!string.IsNullOrEmpty(mapNameToChange))
                    {
                        DoMapChange(mapNameToChange, SSMC_ChangeMapTime.ChangeMapTime_Now);
                    }
                    else
                    {
                        Logger.LogWarning("EventCsWinPanelMatch: _roundEndMap is null, so don't change");
                    }
                }, TimerFlags.STOP_ON_MAPCHANGE);
            }
            else
            {
                if (IsVoteInProgress && voteEndTimer == null)
                {
                    Logger.LogError("Can't change map after Win/Draw because vote still in Progress");
                    voteEndTimer = AddTimer(1.0f, Handle_VoteEndTimer, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
                }
                else
                    Logger.LogError("Can't change map after Win/Draw because _roundEndMap is null");
            }
        }
        return HookResult.Continue;
    }
    private void Handle_VoteEndTimer()
    { 
        if (!IsVoteInProgress && !MapIsChanging)
        {
            if (voteEndTimer != null)
            {
                timeManager.EnqueueOperation(async () => 
                {
                    bool success = await KillTimer(voteEndTimer);
                    if (success)
                    {
                        voteEndTimer = null;
                    }
                });
            }
            if (!string.IsNullOrEmpty(_roundEndMap))
            {
                DoMapChange(_roundEndMap, SSMC_ChangeMapTime.ChangeMapTime_Now);
            }
            else
            {
                Logger.LogWarning("Handle_VoteEndTimer: _roundEndMap is null, so don't change");
            }
        }
    }
    private void Timer_ChangeMapOnEmpty()
    {   
        Logger.LogInformation("Timer_ChangeMapOnEmpty start");
        if (changeRequested != null)
        {
            changeRequested = null;
        }
        var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
        if (playerEntities != null)
        {
            if (playerEntities.Any())
            {
                Logger.LogInformation("Server is not empty, cancel map change on empty or first start");
                return;
            }
        }
        Logger.LogInformation("Change map on empty server");
        mapChangedOnStart = true;
        GGMCDoAutoMapChange();
    }
    private void TimeLimitTimerHandle()
    {
        _timeLimitTimer = null;
        Logger.LogInformation("TimeLimit Timer to start a vote happen.");
        
        if (_timeLimitMapChangeTimer != null)
        {
            timeManager.EnqueueOperation(async () => 
            {
                bool success = await KillTimer(_timeLimitMapChangeTimer);
                if (success)
                {
                    _timeLimitMapChangeTimer = null;
                }
            });
        }
        if (canVote)
        {
            if (!roundsManager.MaxRoundsVoted)
            {
                if (Config.TimeLimitSettings.ChangeMapAfterTimeLimit)
                {
                    _timeLimitMapChangeTimer = AddTimer((float)(Config.TimeLimitSettings.TriggerSecondsBeforeEnd - Config.VoteSettings.VotingTime + Config.OtherSettings.DelayBeforeChangeSeconds), TimeLimitChangeMapTimer, TimerFlags.STOP_ON_MAPCHANGE);
                    Logger.LogInformation($"Start MapChange timer in {Config.TimeLimitSettings.TriggerSecondsBeforeEnd - Config.VoteSettings.VotingTime + Config.OtherSettings.DelayBeforeChangeSeconds} sec.");
                }
                Logger.LogInformation("Time to vote because of TimeLimitTimerHandle");
                if (Config.TimeLimitSettings.VoteNextRoundStartAfterTrigger)
                {
                    _timeLimitVoteRoundStart = true;
                }
                else
                {
                    StartVote();
                }
            }
            else
            {
                Logger.LogInformation($"TimeLimit Timer is finished but vote was not started because the vote already done");
                if (Config.TimeLimitSettings.ChangeMapAfterTimeLimit)
                {
                    lock (_timerLock)
                    {
                        _timeLimitMapChangeTimer = AddTimer((float)(Config.TimeLimitSettings.TriggerSecondsBeforeEnd + Config.OtherSettings.DelayBeforeChangeSeconds), TimeLimitChangeMapTimer, TimerFlags.STOP_ON_MAPCHANGE);
                    }
                    Logger.LogInformation($"Start MapChange timer in {Config.TimeLimitSettings.TriggerSecondsBeforeEnd + Config.OtherSettings.DelayBeforeChangeSeconds} sec.");
                }
            }
        }
        else
        {
            Logger.LogInformation($"TimeLimit Timer is finished but vote was not started because canVote is False");
            if (Config.TimeLimitSettings.ChangeMapAfterTimeLimit)
            {
                lock (_timerLock)
                {
                    _timeLimitMapChangeTimer = AddTimer((float)(Config.TimeLimitSettings.TriggerSecondsBeforeEnd + Config.OtherSettings.DelayBeforeChangeSeconds), TimeLimitChangeMapTimer, TimerFlags.STOP_ON_MAPCHANGE);
                }
                Logger.LogInformation($"Start MapChange timer in {Config.TimeLimitSettings.TriggerSecondsBeforeEnd + Config.OtherSettings.DelayBeforeChangeSeconds} sec.");
            }
        }
    }
    private void TimeLimitChangeMapTimer()
    {
        lock (_timerLock)
        {
            _timeLimitMapChangeTimer = null;
        }
        if (!string.IsNullOrEmpty(_roundEndMap))
        {
            Logger.LogInformation("Change map after Time Limit passed.");
            DoMapChange(_roundEndMap, SSMC_ChangeMapTime.ChangeMapTime_Now);
        }
        else
        {
            Logger.LogError("Can't change map after Time Limit passed, _roundEndMap is null or empty");
        }
    }
    private bool ReloadMapcycle()
    {
        try
        {
            string jsonString = File.ReadAllText(mapsFilePath);
            if (string.IsNullOrEmpty(jsonString))
            {
                // Log an error or throw an exception, as appropriate for your application
                Logger.LogError("[GGMC] Error: GGMCmaps.json file is empty.");
                return false;
            }

            var deserializedDictionary = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, MapInfo>>(jsonString);

            // Check if deserialization was successful and the result is not null
            if (deserializedDictionary != null)
            {
                Maps_from_List = deserializedDictionary;
            }
            else
            {
                Logger.LogError("[GGMC] Error: GGMCmaps.json deserialization failed, resulting in null MapList.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[GGMC] Error reading or deserializing GGMCmaps.json file: {ex.Message}");
            return false;
        }
        foreach (var mapInfo in Maps_from_List)
        {
            if (!string.IsNullOrEmpty(mapInfo.Value.Display))
            {
                DisplayNameToKeyMap[mapInfo.Value.Display] = mapInfo.Key;
            }
        }
        maplist = Maps_from_List.Keys.ToList();
        mapstotal = maplist.Count;
        if (mapstotal > 0) {
            return true;
        }
        return false;
    }
    //Start vote at the beginning of the round according to rules of Round Manager
    private void StartVote()
    {
        Logger.LogInformation("Vote started according to Round Manager Rules or TimeLimit Timer");
        if (IsVoteInProgress)
        {
            Logger.LogInformation("MapVoteCommand: Another vote active when mapvote start. Skip votes");
            return;
        }
        
        if (voteTimer == null)
        {
            IsVoteInProgress = true;
            roundsManager.MaxRoundsVoted = true;
            timeToVote = Config.VoteSettings.VotingTime;
            VotesCounter = 0;
            voteTimer = AddTimer(1.0f, EndOfVotes, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            Logger.LogInformation("Vote Timer started at StartVote");
            DoAutoMapVote(null!, timeToVote, SSMC_ChangeMapTime.ChangeMapTime_MapEnd, Config.VoteSettings.EndMapVoteWASDMenu);
        }
        else
        {
            Logger.LogInformation("Vote Timer already works, we don't start new Vote at StartVote");
        }
    }
// Автоматическая смена карты на рандомную подходящую
    private void GGMCDoAutoMapChange(SSMC_ChangeMapTime changeTime = SSMC_ChangeMapTime.ChangeMapTime_Now)
    {
        if (!canVote)
        {
            Logger.LogInformation("GGMCDoAutoMapChange: Can't do Auto Map Change - mapcycle is invalid.");
            return;
        }

        if(IsVoteInProgress) {
            Logger.LogInformation("GGMCDoAutoMapChange: Can't do Auto Map Change, vote is in progress.");
            return;
        }

        //Variables
        string[] validmapnames = new string [512];
        int[] validmapweight = new int[512];
        int validmaps = 0, i = 0;
        int numplayers = GetRealClientCount(); 
        if (numplayers == 0) numplayers = 1;
    //  create map list to chose
        foreach (var mapcheck in Maps_from_List)
        {
            if ((mapcheck.Value.MinPlayers != 0 && numplayers < mapcheck.Value.MinPlayers) || (mapcheck.Value.MaxPlayers != 0 && numplayers > mapcheck.Value.MaxPlayers) || _playedMaps.Contains(mapcheck.Key.ToLower()))
            {
                i++;
                continue; //  map does not suite
            }
    //   add map to maplist[i])
            validmaps++;
            validmapnames[validmaps] = mapcheck.Key;
            validmapweight[validmaps] = validmapweight[validmaps - 1] + mapcheck.Value.Weight;
            i++;
        }
        if (validmaps < 1)
        {
            Logger.LogInformation("DoAutoMapChange: Could not automatically change the map, no valid maps available.");
            return;
        }
        int choice = random.Next(1, validmapweight[validmaps]);
        int map = WeightedMap(validmaps, validmapweight, choice);
        if (map == 0)
        {
            Logger.LogInformation($"DoAutoMapChange: Could not automatically change the map, no valid maps available. players {numplayers}, validmaps {validmaps}, validmapweight {validmapweight[validmaps]}, random = {choice} chosen map {map}");
            return;
        }
        Logger.LogInformation($"DoAutoMapChange: Changing map to {validmapnames[map]}");
        DoMapChange(validmapnames[map], changeTime);
    }

//  Команда на смену выбранной карты
    private void DoMapChange(string mapChange, SSMC_ChangeMapTime changeTime = SSMC_ChangeMapTime.ChangeMapTime_Now)
    {
        if (MapIsChanging)
        {
            Logger.LogInformation($"DoMapChange: Map is changing already. Request to change to {mapChange} is cancelled");
            return;
        }
        if (mapChange == "extend.map")
        {
            ExtendMap();

/*            try
            {
                ExtendMap();
            }
            catch (Exception ex)
            {
                Server.NextFrame( () => {Logger.LogError($"ExtendMap error: {ex.Message}");});
            } */
            return;
        }
        MapIsChanging = true;
        string mapname = mapChange;
//        _selectedMap = null;
        PrintToServerCenter("mapchange.to", mapname);
        PrintToServerChat("nextmap.info", mapname);
        Console.WriteLine($"Map is changing to {mapname}");
        Logger.LogInformation($"DoMapChange: Map is going to be change to {mapname}");
        MapToChange = mapname;
        if (changeTime == SSMC_ChangeMapTime.ChangeMapTime_Now)
        {
            ChangeMapInFive(mapname);
        }
        else if (changeTime == SSMC_ChangeMapTime.ChangeMapTime_MapEnd)
        {
            MapIsChanging = false;
            Server.ExecuteCommand($"nextlevel {mapname}");
            Logger.LogInformation($"Set NextLevel to {mapname}");
            _roundEndMap = mapname;
/*            if (endTimer == null)
            {
                EmergencyMap = mapname;
                endTimer = AddTimer(10.0f, EmergencyChange, TimerFlags.STOP_ON_MAPCHANGE);
            } */
        }
        else 
        {
            Logger.LogError($"Something wrong in DoMapChange - {mapname}");
        }
    }
/*    private void EmergencyChange()
    {
        Logger.LogInformation($"Emergency Timer to {EmergencyMap}");
        endTimer = null;
        Server.ExecuteCommand($"ds_workshop_changelevel {EmergencyMap}");
    } */
    private void ExtendMap()
    {
        PrintToServerCenter("extend.win");
        PrintToServerChat("extend.win");
        var mp_timelimit = ConVar.Find("mp_timelimit");
        if (mp_timelimit != null)
        {
            var timelimitOld = mp_timelimit.GetPrimitiveValue<float>();
            if (timelimitOld > 0)
            {
                float newTimeLimit = timelimitOld + Config.VoteSettings.ExtendMapTimeMinutes;
                Logger.LogInformation($"DoMapChange: Extend Map is winning option. TimeLimit is {timelimitOld}. Time extended by {Config.VoteSettings.ExtendMapTimeMinutes} minutes.");
                mp_timelimit.SetValue(newTimeLimit);
                Logger.LogInformation($"DoMapChange: TimeLimit now {mp_timelimit.GetPrimitiveValue<float>()}");
                if (Config.TimeLimitSettings.VoteDependsOnTimeLimit)
                {
                    var secondsPassed = timelimitOld * 60 - Config.TimeLimitSettings.TriggerSecondsBeforeEnd + Config.VoteSettings.VotingTime;                        
                    float newTrigger = Config.TimeLimitSettings.TriggerSecondsBeforeEnd;
                    
                    if (newTimeLimit * 60 - Config.TimeLimitSettings.TriggerSecondsBeforeEnd < secondsPassed) //Time left to play after new timelimit is less than TriggerSecondsBeforEnd, but we need to vote again
                    {
                        newTrigger = (newTimeLimit * 60 - secondsPassed) / 2;
                    }
                    float newVoteDelay = newTimeLimit * 60 - secondsPassed - newTrigger;
                    roundsManager.MaxRoundsVoted = false;
                    Logger.LogInformation($"DoMapChange: Seconds passed: {secondsPassed}, timer restart for new vote at the end of time limit");
                    StartOrRestartTimeLimitTimer(newVoteDelay, TimeLimitTimerHandle);

                    if (Config.TimeLimitSettings.ChangeMapAfterTimeLimit && _timeLimitMapChangeTimer != null)
                    {
                        timeManager.EnqueueOperation(async () => 
                        {
                            bool success = await KillTimer(_timeLimitMapChangeTimer);
                            if (success)
                            {
                                _timeLimitMapChangeTimer = null;
                            }
                        });
                    }
                }
            }
            else
            {
                Logger.LogError($"DoMapChange: Extend Map is winning option. But time can't be extended because of the cvar mp_timelimit is 0");
            }
        }
        else
        {
            Logger.LogInformation($"DoMapChange: Extend Map is winning option. But time can't be extended because of the problem with the cvar mp_timelimit.");
        }
    }
    private void ChangeMapInFive(string mapname)
    {
        Logger.LogInformation($"Map will be changed in 5 seconds");
        AddTimer (5.0f, () => {
            if (Maps_from_List.TryGetValue(mapname, out var mapInfo))
            {
                MapToChange = mapname;
                MapIsChanging = false;
                
                if (mapInfo.WS)
                {
                    if (mapInfo.MapId.Length > 0)
                    {
                        Logger.LogInformation($"Execute command host_workshop_map {mapInfo.MapId}");
                        Server.ExecuteCommand($"host_workshop_map {mapInfo.MapId}");
                    }
                    else
                    {
                        Logger.LogInformation($"Execute command ds_workshop_changelevel {mapname}");
                        Server.ExecuteCommand($"ds_workshop_changelevel {mapname}");
                    }
                }
                else
                {
                    Logger.LogInformation($"Execute command changelevel {mapname}");
                    Server.ExecuteCommand($"changelevel {mapname}");
                }
            }
            else
            {
                Logger.LogError($"DoMapChange: can't find map in list {mapname}");
                MapIsChanging = false;
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }
    private int WeightedMap(int validmaps, int[] validmapweight, int choice)
    {
        for (int i = 1; i <= validmaps; i++)
        {
            if (validmapweight[i] >= choice)
                return i;
        }
        return 0;
    }

//  Делает меню из карт 
    private ChatMenu CreateMapsMenu(Action<CCSPlayerController,ChatMenuOption> action, CCSPlayerController playerController, bool limits=true)
    {
        var MapsMenu = new ChatMenu("List of maps:");
      
        List<string> selectedMapList = new();
        bool haveSelectedMaps = false;
        if (IsValidPlayer(playerController))
        {
            if (players[playerController.Slot] != null)
            {
                if (players[playerController.Slot].selectedMaps.Count > 0)
                {
                    selectedMapList = players[(int)playerController.Slot].selectedMaps;
                    haveSelectedMaps = true;
                    MapsMenu.AddMenuOption(Localizer["stop.line", selectedMapList.Count], action);
                }
            }
            else
            {
                Logger.LogError($"CreateMapsMenu: Player {playerController.PlayerName} ({playerController.Slot}) is not in players list");
                return null!;
            }
        }
        string[] validmapnames = new string [512];
        string displayName = "";
        int menuSize = 0;
        int numplayers = GetRealClientCount(false);
        bool playersvalid = true;
        if (numplayers == 0) numplayers = 1;
    //  create map list to chose
        foreach (var mapcheck in Maps_from_List)
        {
            if ((mapcheck.Value.MinPlayers != 0 && numplayers < mapcheck.Value.MinPlayers) || (mapcheck.Value.MaxPlayers != 0 && numplayers > mapcheck.Value.MaxPlayers) )
            {
                playersvalid = false; //сначала маркируем карту проходит ли она по кол-ву игроков
            }
            if (limits && (_playedMaps.Contains(mapcheck.Key.ToLower()) || !playersvalid || IsNominated(mapcheck.Key))) //если включены лимиты и карта либо не валидна по списку 
            {                                              // карт, которые уже были, либо по кол-ву игроков или номинирована - исключаем
                playersvalid = true;
                continue;
            }
            if (haveSelectedMaps && selectedMapList.Contains(mapcheck.Key))
                continue;
            if (mapcheck.Value.Display.Length > 0)
                displayName = mapcheck.Value.Display;
            else
                displayName = mapcheck.Key;
            if (!playersvalid || _playedMaps.Contains(mapcheck.Key.ToLower()))
            {
                MapsMenu.AddMenuOption(displayName + " (!)", action);
            }
            else
            {
                MapsMenu.AddMenuOption(displayName, action);
            }
            playersvalid = true;
            menuSize++;
        }
        if (menuSize < 1)
        {
            Logger.LogInformation("[GGMC]: Could not create map menu, no valid maps available.");
            return null!;
        }
//        MapsMenu.PostSelectAction = PostSelectAction.Close;
        return MapsMenu;
    }

    private IWasdMenu CreateMapsMenuWASD(Action<CCSPlayerController,IWasdMenuOption> action, CCSPlayerController playerController, bool limits=true)
    {
        /*************************************** Localizer *********************/
        IWasdMenu MapsMenu = wASDMenu.manager.CreateMenu("List of maps:");
      
        List<string> selectedMapList = new();
        bool haveSelectedMaps = false;
        if (IsValidPlayer(playerController))
        {
            if (players[playerController.Slot] != null)
            {
                if (players[playerController.Slot].selectedMaps.Count > 0)
                {
                    selectedMapList = players[(int)playerController.Slot].selectedMaps;
                    haveSelectedMaps = true;
                    MapsMenu.Add(Localizer["stop.line", selectedMapList.Count], action);
                }
            }
            else
            {
                Logger.LogError($"CreateMapsMenu: Player {playerController.PlayerName} ({playerController.Slot}) is not in players list");
                return null!;
            }
        }
        string[] validmapnames = new string [512];
        string displayName = "";
        int menuSize = 0;
        int numplayers = GetRealClientCount(false);
        bool playersvalid = true;
        if (numplayers == 0) numplayers = 1;
    //  create map list to chose
        foreach (var mapcheck in Maps_from_List)
        {
            if ((mapcheck.Value.MinPlayers != 0 && numplayers < mapcheck.Value.MinPlayers) || (mapcheck.Value.MaxPlayers != 0 && numplayers > mapcheck.Value.MaxPlayers) )
            {
                playersvalid = false; //сначала маркируем карту проходит ли она по кол-ву игроков
            }
            if (limits && (_playedMaps.Contains(mapcheck.Key.ToLower()) || !playersvalid || IsNominated(mapcheck.Key))) //если включены лимиты и карта либо не валидна по списку 
            {                                              // карт, которые уже были, либо по кол-ву игроков или номинирована - исключаем
                playersvalid = true;
                continue;
            }
            if (haveSelectedMaps && selectedMapList.Contains(mapcheck.Key))
                continue;

            if (mapcheck.Value.Display.Length > 0)
                displayName = mapcheck.Value.Display;
            else
                displayName = mapcheck.Key;
            if (!playersvalid || _playedMaps.Contains(mapcheck.Key.ToLower()))
            {
                MapsMenu.Add(displayName + " (!)", action);
            }
            else
            {
                MapsMenu.Add(displayName, action);
            }
            playersvalid = true;
            menuSize++;
        }
        if (menuSize < 1)
        {
            Logger.LogInformation("[GGMC]: Could not create map menu, no valid maps available.");
            return null!;
        }
//        MapsMenu.PostSelectAction = PostSelectAction.Close;
        return MapsMenu;
    }

// Админ выбирает ручной выбор карты для смены или автоматический
    private void AdminChangeMapHandle(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (IsValidPlayer(caller))
        {
            IWasdMenu acm_menu = wASDMenu.manager.CreateMenu(Localizer["choose.map"]);
            acm_menu.Add(Localizer["manual.map"], AdminChangeMapManual); // Simply change the map
            acm_menu.Add(Localizer["automatic.map"], AdminChangeMapAuto); // Start voting for map
            acm_menu.Prev = option.Parent?.Options?.Find(option);
            wASDMenu.manager.OpenSubMenu(caller, acm_menu);
        }
    }
//  Админ выбрал ручной выбор для смены, выбор карты и смена
    private void AdminChangeMapManual(CCSPlayerController player, IWasdMenuOption option)
    {
        if (IsValidPlayer(player))
        {
            wASDMenu.manager.CloseMenu(player);
            IWasdMenu acmm_menu = CreateMapsMenuWASD(Handle_AdminManualChange, player, false); // no restrictions, because admn choose maps
            if (acmm_menu != null)
                wASDMenu.manager.OpenMainMenu(player, acmm_menu);
        }
        return;
    }
//  Карта выбрана - меняем    
    private void Handle_AdminManualChange(CCSPlayerController player, IWasdMenuOption option)
    {
        if (option == null || option.OptionDisplay == null)
        {
            Logger.LogInformation("[GGMC]: Admin " + player.PlayerName + " has chosen map for manual change but option is null.");
            wASDMenu.manager.CloseMenu(player);
            return;
        }
        string map = ClearSuffix(option.OptionDisplay);
        
        Logger.LogInformation($"[GGMC]: Admin {player.PlayerName} has chosen map {map} for manual change.");
        wASDMenu.manager.CloseMenu(player);
        DoMapChange(map, SSMC_ChangeMapTime.ChangeMapTime_Now);
    }
//  Админ выбрал автоматический выбор для смены, отсылка на GGMCDoAutoMapChange, которая с этим справляется
    private void AdminChangeMapAuto(CCSPlayerController player, IWasdMenuOption option)
    {
        Logger.LogInformation("[GGMC]: Admin " + player.PlayerName + " has chosen auto map change.");
        wASDMenu.manager.CloseMenu(player);
        GGMCDoAutoMapChange(SSMC_ChangeMapTime.ChangeMapTime_Now);
    }
//  Админ запускает общее голосования за выбор карты - выбор карт для голосования ручной или автоматом
    private void AdminStartVotesMapHandle(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (IsValidPlayer(caller))
        {
            Logger.LogInformation($"[GGMC]: Admin {caller.PlayerName} want to start vote for map.");
            
            IWasdMenu acvm_menu = wASDMenu.manager.CreateMenu(Localizer["choose.map"]);
            acvm_menu.Add(Localizer["manual.map"], AdminVoteMapManual); // Simply change the map
            acvm_menu.Add(Localizer["automatic.map"], AdminVoteMapAuto); // Start voting for map
            acvm_menu.Prev = option.Parent?.Options?.Find(option);
            wASDMenu.manager.OpenSubMenu(caller, acvm_menu);
        }
    }
//  Админ выбрал ручной выбор карт    
    private void AdminVoteMapManual(CCSPlayerController player, IWasdMenuOption option)
    {
        if (IsValidPlayer(player))
        {
            wASDMenu.manager.CloseMenu(player);
            IWasdMenu avmm_menu = CreateMapsMenuWASD(Handle_VoteMapManual, player, false); // no restrictions, because admn choose maps
            if (avmm_menu != null)
                wASDMenu.manager.OpenMainMenu(player, avmm_menu);
        }
        return;
    }
//  Обработка процесса, пока админ набирает карты. Когда готово - запуск голосования
    private void Handle_VoteMapManual(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (IsValidPlayer(caller) && option != null && option.OptionDisplay != null)
        {
            string stopline = Localizer["stop.line", players[caller.Slot].selectedMaps.Count];
            string fromMenu = option.OptionDisplay;

            if (fromMenu == stopline)
            {
                wASDMenu.manager.CloseMenu(caller);
                if (IsVoteInProgress)
                {
                    caller.PrintToChat(Localizer["vote.inprogress"]);
                }
                else
                {
                    DoManualMapVote(caller);
                }
            }
            else
            {
                players[caller.Slot].selectedMaps.Add(ClearSuffix(option.OptionDisplay));
                if (players[caller.Slot].selectedMaps.Count == Config.VoteSettings.MapsInVote)
                {
                    wASDMenu.manager.CloseMenu(caller);
                    DoManualMapVote(caller);
                }
                else
                {
                    AdminVoteMapManual(caller, null!);
                }
            }
        }
    }
//  В итоге Запуск голосования по вручную набранным картам
    private void DoManualMapVote(CCSPlayerController caller)
    {
        if (IsValidPlayer(caller))
        {
            if (players[caller.Slot].selectedMaps.Count < 2)
            {
                caller.PrintToChat(Localizer["vote.notenough"]);
            }
            if (IsVoteInProgress)
            {
                caller.PrintToChat(Localizer["vote.inprogress"]);
            }
            else
            {
                Logger.LogInformation($"[GGMC]: Admin {caller.PlayerName} starts vote.");
                IsVoteInProgress = true;
                DoAutoMapVote(caller, Config.VoteSettings.VotingTime, SSMC_ChangeMapTime.ChangeMapTime_Now );
            }
        }
    }
    private void AdminVoteMapAuto(CCSPlayerController player, IWasdMenuOption option)
    {
        wASDMenu.manager.CloseMenu(player);
        Logger.LogInformation($"[GGMC]: Admin {player.PlayerName} started vote auto.");
        if (IsVoteInProgress)
        {
            Logger.LogInformation($"AdminVoteMapAuto: Another vote active when admin {player.PlayerName} started votes. Skip votes");
            if (IsValidPlayer(player))
            {
                player.PrintToChat(Localizer["vote.inprogress"]);
            }
            return;
        }
    
        if (voteTimer == null)
        {
            IsVoteInProgress = true;
            timeToVote = Config.VoteSettings.VotingTime;
            VotesCounter = 0;
            voteTimer = AddTimer(1.0f, EndOfVotes, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            DoAutoMapVote(null!, timeToVote, SSMC_ChangeMapTime.ChangeMapTime_Now);
            Logger.LogInformation("Vote Timer started at AdminVoteMapAuto");
        }
        else
        {
            Logger.LogInformation("Vote Timer already works, we don't start new Vote at AdminVoteMapAuto");
        }
    }
//  Старт голосования "менять карту или нет"
    private void VotesForChangeMapHandle(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (IsValidPlayer(caller))
        {
            wASDMenu.manager.CloseMenu(caller);
            if (IsVoteInProgress)
            {
                caller.PrintToChat(Localizer["vote.inprogress"]);
                return;
            }
            Logger.LogInformation($"[GGMC]: Admin {caller.PlayerName} started vote to change map or not.");
            IsVoteInProgress = true;
            optionCounts.Clear();
            votePlayers.Clear();
            if (GlobalChatMenu != null)
            {
                Logger.LogError($"GlobalChatMenu is not null but should be, possibly another vote is active");
                return;
            }
            GlobalChatMenu = new ChatMenu(Localizer["vote.changeornot"]);
            GlobalChatMenu.AddMenuOption(Localizer["vote.yes"], (player, option) =>
            {
                if (!votePlayers.ContainsKey(player.Slot))
                {
                    votePlayers.Add(player.Slot, "Yes");
                    if (!optionCounts.TryGetValue("Yes", out int count))
                        optionCounts["Yes"] = 1;
                    else
                        optionCounts["Yes"] = count + 1;
                    if (Config.OtherSettings.PrintPlayersChoiceInChat)
                    {
                        PrintToServerChat("player.voteforchange", player.PlayerName);
                    }
                    else
                    {
                        PrintToPlayerChat(player, "player.voteforchange", player.PlayerName);
                    }
                }
                MenuManager.CloseActiveMenu(player);
            });
            GlobalChatMenu.AddMenuOption(Localizer["vote.no"], (player, option) =>
            {
                if (!votePlayers.ContainsKey(player.Slot))
                {
                    votePlayers.Add(player.Slot, "No");
                    if (!optionCounts.TryGetValue("No", out int count))
                        optionCounts["No"] = 1;
                    else
                        optionCounts["No"] = count + 1;
                    if (Config.OtherSettings.PrintPlayersChoiceInChat)
                    {
                        PrintToServerChat("player.voteagainstchange", player.PlayerName);
                    }
                    else
                    {
                        PrintToPlayerChat(player, "player.voteagainstchange", player.PlayerName);
                    }
                }
                MenuManager.CloseActiveMenu(player);
            });
//            ChangeOrNotMenu.PostSelectAction = PostSelectAction.Close;
        
            var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var player in playerEntities)
                {
                    MenuManager.OpenChatMenu(player, GlobalChatMenu);
                }

                AddTimer((float)Config.VoteSettings.VotingTime, () => TimerChangeOrNot(), TimerFlags.STOP_ON_MAPCHANGE);
            }
        }
    }
    private void AdminSetNextMapHandle(CCSPlayerController player, IWasdMenuOption option)
    {
        if (IsValidPlayer(player))
        {
/*            var manager = GetMenuManager();
            if(manager == null)
                return; */
            wASDMenu.manager.CloseMenu(player);
            IWasdMenu acmm_menu = CreateMapsMenuWASD(Handle_AdminSetNextMap, player, false); // no restrictions, because admn choose maps
            if (acmm_menu != null)
                wASDMenu.manager.OpenMainMenu(player, acmm_menu);
            
/*            ChatMenu chatMenu = CreateMapsMenu(Handle_AdminManualChange, player, false); // no restrictions, because admn choose maps
            if (chatMenu != null)
            {
                MenuManager.OpenChatMenu(player, chatMenu);
            } */
        }
        return;
    }
    private void Handle_AdminSetNextMap(CCSPlayerController player, IWasdMenuOption option)
    {
/*        var manager = GetMenuManager();
        if(manager == null)
            return; */
        if (option == null || option.OptionDisplay == null)
        {
            Logger.LogInformation("[GGMC]: Admin " + player.PlayerName + " has chosen null map to set as nextmap.");
            wASDMenu.manager.CloseMenu(player);
            return;
        }
        string map = ClearSuffix(option.OptionDisplay);
        
        Logger.LogInformation($"[GGMC]: Admin {player.PlayerName} has chosen map {map} to set as nextmap.");
        wASDMenu.manager.CloseMenu(player);
        DoMapChange(map, SSMC_ChangeMapTime.ChangeMapTime_MapEnd);
    }
    private void TimerChangeOrNot()
    {
        IsVoteInProgress = false;
        GlobalChatMenu = null;
        if (optionCounts.Count == 0)
        {
            PrintToServerCenter("vote.failed");
            return;
        }
        string result;
        if (optionCounts.Aggregate((x, y) => x.Value > y.Value ? x : y).Key == "Yes")
        {
            result = "voted.forchange"; 
        }
        else
        {
            result = "voted.againstchange";
        }
        PrintToServerCenter(result);
        PrintToServerChat(result);
    }
//  Автоматически набираются карты для голосования или используются набранные админом и запускается общее голосование
    private void DoAutoMapVote(CCSPlayerController caller, int timeToMapVote = 20, SSMC_ChangeMapTime changeTime = SSMC_ChangeMapTime.ChangeMapTime_Now, bool wasdmenu = false)
    {
        string mapNameKey;
        if (!canVote)
        {
            Console.WriteLine("[GGMC] Can't vote now");
            IsVoteInProgress = false;
            return;
        }
        if (GlobalWASDMenu != null)
        {
            Logger.LogError($"GlobalWASDMenu is not null but should be, possibly another vote is active");
            IsVoteInProgress = false;
            return;
        }
        if (GlobalChatMenu != null)
        {
            Logger.LogError($"GlobalChatMenu is not null but should be, possibly another vote is active");
            IsVoteInProgress = false;
            return;
        }
        int ConfigMapsInVote = 0;
//        IWasdMenuManager? manager = null;
        optionCounts.Clear();
        votePlayers.Clear();
        if (wasdmenu)
        {
            ConfigMapsInVote = Config.VoteSettings.MapsInVote;
/*            manager = GetMenuManager();
            if(manager == null)
            {
                Logger.LogError("[GGMC] Can't get menu manager");
                IsVoteInProgress = false;
                return;
            } */
            GlobalWASDMenu = wASDMenu.manager.CreateMenu(Localizer["choose.map"]);
            if (GlobalWASDMenu == null)
            {
                Logger.LogError($"GlobalWASDMenu is null but should bot be, something is wrong");
                IsVoteInProgress = false;
                return;
            }
        }
        else
        {
            if (Config.VoteSettings.MapsInVote < 6)
            {
                ConfigMapsInVote = Config.VoteSettings.MapsInVote;
            }
            else
            {
                ConfigMapsInVote = 5;
                Logger.LogWarning("Maps in Vote set to 5 because this is the maximum for ChatMenu");
            }
            GlobalChatMenu = new ChatMenu(Localizer["choose.map"]);
            if (GlobalChatMenu == null)
            {
                Logger.LogError($"GlobalChatMenu is null but should not be, something is wrong");
                IsVoteInProgress = false;
                return;
            }
        }
        mapsToVote.Clear();
        int mapsinvote = 0, i = 0;

//        string mapsToVoteStr;

        // If called by admin, he has selected maps to vote
        if (IsValidPlayer(caller) && players[caller.Slot].selectedMaps.Count > 1)
        {
            foreach (var mapName in players[caller.Slot].selectedMaps)
            {
                mapsToVote.Add(mapName);
                if (++mapsinvote == ConfigMapsInVote) break;
            }
        }
        else // otherwise we select random maps
        {
            if (nominatedMaps.Count > 0)
            {
                foreach (var mapName in nominatedMaps)
                {
                    mapsToVote.Add(mapName);
                    if (++mapsinvote == ConfigMapsInVote) break;
                }
//                mapsToVoteStr = string.Join(", ", mapsToVote);
//                Logger.LogInformation($"mapsinvote: {mapsinvote}, Nominated mapsToVoteStr: {mapsToVoteStr}");
            }
            if (mapsinvote < ConfigMapsInVote)
            {
                string[] validmapnames = new string [512];
                int[] validmapweight = new int[512];
                validmapweight[0] = 0;
                int validmaps = 0;
                int numplayers = GetRealClientCount(false);
                if (numplayers == 0) numplayers = 1;
            //  create map list to chose
                foreach (var mapcheck in Maps_from_List)
                {
                    if ((mapcheck.Value.MinPlayers != 0 && numplayers < mapcheck.Value.MinPlayers) || (mapcheck.Value.MaxPlayers != 0 && numplayers > mapcheck.Value.MaxPlayers) 
                        || _playedMaps.Contains(mapcheck.Key.ToLower()) || (mapsToVote.Count > 0 && mapsToVote.Contains(mapcheck.Key)))
                    {
                        continue; //  map does not suite
                    }
            //   add map to maplist[i])
                    validmaps++;
                    validmapnames[validmaps] = mapcheck.Key;
                    validmapweight[validmaps] = validmapweight[validmaps - 1] + mapcheck.Value.Weight;
                }
                if (validmaps < 1 && mapsinvote == 0)
                {
                    Logger.LogInformation("DoAutoMapChange: Could not automatically change the map, no nominated and valid maps available.");
                    IsVoteInProgress = false;
                    return;
                }
                
//                mapsToVoteStr = string.Join(", ", validmapnames);
//                Logger.LogInformation($"mapsinvote: {mapsinvote}, Validmaps mapsToVoteStr: {validmapnames}");


                int mapstochose = ConfigMapsInVote - mapsinvote;
                if (mapstochose > validmaps)
                {
                    Logger.LogWarning($"Number of valid maps ({validmaps}) is less then maps to choose. Only {validmaps} will be selected");
                    mapstochose = validmaps;
                }
                if (mapstochose > 0)
                {
//                    int cycles = 30; // если карты будут дублироваться, то повторных циклов не больше этого числа
                    int choice, map;
                    List<int> selectedIndices = new List<int>();
                    i = 0;
                    for ( i = 0; i < mapstochose; i++) 
                    {
                        choice = random.Next(1, validmapweight[validmaps]);
                        map = WeightedMap(validmaps, validmapweight, choice);
                        if (map < 1)
                        {
                            Logger.LogError($"Cannot choose map. players {numplayers}, validmaps {validmaps}. Something wrong with map weights");
                            break;
                        }
                        // Ensure unique map selection
                        if (selectedIndices.Contains(map))
                        {
                            int originalMap = map;
                            bool foundUnique = false;
                            for (int offset = 1; offset < validmaps && !foundUnique; offset++)
                            {
                                int up = map + offset;
                                int down = map - offset;
                                if (up <= validmaps && !selectedIndices.Contains(up))
                                {
                                    map = up;
                                    foundUnique = true;
                                }
                                else if (down > 0 && !selectedIndices.Contains(down))
                                {
                                    map = down;
                                    foundUnique = true;
                                }
                            }

                            if (!foundUnique)
                            {
                                Logger.LogError($"Cannot choose unique map. Players: {numplayers}, valid maps: {validmaps}, original choice: {originalMap}");
                                continue;
                            }
                        }
                        
//                        if (mapsToVote.Contains(validmapnames[map]))
//                            continue;
                        selectedIndices.Add(map);
                        mapsToVote.Add(validmapnames[map]);
                        mapsinvote++;
                        // Convert selectedIndices to a string
//                        string selectedIndicesStr = string.Join(", ", selectedIndices);

                        // Convert mapsToVote to a string
//                        mapsToVoteStr = string.Join(", ", mapsToVote);

                        // Log the arrays
//                        Logger.LogInformation($"Selected Indices: {selectedIndicesStr}");
//                        Logger.LogInformation($"Maps to Vote: {mapsToVoteStr}");
                    }
                }
            }
        }
        if (mapsinvote == 0)
        {
            Console.WriteLine("DoAutoMapVote: no maps for the vote. Exit with error.");
            Logger.LogError("DoAutoMapVote: no maps for the vote. Exit with error.");
            IsVoteInProgress = false;
            return;
        }
        if (Config.VoteSettings.ExtendMapInVote)
        {
            if (wasdmenu)
            {
                GlobalWASDMenu?.Add(Localizer["extend.map"], (player, option) =>
                {
                    if (!votePlayers.ContainsKey(player.Slot) && option != null && option.OptionDisplay != null)
                    {
                        votePlayers.Add(player.Slot, "extend.map");
                        
                        if (!optionCounts.TryGetValue("extend.map", out int count))
                            optionCounts["extend.map"] = 1;
                        else
                            optionCounts["extend.map"] = count + 1;
                        _votedMap++;
                        PrintToServerChat("player.choice", player.PlayerName, option.OptionDisplay);
                    }
/*                    var manager = GetMenuManager();
                    if(manager == null)
                    {
                        IsVoteInProgress = false;
                        return;
                    } */
                    wASDMenu.manager.CloseMenu(player);
                });
            }
            else
            {
                GlobalChatMenu?.AddMenuOption(Localizer["extend.map"], (player, option) =>
                {
                    if (!votePlayers.ContainsKey(player.Slot) && option != null && option.Text != null)
                    {
                        votePlayers.Add(player.Slot, "extend.map");
                        
                        if (!optionCounts.TryGetValue("extend.map", out int count))
                            optionCounts["extend.map"] = 1;
                        else
                            optionCounts["extend.map"] = count + 1;
                        _votedMap++;
                        PrintToServerChat("player.choice", player.PlayerName, option.Text);
                    }
                    MenuManager.CloseActiveMenu(player);
                });
            }
        }

        Logger.LogInformation($"List of maps to vote: {string.Join(", ", mapsToVote)}");

        for (i = 0; i < mapsinvote; i++)
        {
            try
            {
                if (wasdmenu)
                {
                    GlobalWASDMenu?.Add(GetDisplayName(mapsToVote[i]), (player, option) =>
                    {
                        if (!votePlayers.ContainsKey(player.Slot) && option != null && option.OptionDisplay != null)
                        {
                            mapNameKey = GetMapKeyByDisplayNameOrKey(option.OptionDisplay);
                            votePlayers.Add(player.Slot, mapNameKey);
                            
                            if (!optionCounts.TryGetValue(mapNameKey, out int count))
                                optionCounts[mapNameKey] = 1;
                            else
                                optionCounts[mapNameKey] = count + 1;
                            _votedMap++;
                            if (Config.OtherSettings.PrintPlayersChoiceInChat)
                            {
                                PrintToServerChat("player.choice", player.PlayerName, option.OptionDisplay);
                            }
                            else
                            {
                                PrintToPlayerChat(player, "player.choice", player.PlayerName, option.OptionDisplay);
                            }
                        }
/*                        var mngr = GetMenuManager();
                        if(mngr == null)
                        {
                            IsVoteInProgress = false;
                            return;
                        } */
                        wASDMenu.manager.CloseMenu(player);
                    });
                }
                else
                {
                    GlobalChatMenu?.AddMenuOption(GetDisplayName(mapsToVote[i]), (player, option) =>
                    {
                        if (!votePlayers.ContainsKey(player.Slot) && option != null && option.Text != null) // if contains - means we have his vote already and skip this vote
                        {
                            mapNameKey = GetMapKeyByDisplayNameOrKey(option.Text);
                            votePlayers.Add(player.Slot, mapNameKey);
                            
                            if (!optionCounts.TryGetValue(mapNameKey, out int count))
                                optionCounts[mapNameKey] = 1;
                            else
                                optionCounts[mapNameKey] = count + 1;
                            _votedMap++;
                            if (Config.OtherSettings.PrintPlayersChoiceInChat)
                            {
                                PrintToServerChat("player.choice", player.PlayerName, option.Text);
                            }
                            else
                            {
                                PrintToPlayerChat(player, "player.choice", player.PlayerName, option.Text);
                            }
                        }
                        MenuManager.CloseActiveMenu(player);
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during voting menu preparation: {ex.Message}");
                Logger.LogError($"An error occurred during voting menu preparation: {ex.Message}");
            }
        }
        
        if (!wasdmenu && GlobalChatMenu != null)
        {
            GlobalChatMenu.PostSelectAction = PostSelectAction.Close;   
        }
        
        var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
        bool playSound = !string.IsNullOrEmpty(Config.OtherSettings.VoteStartSound);

        foreach (var player in playerEntities)
        {
            if (wasdmenu)
                wASDMenu.manager.OpenMainMenu(player, GlobalWASDMenu);
            else
                MenuManager.OpenChatMenu(player, GlobalChatMenu!);
            if (playSound)
                player.ExecuteClientCommand("play " + Config.OtherSettings.VoteStartSound);
        }

        AddTimer((float)timeToMapVote, () => TimerVoteMap(changeTime), TimerFlags.STOP_ON_MAPCHANGE);
        Logger.LogInformation($"TimerVoteMap started to trigger in {timeToMapVote}");
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult OnChat(EventPlayerChat @event, GameEventInfo info)
    {
        var player = Utilities.GetPlayerFromUserid(@event.Userid);
        if (player == null || !IsValidPlayer(player) || !canVote)
            return HookResult.Continue;

        if (@event.Text.Trim() == "rtv" || @event.Text.Trim() == "RTV")
        {
            RtvCommand(player, null!);
        }
        else if (@event.Text.StartsWith("nominate"))
        {
            Nominate(player, @event.Text);
        }
        else if (@event.Text.StartsWith("nextmap"))
        {
            PrintNextMap(player, null!);
        }
        return HookResult.Continue;
    }

    [ConsoleCommand("nominate", "Nominate")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void NominateCommand(CCSPlayerController caller, CommandInfo command)
    {
        if (IsValidPlayer(caller))
        {
            Nominate(caller, command.GetCommandString);
        }
    }
    private void Nominate(CCSPlayerController player, string command)
    {
        if (IsValidPlayer(player) && canVote)
        {
            if (Config.VoteSettings.AllowNominate)
            {
                if (_roundEndMap != null && _roundEndMap.Length > 0)
                {
                    player.PrintToChat(Localizer["no.nomination"]);
                    return;
                }
                if (nominatedMaps.Count < Config.VoteSettings.MapsInVote)
                {
                    string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length > 1)
                    {
                        TryNominate (player, words[1]);
                    }
                    else
                    {
                        Logger.LogInformation($"{player.PlayerName} wants to nominate map");
                        GGMCNominationMenu (player);
        //                if (GGMCNominationMenu (player) == Nominations.NotNow)
        //                    PrintToPlayerChat(player, "not.now");
                    }
                }
                else
                {
                    PrintToPlayerChat(player, "nominated.enough");
                }
            }
            else
            {
                PrintToPlayerChat(player, "nominate.notallowed");
            }
        }
        else
        {
            PrintToPlayerChat(player, "no.nomination");
        }
    }
    
    [ConsoleCommand("rtv", "Roke the vote")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void RtvCommand(CCSPlayerController caller, CommandInfo command)
    {
        if (!IsValidPlayer(caller))
        {
            Console.WriteLine("Invalid rtv caller");
            return;
        }
        if (Config.RTVSettings.AllowRTV)
        {
            if (rtv_can_start > 0) //Работает таймер задержки
            {
                caller.PrintToChat(Localizer["nortv.time", rtv_can_start]);
                return;
            }
            if (_roundEndMap != null && _roundEndMap.Length > 0)
            {
                caller.PrintToChat(Localizer["nortv.now"]);
                return;
            }
            if (players[caller.Slot] != null)
            {
                if (players[caller.Slot].VotedRtv)
                {
                    Console.WriteLine($"Player {caller.PlayerName} {caller.Slot} already voted");
                    caller.PrintToChat(Localizer["already.rtv", rtv_need_more]);
                }
                else
                {
                    if (canRtv)
                    {
                        Logger.LogInformation($"{caller.PlayerName} voted rtv");
                        players[caller.Slot].VotedRtv = true;
                        if (IsRTVThreshold())  // если достаточно голосов - запускаем голосование
                        {
                            StartRTV();
                            return;
                        }
                        var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
                        if (playerEntities != null && playerEntities.Any())
                        {
                            foreach (var playerController in playerEntities)
                            {
                                PrintToPlayerChat(playerController, "rtv.entered", caller.PlayerName);
                                if (!players[playerController.Slot].SeenRtv)
                                {
                                    players[playerController.Slot].SeenRtv = true;
                                    PrintToPlayerChat(playerController, "rtv.info");
                                }
                                PrintToPlayerChat(playerController, "more.required", rtv_need_more);
                            }
                        }
                    }
                    else
                    {
                        caller.PrintToChat(Localizer["already.rtv", rtv_need_more]);
                    }
                }
            }
        }
        else
        {
            caller.PrintToChat(Localizer["nortv.allowed"]);
        }
    }

    [ConsoleCommand("setnextmap", "Set Next Map")]
    [RequiresPermissions("@css/changemap")]
    public void SetNextMapCommand(CCSPlayerController caller, CommandInfo command)
    {
        if (!string.IsNullOrEmpty(command.ArgString))
        {
            string playerName = "";
            if (IsValidPlayer(caller))
            {
                playerName = caller.PlayerName;
            }
            if (string.IsNullOrEmpty(playerName))
            {
                Logger.LogInformation($"set next map requested for {command.ArgString}");
            }
            else
            {
                Logger.LogInformation($"{playerName} requested to set next map: {command.ArgString}");
            }
            if (Maps_from_List.ContainsKey(command.ArgString))
            {
                DoMapChange(command.ArgString, SSMC_ChangeMapTime.ChangeMapTime_MapEnd);
                Logger.LogInformation($"Next map set: {command.ArgString}");
                if (!string.IsNullOrEmpty(playerName))
                {
                    PrintToPlayerChat(caller, "nextmap.info", command.ArgString);
                }
            }
            else
            {
                Logger.LogInformation($"{command.ArgString} is not in the map list");
                if (!string.IsNullOrEmpty(playerName))
                {
                    PrintToPlayerChat(caller, "no.map", command.ArgString);
                }
            }
        }
    }

    [ConsoleCommand("revote", "Revote")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void RevoteCommand(CCSPlayerController caller, CommandInfo command)
    {
        if (!IsValidPlayer(caller))
        {
            Console.WriteLine("Invalid rtv caller");
            return;
        }

        if (!IsVoteInProgress)
        {
            caller.PrintToChat(Localizer["noactive.vote"]);
            return;
        }
        if (votePlayers.TryGetValue(caller.Slot, out string? voteResult))
        {
            if (voteResult != null && optionCounts.TryGetValue(voteResult, out int count))
            {
                optionCounts[voteResult] = count - 1;
                votePlayers.Remove(caller.Slot);
            }
            else
            {
                Logger.LogError($"Should be recorded vote for {caller.PlayerName} but it is absent.");
            }
        }
        if (GlobalChatMenu != null)
        {
            MenuManager.OpenChatMenu(caller, GlobalChatMenu);
        }
        else if (GlobalWASDMenu != null)
        {
//            var manager = GetMenuManager();
            wASDMenu.manager.OpenMainMenu(caller, GlobalWASDMenu);
        }
        else
        {
            Logger.LogError("Both GlobalWASDMenu and GlobalChatMenu are null but shouln't be");
            caller.PrintToChat(Localizer["cant.revote"]);
        }
    }

    [ConsoleCommand("nextmap", "NextMap")]
    public void PrintNextMap(CCSPlayerController caller, CommandInfo command)
    {
        Logger.LogInformation($"Nextmap {(_roundEndMap != null ? _roundEndMap : " _roundEndMap null")}");
        if (IsValidPlayer(caller))
        {
            if (_roundEndMap != null && _roundEndMap.Length > 0)
            {
                if (Config.OtherSettings.PrintNextMapForAll)
                {
                    PrintToServerChat("nextmap.info", _roundEndMap);
                }
                else
                {
                    PrintToPlayerChat(caller, "nextmap.info", _roundEndMap);
                }
            }
            else
            {
                PrintToPlayerChat(caller, "nextmap.none");
            }
        }
        else
        {
            if (_roundEndMap != null && _roundEndMap.Length > 0)
                Console.WriteLine($"Next Map: {_roundEndMap}");
            else
                Console.WriteLine($"No Next Map yet.");
        }
    }
    
    [ConsoleCommand("css_maps", "Change maps menu.")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
	[RequiresPermissions("@css/changemap")]
    public void MapMenuCommand(CCSPlayerController caller, CommandInfo command)
    {
        if (!IsValidPlayer(caller))
        {
            return;
        }
        if (IsVoteInProgress)
        {
            caller.PrintToChat(Localizer["vote.inprogress"]);
            return;
        }
/*        var manager = GetMenuManager();
        if(manager == null)
            return; */
        IWasdMenu menu = wASDMenu.manager.CreateMenu(Localizer["maps.menu"]);
        menu.Add(Localizer["change.map"], AdminChangeMapHandle); // Simply change the map
        menu.Add(Localizer["votefor.map"], AdminStartVotesMapHandle); // Start voting for map
        menu.Add(Localizer["vote.changeornot"], VotesForChangeMapHandle); // Start voting to change map or not
        menu.Add(Localizer["set.nextmap"], AdminSetNextMapHandle); // Choose and set next map
        wASDMenu.manager.OpenMainMenu(caller, menu);

/*        var MapsMenu = new ChatMenu(Localizer["maps.menu"]); 
        MapsMenu.AddMenuOption(Localizer["change.map"], AdminChangeMapHandle); // Simply change the map
        MapsMenu.AddMenuOption(Localizer["votefor.map"], AdminStartVotesMapHandle); // Start voting for map
        MapsMenu.AddMenuOption(Localizer["vote.changeornot"], VotesForChangeMapHandle); // Start voting to change map or not
//        MapsMenu.PostSelectAction = PostSelectAction.Close;
        MenuManager.OpenChatMenu(caller, MapsMenu); */
    }

    [ConsoleCommand("ggmc_mapvote_start", "Start map vote.")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void MapVoteCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (IsVoteInProgress)
        {
            Logger.LogInformation("MapVoteCommand: Another vote active when ggmc_mapvote_start. Skip votes");
            return;
        }
        IsVoteInProgress = true;
        Logger.LogInformation("MapVoteCommand: ggmc_mapvote_start votes");
        timeToVote = Config.VoteSettings.VotingTime;
        VotesCounter = 0;
        if (command != null && command.ArgCount > 1)
        {
            if (int.TryParse(command.ArgByIndex(1), out int intValue) && intValue > 0 && intValue < 59)
            {
                timeToVote = intValue;
            }
        }
        if (voteTimer == null)
        {
            voteTimer = AddTimer(1.0f, EndOfVotes, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            DoAutoMapVote(null!, timeToVote, SSMC_ChangeMapTime.ChangeMapTime_MapEnd, Config.VoteSettings.EndMapVoteWASDMenu);
            Logger.LogInformation("Vote Timer started at MapVoteCommand");
        }
        else
        {
            IsVoteInProgress = false;
            Logger.LogInformation("Vote Timer already works, we don't start new Vote at MapVoteCommand");
        }
    }
    [ConsoleCommand("ggmc_mapvote_with_change", "Start map vote and change after the result.")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void MapVoteChangeCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (IsVoteInProgress)
        {
            Logger.LogInformation("MapVoteCommand: Another vote active when ggmc_mapvote_with_change. Skip votes");
            return;
        }
        IsVoteInProgress = true;
        Logger.LogInformation("MapVoteChangeCommand: ggmc_mapvote_with_change start votes");
        timeToVote = Config.VoteSettings.VotingTime;
        VotesCounter = 0;
        if (command != null && command.ArgCount > 1)
        {
            if (int.TryParse(command.ArgByIndex(1), out int intValue) && intValue > 0 && intValue < 59)
            {
                timeToVote = intValue;
            }
        }
        if (voteTimer == null)
        {
            voteTimer = AddTimer(1.0f, EndOfVotes, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            DoAutoMapVote(null!, timeToVote, SSMC_ChangeMapTime.ChangeMapTime_Now, Config.VoteSettings.EndMapVoteWASDMenu);
            Logger.LogInformation("Vote Timer started at MapVoteChangeCommand");
        }
        else
        {
            IsVoteInProgress = false;
            Logger.LogInformation("Vote Timer already works, we don't start new Vote at MapVoteChangeCommand");
        }
    }
    [ConsoleCommand("ggmc_auto_mapchange", "Automatically change the map to a random map.")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void AutoMapChangeCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (IsVoteInProgress)
        {
            Logger.LogInformation("MapVoteCommand: Another vote active when ggmc_auto_mapchange. Skip change");
            return;
        }
        GGMCDoAutoMapChange();
    }
    [ConsoleCommand("ggmc_change_nextmap", "Change the map to a voted map or set next level.")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void ChangeNextMapCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (IsVoteInProgress)
        {
            Logger.LogInformation("MapVoteCommand: Another vote active when ggmc_auto_mapchange. Skip change");
            return;
        }
        if (string.IsNullOrEmpty(_roundEndMap))
        {
            GGMCDoAutoMapChange();
        }
        else
        {
            DoMapChange(_roundEndMap);
        }
    }
    [ConsoleCommand("ggmc_nortv", "Turn off rtv")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void NoRtvCommand(CCSPlayerController? caller, CommandInfo command) // turn off rtv feature
    {
        canRtv = false;
    }

    [ConsoleCommand("ggmap", "Change map")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/changemap")]
    public void QuickChangeMapCommand(CCSPlayerController caller, CommandInfo command)
    {
        if (!IsValidPlayer(caller))
            return;
        if (command == null || command.ArgCount < 1)
        {
            caller.PrintToChat(Localizer["ggmap.usage"]);
            return;
        }
        string mapguess = command.ArgString;
        if (maplist.Contains(mapguess))
        {
            DoMapChange(mapguess, SSMC_ChangeMapTime.ChangeMapTime_Now);
        }
        else
        {        
            string [] mapnames = FindSimilarMaps(mapguess, maplist);
            if (mapnames.Length == 0)
            {
                caller.PrintToChat(Localizer["ggmap.nomaps"]);
            }
            else if (mapnames.Length == 1)
            {
                caller.PrintToChat(Localizer["ggmap.change"]);
                DoMapChange(mapnames[0], SSMC_ChangeMapTime.ChangeMapTime_Now);
            }
            else
            {
                caller.PrintToChat(string.Join(" ", mapnames));
            }
        }
    }
    [ConsoleCommand("reloadmaps", "Reload maps")]
	[RequiresPermissions("@css/changemap")]
    public void ReloadMapsCommand(CCSPlayerController caller, CommandInfo command)
    {
        canVote = ReloadMapcycle();
        if (IsValidPlayer(caller))
        {
            if (canVote)
            {
                caller.PrintToChat("Map file reloaded");
            }
            else
            {
                caller.PrintToChat("Map file reloaded but vote is not allowed. See logs");
            }
        }
        else
        {
            if (canVote)
            {
                Console.WriteLine("Map file reloaded");
            }
            else
            {
                Console.WriteLine("Map file reloaded but vote is not allowed. See logs");
            }
        }
    }
    private static string RemovePrefixesAndEndings(string mapName)
    {
        // Define the prefixes and endings to remove
        string[] prefixes = { "aim_", "ar_", "cs_", "de_", "dm_", "fy_", "gg_" };
        string[] endings = { "_gg1", "_csgo" };

        // Remove prefixes
        foreach (string prefix in prefixes)
        {
            if (mapName.StartsWith(prefix))
            {
                mapName = mapName.Substring(prefix.Length);
                break;
            }
        }

        // Remove endings
        foreach (string ending in endings)
        {
            if (mapName.EndsWith(ending))
            {
                mapName = mapName.Substring(0, mapName.Length - ending.Length);
                break;
            }
        }

        return mapName;
    }
    private static double DiceCoefficient(string s1, string s2)
    {
        HashSet<char> set1 = new HashSet<char>(s1);
        HashSet<char> set2 = new HashSet<char>(s2);

        int intersection = 2 * set1.Intersect(set2).Count();
        int union = set1.Count + set2.Count;

        return (double)intersection / union;
    }
    private static string[] FindSimilarMaps(string input, List<string> mapNames)
    {
        input = RemovePrefixesAndEndings(input);

        // First filter: Use Dice Coefficient to find maps with a coefficient greater than 0.79
        List<string> potentialMatches = mapNames
            .Where(map => DiceCoefficient(input, RemovePrefixesAndEndings(map)) > 0.79)
            .ToList();

        // Second filter: Add maps that contain the search string if not more than 4
        int maxContainingMaps = 4;

        List<string> containingMatches = mapNames
            .Where(map => map.Contains(input) && !potentialMatches.Contains(map))
            .Take(maxContainingMaps)
            .ToList();

        // Combine the results from both filters
        List<string> result = potentialMatches.Concat(containingMatches).ToList();

        return result.ToArray();
    }
    private void EndOfVotes()
    {
        if ( ++VotesCounter < timeToVote )
        {   
/*            if ( VotesCounter < 5)
            {
                PlaySound(null, Config.VoteEndSound);
            } */
            var seconds = timeToVote - VotesCounter;
            if (seconds > 0 && seconds < 6)
            {
                var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
                if (playerEntities != null && playerEntities.Any())
                {
                    foreach (var playerController in playerEntities)
                    {
                        PrintToPlayerCenter(playerController, "timetovote.left", seconds);
                    }
                }
            }
            return;
        }
        if (voteTimer != null)
        {
            timeManager.EnqueueOperation(async () => 
            {
                bool success = await KillTimer(voteTimer);
                if (success)
                {
                    voteTimer = null;
                }
            });
        }
    }
    private void TryNominate(CCSPlayerController player, string map)
    {
        if (nominatedMaps.Contains(map))
        {
            PrintToPlayerChat(player, "nominated.already");
        }
        else
        {
            Logger.LogInformation($"{player.PlayerName} wants to nominate {map}");
            switch (GGMC_Nominate(map, player))
            {
                case Nominations.Nominated:
                    PrintToPlayerChat(player, "map.nominated", map);
                    break;

                case Nominations.AlreadyNominated:
                    PrintToPlayerChat(player, "already.nominated");
                    break; 
                case Nominations.NoMap:
                    PrintToPlayerChat(player, "no.map", map);
                    break;

                case Nominations.NotNow:
                    PrintToPlayerChat(player, "not.now");
                    break;

                case Nominations.Error:
                    PrintToPlayerChat(player, "nomination.error");
                    break; 
            } 
        }
    }
    private void GGMCNominationMenu (CCSPlayerController player)
    {
        if (IsValidPlayer(player))
        {
            if (Config.VoteSettings.NominationsWASDMenu)
            {
                IWasdMenu nominate_menu = CreateMapsMenuWASD(Handle_Nominations, player); 
                if (nominate_menu != null)
                    wASDMenu.manager.OpenMainMenu(player, nominate_menu);
            }
            else
            {
                ChatMenu chatMenu = CreateMapsMenu(Handle_NominationsChat, player);
                if (chatMenu != null)
                {
                    MenuManager.OpenChatMenu(player, chatMenu);
                }
            }
        }
    }
    private void Handle_Nominations(CCSPlayerController player, IWasdMenuOption option)
    {
        if(!IsValidPlayer(player) || option == null || option.OptionDisplay == null)
            return;
        
        TryNominate (player, GetMapKeyByDisplayNameOrKey(option.OptionDisplay));
        
        wASDMenu.manager.CloseMenu(player);
    }
    private void Handle_NominationsChat(CCSPlayerController player, ChatMenuOption option)
    {
        if(!IsValidPlayer(player) || option == null || option.Text == null)
            return;
        
        TryNominate (player, GetMapKeyByDisplayNameOrKey(option.Text));
        MenuManager.CloseActiveMenu(player);
    }
    private Nominations GGMC_Nominate(string map, CCSPlayerController player)
    {
        if (IsNominated(map))
            return Nominations.Nominated;

        int numplayers = GetRealClientCount(false);
        if (numplayers == 0) numplayers = 1;
        bool can_be_nominated = false, found = false;
        foreach (var mapcheck in Maps_from_List)
        {
            if (mapcheck.Key == map)
            {
                found = true;
                if ((mapcheck.Value.MinPlayers != 0 && numplayers < mapcheck.Value.MinPlayers) || (mapcheck.Value.MaxPlayers != 0 && numplayers > mapcheck.Value.MaxPlayers) || _playedMaps.Contains(mapcheck.Key.ToLower()))
                {
                    break;
                }
                can_be_nominated = true;
                break;
            }
        }
        if (!found) return Nominations.NoMap;
        if (!can_be_nominated) return Nominations.NotNow;
        nominatedMaps.Add(map);
        if (players[player.Slot].HasProposedMaps())
            nominatedMaps.Remove(players[player.Slot].ProposedMaps);
        players[player.Slot].ProposedMaps = map;
        PrintToServerChat("player.nominated", player.PlayerName, map);
        return Nominations.Nominated;
    }
    private bool IsNominated(string map)
    {
        return nominatedMaps.Contains(map);
    }
    private void TimerVoteMap(SSMC_ChangeMapTime changeTime = SSMC_ChangeMapTime.ChangeMapTime_Now)
    {
        GlobalChatMenu = null;
        GlobalWASDMenu = null;
        if (Config.VoteSettings.ChangeMapAfterVote)
        {
            Logger.LogInformation($"Vote has been done. Start change map timer after the vote in {Config.OtherSettings.DelayBeforeChangeSeconds}");
            timeManager.EnqueueOperation(async () => 
            {
                await KillTimer(voteEndChange);

                Server.NextFrame(() => 
                {
                    voteEndChange = AddTimer(Config.OtherSettings.DelayBeforeChangeSeconds, () =>
                    {
                        voteEndChange = null;
                        if (!string.IsNullOrEmpty(_roundEndMap) && _roundEndMap != "extend.map")
                        {
                            Logger.LogInformation($"TimerVoteMap: ChangeMapInFive - {_roundEndMap}");
                            ChangeMapInFive(_roundEndMap);
                        }
                        else
                        {
                            Logger.LogInformation($"TimerVoteMap: Can't call ChangeMapInFive");
                        }
                    }, TimerFlags.STOP_ON_MAPCHANGE);
                });
            });
        }
        if (mapsToVote.Count > 0)
        {
            var random = Random.Shared;
            if (_votedMap == 0 || optionCounts.Count == 0)
            {
                _selectedMap = mapsToVote[random.Next(mapsToVote.Count)];
                PrintToServerChat("randommap.selected", _selectedMap);
                Logger.LogInformation($"TimerVoteMap: selected random map {_selectedMap}");
            }
            else
            {
                // Find the maximum number of votes
                int maxVotes = optionCounts.Values.Max();

                // Get all maps with the maximum number of votes
                var winningMaps = optionCounts.Where(kv => kv.Value == maxVotes).Select(kv => kv.Key).ToList();

                // Choose a random map from the winning maps
                if (winningMaps.Count == 1)
                {
                    _selectedMap = winningMaps[0];
                }
                else if (winningMaps.Count > 1)
                {
                    var rand = random.Next(0, winningMaps.Count-1);
                    _selectedMap = winningMaps[rand];
                }
                else
                {
                    Logger.LogInformation($"winningMaps.Count {winningMaps.Count}, something wrong");
                }
                Logger.LogInformation($"[Selected map] {_selectedMap}");
            }
            if (_selectedMap != null )
            {
                if (Config.DiscordSettings.DiscordWebhook != "" && Config.DiscordSettings.DiscordMessageAfterVote)
                {
                    _ = webhookService.SendWebhookMessage(_selectedMap, GetDisplayName(_selectedMap));
                }
                
                IsVoteInProgress = false;
                _roundEndMap = _selectedMap;
                DoMapChange(_selectedMap, changeTime);
                Logger.LogInformation("DoMapChange finished");
/*                try
                {
                    DoMapChange(_selectedMap, changeTime);
                }
                catch (Exception ex)
                {
                    Server.NextFrame( () => {Logger.LogError($"Failed to do MapChange: {ex.Message}");});
                } */

                return;
            }
            Logger.LogInformation($"[TimerVoteMap] no selected map ");
        }
        else
        {
            Logger.LogInformation("TimerVoteMap: mapsToVote.Count <= 0");
        }
        IsVoteInProgress = false;
        GGMCDoAutoMapChange(changeTime);
    }
    private void ResetData(string Reason)
    {
        Logger.LogInformation($"ResetData from {Reason}");
        IsVoteInProgress = false;
        nominatedMaps.Clear();
        mapsToVote.Clear();
        _selectedMap = null;
        _votedMap = 0;
        optionCounts = new Dictionary<string, int>(0);
        canRtv = true;
        CleanRTVArrays();
        rtv_can_start = 0;
        rtvRestartProblems = 0;
        roundsManager.InitialiseMap();
        MapIsChanging = false;
        GlobalChatMenu = null;
        GlobalWASDMenu = null;
        _timeLimitVoteRoundStart = false;
    }
    private static bool IsValidPlayer (CCSPlayerController? p)
    {
        if (p != null && p.IsValid && p.SteamID.ToString().Length == 17 && 
                p.Connected == PlayerConnectedState.PlayerConnected && !p.IsBot && !p.IsHLTV)
        {
            return true;
        }
        return false;
    }
    private static int GetRealClientCount(bool inGameOnly=true)
    {
        int clients = 0;
        var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
        if (playerEntities != null && playerEntities.Any())
        {
            foreach (var playerController in playerEntities)
            {
                if (inGameOnly && playerController.Team != CsTeam.CounterTerrorist && playerController.Team != CsTeam.Terrorist)
                {
                    continue;
                }
                clients++;
            }
        }
        return clients;
    }
    private void MakeRTVTimer (int interval)  // таймер для того, чтобы если кто-то напишет rtv, ему написали, через сколько секунд можно
    {
        if (interval > 0)
        {
            canRtv = false;
            rtv_can_start = interval;
            if (rtvTimer != null)
            {
                Server.NextFrame ( () => {
                    rtvTimer.Kill();
                    rtvTimer = null;
//                    Logger.LogError("rtvTimer is not null but should be");
                    rtvTimer= AddTimer(1.0f, Handle_RTVTimer, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
                });
            }
            else
            {
                rtvTimer= AddTimer(1.0f, Handle_RTVTimer, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            }
        }
    }
    private void Handle_RTVTimer()
    {
        //Continue ticking if there is still time left on the counter.
        if (--rtv_can_start >= 0)
        {
//            Console.WriteLine($"rtv timer {rtv_can_start}");
            return;
        }
        if (canVote) canRtv = true;
        timeManager.EnqueueOperation(async () => 
        {
            if (await KillTimer(rtvTimer))
            {
                rtvTimer = null;
                Logger.LogInformation("RTV timer killed in Handle_RTVTimer");
            }
        });
        timeManager.EnqueueOperation(async () => 
        {
            if (await KillTimer(voteTimer))
            {
                voteTimer = null;
            }
        });
        return;
    }
    private bool IsRTVThreshold(bool log = true)
    {
        int total = 0;
        int rtvs = 0;
        var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
        if (playerEntities != null && playerEntities.Any())
        {
            foreach (var player in playerEntities)
            {
                if (players[player.Slot] != null)
                {
                    if (players[player.Slot].VotedRtv)
                    {
                        rtvs++;
                        total++;
                    }
                    else
                    {
                        if (player.Team == CsTeam.CounterTerrorist 
                            || player.Team == CsTeam.Terrorist)
                        {
                            total++;
                        }
                    }
                }
            }
        }

        if (total > 0)
        {
            double percent_now = (double)rtvs / total;
            
            rtv_need_more = (int)Math.Ceiling((Config.VoteSettings.VotesToWin - percent_now) * total);
            
            
            if ((total == 1 && rtvs == 1) || rtv_need_more <= 0)
            {
                Logger.LogInformation($"Successful rtv %: {percent_now}, {rtvs} out of {total}");
                return true;
            } 
            else
            {
                if (log)
                    Logger.LogInformation($"Not enough for rtv: % - {percent_now}, {rtvs} out of {total}, {rtv_need_more} need more");
            }
        }
        return false;
    }
    private void StartRTV()
    {
        CleanRTVArrays();
        Logger.LogInformation("Starting RTV vote");
        if (IsVoteInProgress)
        {
            if (++rtvRestartProblems < 6)
            {
                AddTimer(10.0f, StartRTV, TimerFlags.STOP_ON_MAPCHANGE); 
                Logger.LogInformation("Starting RTV: another vote in progress, waiting for 10 seconds more.");
            }
            else
            {
                Logger.LogError("Starting RTV: another vote in progress, waited a minute and stop rtv.");
            }
            return;
        }
        IsVoteInProgress = true;
        MakeRTVTimer(Config.RTVSettings.IntervalBetweenRTV);
        DoAutoMapVote(null!, Config.VoteSettings.VotingTime, SSMC_ChangeMapTime.ChangeMapTime_Now, Config.VoteSettings.EndMapVoteWASDMenu );
    }
    private void CleanRTVArrays()
    {
        foreach (var player in players)
        {
            if (player != null)
            {
                player.ProposedMaps = "";
                player.VotedRtv = false;
                player.SeenRtv = false;
            }
        }
    }
    private void PrintToPlayerCenter(CCSPlayerController player, string message, params object[] arguments)
    {
        if (IsValidPlayer(player))
        {
            string text;
            using (new WithTemporaryCulture(player.GetLanguage()))
            {
                text = _localizer[message, arguments];
            }
            player.PrintToCenter(text);
        }
    }
    private void PrintToPlayerChat(CCSPlayerController player, string message, params object[] arguments)
    {
        if (IsValidPlayer(player))
        {
            string text;
            using (new WithTemporaryCulture(player.GetLanguage()))
            {
                text = _localizer[message, arguments];
            }
            player.PrintToChat(text);
        }
    }
    private void PrintToServerCenter(string message, params object[] arguments)
    {
        var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
        if (playerEntities != null && playerEntities.Any())
        {
            foreach (var player in playerEntities)
            {
                PrintToPlayerCenter(player, message, arguments);
            }
        }
    }
    private void PrintToServerChat(string message, params object[] arguments)
    {
        var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
        if (playerEntities != null && playerEntities.Any())
        {
            foreach (var player in playerEntities)
            {
                PrintToPlayerChat(player, message, arguments);
            }
        }
    }
    private void PrintToServerCenterHTML(string message, params object[] arguments)
    {
        var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
        if (playerEntities != null && playerEntities.Any())
        {
            foreach (var player in playerEntities)
            {
                PrintToPlayerCenterHTML(player, message, arguments);
            }
        }
    }
    private void PrintToPlayerCenterHTML(CCSPlayerController player, string message, params object[] arguments)
    {
        if (IsValidPlayer(player))
        {
            string text;
            using (new WithTemporaryCulture(player.GetLanguage()))
            {
                text = _localizer[message, arguments];
            }
            player.PrintToCenterHtml(text);
        }
    }
    private string ClearSuffix (string mapName)
    {
        string suffix = " (!)";
        string map;
        if (mapName.EndsWith(suffix))
        {
            map = mapName.Substring(0, mapName.Length - suffix.Length);
        }
        else
        {
            map = mapName;
        }
        return GetMapKeyByDisplayNameOrKey(map);
    }
    private string GetMapKeyByDisplayNameOrKey(string searchString)
    {
        // Check if searchString is a display name
        if (DisplayNameToKeyMap.TryGetValue(searchString, out var key))
        {
            return key;
        }

        // Check if searchString is a key
        if (Maps_from_List.ContainsKey(searchString))
        {
            return searchString;
        }

        // If no match found, return null
        Logger.LogError($"Tried to search {searchString} in dictionaries and can't find");
        return searchString;
    }
    private string GetDisplayName(string searchString)
    {
        if (Maps_from_List.TryGetValue(searchString, out MapInfo? mapInfo))
        {
            if (mapInfo != null)
            {
                if (!string.IsNullOrEmpty(mapInfo.Display))
                {
                    return mapInfo.Display;
                }
                else
                {
                    return searchString;
                }
            }
            else
            {
                Logger.LogError($"mapInfo null for {searchString} in Maps_from_List");
                return searchString;
            }
        }
        else
        {
            Logger.LogError($"Can't find {searchString} in Maps_from_List");
        }
        return searchString;
    }
    private void StartOrRestartTimeLimitTimer(float duration, Action callback)
    {
        lock (_timerLock)
        {
            if (_timeLimitTimer != null)
            {
                timeManager.EnqueueOperation(async () => 
                {
                    bool success = await KillTimer(_timeLimitTimer);
                    if (success)
                    {
                        _timeLimitTimer = null; // Ensure it's null before recreating
                        _timeLimitTimer = AddTimer(duration, callback, TimerFlags.STOP_ON_MAPCHANGE);
                    }
                });
            }
            else
            {
                _timeLimitTimer = AddTimer(duration, callback, TimerFlags.STOP_ON_MAPCHANGE);
            }
        }
    }
    public async Task<bool> KillTimer(Timer? timerToKill)
    {
        return await Task.Run(() =>
        {
            try
            {
                Server.NextFrame(() =>
                {
                    if (timerToKill != null)
                    {
                        timerToKill.Kill();
                    }
                });
                return true; // Return true if the timer was successfully killed
            }
            catch (Exception ex)
            {
                Server.NextFrame(() => { Logger.LogError($"Failed to kill the timer: {ex.Message}"); });
                return false; // Return false if there was an error
            }
        });
    }
    public enum Nominations
    {
        Nominated = 0,
        AlreadyNominated,
        NoMap,
        NotNow,
        Error
    };
    
    public async Task SendWebhookMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Logger.LogError("SendWebhookMessage called with empty Message");
            return;
        }

        using (var httpClient = new HttpClient())
        {
            try
            {
                var payload = new
                {
                    content = message
                };

                var jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Configure cancellation token if needed (e.g., with a timeout)
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    var response = await httpClient.PostAsync(Config.DiscordSettings.DiscordWebhook, content, cts.Token);

                    // Ensure a successful status code
                    response.EnsureSuccessStatusCode();
                }
            }
            catch (HttpRequestException ex)
            {
                // Log and handle network-related errors
                Console.WriteLine($"HttpRequestException occurred: {ex.Message}");
                // Optionally rethrow or handle the exception in a way that suits your application
                throw;
            }
            catch (TaskCanceledException ex)
            {
                // Handle request timeout scenarios
                Console.WriteLine($"Request timed out: {ex.Message}");
                // Handle accordingly, for example, retry logic or notifying the user
            }
            catch (Exception ex)
            {
                // Catch all other exceptions
                Console.WriteLine($"An error occurred: {ex.Message}");
                // You might want to rethrow or handle specific cases
                throw;
            }
        }
    }
    public void EnsureNextMapMessageFileExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            var jsonData = new
            {
                content = "Next map: ",
                embeds = new[]
                {
                    new
                    {
                        image = new
                        {
                            url = "https://example.com/folder/with/mapimages/"
                        }
                    }
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = JsonSerializer.Serialize(jsonData, options);
            File.WriteAllTextAsync(filePath, jsonString);
        }
    }
}
public class WASDMenu 
{
    public WASDMenu (MapChooser plugin)
    {
        Plugin = plugin;
        Localizer = Plugin.Localizer;
    }
    MapChooser Plugin;
    public WasdManager manager = new();  
    public static IStringLocalizer? Localizer = null;
    public static readonly Dictionary<int, WasdMenuPlayer> Players = new();
    public void Load(bool hotReload)
    {
        var wasdMenuManager = new WasdManager();

        Plugin.RegisterEventHandler<EventPlayerActivate>((@event, info) =>
        {
            if (@event.Userid != null && @event.Userid.IsValid && !@event.Userid.IsBot && !@event.Userid.IsHLTV)
                Players[@event.Userid.Slot] = new WasdMenuPlayer
                {
                    player = @event.Userid,
                    Buttons = @event.Userid.Buttons,
                    Localizer = Plugin._localizer
                };
            return HookResult.Continue;
        });
        Plugin.RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
        {
            if (@event.Userid != null) Players.Remove(@event.Userid.Slot);
            return HookResult.Continue;
        });
        
        Plugin.RegisterListener<Listeners.OnTick>(OnTick);
        
        if(hotReload)
        {
            foreach (var pl in Utilities.GetPlayers())
            {
               if (pl != null && pl.IsValid && !pl.IsBot && !pl.IsHLTV)
               {
                    Players[pl.Slot] = new WasdMenuPlayer
                    {
                        player = pl,
                        Buttons = pl.Buttons
                    };
               }
            }
        }
    }
    public void Unload(bool hotReload)
    {
        Plugin.RemoveListener<Listeners.OnTick>(OnTick);
    }

    public void OnTick()
    {
        foreach (var player in Players.Values.Where(p => p.MainMenu != null))
        {
            if ((player.Buttons & PlayerButtons.Forward) == 0 && (player.player.Buttons & PlayerButtons.Forward) != 0)
            {
                player.ScrollUp();
            }
            else if((player.Buttons & PlayerButtons.Back) == 0 && (player.player.Buttons & PlayerButtons.Back) != 0)
            {
                player.ScrollDown();
            }
            else if((player.player.TeamNum == 1) && (player.Buttons & PlayerButtons.Moveright) == 0 &&(player.player.Buttons & PlayerButtons.Moveright) != 0)
            {
                player.Choose();
            } 
            else if((player.player.TeamNum == 2 || player.player.TeamNum == 3) && (player.Buttons & PlayerButtons.Jump) == 0 &&(player.player.Buttons & PlayerButtons.Jump) != 0)
            {
                player.Choose();
            } 
            else if ((player.Buttons & PlayerButtons.Moveleft) == 0 && (player.player.Buttons & PlayerButtons.Moveleft) != 0)
            {
                player.CloseSubMenu();
            }

            if (((long)player.player.Buttons & 8589934592) == 8589934592)
            { 
                player.OpenMainMenu(null);
            }
            
            player.Buttons = player.player.Buttons;
            if(player.CenterHtml != "")
                player.player.PrintToCenterHtml(player.CenterHtml);
        }
    }

}
public class MCConfig : BasePluginConfig 
{
    [JsonPropertyName("VoteSettings")]
    public VoteSettings VoteSettings { get; set; } = new VoteSettings();

    [JsonPropertyName("RTVSettings")]
    public RTVSettings RTVSettings { get; set; } = new RTVSettings();

    [JsonPropertyName("WinDrawSettings")]
    public WinDrawSettings WinDrawSettings { get; set; } = new WinDrawSettings();

    [JsonPropertyName("TimeLimitSettings")]
    public TimeLimitSettings TimeLimitSettings { get; set; } = new TimeLimitSettings();

    [JsonPropertyName("DiscordSettings")]
    public DiscordSettings DiscordSettings { get; set; } = new DiscordSettings();

    [JsonPropertyName("OtherSettings")]
    public OtherSettings OtherSettings { get; set; } = new OtherSettings();
}
public class VoteSettings
{
    [JsonPropertyName("RememberPlayedMaps")]
    public int RememberPlayedMaps { get; set; } = 3;

    /* Number of maps in vote for players  */
    [JsonPropertyName("MapsInVote")]
    public int MapsInVote { get; set; } = 5;

    /* Percent of players to win a vote. 0.6 - 60%. Spectators without a vote do not counts*/ 
    [JsonPropertyName("VotesToWin")]
    public double VotesToWin { get; set; } = 0.6;

    [JsonPropertyName("AllowNominate")]
    public bool AllowNominate { get; set; } = true;

    /* Nominations in WASD menu */
    [JsonPropertyName("NominationsWASDMenu")]
    public bool NominationsWASDMenu { get; set; } = true;

    /* End of Map Vote in WASD menu */
    [JsonPropertyName("EndMapVoteWASDMenu")]
    public bool EndMapVoteWASDMenu { get; set; } = false;

    /* Time in seconds to wait while players make their choice */
    [JsonPropertyName("VotingTime")]
    public int VotingTime { get; set; } = 25;

    /* Add Extend Map option to vote */
    [JsonPropertyName("ExtendMapInVote")]
    public bool ExtendMapInVote { get; set; } = false;

    /* time to Extend Map */
    [JsonPropertyName("ExtendMapTimeMinutes")]
    public int ExtendMapTimeMinutes { get; set; } = 10;

    /* Plugin will Change the Map after the vote which called by external plugin. */
    [JsonPropertyName("ChangeMapAfterVote")]
    public bool ChangeMapAfterVote { get; set; } = false;
}
public class RTVSettings
{
    [JsonPropertyName("AllowRTV")]
    public bool AllowRTV { get; set; } = true;
    
    /* Time (in seconds) before first RTV can be held. */
    [JsonPropertyName("RTVDelayFromStart")]
    public int RTVDelayFromStart { get; set; } = 90;

    /* Time (in seconds) after a failed RTV before another can be held. */
    [JsonPropertyName("IntervalBetweenRTV")]
    public int IntervalBetweenRTV { get; set; } = 120;
}
public class WinDrawSettings
{
    /* Set True if Vote start depends on number of Round Wins by CT or T  */
    [JsonPropertyName("VoteDependsOnRoundWins")]
    public bool VoteDependsOnRoundWins { get; set; } = false;

    /* Number of rounds before the game end to start a vote, usually 1. 0 does not work :( */
    [JsonPropertyName("TriggerRoundsBeforeEnd")]
    public int TriggerRoundsBeforeEnd { get; set; } = 1;

    /* Vote triggered in number of rounds before the game end executed on RoundStart if true and RoundEnd if false */
    [JsonPropertyName("TriggerRoundsBeforeEndVoteAtRoundStart")]
    public bool TriggerRoundsBeforeEndVoteAtRoundStart { get; set; } = true;

    /* Seconds from Round Start to trigger the Vote */
    [JsonPropertyName("TriggerVoteAtRoundStartSecondsFromStart")]
    public int TriggerVoteAtRoundStartSecondsFromStart { get; set; } = 0;

    /* Plugin will Change to Map voted before, after the win or draw event */
    [JsonPropertyName("ChangeMapAfterWinDraw")]
    public bool ChangeMapAfterWinDraw { get; set; } = false;
}
public class TimeLimitSettings
{
    /* Set True if Vote start depends on Game Time Limit (cvar: mp_timelimit)  */
    [JsonPropertyName("VoteDependsOnTimeLimit")]
    public bool VoteDependsOnTimeLimit { get; set; } = false;
    
    /* Number of seconds before the end to run the vote */
    [JsonPropertyName("TriggerSecondsBeforeEnd")]
    public int TriggerSecondsBeforeEnd { get; set; } = 35;

    /* Plugin will Change the Map after the Time Limit.  */
    [JsonPropertyName("ChangeMapAfterTimeLimit")]
    public bool ChangeMapAfterTimeLimit { get; set; } = false;

    /* Start the vote in the next round after the trigger happened*/
    [JsonPropertyName("VoteNextRoundStartAfterTrigger")]
    public bool VoteNextRoundStartAfterTrigger { get; set; } = false;
}   
public class DiscordSettings
{  
    /* Discord webhook link to logging map change*/
    [JsonPropertyName("DiscordWebhook")]
    public string DiscordWebhook { get; set; } = "";

    /* Send message at map start*/
    [JsonPropertyName("DiscordMessageMapStart")]
    public bool DiscordMessageMapStart { get; set; } = true;

    /* Send message after Vote */
    [JsonPropertyName("DiscordMessageAfterVote")]
    public bool DiscordMessageAfterVote { get; set; } = false;
}
public class OtherSettings
{    
    /* Print Player's choice to other players in Chat.  */
    [JsonPropertyName("PrintPlayersChoiceInChat")]
    public bool PrintPlayersChoiceInChat { get; set; } = true;
    /* Print NextMap command result for all players.  */
    [JsonPropertyName("PrintNextMapForAll")]
    public bool PrintNextMapForAll { get; set; } = false;

    /* Delay before Plugin will Change the Map after the events: Win/Draw event (ChangeMapAfterWinDraw); Vote ended (ChangeMapAfterVote) */
    [JsonPropertyName("DelayBeforeChangeSeconds")]
    public int DelayBeforeChangeSeconds { get; set; } = 20;

    /* Sound when vote start */
    [JsonPropertyName("VoteStartSound")]
    public string VoteStartSound { get; set; } = "sounds/vo/announcer/cs2_classic/felix_broken_fang_pick_1_map_tk01.wav";

    /* Change map to random after the server restart */
    [JsonPropertyName("RandomMapOnStart")]
    public bool RandomMapOnStart { get; set; } = true;

    /* Delay in seconds before random map load after the server restart */
    [JsonPropertyName("RandomMapOnStartDelay")]
    public int RandomMapOnStartDelay { get; set; } = 40;

    /* Change map to random after the last player disconnected */
    [JsonPropertyName("LastDisconnectedChangeMap")]
    public bool LastDisconnectedChangeMap { get; set; } = true;
    
    /* Check if problems with Workshop map  in collection (if it doesn't exists, server default map will be loaded, so plugin change to a random map) */
    [JsonPropertyName("WorkshopMapProblemCheck")]
    public bool WorkshopMapProblemCheck { get; set; } = true;
}
public enum SSMC_ChangeMapTime
{
    ChangeMapTime_Now = 0,
    ChangeMapTime_RoundEnd,
    ChangeMapTime_MapEnd,
};
public class MapInfo
{
    [JsonPropertyName("ws")]
    public bool WS { get; set; } = false;
    [JsonPropertyName("display")]
    public string Display { get; set; } = "";
    [JsonPropertyName("mapid")]
    public string MapId { get; init; } = "";
    [JsonPropertyName("minplayers")]
    public int MinPlayers { get; set; } = 0;
    [JsonPropertyName("maxplayers")]
    public int MaxPlayers { get; set; } = 0;
    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 1;
}
public class Player
{
    public Player ()
    {
        ProposedMaps = "";
        VotedRtv = false;
        SeenRtv = false;
        selectedMaps = [];
        putInServer = false;
    }
    public bool putInServer { get; set; } = false;
    public string ProposedMaps { get; set; } = "";
    public bool VotedRtv { get; set; } = false;
    public bool SeenRtv { get; set; } = false;
    public List<string> selectedMaps { get; set; } = [];
    public bool HasProposedMaps()
    {
        return !string.IsNullOrEmpty(ProposedMaps);
    }
}
public class MaxRoundsManager
{
    public MaxRoundsManager (MapChooser plugin)
    {
        Plugin = plugin;
        MaxRoundsValue = -1;
        CanClinch = true;
    }
    MapChooser Plugin;
    private int CTWins = 0;
    private int TWins = 0;
    public bool MaxRoundsVoted = false;
    public int MaxRoundsValue;
    public bool CanClinch;
    public bool UnlimitedRounds => MaxRoundsValue <= 0;
    public bool LastBeforeHalf = false;
    public bool WarmupRunning
    {
        get
        {
            CCSGameRules GameRules = null!;
            try
            {
                GameRules = GetGameRules();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Can't GetGameRules: {ex}.");
                return false;
            }
            return GameRules?.WarmupPeriod ?? false;
        }
    }
    public void InitialiseMap()
    {
        UpdateMaxRoundsValue();

        var CvarCanClinch = ConVar.Find("mp_match_can_clinch");
        if (CvarCanClinch != null)
        {
            CanClinch = CvarCanClinch.GetPrimitiveValue<bool>();
//            Plugin.Logger.LogInformation($"On Initialise Map set: CanClinch {(CanClinch ? "true" : "false")}");
        }
        else
        {
            Plugin.Logger.LogInformation($"On Initialise Map cant set: CanClinch because CvarCanClinch is null");
        }
        ClearRounds();
        MaxRoundsVoted = false;
    }
    public void UpdateMaxRoundsValue()
    {
        try
        {
            var CvarMaxRounds = ConVar.Find("mp_maxrounds");
            if (CvarMaxRounds != null)
            {    
                MaxRoundsValue = CvarMaxRounds.GetPrimitiveValue<int>();
            }
            else
            {
                Plugin.Logger.LogInformation($"On UpdateMaxRoundsValue cant set: MaxRoundsValue because CvarMaxRounds is null");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Can't update MaxRoundsValue: {ex}.");
        }
    }
    public int RemainingRounds
    {
        get
        {
            CCSGameRules GameRules = null!;
            try
            {
                GameRules = GetGameRules();
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Can't GetGameRules: {ex}.");
                return 0;
            }
            var played = MaxRoundsValue - (GameRules != null ? GameRules.TotalRoundsPlayed : 0);
            if (played < 0)
                return 0;
            return played;
        }
    }
    public int RemainingWins
    {
        get
        {
            return MaxWins - CurrentHighestWins;
        }
    }
    public int MaxWins
    {
        get
        {
            if (MaxRoundsValue <= 0)
                return 0;

            if (!CanClinch)
                return MaxRoundsValue;

            return ((int)Math.Floor(MaxRoundsValue / 2M)) + 1;
        }
    }
    public int CurrentHighestWins => CTWins > TWins ? CTWins : TWins;
    public void ClearRounds()
    {
        CTWins = 0;
        TWins = 0;
        LastBeforeHalf = false;
    }
    public void SwapScores()
    {
        var oldCtWins = CTWins;
        CTWins = TWins;
        TWins = oldCtWins;
    }
    public void RoundWin(CsTeam team)
    {
        if (team == CsTeam.CounterTerrorist)
        {
            CTWins++;

        }
        else if (team == CsTeam.Terrorist)
        {
            TWins++;
        }
        Plugin.Logger.LogInformation($"T Wins {TWins}, CTWins {CTWins}");
    }
    public bool CheckMaxRounds()
    {
        Plugin.Logger.LogInformation($"UnlimitedRounds {(UnlimitedRounds ? "true" : "false")}, RemainingRounds {RemainingRounds}, RemainingWins {RemainingWins}");
        if (!Plugin.Config.WinDrawSettings.VoteDependsOnRoundWins || UnlimitedRounds || MaxRoundsVoted)
        {
            return false;
        }

        if (RemainingRounds <= Plugin.Config.WinDrawSettings.TriggerRoundsBeforeEnd)
            return true;

        return CanClinch && (RemainingWins <= Plugin.Config.WinDrawSettings.TriggerRoundsBeforeEnd);
    }
    private static CCSGameRules GetGameRules()
    {
        return CounterStrikeSharp.API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
    }
    public void CheckConfig()
    {
        if (Plugin.Config.WinDrawSettings.VoteDependsOnRoundWins)
        {
            if (Plugin.Config.VoteSettings.ChangeMapAfterVote)
            {
                Plugin.Logger.LogError("VoteDependsOnRoundWins will not work because ChangeMapAfterVote set true");
            }
            if (MaxRoundsValue < 2)
            {
                Plugin.Logger.LogError($"VoteDependsOnRoundWins set true, but cvar mp_maxrounds set less than 2. Plugin can't work correctly with these settings.");
            }
        }
    }
}
public class TimerManager
{
    private ConcurrentQueue<Func<Task>> _operationsQueue = new ConcurrentQueue<Func<Task>>();
    private SemaphoreSlim _signal = new SemaphoreSlim(0);
    private Task _worker;
    private bool _running = true;
    private MapChooser Plugin;
    public TimerManager(MapChooser plugin)
    {
        Plugin = plugin;
        // Start the worker task
        _worker = Task.Run(ProcessQueueAsync);
    }

    public void EnqueueOperation(Func<Task> operation)
    {
        _operationsQueue.Enqueue(operation);
        _signal.Release();
    }

    private async Task ProcessQueueAsync()
    {
        while (_running)
        {
            await _signal.WaitAsync();

            if (_operationsQueue.TryDequeue(out Func<Task>? operation) && operation != null)
            {
                try
                {
                    Server.NextFrame(async () => {
                        await operation();
                    });
                    
                }
                catch (Exception ex)
                {
                    // Handle exception (e.g., log error)
                    Console.WriteLine($"******************* Timer operation failed: {ex.Message}");
                    Server.NextFrame( () => 
                    {
                        Plugin.Logger.LogError($"ProcessQueueAsync: Timer operation failed: {ex.Message}");
                    });
                }
            }
        }
    }
    public void Stop()
    {
        _running = false;
        _signal.Release(); // Ensure the worker can exit if it's waiting
    }
}
public class WebhookService
{
    MapChooser Plugin;
    private readonly HttpClient httpClient;

    public WebhookService(MapChooser plugin)
    {
        Plugin = plugin;
        httpClient = new HttpClient();
    }

    public async Task SendWebhookMessage(string mapName, string mapDisplayName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            Plugin.Logger.LogError("SendWebhookMessage called with empty Message");
            return;
        }

        try
        {
            var jsonstring = await UpdateJsonWithMapInfo(mapName, mapDisplayName);
            var content = new StringContent(jsonstring, Encoding.UTF8, "application/json");

            // Configure cancellation token if needed (e.g., with a timeout)
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var response = await httpClient.PostAsync(Plugin.Config.DiscordSettings.DiscordWebhook, content, cts.Token);

                // Ensure a successful status code
                response.EnsureSuccessStatusCode();
            }
        }
        catch (HttpRequestException ex)
        {
            // Log and handle network-related errors
            Console.WriteLine($"HttpRequestException occurred: {ex.Message}");
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"SendWebhookMessage: HttpRequestException occurred: {ex.Message}");
            });
        }
        catch (TaskCanceledException ex)
        {
            // Handle request timeout scenarios
            Console.WriteLine($"Request timed out: {ex.Message}");
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"SendWebhookMessage: Request timed out: {ex.Message}");
            });
        }
        catch (Exception ex)
        {
            // Catch all other exceptions
            Console.WriteLine($"An error occurred: {ex.Message}");
            Server.NextFrame(() => {
                Plugin.Logger.LogError($"SendWebhookMessage: An error occurred: {ex.Message}");
            });
        }
    }
    public async Task<string> UpdateJsonWithMapInfo(string mapName, string mapDisplayName)
    {
        // Read the existing JSON file
        string jsonString = await File.ReadAllTextAsync(Plugin.WebhookNextMapMessagePath);
        using (var jsonDoc = JsonDocument.Parse(jsonString))
        {
            var root = jsonDoc.RootElement.Clone();
            var newcontent = root.GetProperty("content").GetString() + " " + mapDisplayName;

            var embeds = root.GetProperty("embeds").EnumerateArray().ToArray();
            var embed = embeds[0].Clone();
            var imageUrl = embed.GetProperty("image").GetProperty("url").GetString();
            imageUrl = imageUrl + mapName + ".jpg";

            // Construct new JSON object
            var updatedJson = new
            {
                content = newcontent,
                embeds = new[]
                {
                    new
                    {
                        image = new
                        {
                            url = imageUrl
                        }
                    }
                }
            };

//            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(updatedJson);
        }
    }
}
public class MCCoreAPI : MCIAPI
{
    private MapChooser _mapChooser;
    public MCCoreAPI(MapChooser mapChooser)
    {
        _mapChooser = mapChooser;
    }
    public bool GGMC_IsVoteInProgress()
    {
        return _mapChooser.IsVoteInProgress;
    }
}