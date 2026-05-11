using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace MapChooser;

public class PanoramaVote
{
    private MapChooser _plugin;
    private CVoteController? _voteController;
    private List<int> _playerIDs { get; set; } = new();
    private Dictionary<int, int> _voters { get; set; } = [];
    private RecipientFilter _recipientFilter = new RecipientFilter();
    //    public Action<PanoramaVote, bool>? Callback { get; set; }
    private ConVar? _allowVotes;
    private bool _allowVotesOriginalValue = false;
    private bool _IsVoteInProgress = false;
    public delegate void PanoramaVoteHandler(PanoramaVoteAction action, int param1, int param2);
    public delegate bool PanoramaVoteResult(PanoramaVoteInfo info);
    private PanoramaVoteHandler? _voteHandler = null;
    private PanoramaVoteResult? _voteResult = null;

    public PanoramaVote(MapChooser plugin)
    {
        //Action<PanoramaVote, bool> callback
        _plugin = plugin;
        _plugin.RegisterEventHandler<EventVoteCast>(OnVoteCast);
        _plugin.RegisterListener<Listeners.OnMapEnd>(OnMapEnd);

        _allowVotes = ConVar.Find("sv_allow_votes");
        if (_allowVotes != null)
        {
            _allowVotesOriginalValue = _allowVotes.GetPrimitiveValue<bool>();
            _allowVotes.GetPrimitiveValue<bool>() = true;
        }
        else
        {
            _plugin.Logger.LogWarning("ConVar sv_allow_votes not found - votes might not work as expected");
        }
        InitVoteController();

        //        Server.PrintToChatAll("PanoramaVote created with players: " + string.Join(", ", _playerIDs));
    }
    private void InitVoteController()
    {

        var voteController = Utilities.FindAllEntitiesByDesignerName<CVoteController>("vote_controller").Last();
        if (voteController == null || !voteController.IsValid)
        {
            _plugin.Logger.LogInformation("[PanoramaVote] VoteController not available - creating a new one");
            voteController = Utilities.CreateEntityByName<CVoteController>("vote_controller");
        }
        if (voteController == null || !voteController.IsValid)
        {
            _plugin.Logger.LogError("[PanoramaVote] VoteController could not be created - aborting");
            return;
        }
        _voteController = voteController;
    }
    public void StartVote(float votingTime, string sfui, string menuText, PanoramaVoteHandler? voteHandler = null, PanoramaVoteResult? resultCallback = null)
    {
        if (_voteController == null || !_voteController.IsValid)
        {
            _plugin.Logger.LogError("[PanoramaVote] VoteController not available - aborting StartVote");
            return;
        }
        UpdatePlayerIDs();
        ResetVoteController();
        // set default values to vote controller
        _voteController.PotentialVotes = _playerIDs.Count;
        _voteController.ActiveIssueIndex = 2; // don't know what this does
                                              //        _voteController.IsYesNoVote = true;
        _voteHandler = voteHandler;
        _voteResult = resultCallback;

        // send vote update to panorama
        UpdateVoteCounts();
        // send user message to panorama
        SendMessageVoteStart(menuText, sfui);
        // add a timer to end the vote after the specified time
        _plugin.AddTimer(votingTime, () =>
        {
            EndVote(PanoramaVoteEndReason.VoteEnd_TimeUp);
        }, TimerFlags.STOP_ON_MAPCHANGE);
        _plugin.Logger.LogInformation("[PanoramaVote] Vote user message sent.");
    }
    private void UpdatePlayerIDs()
    {
        var players = Utilities.GetPlayers().Where(static p => p != null && p.IsValid && p.Connected == PlayerConnectedState.Connected && !p.IsBot && !p.IsHLTV);
        foreach (var player in players)
        {
            _recipientFilter.Add(player);
        }
        // check if playerIDs in players list if not empty
        if (_playerIDs.Count > 0)
        {
            // get playerids which are disconnected
            List<int> missingPlayers = _playerIDs.Except(players.Select(static p => p.UserId!.Value)).ToList();
            foreach (int missingPlayer in missingPlayers)
            {
                // remove missing players from lists
                _ = _voters.Remove(missingPlayer);
                _ = _playerIDs.Remove(missingPlayer);
            }
        }
        else
        {
            // if playerIDs is empty, add all players
            _playerIDs = [.. players.Where(static p => p.UserId.HasValue).Select(static p => p.UserId!.Value)];
        }
    }
    private void ResetVoteController()
    {
        if (_voteController == null || !_voteController.IsValid)
        {
            return;
        }
        // reset vote controller
        foreach (int i in Enumerable.Range(0, 5))
        {
            _voteController.VoteOptionCount[i] = 0;
        }

        for (int i = 0; i < Server.MaxPlayers; i++)
        {
            _voteController.VotesCast[i] = (int)VoteOptions.REMOVE;
        }
    }
    private void UpdateVoteCounts()
    {
        var _event = NativeAPI.CreateEvent("vote_changed", true);
        NativeAPI.SetEventInt(_event, "vote_option1", GetYesVotes());
        NativeAPI.SetEventInt(_event, "vote_option2", GetNoVotes());
        NativeAPI.SetEventInt(_event, "vote_option3", 0);
        NativeAPI.SetEventInt(_event, "vote_option4", 0);
        NativeAPI.SetEventInt(_event, "vote_option5", 0);
        NativeAPI.SetEventInt(_event, "potentialVotes", _playerIDs.Count);
        NativeAPI.FireEvent(_event, false);
    }
    private void SendMessageVoteStart(string menuText, string sfui)
    {
        if (_voteController == null || !_voteController.IsValid)
        {
            return;
        }
        _IsVoteInProgress = true;
        // set recipients which should get the message (if applicable)
        RecipientFilter recipientFilter = [];
        // Send message to each recipient to allow individual translation.
        _plugin.Logger.LogInformation($"[PanoramaVote] Starting vote with {_playerIDs.Count} eligible players.");
        foreach (int playerID in _playerIDs)
        {
            CCSPlayerController? player = Utilities.GetPlayerFromUserid(playerID);
            if (player == null || !player.IsValid)
            {
                continue;
            }
            recipientFilter.Clear();
            recipientFilter.Add(player);

            // get translation for player (if available), otherwise use server language, otherwise use first entry
            //            string text = vote.Text.TryGetValue(playerLanguageManager.GetLanguage(new SteamID(player.NetworkIDString)).TwoLetterISOLanguageName, out string? playerLanguage) ? playerLanguage
            //                : vote.Text.TryGetValue(CoreConfig.ServerLanguage, out string? serverLanguage) ? serverLanguage
            //                : vote.Text.First().Value ?? string.Empty;
            //            UserMessage userMessage = UserMessage.FromPartialName("VoteStart");
            UserMessage userMessage = UserMessage.FromId(346); //CS_UM_VoteStart = 346;
            userMessage.SetInt("team", -1);
            userMessage.SetInt("player_slot", 99);
            userMessage.SetInt("vote_type", -1); // Reset
            userMessage.SetString("disp_str", "#SFUI_vote_changelevel"); //sfui
            userMessage.SetString("details_str", _plugin.Localizer.ForPlayer(player, menuText)); //_plugin.Localizer.ForPlayer(player, menuText));
            userMessage.SetBool("is_yes_no_vote", true);
            userMessage.Send(recipientFilter);
        }
    }
    private HookResult OnVoteCast(EventVoteCast @event, GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;
        if (player == null || !player.IsValid || !player.UserId.HasValue || !_playerIDs.Contains(player.UserId.Value))
        {
            return HookResult.Continue;
        }

        // check which option got voted for
        VoteOptions votedOption = (VoteOptions)@event.VoteOption;
        if (_voteHandler != null)
        {
            _voteHandler(PanoramaVoteAction.VoteAction_Vote, player.Slot, (int)votedOption);
        }
        if (votedOption == VoteOptions.YES)
        {
            OnVoteYes(player.UserId.Value);
        }
        else if (votedOption == VoteOptions.NO)
        {
            OnVoteNo(player.UserId.Value);
        }
        // send update to panorama
        UpdateVoteCounts();
        // end the vote if all players have voted
        if (CheckIfVoteShouldEnd())
        {
            Server.NextFrame(() => EndVote(PanoramaVoteEndReason.VoteEnd_AllVotes));
        }

        return HookResult.Continue;
    }
    public void OnVoteYes(int playerID)
    {
        // add player to vote list
        _voters[playerID] = (int)VoteOptions.YES;
        // update playerIDs
        UpdatePlayerIDs();
    }
    public void OnVoteNo(int playerID)
    {
        // add player to vote list
        _voters[playerID] = (int)VoteOptions.NO;
        // update playerIDs
        UpdatePlayerIDs();
    }
    private void SendMessageVoteEnd(bool success)
    {
        // set recipients which should get the message (if applicable)
        RecipientFilter recipientFilter = [];
        foreach (int playerID in _playerIDs)
        {
            CCSPlayerController? player = Utilities.GetPlayerFromUserid(playerID);
            if (player == null || !player.IsValid)
            {
                continue;
            }

            recipientFilter.Add(player);
        }
        // send user message to indicate vote result
        if (!success)
        {
            UserMessage userMessage = UserMessage.FromPartialName("VoteFailed");
            userMessage.SetInt("reason", 0);
            userMessage.Send(recipientFilter);
            return;
        }
        else
        {
            UserMessage userMessage = UserMessage.FromPartialName("VotePass");
            userMessage.SetInt("team", -1);
            userMessage.SetInt("vote_type", 1); // custom vote (UNKNOWN)
            userMessage.SetString("disp_str", "#SFUI_vote_passed");
            userMessage.SetString("details_str", "Vote passed"); // ********** translation
            userMessage.Send(recipientFilter);
        }
    }
    public int GetYesVotes()
    {
        return _voters.Count(static v => v.Value == (int)VoteOptions.YES);
    }
    public int GetNoVotes()
    {
        return _voters.Count(static v => v.Value == (int)VoteOptions.NO);
    }
    public bool CheckIfVoteShouldEnd()
    {
        int remainingVotes = _playerIDs.Count - (GetYesVotes() + GetNoVotes());
        return GetYesVotes() + GetNoVotes() >= _playerIDs.Count;
    }

