using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Data;
using System.Text;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using CTimer = System.Threading.Timer;
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
using Microsoft.VisualBasic;
using McMaster.NETCore.Plugins;

namespace MapChooser;

public class MapChooser : BasePlugin, IPluginConfig<MCConfig>
{
    public override string ModuleName => "GG1_MapChooser";
    public override string ModuleVersion => "v1.6.1";
    public override string ModuleAuthor => "Sergey";
    public override string ModuleDescription => "Map chooser, voting, rtv, nominate, etc.";
    public MCCoreAPI MCCoreAPI { get; set; } = null!;
    public static PluginCapability<MCIAPI> MCAPICapability { get; } = new("ggmc:api");
    public static PluginCapability<IWasdMenuManager> WasdMenuManagerCapability = new ("ggmc:wasdmanager");
    public readonly IStringLocalizer<MapChooser> _localizer;
    public MaxRoundsManager roundsManager;
    private TimerManager2 _timerManager;
    private Timer? timersLog = null;
    public WebhookService webhookService;
    public string WebhookNextMapMessagePath = "";
    public MapChooser (IStringLocalizer<MapChooser> localizer)
    {
        _localizer = localizer;
        roundsManager = new(this);
        _timerManager = new(this);
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
        if (Config.TimeLimitSettings.VoteDependsOnTimeLimit)
        {
            if (Config.TimeLimitSettings.TriggerSecondsBeforeEnd < Config.VoteSettings.VotingTime)
            {
                Config.TimeLimitSettings.TriggerSecondsBeforeEnd = Config.VoteSettings.VotingTime + 1;
                Logger.LogInformation($"VoteDependsOnTimeLimit: TriggerSecondsBeforeEnd updates to {Config.VoteSettings.VotingTime + 1} which is minimum value for VotingTime {Config.VoteSettings.VotingTime} in config.");
            }
        }
        wASDMenu.ReadButtons();
    }
    public bool mapChangedOnStart = false; 
    private Random random = new();
    private string mapsFilePath = "";
    public Dictionary<string, MapInfo>Maps_from_List = new Dictionary<string, MapInfo>();
    private Dictionary<string, string> DisplayNameToKeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private List<string> maplist = new();
    private List<string> nominatedMaps = new();
    private List<string> mapsToVote = new List<string>();
    public static Dictionary<int, string> votePlayers = new Dictionary<int, string>();
    private int mapstotal = 0;
    private bool canVote { get; set; } = false;
    private bool canRtv { get; set; } = false;
    private int rtv_need_more { get; set; }
    private DateTime rtvCooldownStartTime;
    private int rtvCoolDownDuration;
    private int rtvRestartProblems = 0;
    private bool _runVoteRoundEnd = false;
    private Timer? changeRequested = null;
    private Timer? voteEndChange = null;
    private Timer? voteEndTimer = null;
    private DateTime lastRoundStartEventTime = DateTime.MinValue;
    private int RestartProblems = 0;
    public bool MapIsChanging = false;
    public bool IsVoteInProgress { get; set; } = false;
    private Timer? _timeLimitTimer = null;
    private Timer? _timeLimitMapChangeTimer = null;
    private bool _timeLimitVoteRoundStart = false;
    private float TimeLimitValue;
    private DateTime timeLimitStartEventTime = DateTime.MinValue;
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
        if (MCCoreAPI != null)
        {
            try
            {            
                MCCoreAPI.RaiseCanVoteEvent();
            }
            catch (Exception ex)
            {
                Server.NextFrame(() =>
                {
                    Logger.LogError($"[MC API ERROR] RaiseCanVoteEvent returned exception: {ex.Message}");
                });
            }
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
//*********                        
                        if (timersLog != null)
                        {
                            try
                            {
                                timersLog.Kill();
                            }
                            catch (System.Exception)
                            {

                            }
                        }
                        timersLog = null;
//*********
                        if (Config.OtherSettings.LastDisconnectedChangeMap && changeRequested == null && !MapIsChanging)
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
            if (timersLog == null)
            {
                timersLog = AddTimer(5.0f, () => {
                    _timerManager.LogAllTimers();
                }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            }
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
                try
                {
                    SimpleKillTimer(changeRequested);
                }
                catch (System.Exception)
                {
                    Server.NextFrame(() => 
                    {
                        Logger.LogError("Error killing changeRequested timer");
                    });
                }
                changeRequested = null;
            }
        }
    }
    private void OnMapStart(string name)
    {
        MapIsChanging = false;
        Logger.LogInformation(name + " loaded");
        if (Config.DiscordSettings.DiscordWebhook != "" && Config.DiscordSettings.DiscordMessageMapStart)
            _ = webhookService.SendWebhookMessage(name, GetDisplayName(name));
        ResetData("On Map Start");

        canVote = ReloadMapcycle();
        if (canVote)
        {
            if (MCCoreAPI != null)
            {
                try
                {            
                    MCCoreAPI.RaiseCanVoteEvent();
                }
                catch (Exception ex)
                {
                    Server.NextFrame(() =>
                    {
                        Logger.LogError($"[MC API ERROR] RaiseCanVoteEvent returned exception: {ex.Message}");
                    });
                }
            }
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
                if (!_playedMaps.Contains(name.ToLower()))
                {
                    if (_playedMaps.Count > 0 && _playedMaps.Count >= Config.VoteSettings.RememberPlayedMaps)
                    {
                        _playedMaps.RemoveAt(0);
                    }
                    _playedMaps.Add(name.ToLower());
                }

                string excludedMaps = string.Join(", ", _playedMaps);

                Logger.LogInformation($"Played maps: {excludedMaps}");
            }

            if (Config.OtherSettings.RandomMapOnStart && !mapChangedOnStart && changeRequested == null)
            {
                Logger.LogInformation($"OnMapStart Requested map change after the server restarted");
                AddTimer(1.0f, () => {
                    changeRequested = AddTimer((float)Config.OtherSettings.RandomMapOnStartDelay, Timer_ChangeMapOnEmpty, TimerFlags.STOP_ON_MAPCHANGE);
                });
            }
            if (Config.RTVSettings.AllowRTV && Config.RTVSettings.RTVDelayFromStart > 0)
            {
                AddTimer(1.0f, () => {
                    MakeRTVTimer(Config.RTVSettings.RTVDelayFromStart);
                });
            }
            if (Config.TimeLimitSettings.VoteDependsOnTimeLimit)
            {
                var TimeLimit = ConVar.Find("mp_timelimit");
                TimeLimitValue = TimeLimit?.GetPrimitiveValue<float>() ?? 0;

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
                    StartOrRestartTimeLimitTimer(timerTime);
                    timeLimitStartEventTime = DateTime.Now;
                }
            }
