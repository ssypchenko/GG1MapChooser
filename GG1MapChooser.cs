using System.Data;
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
using WASDSharedAPI;

namespace MapChooser;

public class MapChooser : BasePlugin, IPluginConfig<MCConfig>
{
    public override string ModuleName => "GG1_MapChooser";
    public override string ModuleVersion => "v1.2.3";
    public override string ModuleAuthor => "Sergey";
    public override string ModuleDescription => "Map chooser, voting, rtv, nominate, etc.";
    public MCCoreAPI MCCoreAPI { get; set; } = null!;
    private static PluginCapability<MCIAPI> MCAPICapability { get; } = new("ggmc:api"); 
    public readonly IStringLocalizer<MapChooser> _localizer;
    public MaxRoundsManager roundsManager;
    public MapChooser (IStringLocalizer<MapChooser> localizer)
    {
        _localizer = localizer;
        roundsManager = new(this);
    }
    public MCConfig Config { get; set; } = new();
    public void OnConfigParsed (MCConfig config)
    { 
        Config = config;
        if (Config.MapsInVote < 0 || Config.MapsInVote > 5)
        {
            Config.MapsInVote = 5;
            Logger.LogInformation("Set MapsInVote to 5 on plugin load because of an error in config.");
        }
        if (Config.VoteDependsOnRoundsWinned)
        {
            var MaxRounds = ConVar.Find("mp_maxrounds");
            var MaxRoundsValue = MaxRounds?.GetPrimitiveValue<int>() ?? 0;
            if (MaxRoundsValue < 2)
            {
                Logger.LogError($"VoteDependsOnRoundWins set true, but cvar mp_maxrounds set less than 2. Plugin can't work correctly with these settings.");
            }
        }
        if (Config.VoteDependsOnTimeLimit)
        {
            var TimeLimit = ConVar.Find("mp_timelimit");
            var TimeLimitValue = TimeLimit?.GetPrimitiveValue<float>() ?? 0;
            if (TimeLimitValue < 1)
            {
                Logger.LogError($"VoteDependsOnTimeLimit set true, but cvar mp_timelimit set less than 1. Plugin can't work correctly with these settings.");
            }
            var timeToVote = Config.VotingTime + 6;
            if (Config.TriggerSecondsBeforEnd < timeToVote)
            {
                Config.TriggerSecondsBeforEnd = timeToVote;
                Logger.LogInformation($"VoteDependsOnTimeLimit: TriggerSecondsBeforEnd updates to {timeToVote} which is minimum value for VotingTime {Config.VotingTime} in config.");
            }
            if (Config.TriggerSecondsBeforEnd < (TimeLimitValue * 60))
            {
                Logger.LogError($"VoteDependsOnTimeLimit set true, but TriggerSecondsBeforEnd is more than cvar mp_timelimit value. Plugin can't work correctly with these settings.");
            }
        }
    }
    public bool mapChangedOnStart = false; 
    private Random random = new();
    private string mapsFilePath = "";
    private Dictionary<string, MapInfo>Maps_from_List = new Dictionary<string, MapInfo>();
    private List<string> maplist = new();
    private List<string> nominatedMaps = new();
    private List<string> mapsToVote = new List<string>();
    public static HashSet<int> votePlayers = new HashSet<int>();
    private int mapstotal = 0;
    private bool canVote { get; set; } = false;
    private bool canRtv { get; set; } = false;
    private int rtv_can_start { get; set; } = 0;
    private int rtv_need_more { get; set; }
    private Timer? rtvTimer = null;
    private int rtvRestartProblems = 0;
    private Timer? changeRequested = null;
    private int RestartProblems = 0;
    private bool Restart = true;
    public bool IsVoteInProgress { get; set; } = false;
    private Timer? _timeLimitTimer = null;
    private Dictionary<string, int> optionCounts = new Dictionary<string, int>();
    private CounterStrikeSharp.API.Modules.Timers.Timer? voteTimer = null;
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
    public CCSGameRules GameRules = null!;
    public static IWasdMenuManager? WMenuManager;
    public override void Load(bool hotReload)
    {
        MCCoreAPI = new MCCoreAPI(this);
        if (MCCoreAPI != null)
        {
            Capabilities.RegisterPluginCapability(MCAPICapability, () => MCCoreAPI);
            Logger.LogInformation("GGMC API registered");
        }
        else
        {
            Logger.LogWarning("API not registered");
        }
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
        RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
        RegisterListener<Listeners.OnMapStart>(name =>
        {
            Logger.LogInformation(name + " loaded");
            ResetData("On Map Start");
            KillRtvTimer();
            canVote = ReloadMapcycle();
            if (Config.WorkshopMapProblemCheck && MapToChange != "" && MapToChange != name) // case when the server loaded the map different from requested in case the collection is broken, so we need to restart the server to fix the collection
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
            MapToChange = "";
            RestartProblems = 0;
/*            if (!mapChangedOnStart ) // just in case to check that a map from workshop is loaded after the first restart
            {
                AddTimer(20.0f, Timer_ChangeMapAfterRestart, TimerFlags.STOP_ON_MAPCHANGE);
            }*/
            
            if (!canVote)
            {
                Console.WriteLine(NoMapcycle);
                Logger.LogError(NoMapcycle);
                return;
            }
            if (_playedMaps.Count > 0)
            {   
                if ( _playedMaps.Count >= Config.RememberPlayedMaps)
                {
                    _playedMaps.RemoveAt(0);
                }
            }
            if (!_playedMaps.Contains(name))
                _playedMaps.Add(name);
            MakeRTVTimer (Config.RTVDelay);

            if (Config.RandomMapOnStart && !mapChangedOnStart && changeRequested == null)
            {
                Logger.LogInformation($"OnMapStart Requested map change after the server restarted");
                changeRequested = AddTimer((float)Config.RandomMapOnStartDelay, Timer_ChangeMapOnEmpty, TimerFlags.STOP_ON_MAPCHANGE);
                if (changeRequested == null)
                {
                    Console.WriteLine("*************** Could not create timer for map change on server start");
                    Logger.LogError("Could not create timer for map change on server start");
                }
            }
            AddTimer(3.0f, () => {
                GameRules = null!;
                GameRules = GetGameRules();
            });
        });
        RegisterListener<Listeners.OnMapEnd>(() => 
        {
            if (changeRequested != null)
            {
                changeRequested.Kill();
                changeRequested = null;
            }
            KillRtvTimer();
        });
        RegisterListener<Listeners.OnClientAuthorized>((slot, id) =>
        {
            var p = Utilities.GetPlayerFromSlot(slot);
            if (p != null && p.IsValid && slot < 65 && !p.IsBot && !p.IsHLTV)
            {
                players[slot] = new Player ();
                if (changeRequested != null)
                {
                    changeRequested.Kill();
                    changeRequested = null;
                }
            }
            else
            {
                Logger.LogInformation($"Don't create Player() for slot {slot}");
            }
        });
        RegisterListener<Listeners.OnClientDisconnectPost>(slot =>
        {            
            if (players[slot] != null)
            {
                if (players[slot].HasProposedMaps())
                    nominatedMaps.Remove(players[slot].ProposedMaps);
                if (IsRTVThreshold())  // если достаточно голосов - запускаем голосование
                {
                    StartRTV();
                    return;
                }       
                players[slot] = null!;
            } 

            var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
            if (playerEntities != null && playerEntities.Any())
            {
                return;
            }
            else
            {
                ResetData("On last client disconnect");
                if (Config.LastDisconnectedChangeMap && changeRequested == null && !Restart)
                {
                    Logger.LogInformation($"Requested map change on last disconnected player");
                    changeRequested = AddTimer(60.0f, Timer_ChangeMapOnEmpty, TimerFlags.STOP_ON_MAPCHANGE);
                }
            }
        });
                
        if (hotReload)
        {
            mapChangedOnStart = true;
            var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var playerController in playerEntities)
                {
                    if (players[playerController.Slot] != null)
                        players[playerController.Slot] = null!;
                    players[playerController.Slot] = new Player ();
                }
            }
        }
        AddTimer(2.0f, () => {
            GameRules = GetGameRules();
        });
    }
    public override void Unload(bool hotReload)
    {
        DeregisterEventHandler<EventRoundStart>(EventRoundStartHandler);
        DeregisterEventHandler<EventRoundEnd>(EventRoundEndHandler);
        DeregisterEventHandler<EventRoundAnnounceLastRoundHalf>(EventRoundAnnounceLastRoundHalfHandler);
        DeregisterEventHandler<EventRoundAnnounceMatchStart>(EventRoundAnnounceMatchStartHandler);
        DeregisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
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

        roundsManager.ClearRounds();
        return HookResult.Continue;
    }
    public HookResult EventRoundStartHandler(EventRoundStart @event, GameEventInfo info)
    {
        Restart = false;
        MapIsChanging = false;
        if (canVote)
        {
            if (!roundsManager.WarmupRunning && roundsManager.CheckMaxRounds())
            {
                Logger.LogInformation("Time to vote because of CheckMaxRounds");
                roundsManager.MaxRoundsVoted = true;
                StartVote();
            }
            else
            {
                Logger.LogInformation($"Round start, canVote {(canVote ? "True" : "False")}, Warmup {(roundsManager.WarmupRunning ? "True" : "False")} ");
            }
            if (Config.VoteDependsOnTimeLimit)
            {
                var TimeLimit = ConVar.Find("mp_timelimit");
                var TimeLimitValue = TimeLimit?.GetPrimitiveValue<float>() ?? 0;
                var timeToVote = Config.VotingTime + 6;

                if ((TimeLimitValue * 60) > Config.TriggerSecondsBeforEnd && Config.TriggerSecondsBeforEnd > timeToVote)
                {
                    _timeLimitTimer = AddTimer((float)((TimeLimitValue * 60) - Config.TriggerSecondsBeforEnd), TimeLimitTimerHandle, TimerFlags.STOP_ON_MAPCHANGE);
                }
                else
                {
                    Logger.LogError($"Vote Depends On TimeLimit can't be started");
                }
            }
        }
        return HookResult.Continue;
    }
    public HookResult EventRoundEndHandler(EventRoundEnd @event, GameEventInfo info)
    {
        if (_timeLimitTimer != null)
        {
            _timeLimitTimer.Kill();
            _timeLimitTimer = null;
        }
        Restart = true;
        if (@event is null)
            return HookResult.Continue;
        CsTeam? winner = Enum.IsDefined(typeof(CsTeam), (byte)@event.Winner) ? (CsTeam)@event.Winner : null;
        if (winner is not null)
            roundsManager.RoundWin(winner.Value);

        if (roundsManager.LastBeforeHalf)
            roundsManager.SwapScores();

        roundsManager.LastBeforeHalf = false;
        return HookResult.Continue;
    }
    public HookResult EventCsWinPanelMatchHandler(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        if (Config.ChangeMapAfterVote)
        {
            var delay = Config.EndOfMapDelayBeforeChangeSeconds - 5.0f;
            if (delay < 0)
                delay = 0;
            Logger.LogInformation($"EventCsWinPanelMatch: plugin is responsible for map change, delay for {delay} seconds before the change of map.");
            
            AddTimer(delay, () =>
            {
                if (_roundEndMap != null)
                {
                    DoMapChange(_roundEndMap, SSMC_ChangeMapTime.ChangeMapTime_Now);
                }
                else
                {
                    Logger.LogWarning("EventCsWinPanelMatch: _selectedMap is null, so don't change");
                }
            });
        }
        return HookResult.Continue;
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
        if (canVote)
        {
            Logger.LogInformation("Time to vote because of TimeLimitTimerHandle");
            StartVote();
        }
        else
        {
            Logger.LogInformation($"Round start, canVote {(canVote ? "True" : "False")}, Warmup {(roundsManager.WarmupRunning ? "True" : "False")} ");
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
        IsVoteInProgress = true;
        timeToVote = Config.VotingTime;
        VotesCounter = 0;
        voteTimer??= AddTimer(1.0f, EndOfVotes, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        DoAutoMapVote(null!, timeToVote, SSMC_ChangeMapTime.ChangeMapTime_MapEnd, Config.EndMapVoteWASDMenu);
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
            if ((mapcheck.Value.MinPlayers != 0 && numplayers < mapcheck.Value.MinPlayers) || (mapcheck.Value.MaxPlayers != 0 && numplayers > mapcheck.Value.MaxPlayers) || _playedMaps.Contains(mapcheck.Key))
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
            var mp_timelimit = ConVar.Find("mp_timelimit");
            if (mp_timelimit != null)
            {
                var timelimit = mp_timelimit.GetPrimitiveValue<float>();
                if (timelimit > 0)
                {
                    Logger.LogInformation($"DoMapChange: Extend Map is winning option. TimeLimit is {timelimit}. Time extended by {Config.ExtendMapTimeMinutes} minutes.");
                    mp_timelimit.SetValue(mp_timelimit.GetPrimitiveValue<float>() + Config.ExtendMapTimeMinutes);
                    Logger.LogInformation($"DoMapChange: TimeLimit now {mp_timelimit.GetPrimitiveValue<float>()}");
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
            return;
        }
        MapIsChanging = true;
        string mapname = mapChange;
        _selectedMap = null;
        PrintToServerCenter("mapchange.to", mapname);
        
        Console.WriteLine($"Map is changing to {mapname}");
        MapToChange = mapname;
        if (changeTime == SSMC_ChangeMapTime.ChangeMapTime_Now)
        {
            ChangeMapInFive(mapname);
        }
        else if (changeTime == SSMC_ChangeMapTime.ChangeMapTime_MapEnd)
        {
            MapIsChanging = false;
            Server.ExecuteCommand($"nextlevel {mapname}");
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
    private void ChangeMapInFive(string mapname)
    {
        AddTimer (5.0f, () => {
            if (Maps_from_List.TryGetValue(mapname, out var mapInfo))
            {
                MapIsChanging = false;
                if (mapInfo.WS)
                {
                    if (mapInfo.MapId.Length > 0)
                    {
                        Server.ExecuteCommand($"host_workshop_map {mapInfo.MapId}");
                    }
                    else
                    {
                        Server.ExecuteCommand($"ds_workshop_changelevel {mapname}");
                    }
                }
                else
                {
                    Server.ExecuteCommand($"changelevel {mapname}");
                }
            }
            else
            {
                Logger.LogError($"DoMapChange: can't find map in list {mapname}");
                MapIsChanging = false;
            }
        });
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
            if (limits && (_playedMaps.Contains(mapcheck.Key) || !playersvalid || IsNominated(mapcheck.Key))) //если включены лимиты и карта либо не валидна по списку 
            {                                              // карт, которые уже были, либо по кол-ву игроков или номинирована - исключаем
                playersvalid = true;
                continue;
            }
            if (haveSelectedMaps && selectedMapList.Contains(mapcheck.Key))
                continue;

            if (!playersvalid || _playedMaps.Contains(mapcheck.Key))
            {
                MapsMenu.AddMenuOption(mapcheck.Key + " (!)", action);
            }
            else
            {
                MapsMenu.AddMenuOption(mapcheck.Key, action);
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
        var manager = GetMenuManager();
        if(manager == null)
            return null!;
        /*************************************** Localizer *********************/
        IWasdMenu MapsMenu = manager.CreateMenu("List of maps:");
//        var MapsMenu = new ChatMenu("List of maps:");
      
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
            if (limits && (_playedMaps.Contains(mapcheck.Key) || !playersvalid || IsNominated(mapcheck.Key))) //если включены лимиты и карта либо не валидна по списку 
            {                                              // карт, которые уже были, либо по кол-ву игроков или номинирована - исключаем
                playersvalid = true;
                continue;
            }
            if (haveSelectedMaps && selectedMapList.Contains(mapcheck.Key))
                continue;

            if (!playersvalid || _playedMaps.Contains(mapcheck.Key))
            {
                MapsMenu.Add(mapcheck.Key + " (!)", action);
            }
            else
            {
                MapsMenu.Add(mapcheck.Key, action);
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
//    private void AdminChangeMapHandle(CCSPlayerController caller, ChatMenuOption option)
    private void AdminChangeMapHandle(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (IsValidPlayer(caller))
        {
            var manager = GetMenuManager();
            if(manager == null)
                return;
            IWasdMenu acm_menu = manager.CreateMenu(Localizer["choose.map"]);
            acm_menu.Add(Localizer["manual.map"], AdminChangeMapManual); // Simply change the map
            acm_menu.Add(Localizer["automatic.map"], AdminChangeMapAuto); // Start voting for map
            acm_menu.Prev = option.Parent?.Options?.Find(option);
            manager.OpenSubMenu(caller, acm_menu);

/*            var ChangeMapsMenu = new ChatMenu(Localizer["choose.map"]);
            ChangeMapsMenu.AddMenuOption(Localizer["manual.map"], AdminChangeMapManual);
            ChangeMapsMenu.AddMenuOption(Localizer["automatic.map"], AdminChangeMapAuto);
            MenuManager.OpenChatMenu(caller, ChangeMapsMenu); */
        }
    }
//  Админ выбрал ручной выбор для смены, выбор карты и смена
//    private void AdminChangeMapManual(CCSPlayerController player, ChatMenuOption option)
    private void AdminChangeMapManual(CCSPlayerController player, IWasdMenuOption option)
    {
        if (IsValidPlayer(player))
        {
            var manager = GetMenuManager();
            if(manager == null)
                return;
            manager.CloseMenu(player);
            IWasdMenu acmm_menu = CreateMapsMenuWASD(Handle_AdminManualChange, player, false); // no restrictions, because admn choose maps
            if (acmm_menu != null)
                manager.OpenMainMenu(player, acmm_menu);
            
/*            ChatMenu chatMenu = CreateMapsMenu(Handle_AdminManualChange, player, false); // no restrictions, because admn choose maps
            if (chatMenu != null)
            {
                MenuManager.OpenChatMenu(player, chatMenu);
            } */
        }
        return;
    }
//  Карта выбрана - меняем    
//    private void Handle_AdminManualChange(CCSPlayerController player, ChatMenuOption option)
    private void Handle_AdminManualChange(CCSPlayerController player, IWasdMenuOption option)
    {
        var manager = GetMenuManager();
        if(manager == null)
            return;
        if (option == null || option.OptionDisplay == null)
        {
            Logger.LogInformation("[GGMC]: Admin " + player.PlayerName + " has chosen map for manual change but option is null.");
            manager.CloseMenu(player);
            return;
        }
        string map = ClearSuffix(option.OptionDisplay);
        
        Logger.LogInformation($"[GGMC]: Admin {player.PlayerName} has chosen map {map} for manual change.");
        manager.CloseMenu(player);
        DoMapChange(map, SSMC_ChangeMapTime.ChangeMapTime_Now);
    }
//  Админ выбрал автоматический выбор для смены, отсылка на GGMCDoAutoMapChange, которая с этим справляется
//    private void AdminChangeMapAuto(CCSPlayerController player, ChatMenuOption option)
    private void AdminChangeMapAuto(CCSPlayerController player, IWasdMenuOption option)
    {
        Logger.LogInformation("[GGMC]: Admin " + player.PlayerName + " has chosen auto map change.");
        var manager = GetMenuManager();
        if(manager == null)
            return;
        manager.CloseMenu(player);
        GGMCDoAutoMapChange(SSMC_ChangeMapTime.ChangeMapTime_Now);
    }
//  Админ запускает общее голосования за выбор карты - выбор карт для голосования ручной или автоматом
//    private void AdminStartVotesMapHandle(CCSPlayerController caller, ChatMenuOption option)
    private void AdminStartVotesMapHandle(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (IsValidPlayer(caller))
        {
            Logger.LogInformation($"[GGMC]: Admin {caller.PlayerName} want to start vote for map.");
            
            var manager = GetMenuManager();
            if(manager == null)
                return;
            IWasdMenu acvm_menu = manager.CreateMenu(Localizer["choose.map"]);
            acvm_menu.Add(Localizer["manual.map"], AdminVoteMapManual); // Simply change the map
            acvm_menu.Add(Localizer["automatic.map"], AdminVoteMapAuto); // Start voting for map
            acvm_menu.Prev = option.Parent?.Options?.Find(option);
            manager.OpenSubMenu(caller, acvm_menu);
                        
/*            var ChangeMapsMenu = new ChatMenu(Localizer["choose.map"]);
            ChangeMapsMenu.AddMenuOption(Localizer["manual.map"], AdminVoteMapManual);
            ChangeMapsMenu.AddMenuOption(Localizer["automatic.map"], AdminVoteMapAuto);
            MenuManager.OpenChatMenu(caller, ChangeMapsMenu); */
        }
    }
//  Админ выбрал ручной выбор карт    
//    private void AdminVoteMapManual(CCSPlayerController player, ChatMenuOption option)
    private void AdminVoteMapManual(CCSPlayerController player, IWasdMenuOption option)
    {
        if (IsValidPlayer(player))
        {
            var manager = GetMenuManager();
            if(manager == null)
                return;
            manager.CloseMenu(player);
            IWasdMenu avmm_menu = CreateMapsMenuWASD(Handle_VoteMapManual, player, false); // no restrictions, because admn choose maps
            if (avmm_menu != null)
                manager.OpenMainMenu(player, avmm_menu);
            
/*            ChatMenu chatMenu = CreateMapsMenu(Handle_VoteMapManual, player, false); // no restrictions, because admn choose maps
            if (chatMenu != null)
            {
                MenuManager.OpenChatMenu(player, chatMenu);
            } */
        }
        return;
    }
//  Обработка процесса, пока админ набирает карты. Когда готово - запуск голосования
//    private void Handle_VoteMapManual(CCSPlayerController caller, ChatMenuOption option)
    private void Handle_VoteMapManual(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (IsValidPlayer(caller) && option != null && option.OptionDisplay != null)
        {
            var manager = GetMenuManager();
            if(manager == null)
                return;
            
            string stopline = Localizer["stop.line", players[caller.Slot].selectedMaps.Count];
            string fromMenu = option.OptionDisplay;

            if (fromMenu == stopline)
            {
                manager.CloseMenu(caller);
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
                if (players[caller.Slot].selectedMaps.Count == Config.MapsInVote)
                {
                    manager.CloseMenu(caller);
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
                DoAutoMapVote(caller, Config.VotingTime, SSMC_ChangeMapTime.ChangeMapTime_Now );
            }
        }
    }
//    private void AdminVoteMapAuto(CCSPlayerController player, ChatMenuOption option)
    private void AdminVoteMapAuto(CCSPlayerController player, IWasdMenuOption option)
    {
        var manager = GetMenuManager();
        if(manager == null)
            return;
        manager.CloseMenu(player);
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
        IsVoteInProgress = true;
        timeToVote = Config.VotingTime;
        VotesCounter = 0;
    
        voteTimer??= AddTimer(1.0f, EndOfVotes, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        DoAutoMapVote(null!, timeToVote, SSMC_ChangeMapTime.ChangeMapTime_Now);
    }
//  Старт голосования "менять карту или нет"
//    private void VotesForChangeMapHandle(CCSPlayerController caller, ChatMenuOption option)
    private void VotesForChangeMapHandle(CCSPlayerController caller, IWasdMenuOption option)
    {
        if (IsValidPlayer(caller))
        {
            if (IsVoteInProgress)
            {
                caller.PrintToChat(Localizer["vote.inprogress"]);
                return;
            }
            Logger.LogInformation($"[GGMC]: Admin {caller.PlayerName} started vote to change map or not.");
            IsVoteInProgress = true;
            optionCounts.Clear();
            votePlayers.Clear();
            var ChangeOrNotMenu = new ChatMenu(Localizer["vote.changeornot"]);
            ChangeOrNotMenu.AddMenuOption(Localizer["vote.yes"], (player, option) =>
            {
                if (!votePlayers.Contains(player.Slot))
                {
                    votePlayers.Add(player.Slot);
                    if (!optionCounts.TryGetValue("Yes", out int count))
                        optionCounts["Yes"] = 1;
                    else
                        optionCounts["Yes"] = count + 1;
                    Server.PrintToChatAll(Localizer["player.voteforchange", player.PlayerName]);
                }
            });
            ChangeOrNotMenu.AddMenuOption(Localizer["vote.no"], (player, option) =>
            {
                if (!votePlayers.Contains(player.Slot))
                {
                    votePlayers.Add(player.Slot);
                    if (!optionCounts.TryGetValue("No", out int count))
                        optionCounts["No"] = 1;
                    else
                        optionCounts["No"] = count + 1;
                    Server.PrintToChatAll(Localizer["player.voteagainstchange", player.PlayerName, option.Text]);
                }
            });
//            ChangeOrNotMenu.PostSelectAction = PostSelectAction.Close;
        
            var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
            if (playerEntities != null && playerEntities.Any())
            {
                foreach (var player in playerEntities)
                {
                    MenuManager.OpenChatMenu(player, ChangeOrNotMenu);
                }

                AddTimer((float)timeToVote, () => TimerChangeOrNot());
            }
        }
    }
    private void TimerChangeOrNot()
    {
        IsVoteInProgress = false;
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
    private void DoAutoMapVote(CCSPlayerController caller, int timeToVote = 20, SSMC_ChangeMapTime changeTime = SSMC_ChangeMapTime.ChangeMapTime_Now, bool wasdmenu = false)
    {
        if (!canVote)
        {
            Console.WriteLine("[GGMC] Can't vote now");
            return;
        }
        IWasdMenu voteMenuWM = null!;
        IWasdMenuManager? manager = null;
        ChatMenu voteMenu = null!;
        optionCounts.Clear();
        votePlayers.Clear();
        if (wasdmenu)
        {
            manager = GetMenuManager();
            if(manager == null)
            {
                Logger.LogError("[GGMC] Can't get menu manager");
                return;
            }
            voteMenuWM = manager.CreateMenu(Localizer["choose.map"]);
        }
        else
        {
            voteMenu = new ChatMenu(Localizer["choose.map"]);
        }
        mapsToVote.Clear();
        int mapsinvote = 0, i = 0;
        // If called by admin, he has selected maps to vote
        if (IsValidPlayer(caller) && players[caller.Slot].selectedMaps.Count > 1)
        {
            foreach (var mapName in players[caller.Slot].selectedMaps)
            {
                mapsToVote.Add(mapName);
                if (++mapsinvote == Config.MapsInVote) break;
            }
        }
        else // otherwise we select random maps
        {
            if (nominatedMaps.Count > 0)
            {
                foreach (var mapName in nominatedMaps)
                {
                    mapsToVote.Add(mapName);
                    if (++mapsinvote == Config.MapsInVote) break;
                }
            }
            if (mapsinvote < Config.MapsInVote)
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
                    if ((mapcheck.Value.MinPlayers != 0 && numplayers < mapcheck.Value.MinPlayers) || (mapcheck.Value.MaxPlayers != 0 && numplayers > mapcheck.Value.MaxPlayers) || _playedMaps.Contains(mapcheck.Key))
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
                    return;
                }
                
                int mapstochose = Config.MapsInVote - mapsinvote;
                if (mapstochose > 0)
                {
                    int cycles = 30; // если карты будут дублироваться, то повторных циклов не больше этого числа
                    int choice, map;
                    i = 0;
                    do 
                    {
                        choice = random.Next(1, validmapweight[validmaps]);
                        map = WeightedMap(validmaps, validmapweight, choice);
                        if (map < 1)
                        {
                            Logger.LogError($"Cannot choose map. players {numplayers}, validmaps {validmaps}");
                            continue;
                        }
                        if (mapsToVote.Contains(validmapnames[map]))
                            continue;
                        mapsToVote.Add(validmapnames[map]);
                        i++;
                    } while (i < mapstochose && i < cycles);
                    mapsinvote += i;
                }
            }
        }
        if (mapsinvote == 0)
        {
            Console.WriteLine("DoAutoMapVote: no maps for the vote. Exit with error.");
            Logger.LogError("DoAutoMapVote: no maps for the vote. Exit with error.");
            return;
        }
        if (Config.ExtendMapInVote)
        {
            if (wasdmenu)
            {
                voteMenuWM.Add(Localizer["extend.map"], (player, option) =>
                {
                    if (!votePlayers.Contains(player.Slot) && option != null && option.OptionDisplay != null)
                    {
                        votePlayers.Add(player.Slot);
                        
                        if (!optionCounts.TryGetValue("extend.map", out int count))
                            optionCounts["extend.map"] = 1;
                        else
                            optionCounts["extend.map"] = count + 1;
                        _votedMap++;
                        PrintToServerChat("player.choice", player.PlayerName, option.OptionDisplay);
                    }
                    var manager = GetMenuManager();
                    if(manager == null)
                        return;
                    manager.CloseMenu(player);
                });
            }
            else
            {
                voteMenu.AddMenuOption(Localizer["extend.map"], (player, option) =>
                {
                    if (!votePlayers.Contains(player.Slot))
                    {
                        votePlayers.Add(player.Slot);
                        
                        if (!optionCounts.TryGetValue("extend.map", out int count))
                            optionCounts["extend.map"] = 1;
                        else
                            optionCounts["extend.map"] = count + 1;
                        _votedMap++;
                        PrintToServerChat("player.choice", player.PlayerName, option.Text);
                    }
                });
            }
        }

        for (i = 0; i < mapsinvote; i++)
        {
            try
            {
                if (wasdmenu)
                {
                    voteMenuWM.Add($"{mapsToVote[i]}", (player, option) =>
                    {
                        if (!votePlayers.Contains(player.Slot) && option != null && option.OptionDisplay != null)
                        {
                            votePlayers.Add(player.Slot);
                            
                            if (!optionCounts.TryGetValue(option.OptionDisplay, out int count))
                                optionCounts[option.OptionDisplay] = 1;
                            else
                                optionCounts[option.OptionDisplay] = count + 1;
                            _votedMap++;
                            PrintToServerChat("player.choice", player.PlayerName, option.OptionDisplay);
        //                    Server.PrintToChatAll(Localizer["player.choice", player.PlayerName, option.Text]);
                        }
                        var mngr = GetMenuManager();
                        if(mngr == null)
                            return;
                        mngr.CloseMenu(player);
                    });
                }
                else
                {
                    voteMenu.AddMenuOption($"{mapsToVote[i]}", (player, option) =>
                    {
                        if (!votePlayers.Contains(player.Slot)) // if contains - means we have his vote already and skip this vote
                        {
                            votePlayers.Add(player.Slot);
                            
                            if (!optionCounts.TryGetValue(option.Text, out int count))
                                optionCounts[option.Text] = 1;
                            else
                                optionCounts[option.Text] = count + 1;
                            _votedMap++;
                            PrintToServerChat("player.choice", player.PlayerName, option.Text);
        //                    Server.PrintToChatAll(Localizer["player.choice", player.PlayerName, option.Text]);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred during voting menu preparation: {ex.Message}");
                Logger.LogError($"An error occurred during voting menu preparation: {ex.Message}");
            }
        }
        
        if (!wasdmenu)
        {
            voteMenu.PostSelectAction = PostSelectAction.Close;   
        }
        
        var playerEntities = Utilities.GetPlayers().Where(p => IsValidPlayer(p));
        
        foreach (var player in playerEntities)
        {
            if (wasdmenu)
                manager?.OpenMainMenu(player, voteMenuWM);
            else
                MenuManager.OpenChatMenu(player, voteMenu);
            player.ExecuteClientCommand("play " + Config.VoteStartSound);
        }

        AddTimer((float)timeToVote, () => TimerVoteMap(changeTime));
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
            if (nominatedMaps.Count < Config.MapsInVote)
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
        if (rtv_can_start > 0) //Работает таймер задержки
        {
            caller.PrintToChat(Localizer["nortv.time", rtv_can_start]);
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

    [ConsoleCommand("nextmap", "NextMap")]
    public void PrintNextMap(CCSPlayerController caller, CommandInfo command)
    {
        Logger.LogInformation($"Nextmap {(_selectedMap != null ? _selectedMap : "null")}");
        if (IsValidPlayer(caller))
        {
            if (_selectedMap != null && _selectedMap.Length > 0)
            {
                PrintToPlayerChat(caller, "nextmap.info", _selectedMap);
            }
            else
            {
                PrintToPlayerChat(caller, "nextmap.none");
            }
        }
        else
        {
            if (_selectedMap != null && _selectedMap.Length > 0)
                Console.WriteLine($"Next Map: {_selectedMap}");
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
        var manager = GetMenuManager();
        if(manager == null)
            return;
        IWasdMenu menu = manager.CreateMenu(Localizer["maps.menu"]);
        menu.Add(Localizer["change.map"], AdminChangeMapHandle); // Simply change the map
        menu.Add(Localizer["votefor.map"], AdminStartVotesMapHandle); // Start voting for map
        menu.Add(Localizer["vote.changeornot"], VotesForChangeMapHandle); // Start voting to change map or not
        manager.OpenMainMenu(caller, menu);

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
        timeToVote = Config.VotingTime;
        VotesCounter = 0;
        if (command != null && command.ArgCount > 1)
        {
            if (int.TryParse(command.ArgByIndex(1), out int intValue) && intValue > 0 && intValue < 59)
            {
                timeToVote = intValue;
            }
        }
        voteTimer??= AddTimer(1.0f, EndOfVotes, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        DoAutoMapVote(null!, timeToVote, SSMC_ChangeMapTime.ChangeMapTime_MapEnd, Config.EndMapVoteWASDMenu);
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
        timeToVote = Config.VotingTime;
        VotesCounter = 0;
        if (command != null && command.ArgCount > 1)
        {
            if (int.TryParse(command.ArgByIndex(1), out int intValue) && intValue > 0 && intValue < 59)
            {
                timeToVote = intValue;
            }
        }
        voteTimer??= AddTimer(1.0f, EndOfVotes, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        DoAutoMapVote(null!, timeToVote, SSMC_ChangeMapTime.ChangeMapTime_Now);
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
        if (command.ArgCount < 1)
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
        KillVoteTimer();
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
            var manager = GetMenuManager();
            if(manager != null)
            {
                IWasdMenu nominate_menu = CreateMapsMenuWASD(Handle_Nominations, player); 
                if (nominate_menu != null)
                    manager.OpenMainMenu(player, nominate_menu);
            }
/*            
            ChatMenu chatMenu = CreateMapsMenu(Handle_Nominations, player);
            if (chatMenu != null)
            {
                MenuManager.OpenChatMenu(player, chatMenu);
                return Nominations.Nominated;
            } */
        }
    }
//    private void Handle_Nominations(CCSPlayerController player, ChatMenuOption option)
    private void Handle_Nominations(CCSPlayerController player, IWasdMenuOption option)
    {
        if(!IsValidPlayer(player) || option == null || option.OptionDisplay == null)
            return;
        
        TryNominate (player, option.OptionDisplay);
        
        var manager = GetMenuManager();
        if (manager != null)
        {
            manager.CloseMenu(player);
        }
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
                if ((mapcheck.Value.MinPlayers != 0 && numplayers < mapcheck.Value.MinPlayers) || (mapcheck.Value.MaxPlayers != 0 && numplayers > mapcheck.Value.MaxPlayers) || _playedMaps.Contains(mapcheck.Key))
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
                IsVoteInProgress = false;
                _roundEndMap = _selectedMap;
                DoMapChange(_selectedMap, changeTime);
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
        rtvRestartProblems = 0;
        roundsManager.InitialiseMap();
        MapIsChanging = false;
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
    private void KillVoteTimer()
    {
        if (voteTimer != null)
        {
            voteTimer.Kill();
            voteTimer = null;
        }
    }
    private void KillRtvTimer()
    {
        if (rtvTimer != null)
        {
            rtvTimer.Kill();
            rtvTimer = null;
        }
    }
    private void MakeRTVTimer (int interval)  // таймер для того, чтобы если кто-то напишет rtv, ему написали, через сколько секунд можно
    {
        canRtv = false;
        rtv_can_start = interval;
        rtvTimer??= AddTimer(1.0f, Handle_RTVTimer, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        if (rtvTimer == null)
        {
            Logger.LogError("Could not create rtvTimer!");
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
        KillRtvTimer();
        KillVoteTimer();
        return;
    }
    private bool IsRTVThreshold()
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
            
            rtv_need_more = (int)Math.Ceiling((Config.VotesToWin - percent_now) * total);
            if ((total == 1 && rtvs == 1) || rtv_need_more <= 0)
            {
                return true;
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
        MakeRTVTimer(Config.RTVInterval);
        DoAutoMapVote(null!, Config.VotingTime, SSMC_ChangeMapTime.ChangeMapTime_Now );
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
        return map;
    }
    private static CCSGameRules GetGameRules()
    {
        return CounterStrikeSharp.API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules!;
    }
    public IWasdMenuManager? GetMenuManager()
    {
        if (WMenuManager == null)
        {
            try
            {
                WMenuManager = new PluginCapability<IWasdMenuManager>("wasdmenu:manager").Get();
                if (WMenuManager == null)
                {
                    Logger.LogError("GG1MapChooser: wasdmenu:manager not found");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("GG1MapChooser: wasdmenu:manager not found: " + ex.Message);
            }
        }

        return WMenuManager;
    }
    public enum Nominations
    {
        Nominated = 0,
        AlreadyNominated,
        NoMap,
        NotNow,
        Error
    };
}
public class MCConfig : BasePluginConfig 
{
    [JsonPropertyName("RememberPlayedMaps")]
    public int RememberPlayedMaps { get; set; } = 3;

    /* Time (in seconds) before first RTV can be held. */
    [JsonPropertyName("RTVDelay")]
    public int RTVDelay { get; set; } = 90;
    
    /* Time (in seconds) after a failed RTV before another can be held. */
    [JsonPropertyName("RTVInterval")]
    public int RTVInterval { get; set; } = 120;

    /* Time in seconds to wait while players make their choice */
    [JsonPropertyName("VotingTime")]
    public int VotingTime { get; set; } = 25;

    /* End of Map Vote in WASD menu */
    [JsonPropertyName("EndMapVoteWASDMenu")]
    public bool EndMapVoteWASDMenu { get; set; } = false;
    
    /* Number of maps in votre for players 1-7 */
    [JsonPropertyName("MapsInVote")]
    public int MapsInVote { get; set; } = 5;

    /* Add Extend Map option to vote */
    [JsonPropertyName("ExtendMapInVote")]
    public bool ExtendMapInVote { get; set; } = false;

    /* time to Extend Map */
    [JsonPropertyName("ExtendMapTimeMinutes")]
    public int ExtendMapTimeMinutes { get; set; } = 10;

    /* Percent of players required to win rtv. Spectators without a vote do not counts */
    [JsonPropertyName("VotesToWin")]
    public double VotesToWin { get; set; } = 0.6;

    /* Plugin will Change the Map after the vote for map */
    [JsonPropertyName("ChangeMapAfterVote")]
    public bool ChangeMapAfterVote { get; set; } = false;

    /* Delay before Plugin will Change the Map after the map end */
    [JsonPropertyName("EndOfMapDelayBeforeChangeSeconds")]
    public int EndOfMapDelayBeforeChangeSeconds { get; set; } = 20;

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
    
    /* Check if problems with Workshop map (if it doesn't exists, server default map will be loaded, so plugin change to a random map) */
    [JsonPropertyName("WorkshopMapProblemCheck")]
    public bool WorkshopMapProblemCheck { get; set; } = true;

    /* Set True if Vote start depends on number of Round Wins by CT or T  */
    [JsonPropertyName("VoteDependsOnRoundWins")]
    public bool VoteDependsOnRoundsWinned { get; set; } = false;
    
    /* Number of maps in votre for players 1-7 */
    [JsonPropertyName("TriggerRoundsBeforEnd")]
    public int TriggerRoundsBeforEnd { get; set; } = 1;

    /* Set True if Vote start depends on Game Time Limit (cvar: mp_timelimit)  */
    [JsonPropertyName("VoteDependsOnTimeLimit")]
    public bool VoteDependsOnTimeLimit { get; set; } = false;
    
    /* Number of seconds before the end to run the vote */
    [JsonPropertyName("TriggerSecondsBeforEnd")]
    public int TriggerSecondsBeforEnd { get; set; } = 35;
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
    }
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
        CvarMaxRounds = ConVar.Find("mp_maxrounds");
        CvarCanClinch = ConVar.Find("mp_match_can_clinch");
        MaxRoundsValue = CvarMaxRounds?.GetPrimitiveValue<int>() ?? 0;
        CanClinch = CvarCanClinch?.GetPrimitiveValue<bool>() ?? true;
    }
    MapChooser Plugin;
    private int CTWins = 0;
    private int TWins = 0;
    public ConVar? CvarMaxRounds;
    public ConVar? CvarCanClinch;
    public bool MaxRoundsVoted = false;
    public int MaxRoundsValue;
    public bool CanClinch;
    public bool UnlimitedRounds => MaxRoundsValue <= 0;
    public bool LastBeforeHalf = false;
    public bool WarmupRunning => Plugin.GameRules?.WarmupPeriod ?? false;
    public void InitialiseMap()
    {
        MaxRoundsValue = CvarMaxRounds?.GetPrimitiveValue<int>() ?? 0;
        CanClinch = CvarCanClinch?.GetPrimitiveValue<bool>() ?? true;
        ClearRounds();
        MaxRoundsVoted = false;
        Plugin.Logger.LogInformation($"On Initialise Map set: MaxRoundsValue {MaxRoundsValue}, CanClinch {(CanClinch ? "true" : "false")}");
    }
    public int RemainingRounds
    {
        get
        {
            var played = MaxRoundsValue - (Plugin.GameRules != null ? Plugin.GameRules.TotalRoundsPlayed : 0);
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
        if (!Plugin.Config.VoteDependsOnRoundsWinned || UnlimitedRounds || MaxRoundsVoted)
        {
            return false;
        }

        if (RemainingRounds <= Plugin.Config.TriggerRoundsBeforEnd)
            return true;

        return CanClinch && RemainingWins <= Plugin.Config.TriggerRoundsBeforEnd;
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