    private void EndVote(PanoramaVoteEndReason reason)
    {
        if (!_IsVoteInProgress)
            return;

        _IsVoteInProgress = false;

        switch (reason)
        {
            case PanoramaVoteEndReason.VoteEnd_AllVotes:
                _plugin.Logger.LogInformation($"[PanoramaVote Ending] All possible players voted: {_playerIDs.Count}.");
                break;
            case PanoramaVoteEndReason.VoteEnd_TimeUp:
                _plugin.Logger.LogInformation($"[PanoramaVote Ending] Time ran out.");
                break;
            case PanoramaVoteEndReason.VoteEnd_Cancelled:
                _plugin.Logger.LogInformation($"[PanoramaVote Ending] The vote has been cancelled.");
                break;
        }

        _plugin.DeregisterEventHandler<EventVoteCast>(OnVoteCast);
        _plugin.RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
        if (_allowVotes != null)
        {
            _allowVotes.GetPrimitiveValue<bool>() = _allowVotesOriginalValue;
        }

        if (_voteHandler != null)
            _voteHandler(PanoramaVoteAction.VoteAction_End, (int)reason, 0);

        if(_voteController == null)
        {
            SendVoteFailed(reason);
            return;
        }
        if (_voteResult == null || reason == PanoramaVoteEndReason.VoteEnd_Cancelled)
        {
            SendVoteFailed(reason);
            _voteController.ActiveIssueIndex = -1;
            return;
        }
        PanoramaVoteInfo info = new PanoramaVoteInfo();
        info.num_clients = _playerIDs.Count;
        info.yes_votes = GetYesVotes();
        info.no_votes = GetNoVotes();
        info.num_votes = info.yes_votes + info.no_votes;

        bool passed = _voteResult(info);
        if (passed)
            SendVotePassed("#SFUI_vote_passed_panorama_vote", "Vote Passed!");
        else
            SendVoteFailed(reason);

        
        // reset vote controller
        ResetVoteController();
        // reset current vote
    }
    private void OnMapEnd()
    {
        _plugin.RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
        EndVote(PanoramaVoteEndReason.VoteEnd_Cancelled);
    }
    private void SendVotePassed(string disp_str = "#SFUI_Vote_None", string details_str = "")
    {
        /* 
        UserMessage userMessage = UserMessage.FromPartialName("VotePass");
                userMessage.SetInt("team", vote.Team);
                userMessage.SetInt("vote_type", (int)VoteTypes.UNKNOWN);
                userMessage.SetString("disp_str", "#SFUI_vote_passed");
                userMessage.SetString("details_str", "");
                userMessage.Send(recipientFilter);
            */
        UserMessage votePass = UserMessage.FromPartialName("VotePass");
//        UserMessage votePass = UserMessage.FromId(347); //CS_UM_VotePass = 347;
        votePass.SetInt("team", -1);
        votePass.SetInt("vote_type", 1);
        votePass.SetString("disp_str", "#SFUI_vote_passed");
        votePass.SetString("details_str", details_str);

        RecipientFilter pFilter = new RecipientFilter();
        pFilter.AddAllPlayers();

        votePass.Send(pFilter);
    }
    private void SendVoteFailed(PanoramaVoteEndReason reason)
    {
        UserMessage voteFailed = UserMessage.FromId(348); //CS_UM_VoteFailed = 348;

        voteFailed.SetInt("team", -1);
        voteFailed.SetInt("reason", (int)reason);

        RecipientFilter pFilter = new RecipientFilter();
        pFilter.AddAllPlayers();

        voteFailed.Send(pFilter);
    }
}
public class PanoramaVoteInfo
{
    public int num_votes;                // Number of votes tallied in total
    public int yes_votes;                // Number of votes for yes
    public int no_votes;                 // Number of votes for no
    public int num_clients;              // Number of clients who could vote
    //public int[,] clientInfo = new int[MAXPLAYERS, 2];  // Client voting info, user VOTEINFO_CLIENT_ defines. Anything >= [num_clients] is VOTE_NOTINCLUDED, VOTE_UNCAST = client didn't vote
    public Dictionary<int, (int, int)> clientInfo = new Dictionary<int, (int, int)>();
}
public enum PanoramaVoteAction
{
    VoteAction_Start,  // nothing passed
    VoteAction_Vote,   // param1 = client slot, param2 = choice (VOTE_OPTION1 = yes, VOTE_OPTION2 = no)
    VoteAction_End     // param1 = YesNoVoteEndReason reason why the vote ended
}
public enum VoteOptions
{
    UNKNOWN = -1,
    YES,
    NO,
    OPTION3,
    OPTION4,
    OPTION5,
    REMOVE
}
public enum PanoramaVoteEndReason
{
    VoteEnd_AllVotes,  // All possible votes were cast
    VoteEnd_TimeUp,    // Time ran out
    VoteEnd_Cancelled  // The vote got cancelled
}