/*            if (timersLog == null)
            {
                timersLog = AddTimer(5.0f, () => {
                    _timerManager.LogAllTimers();
                }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
            } */
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
        SimpleKillTimer(changeRequested);
        changeRequested = null;
        SimpleKillTimer(_timeLimitTimer);
        _timeLimitTimer = null;
        SimpleKillTimer(_timeLimitMapChangeTimer);
        _timeLimitMapChangeTimer = null;
        KillRTVtimer();
        SimpleKillTimer(voteEndTimer);
        voteEndTimer = null;
        SimpleKillTimer(timersLog);
        timersLog = null;
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
        if ((DateTime.Now - lastRoundStartEventTime).TotalSeconds < 3)
            return HookResult.Continue;

        lastRoundStartEventTime = DateTime.Now;
        if (canVote)
        {
            roundsManager.UpdateMaxRoundsValue();
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
                try
                {
                    SimpleKillTimer(voteEndTimer);
                }
                catch (System.Exception)
                {
                    Server.NextFrame(() => 
                    {
                        Logger.LogError("Error killing voteEndTimer timer");
                    });
                }
                voteEndTimer = null;
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
            try
            {
                SimpleKillTimer(_timeLimitMapChangeTimer);
            }
            catch (System.Exception)
            {
                Server.NextFrame(() => 
                {
                    Logger.LogError("Error killing _timeLimitMapChangeTimer timer");
                });
            }
            _timeLimitMapChangeTimer = null;            
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
        int currentmapweight;
    //  create map list to chose
        foreach (var mapcheck in Maps_from_List)
        {
            if ((mapcheck.Value.MinPlayers != 0 && numplayers < mapcheck.Value.MinPlayers) 
                || (mapcheck.Value.MaxPlayers != 0 && numplayers > mapcheck.Value.MaxPlayers) 
                || _playedMaps.Contains(mapcheck.Key.ToLower()) || mapcheck.Value.Weight == 0)
            {
                i++;
                continue; //  map does not suite
            }
    //   add map to maplist[i])
            validmaps++;
            validmapnames[validmaps] = mapcheck.Key;
            currentmapweight = mapcheck.Value.Weight == -1 ? 1 : mapcheck.Value.Weight;
            validmapweight[validmaps] = validmapweight[validmaps - 1] + currentmapweight;
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
/**********************************************************************
        Logger.LogInformation($"validmaps: {validmaps}, validmapweight: {validmapweight[validmaps]}, choice: {choice}, map {map}");
*/
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
            _roundEndMap = "";
        }
        else
        {
            string mapname = mapChange;
            PrintToServerCenter("mapchange.to", mapname);
            PrintToServerChat("nextmap.info", mapname);
            Console.WriteLine($"Map is changing to {mapname}");
            Logger.LogInformation($"DoMapChange: Map is going to be change to {mapname}");
            MapToChange = mapname;
            if (changeTime == SSMC_ChangeMapTime.ChangeMapTime_Now)
            {
                MapIsChanging = true;
                ChangeMapInFive(mapname);
            }
            else if (changeTime == SSMC_ChangeMapTime.ChangeMapTime_MapEnd)
            {
                if (!Config.VoteSettings.ChangeMapAfterVote)
                {
                    Server.ExecuteCommand($"nextlevel {mapname}");
                    Logger.LogInformation($"Set NextLevel to {mapname}");
                }
                _roundEndMap = mapname;
            }
            else 
            {
                Logger.LogError($"Something wrong in DoMapChange - {mapname}");
            }
        }
        Logger.LogInformation("DoMapChange finished");
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
                TimeLimitValue = timelimitOld + Config.VoteSettings.ExtendMapTimeMinutes;
                Logger.LogInformation($"DoMapChange: Extend Map is winning option. TimeLimit is {timelimitOld}. Time extended by {Config.VoteSettings.ExtendMapTimeMinutes} minutes.");
                mp_timelimit.SetValue(TimeLimitValue);
                Logger.LogInformation($"DoMapChange: TimeLimit now {mp_timelimit.GetPrimitiveValue<float>()}");
                if (Config.TimeLimitSettings.VoteDependsOnTimeLimit)
                {
                    var secondsLeft = (int)TimeLimitValue * 60 - (int)(DateTime.Now - timeLimitStartEventTime).TotalSeconds;
                    float newVoteDelay = secondsLeft - Config.TimeLimitSettings.TriggerSecondsBeforeEnd;
                    if (newVoteDelay <= 0)
                    {
                        Logger.LogError($"ExtendMap: Something wrong with your settings: Map extended on {Config.VoteSettings.ExtendMapTimeMinutes} minutes, New TimeLimit {TimeLimitValue}, Seconds Left to play {secondsLeft}, Trigger Seconds before End {Config.TimeLimitSettings.TriggerSecondsBeforeEnd}");
                    }
                    roundsManager.MaxRoundsVoted = false;
                    Logger.LogInformation($"DoMapChange: Seconds left to play: {secondsLeft}, timer restart for new vote at the end of time limit");
                    StartOrRestartTimeLimitTimer(newVoteDelay);
                    try
                    {
                        SimpleKillTimer(_timeLimitMapChangeTimer);
                    }
                    catch (System.Exception)
                    {
                        Server.NextFrame(() => 
                        {
                            Logger.LogError("Error killing _timeLimitMapChangeTimer timer");
                        });
                    }
                    _timeLimitMapChangeTimer = null;
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

    private IWasdMenu CreateMapsMenuWASD(Action<CCSPlayerController,IWasdMenuOption> action, CCSPlayerController playerController, bool freezePlayers = true, bool limits=true)
    {
        IWasdMenu MapsMenu = wASDMenu.manager.CreateMenu();
      
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
                    using (new WithTemporaryCulture(playerController.GetLanguage()))
                    {
                        MapsMenu.Add(_localizer["stop.line", selectedMapList.Count], action);
                    }
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
        return MapsMenu;
    }

// Админ выбирает ручной выбор карты для смены или автоматический
    private void AdminChangeMapHandle(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (IsValidPlayer(caller))
        {
            IWasdMenu? acm_menu;
            using (new WithTemporaryCulture(caller.GetLanguage()))
            {
                acm_menu = wASDMenu.manager.CreateMenu(_localizer["choose.map"], Config.MenuSettings.FreezeAdminInMenu);
                acm_menu.Add(_localizer["manual.map"], AdminChangeMapManual); // Simply change the map
                acm_menu.Add(_localizer["automatic.map"], AdminChangeMapAuto); // Start voting for map
            }
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
            IWasdMenu acmm_menu = CreateMapsMenuWASD(Handle_AdminManualChange, player, Config.MenuSettings.FreezeAdminInMenu, false); // no restrictions, because admn choose maps
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
            
            IWasdMenu? acvm_menu;
            using (new WithTemporaryCulture(caller.GetLanguage()))
            {
                acvm_menu = wASDMenu.manager.CreateMenu(_localizer["choose.map"]);
                acvm_menu.Add(_localizer["manual.map"], AdminVoteMapManual); // Simply change the map
                acvm_menu.Add(_localizer["automatic.map"], AdminVoteMapAuto); // Start voting for map
            }
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
            IWasdMenu avmm_menu = CreateMapsMenuWASD(Handle_VoteMapManual, player, Config.MenuSettings.FreezeAdminInMenu, false); // no restrictions, because admn choose maps
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
                DoAutoMapVote(caller, Config.VoteSettings.VotingTime, SSMC_ChangeMapTime.ChangeMapTime_Now, Config.VoteSettings.EndMapVoteWASDMenu );
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
            DoAutoMapVote(null!, timeToVote, SSMC_ChangeMapTime.ChangeMapTime_Now, Config.VoteSettings.EndMapVoteWASDMenu);
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
                using (new WithTemporaryCulture(caller.GetLanguage()))
                {
                    caller.PrintToChat(_localizer["vote.inprogress"]);
                }
                return;
            }
            Logger.LogInformation($"[GGMC]: Admin {caller.PlayerName} started vote to change map or not.");
            if (GlobalWASDMenu != null)
            {
                Logger.LogError($"GlobalWASDMenu is not null but should be, possibly another vote is active");
                return;
            }
            if (GlobalChatMenu != null)
            {
                Logger.LogError($"GlobalChatMenu is not null but should be, possibly another vote is active");
                return;
            }
            IsVoteInProgress = true;
            optionCounts.Clear();
            votePlayers.Clear();

            if (Config.VoteSettings.EndMapVoteWASDMenu)
            {
                GlobalWASDMenu = wASDMenu.manager.CreateMenu(_localizer["vote.changeornot"], Config.MenuSettings.FreezePlayerInMenu);
                if (GlobalWASDMenu == null)
                {
                    Logger.LogError($"GlobalWASDMenu is null but should not be, something is wrong");
                    IsVoteInProgress = false;
                    return;
                }
                GlobalWASDMenu.Add(_localizer["vote.yes"], (player, option) =>
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
                    wASDMenu.manager.CloseMenu(player);
                });
                GlobalWASDMenu.Add(_localizer["vote.no"], (player, option) =>
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
                    wASDMenu.manager.CloseMenu(player);
                });
            }
            else
            {
                GlobalChatMenu = new ChatMenu(_localizer["vote.changeornot"]);
                if (GlobalChatMenu == null)
                {
                    Logger.LogError($"GlobalChatMenu is null but should not be, something is wrong");
                    IsVoteInProgress = false;
                    return;
                }
                GlobalChatMenu.AddMenuOption(_localizer["vote.yes"], (player, option) =>
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
                GlobalChatMenu.AddMenuOption(_localizer["vote.no"], (player, option) =>
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
            }
//            ChangeOrNotMenu.PostSelectAction = PostSelectAction.Close;
        
            var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var player in playerEntities)
                {
                    if (Config.VoteSettings.EndMapVoteWASDMenu)
                    {
                        wASDMenu.manager.OpenMainMenu(player, GlobalWASDMenu);
                    }
                    else if (GlobalChatMenu != null)
                    {
                        MenuManager.OpenChatMenu(player, GlobalChatMenu);
                    }
                }

                AddTimer((float)Config.VoteSettings.VotingTime, () => TimerChangeOrNot(), TimerFlags.STOP_ON_MAPCHANGE);
            }
        }
    }
    private void AdminSetNextMapHandle(CCSPlayerController player, IWasdMenuOption option)
    {
        if (IsValidPlayer(player))
        {
            wASDMenu.manager.CloseMenu(player);
            IWasdMenu acmm_menu = CreateMapsMenuWASD(Handle_AdminSetNextMap, player, Config.MenuSettings.FreezeAdminInMenu, false); // no restrictions, because admn choose maps
            if (acmm_menu != null)
                wASDMenu.manager.OpenMainMenu(player, acmm_menu);            
        }
        return;
    }
    private void Handle_AdminSetNextMap(CCSPlayerController player, IWasdMenuOption option)
    {
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
        GlobalWASDMenu = null;
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
        optionCounts.Clear();
        votePlayers.Clear();
        if (wasdmenu)
        {
            ConfigMapsInVote = Config.VoteSettings.MapsInVote;
            GlobalWASDMenu = wASDMenu.manager.CreateMenu("", Config.MenuSettings.FreezePlayerInMenu); //_localizer["choose.map"]
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
            GlobalChatMenu = new ChatMenu(_localizer["choose.map"]);
            if (GlobalChatMenu == null)
            {
                Logger.LogError($"GlobalChatMenu is null but should not be, something is wrong");
                IsVoteInProgress = false;
                return;
            }
        }
        mapsToVote.Clear();
        int mapsinvote = 0, i = 0;

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
                int currentmapweight;
                if (numplayers == 0) numplayers = 1;
            //  create map list to chose
                foreach (var mapcheck in Maps_from_List)
                {
                    if ((mapcheck.Value.MinPlayers != 0 && numplayers < mapcheck.Value.MinPlayers) 
                        || (mapcheck.Value.MaxPlayers != 0 && numplayers > mapcheck.Value.MaxPlayers) 
                        || _playedMaps.Contains(mapcheck.Key.ToLower()) 
                        || (mapsToVote.Count > 0 && mapsToVote.Contains(mapcheck.Key)) 
                        || mapcheck.Value.Weight == 0)
                    {
                        continue; //  map does not suite
                    }
            //   add map to maplist[i])
                    validmaps++;
                    validmapnames[validmaps] = mapcheck.Key;
                    currentmapweight = mapcheck.Value.Weight == -1 ? 1 : mapcheck.Value.Weight;
                    if (currentmapweight < 1)
                    {
                        Logger.LogInformation($"Update weight for {mapcheck.Key} from {currentmapweight} to 1");
                        currentmapweight = 1;
                    }
                    validmapweight[validmaps] = validmapweight[validmaps - 1] + currentmapweight;
                }
                if (validmaps < 1 && mapsinvote == 0)
                {
                    Logger.LogInformation("DoAutoMapVote: Could not run automatic vote, no nominated and valid maps available.");
                    IsVoteInProgress = false;
                    return;
                }

                int mapstochose = ConfigMapsInVote - mapsinvote;
                if (mapstochose > validmaps)
                {
                    Logger.LogWarning($"Number of valid maps ({validmaps}) is less then maps to choose. Only {validmaps} will be selected");
                    mapstochose = validmaps;
                }
                if (mapstochose > 0)
                {
                    int choice, map;
                    List<int> selectedIndices = new List<int>();
                    i = 0;
                    int maxweight = 1;
                    for ( i = 0; i < mapstochose; i++) 
                    {
                        if (validmapweight[validmaps] < validmaps)
                        {
                            maxweight = validmaps;
                            Logger.LogError($"Fix maxweight from {validmapweight[validmaps]} to {maxweight}");
                        }
                        else
                        {
                            maxweight = validmapweight[validmaps];
                        }
                        choice = random.Next(1, maxweight);
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
//                        string mapsToVoteStr = string.Join(", ", mapsToVote);

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
                GlobalWASDMenu?.Add(_localizer["extend.map"], (player, option) =>
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
                    wASDMenu.manager.CloseMenu(player);
                });
            }
            else
            {
                GlobalChatMenu?.AddMenuOption(_localizer["extend.map"], (player, option) =>
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

    [ConsoleCommand("timeleft", "Get the time left")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void TimeLeftCommand(CCSPlayerController caller, CommandInfo command)
    {
        if (!IsValidPlayer(caller))
        {
            Console.WriteLine("Invalid rtv caller");
            return;
        }
        if (Config.TimeLimitSettings.VoteDependsOnTimeLimit)
        {
            int timePassed = (int)(DateTime.Now - timeLimitStartEventTime).TotalSeconds;
            int remainingTime = (int)TimeLimitValue * 60 - timePassed;
            int minutesLeft = remainingTime / 60; // Get the total minutes
            int secondsLeft = remainingTime % 60; // Get the remaining seconds
            caller.PrintToChat(Localizer["time.left", minutesLeft, secondsLeft]);
        }
        if (Config.WinDrawSettings.VoteDependsOnRoundWins)
        {
            int roundsLeft = roundsManager.RemainingRounds;
            if (roundsLeft > 0)
                caller.PrintToChat(Localizer["rounds.left", roundsLeft]);

            int winsLeft = roundsManager.RemainingWins;
            if (winsLeft > 0)
                caller.PrintToChat(Localizer["wins.left", winsLeft]);
        }
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
            //Работает таймер задержки
            if (_timerManager.IsTimerRunning("rtvTimer"))
            {
                int remainingTime = rtvCoolDownDuration - (int)(DateTime.Now - rtvCooldownStartTime).TotalSeconds;
                if (remainingTime > 0)
                {
                    caller.PrintToChat(Localizer["nortv.time", remainingTime]);
                    return;
                }
                else
                {
                    KillRTVtimer();
                    Logger.LogWarning("forced to kill rtvTimer.");
                }
            }
            if (_roundEndMap != null && _roundEndMap.Length > 0)
            {
                caller.PrintToChat(Localizer["nortv.now"]);
                return;
            }
            if (Config.RTVSettings.NoRTVafterRoundsPlayed > 0)
            {
                CCSGameRules GameRules = null!;
                try
                {
                    GameRules = GetGameRules();
                    if (GameRules != null)
                    {
                        if (GameRules.TotalRoundsPlayed > Config.RTVSettings.NoRTVafterRoundsPlayed)
                        {
                            caller.PrintToChat(Localizer["nortv.now"]);
                            Logger.LogInformation("Skipped rtv call because played more rounds that allowed for rtv.");
                            return;
                        }
                    }
                }
                catch (Exception)
                {

                }
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
                        caller.PrintToChat(Localizer["nortv.now"]);
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
        IWasdMenu menu = wASDMenu.manager.CreateMenu(Localizer["maps.menu"], Config.MenuSettings.FreezeAdminInMenu);
        menu.Add(Localizer["change.map"], AdminChangeMapHandle); // Simply change the map
        menu.Add(Localizer["votefor.map"], AdminStartVotesMapHandle); // Start voting for map
        menu.Add(Localizer["vote.changeornot"], VotesForChangeMapHandle); // Start voting to change map or not
        menu.Add(Localizer["set.nextmap"], AdminSetNextMapHandle); // Choose and set next map
        wASDMenu.manager.OpenMainMenu(caller, menu);
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
            Logger.LogInformation("MapVoteCommand: Another vote active when ggmc_change_nextmap. Skip change");
            return;
        }
        if (string.IsNullOrEmpty(_roundEndMap))
        {
            Logger.LogInformation("ggmc_change_nextmap called, change to random map.");
            GGMCDoAutoMapChange();
        }
        else
        {
            Logger.LogInformation($"ggmc_change_nextmap called, change to {_roundEndMap}");
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
    [RequiresPermissions("@css/changemap")]
    public void QuickChangeMapCommand(CCSPlayerController caller, CommandInfo command)
    {
        if (command == null || command.ArgCount < 1)
        {
            if (IsValidPlayer(caller))
                caller.PrintToChat(Localizer["ggmap.usage"]);
            else
                Console.WriteLine(Localizer["ggmap.usage"]);
            return;
        }
        string mapguess = command.ArgString;
        if (IsValidPlayer(caller))
        {
            Logger.LogInformation($"{caller.PlayerName} requested ggmap for {mapguess}");
        }
        else
        {
            Logger.LogInformation($"Console requested ggmap for {mapguess}");
        }
        if (maplist.Contains(mapguess))
        {
            DoMapChange(mapguess, SSMC_ChangeMapTime.ChangeMapTime_Now);
        }
        else
        {        
            string [] mapnames = FindSimilarMaps(mapguess, maplist);
            if (mapnames.Length == 0)
            {
                if (IsValidPlayer(caller))
                    caller.PrintToChat(Localizer["ggmap.nomaps"]);
                else
                    Console.WriteLine(Localizer["ggmap.nomaps"]);
            }
            else if (mapnames.Length == 1)
            {
                if (IsValidPlayer(caller))
                    caller.PrintToChat(Localizer["ggmap.change"]);
                else
                    Console.WriteLine(Localizer["ggmap.change"]);
                DoMapChange(mapnames[0], SSMC_ChangeMapTime.ChangeMapTime_Now);
            }
            else
            {
                if (IsValidPlayer(caller))
                    caller.PrintToChat(string.Join(" ", mapnames));
                else
                    Console.WriteLine(string.Join(" ", mapnames));
            }
        }
    }
    [ConsoleCommand("reloadmaps", "Reload maps")]
	[RequiresPermissions("@css/changemap")]
    public void ReloadMapsCommand(CCSPlayerController caller, CommandInfo command)
    {
        canVote = ReloadMapcycle();
        if (canVote)
        {
            if (MCCoreAPI != null)
            {
                try
                {            
                    MCCoreAPI.RaiseCanVoteEvent();
                }
                catch (Exception ex)
                {
                    Server.NextFrame(() =>
                    {
                        Logger.LogError($"[MC API ERROR] RaiseCanVoteEvent returned exception: {ex.Message}");
                    });
                }
            }
            if (IsValidPlayer(caller))
            {
                caller.PrintToChat("Map file reloaded");
            }
            else
            {
                Console.WriteLine("Map file reloaded");
            }   
        }
        else
        {
            if (IsValidPlayer(caller))
            {
                caller.PrintToChat("Map file reloaded but vote is not allowed. See logs");
            }
            else
            {
                Console.WriteLine("Map file reloaded but vote is not allowed. See logs");
            }
        }
    }
    [ConsoleCommand("mapweights", "Check map weights")]
    public void CheckMapWeightsCommand(CCSPlayerController caller, CommandInfo command)
    {
        if (caller != null && IsValidPlayer(caller))
        {
            foreach (var mapcheck in Maps_from_List)
            {
                caller.PrintToChat($"{mapcheck.Key} weight: {(mapcheck.Value.Weight == -1 ? 1 : mapcheck.Value.Weight)}");
            }
        }
        else
        {
            foreach (var mapcheck in Maps_from_List)
            {
                Console.WriteLine($"{mapcheck.Key} weight: {(mapcheck.Value.Weight == -1 ? 1 : mapcheck.Value.Weight)}");
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
            try
            {
                SimpleKillTimer(voteTimer);
            }
            catch (System.Exception)
            {
                Server.NextFrame(() => 
                {
                    Logger.LogError("Error killing voteTimer timer");
                });
            }
            voteTimer = null;
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
                if (Config.DiscordSettings.DiscordWebhook != "" && Config.DiscordSettings.DiscordMessageAfterVote && _selectedMap != "extend.map")
                {
                    _ = webhookService.SendWebhookMessage(_selectedMap, GetDisplayName(_selectedMap));
                }
                
                IsVoteInProgress = false;
                _roundEndMap = _selectedMap;
                DoMapChange(_selectedMap, changeTime);
                VoteChangeTimerStart(changeTime);

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
        VoteChangeTimerStart(changeTime);
    }
    private void VoteChangeTimerStart(SSMC_ChangeMapTime changeTime = SSMC_ChangeMapTime.ChangeMapTime_Now)
    {
        if (Config.VoteSettings.ChangeMapAfterVote && changeTime != SSMC_ChangeMapTime.ChangeMapTime_Now)
        {
            Logger.LogInformation($"Vote has been done. Start change map timer after the vote in {Config.OtherSettings.DelayBeforeChangeSeconds}");
            if (voteEndChange != null)
            {
                var timerKill = voteEndChange;
                Server.NextFrame(() => 
                {
                    timerKill.Kill();
                });
            }
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
        }
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
        rtvRestartProblems = 0;
        roundsManager.InitialiseMap();
        GlobalChatMenu = null;
        GlobalWASDMenu = null;
        _timeLimitVoteRoundStart = false;
        timeLimitStartEventTime = DateTime.MinValue;
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
            _timerManager.StartTimer("rtvTimer", interval, Handle_RTVTimer, TimerFlags.STOP_ON_MAPCHANGE);
            canRtv = false;
            rtvCoolDownDuration = interval;
            rtvCooldownStartTime = DateTime.Now;
        }
    }
    private void Handle_RTVTimer()
    {
        canRtv = true;
        rtvCoolDownDuration = 0;
        Logger.LogInformation("rtv cooldown off, rtv is allowed");
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
        if (canRtv)
        {
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
            CleanRTVArrays();
            Logger.LogInformation("Starting RTV vote");
            MakeRTVTimer(Config.RTVSettings.IntervalBetweenRTV);
            DoAutoMapVote(null!, Config.VoteSettings.VotingTime, SSMC_ChangeMapTime.ChangeMapTime_Now, Config.VoteSettings.EndMapVoteWASDMenu );
        }
        else
        {
            Logger.LogInformation("Can't start RTV vote now");
        }
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
    private void StartOrRestartTimeLimitTimer(float duration)
    {
        SimpleKillTimer(_timeLimitTimer);
        _timeLimitTimer = AddTimer(duration, TimeLimitTimerHandle, TimerFlags.STOP_ON_MAPCHANGE);
    }
    private void KillRTVtimer()
    {
        _timerManager.StopTimer("rtvTimer");
        canRtv = true;
        rtvCoolDownDuration = 0;
        CleanRTVArrays();
    }
    private void SimpleKillTimer(Timer? parameter)
    {
        if (parameter != null)
        {
            var timerKill = parameter;
            Server.NextFrame(() => {
                timerKill.Kill();
            });
        }
    }
/*    public async Task<bool> KillTimer(Timer? parameter)
    {
        Timer? timerToKill = parameter;
        if (timerToKill != null)
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
                            timerToKill = null;
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
        return true;
    } */
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
    private static CCSGameRules GetGameRules()
    {
        return CounterStrikeSharp.API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
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
    private static readonly ConcurrentDictionary<int, (PlayerButtons Button, DateTime LastPress, int RepeatCount)> ButtonHoldState = new();
    private const float InitialDelay = 0.5f;
    private const float RepeatDelay = 0.1f;
    private PlayerButtons scrollUp;
    private PlayerButtons scrollDown;
    private PlayerButtons choose;
    private PlayerButtons back;
    private PlayerButtons exit;
    public void ReadButtons()
    {
        if (ButtonMapping.TryGetValue(Plugin.Config.MenuSettings.ScrollUp, out var scrollUpButton))
            scrollUp = scrollUpButton;
        else
            scrollUp = PlayerButtons.Forward;

        if (ButtonMapping.TryGetValue(Plugin.Config.MenuSettings.ScrollDown, out var scrollDownButton))
            scrollDown = scrollDownButton;
        else
            scrollDown = PlayerButtons.Back;

        if (ButtonMapping.TryGetValue(Plugin.Config.MenuSettings.Choose, out var chooseButton))
            choose = chooseButton;
        else
            choose = PlayerButtons.Use;

        if (ButtonMapping.TryGetValue(Plugin.Config.MenuSettings.Back, out var backButton))
            back = backButton;
        else
            back = PlayerButtons.Moveleft;

        if (ButtonMapping.TryGetValue(Plugin.Config.MenuSettings.Exit, out var exitButton))
            exit = exitButton;
        else
            exit = PlayerButtons.Reload;
    }
    public void Load(bool hotReload)
    {
        var wasdMenuManager = new WasdManager();

        Plugin.RegisterEventHandler<EventPlayerActivate>((@event, info) =>
        {
            if (@event.Userid != null && @event.Userid.IsValid && !@event.Userid.IsBot && !@event.Userid.IsHLTV)
            {
                Players[@event.Userid.Slot] = new WasdMenuPlayer(Plugin)
                {
                    player = @event.Userid,
                    Buttons = @event.Userid.Buttons,
                    Localizer = Plugin._localizer
                };
                Players[@event.Userid.Slot].UpdateLocalization();
            }
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
                    Players[pl.Slot] = new WasdMenuPlayer(Plugin)
                    {
                        player = pl,
                        Buttons = pl.Buttons
                    };
                    Players[pl.Slot].UpdateLocalization();
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
        DateTime now = DateTime.Now;
        foreach (var player in Players.Values.Where(p => p.MainMenu != null))
        {
            var controller = player.player!;       
            PlayerButtons currentButtons = controller.Buttons;

            if (!ButtonHoldState.TryGetValue(controller.Slot, out var holdState))
            {
                holdState = ((PlayerButtons)0, DateTime.MinValue, 0);
            }

            if (currentButtons != 0)
            {
                if (holdState.Button != currentButtons)
                {
                    ButtonHoldState[controller.Slot] = (currentButtons, now, 0);
                    HandleButton(player, currentButtons);
                }
                else
                {
                    double totalSeconds = (now - holdState.LastPress).TotalSeconds;
                    if (totalSeconds >= InitialDelay)
                    {
                        int repeatCount = (int)((totalSeconds - InitialDelay) / RepeatDelay);
                        if (repeatCount > holdState.RepeatCount)
                        {
                            HandleButton(player, currentButtons, repeatCount);
                            ButtonHoldState[controller.Slot] = (holdState.Button, holdState.LastPress, repeatCount);
                        }
                    }
                }
            }
            else
            {
                ButtonHoldState.TryRemove(controller.Slot, out _);
            }

            player.Buttons = currentButtons;
            if(player.CenterHtml != "")
                player.player.PrintToCenterHtml(player.CenterHtml);
        }
    }
    private void HandleButton(WasdMenuPlayer player, PlayerButtons button, int repeat = 0)
    {
        if ((button & scrollUp) != 0 && ((player.Buttons & scrollUp) == 0 || repeat > 0))
        {
            player.ScrollUp();
        }
        else if((button & scrollDown) != 0 && ((player.Buttons & scrollDown) == 0 || repeat > 0))
        {
            player.ScrollDown();
        }
        else if((button & choose) != 0 && ((player.Buttons & choose) == 0 || repeat > 0))
        {
            player.Choose();
        } 
        else if ((button & back) != 0 && ((player.Buttons & back) == 0 || repeat > 0))
        {
            player.CloseSubMenu();
        }
        else if ((button & exit) != 0 && ((player.Buttons & exit) == 0 || repeat > 0))
        {
            player.OpenMainMenu(null);
        }
    }
    public static readonly Dictionary<string, PlayerButtons> ButtonMapping = new()
    {
        { "Alt1", PlayerButtons.Alt1 },
        { "Alt2", PlayerButtons.Alt2 },
        { "Attack", PlayerButtons.Attack },
        { "Attack2", PlayerButtons.Attack2 },
        { "Attack3", PlayerButtons.Attack3 },
        { "Bullrush", PlayerButtons.Bullrush },
        { "Cancel", PlayerButtons.Cancel },
        { "Duck", PlayerButtons.Duck },
        { "Grenade1", PlayerButtons.Grenade1 },
        { "Grenade2", PlayerButtons.Grenade2 },
        { "Space", PlayerButtons.Jump },
        { "Left", PlayerButtons.Left },
        { "W", PlayerButtons.Forward },
        { "A", PlayerButtons.Moveleft },
        { "S", PlayerButtons.Back },
        { "D", PlayerButtons.Moveright },
        { "E", PlayerButtons.Use },
        { "R", PlayerButtons.Reload },
        { "F", (PlayerButtons)0x800000000 },
        { "Shift", PlayerButtons.Speed },
        { "Right", PlayerButtons.Right },
        { "Run", PlayerButtons.Run },
        { "Walk", PlayerButtons.Walk },
        { "Weapon1", PlayerButtons.Weapon1 },
        { "Weapon2", PlayerButtons.Weapon2 },
        { "Zoom", PlayerButtons.Zoom },
        { "Tab", (PlayerButtons)8589934592 }
    };
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

    [JsonPropertyName("MenuSettings")]
    public WASDMenuSettings MenuSettings { get; set; } = new WASDMenuSettings();

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
    public bool EndMapVoteWASDMenu { get; set; } = true;

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

    /* Prevent RTV after some number of rounds played to allow leaders to finish the game. */
    [JsonPropertyName("NoRTVafterRoundsPlayed")]
    public int NoRTVafterRoundsPlayed { get; set; } = 0;
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
    /* Extension of map pictures: jpg, png, etc.*/
    [JsonPropertyName("PictureExtension")]
    public string PictureExtension { get; set; } = "jpg";
}
public class WASDMenuSettings
{    
    /* Play sound effects in menu: open, scroll, select  */
    [JsonPropertyName("SoundInMenu")]
    public bool SoundInMenu { get; set; } = true;
    /* Freeze player when his menu is opened because navigatin in menu use standard moving buttons  */
    [JsonPropertyName("FreezePlayerInMenu")]
    public bool FreezePlayerInMenu { get; set; } = false;
    /* Freeze player when his menu is opened because navigatin in menu use standard moving buttons  */
    [JsonPropertyName("FreezeAdminInMenu")]
    public bool FreezeAdminInMenu { get; set; } = true;
    [JsonPropertyName("ScrollUp")]
    public string ScrollUp { get; set; } = "W";
    [JsonPropertyName("ScrollDown")]
    public string ScrollDown { get; set; } = "S";
    [JsonPropertyName("Choose")]
    public string Choose { get; set; } = "E";
    [JsonPropertyName("Back")]
    public string Back { get; set; } = "A";
    [JsonPropertyName("Exit")]
    public string Exit { get; set; } = "R";
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
    public int Weight { get; set; } = -1;
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
            catch (Exception)
            {
//                Plugin.Logger.LogError($"Can't GetGameRules: {ex}.");
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
            catch (Exception)
            {
//                Plugin.Logger.LogError($"Can't GetGameRules: {ex}.");
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
            imageUrl = imageUrl + mapName + "." + Plugin.Config.DiscordSettings.PictureExtension;
            
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
public class ManagedTimer
{
    private Timer? _timer;
    public bool IsRunning { get; private set; }
    public string Name { get; }
    private MapChooser _plugin;
    public DateTime StartTime;
    public DateTime EndTime;
    private Action? Callback;
    public ManagedTimer(string name, MapChooser plugin)
    {
        Name = name;
        IsRunning = false;
        _plugin = plugin;
    }
    public void Start(float durationInSeconds, Action callback, TimerFlags flags)
    {
        Stop(); // Ensure any existing timer is stopped
        Callback = callback;
        StartTime = DateTime.Now;
        EndTime = StartTime.AddSeconds(durationInSeconds);
        _timer = new Timer(durationInSeconds, () =>
        {
            IsRunning = false;
            try
            {
                callback.Invoke();
            }
            catch (Exception ex)
            {
                Server.NextFrame (() => {
                    _plugin.Logger.LogError($"Timer {Name} callback failed: {ex.Message}");
                });
            }
        }, flags);
        if (_timer == null)
        {
            _plugin.Logger.LogError($"Errorin start of the Timer '{Name}' with duration {durationInSeconds} seconds.");
        }
        else
        {
            IsRunning = true;
            _plugin.Logger.LogInformation($"Timer '{Name}' started with duration {durationInSeconds} seconds.");
        }
    }
    public void Force()
    {
        if (Callback != null)
        {
            try
            {
                Callback.Invoke();
            }
            catch (Exception ex)
            {
                Server.NextFrame (() => {
                    _plugin.Logger.LogError($"Timer {Name} force callback failed: {ex.Message}");
                });
            }
        }
        if (_timer != null)
        {
            try
            {
                _timer.Kill();
            }
            catch (Exception ex)
            {
                Server.NextFrame (() => {
                    _plugin.Logger.LogError($"Timer {Name} force kill failed: {ex.Message}");
                });
            }
        }
        _timer = null;
        IsRunning = false;
        _plugin.Logger.LogInformation($"Force to stop Timer '{Name}'.");
    }
    public void Stop()
    {
        if (_timer != null)
        {
            _timer.Kill();
            _timer = null;
            IsRunning = false;
            _plugin.Logger.LogInformation($"Timer '{Name}' stopped.");
        }
    }
}
public class TimerManager2
{
    private readonly Dictionary<string, ManagedTimer> _timers = new();
    private MapChooser _plugin;
    public TimerManager2 (MapChooser plugin)
    {
        _plugin = plugin;
    }
    public void StartTimer(string name, float durationInSeconds, Action callback, TimerFlags flags)
    {
        if (_timers.ContainsKey(name))
        {
            _timers[name].Stop(); // Stop any existing timer with the same name
        }

        var timer = new ManagedTimer(name, _plugin);
        timer.Start(durationInSeconds, callback, flags);
        _timers[name] = timer;
    }
    public void StopTimer(string name)
    {
        if (_timers.TryGetValue(name, out var timer))
        {
            timer.Stop();
            _timers.Remove(name);
        }
        else
        {
            _plugin.Logger.LogInformation($"No timer found with name '{name}' to stop.");
        }
    }
    public bool IsTimerRunning(string name)
    {
        return _timers.TryGetValue(name, out var timer) && timer.IsRunning;
    }
    public void LogAllTimers()
    {
        if (_timers.Count == 0)
        {
            return;
        }

        var timeCheck = DateTime.Now;
        foreach (var timer in _timers.Values)
        {
            if (timer.IsRunning)
            {
                if (timer.EndTime < timeCheck)
                {
                    _plugin.Logger.LogError($"Timer {timer.Name} works longer than its time :(");
                    timer.Force();
                }
            }
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
    public event Action? CanVoteEvent;
    public void RaiseCanVoteEvent()
    {
        CanVoteEvent?.Invoke();
    }
    public void UpdateMapWeights(Dictionary<string, int> newWeights)
    {
        Console.WriteLine($"**************[GGMC] Updated weight called.");
        foreach (var entry in newWeights)
        {
            string mapName = entry.Key;
            int newWeight = entry.Value;

            // Check if the map exists in the dictionary
            if (_mapChooser.Maps_from_List.ContainsKey(mapName))
            {
                // Check if the weight needs to be updated (assuming you only want to update unset or zero weights)
                if (_mapChooser.Maps_from_List[mapName].Weight == -1)
                {
                    _mapChooser.Maps_from_List[mapName].Weight = newWeight;
//                    Console.WriteLine($"**************[GGMC] Updated weight for {mapName} to {newWeight}.");
                }
            }
        }
    }
}