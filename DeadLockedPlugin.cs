using DeadworksManaged.Api;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using static CMsgHeroSelectionMatchInfo.Types;

namespace deadlockShit;

public class DeadLockedPlugin : DeadworksPluginBase
{
    public override string Name => "Deadlocked";

    // add bot state variables to this class
    private sealed class BotState
    {
        // --- follow-the-human state (brain-driven) ---
        public bool Following;                 // true = walking toward the human, false = standing still
        public bool OrderIssued;               // true once MoveTo has been given for the current follow
        public Vector3 LastGoal;               // where the human was when MoveTo was last issued
        public float LastOrderTime = -999f;    // CurTime of the last MoveTo
        public int OrderCount;                 // MoveTo orders issued (for the log)
        public int FalseRun;                   // consecutive ApplyMovement == false results
        public int NoMoveReissues;             // consecutive re-orders caused by the brain stopping
        public int FailedOrderCount;           // consecutive failures to even issue an order
        public bool LastApplyResult;           // last ApplyMovement result (for the log)
        public bool WarnedNoBrain;             // log "no brain" only once per bot
        public Vector3 LastStatusPos;          // position at the last STATUS line (for speed/dz)
        public bool HasLastStatusPos;
        public float LastStatusLogTime = -999f;
    }

    // ------------------| FOLLOW TUNING |------------------ //
    // Distances are in game units. First guesses, not tuned in game yet.
    private const float FollowStopDistance = 300f;    // stop walking when this close to the human
    private const float FollowResumeDistance = 450f;  // start walking again when farther than this
    private const float RepathGoalDrift = 150f;       // re-issue MoveTo if the human moved this far from the last goal
    private const float RepathMinInterval = 0.5f;     // seconds, minimum gap between MoveTo orders per bot
    private const int ArrivedFalseRun = 8;            // consecutive ApplyMovement==false that mean "brain is done"
    private const float StatusLogInterval = 1.0f;     // seconds, gap between STATUS log lines per bot

    // ------------------| NAV BLOCKER TEST (/blockhere) |------------------ //
    private const float BlockHalfWidth = 150f;        // half the blocker size on the ground (x and y)
    private const float BlockHalfHeight = 120f;       // half the blocker height, centered on the caller's feet

    private bool _followEnabled = true;
    private CBaseEntity? _testBlocker;   // the navmesh blocker made by /blockhere (null = none)
    private bool _warnedAiOn;

    // Log file lives next to the plugin DLL; falls back to the temp folder if the DLL location is unknown.
    private static readonly string LogPath = Path.Combine(
        string.IsNullOrEmpty(typeof(DeadLockedPlugin).Assembly.Location)
            ? Path.GetTempPath()
            : Path.GetDirectoryName(typeof(DeadLockedPlugin).Assembly.Location) ?? Path.GetTempPath(),
        "deadlocked_follow.log");

    private readonly HashSet<uint> _botHandles = new();
    private readonly EntityData<BotState> _botStates = new();

    private IHandle? _botThinkTimer;

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine($"Deadlocked loaded (reload={isReload}");

        _botHandles.Clear();
        _botThinkTimer = Timer.Every(1.Ticks(), OnBotThink);

        Console.WriteLine($"Deadlocked follow log: {LogPath}");
        Log($"=== plugin loaded (reload={isReload}) ===", console: false);

        // Bots can only be steered by us while the built-in AI is off.
        // Returns false if no map is loaded yet; OnStartupServer tries again.
        Log($"SetAiEnabled(false) on load -> {Bots.SetAiEnabled(false)}");
    }

    public override void OnStartupServer()
    {
        _botHandles.Clear();
        _testBlocker = null; // a new map has no entities from the old one

        ConVar.Find("sv_cheats")?.SetInt(1);

        Log($"SetAiEnabled(false) on server startup -> {Bots.SetAiEnabled(false)}");

        ConVar.Find("citadel_rapid_stamina_regen")?.SetBool(false);
        ConVar.Find("citadel_allow_purchasing_anywhere")?.SetInt(0);
    }

    public override void OnUnload()
    {
        if (_testBlocker != null && _testBlocker.IsValid)
            _testBlocker.Remove();
        _testBlocker = null;

        Server.ExecuteCommand("trooper_kill_all");
        Server.ExecuteCommand("bot_kick_all");

        _botHandles.Clear();
        _botThinkTimer?.Cancel();
    }

    private void OnBotThink()
    {
        var activeHeroes = new List<CCitadelPlayerPawn>();

        foreach (var handle in _botHandles.ToArray())
        {
            var pawn = CBaseEntity.FromHandle<CCitadelPlayerPawn>(handle);

            if (pawn == null || !pawn.IsValid)
            {
                Console.WriteLine($"removed invalid bot {pawn?.Name}");
                _botHandles.Remove(handle);
                continue;
            }

            var state = _botStates.GetOrAdd(pawn, () => new BotState());

            activeHeroes.Add(pawn);
        }
    }

    public override void OnAbilityAttempt(AbilityAttemptEvent args)
    {
        // block all ultimates
        args.Block(InputButton.Ability4);

        if (args.Controller == null || args.Controller.Pawn == null)
            return;

        var pawn = CBaseEntity.FromHandle<CCitadelPlayerPawn>(args.Controller.Pawn.EntityHandle);

        if (pawn == null || !_botHandles.Contains(pawn.EntityHandle))
            return;

        var state = _botStates.GetOrAdd(pawn, () => new BotState());
    }

    private void OnBotDeath(uint killed_handle, uint killer_handle)
    {
        var debug = false;
        var victim = CBaseEntity.FromHandle<CCitadelPlayerPawn>(killed_handle);
        var killer = CBaseEntity.FromHandle<CCitadelPlayerPawn>(killer_handle);

        if (debug || victim == null || !victim.IsValid || victim.Controller == null || killer == null || !killer.IsValid || killer.Controller == null)
            return;

        Console.WriteLine($"OnBotDeath called for {victim.Name}");
    }

    // ------------------| CUSTOM FUNCS |------------------ //

    // Called by Deadworks for every player (humans too) every tick.
    // For bots we hand each usercmd (the "input packet") to the bot's brain, which fills it in
    // so the bot walks toward the destination we gave it with MoveTo.
    public override void OnProcessUsercmds(ProcessUsercmdsEvent e)
    {
        var controller = e.Controller;
        if (controller == null || !controller.IsBot)
            return; // humans: never touch their input

        var bot = controller.GetHeroPawn();
        if (bot == null || !bot.IsValid)
            return;

        var state = _botStates.GetOrAdd(bot, () => new BotState());

        // ApplyMovement only drives the bot while the built-in bot AI is off.
        if (Bots.AiEnabled)
        {
            if (!_warnedAiOn)
            {
                _warnedAiOn = true;
                Log("WARNING: Bots.AiEnabled is true; follow steering is disabled until it is turned off.");
            }
            return;
        }
        _warnedAiOn = false;

        if (!_followEnabled)
        {
            StopFollowing(bot, state, "follow disabled with /follow");
            return;
        }

        if (bot.Health <= 0)
        {
            StopFollowing(bot, state, "bot is dead");
            return;
        }

        var brain = bot.BotBrain;
        if (brain == null)
        {
            if (!state.WarnedNoBrain)
            {
                state.WarnedNoBrain = true;
                Log($"{bot.Name}: has no BotBrain; cannot follow.");
            }
            return;
        }

        var target = FindFollowTarget(bot);
        if (target == null)
        {
            StopFollowing(bot, state, "no living human player");
            return;
        }

        var botPos = bot.Position;
        var targetPos = target.Position;
        var dist = Vector3.Distance(botPos, targetPos);

        // Two thresholds so the bot does not jitter at the edge:
        // stop when close, resume only once clearly far again.
        if (state.Following && dist <= FollowStopDistance)
        {
            StopFollowing(bot, state, $"within {FollowStopDistance:F0} of {target.Name} (dist {dist:F0})");
        }
        else if (!state.Following && dist > FollowResumeDistance)
        {
            state.Following = true;
            Log($"{bot.Name}: START following {target.Name} (dist {dist:F0} > {FollowResumeDistance:F0})");
        }

        if (state.Following)
        {
            MoveToIfNeeded(bot, brain, target, state, targetPos, dist);

            if (state.OrderIssued)
            {
                // The brain turns its planned route into input (walk, jump, mantle...) for each usercmd.
                foreach (var cmd in e.Usercmds)
                {
                    var moved = brain.ApplyMovement(cmd);
                    state.LastApplyResult = moved;

                    if (moved)
                    {
                        state.FalseRun = 0;
                        state.NoMoveReissues = 0;
                    }
                    else
                    {
                        state.FalseRun++;
                    }
                }
            }
        }

        LogStatus(bot, target, state, botPos, dist);
    }

    private static CCitadelPlayerPawn? FindFollowTarget(CCitadelPlayerPawn bot)
    {
        CCitadelPlayerPawn? best = null;
        var bestDist = float.MaxValue;

        foreach (var controller in Players.GetAll())
        {
            if (controller.IsBot)
                continue;

            var pawn = controller.GetHeroPawn();
            if (pawn == null || !pawn.IsValid || pawn.Health <= 0)
                continue;

            var d = Vector3.Distance(bot.Position, pawn.Position);
            if (d < bestDist)
            {
                bestDist = d;
                best = pawn;
            }
        }

        return best;
    }

    private void StopFollowing(CCitadelPlayerPawn bot, BotState state, string reason)
    {
        if (!state.Following && !state.OrderIssued)
            return; // already idle, nothing to log

        Log($"{bot.Name}: STOP following ({reason})");
        state.Following = false;
        state.OrderIssued = false;
        state.FalseRun = 0;
        state.NoMoveReissues = 0;
    }

    // Gives the brain a new destination only when needed. Pathfinding is done by the game, not by us.
    private void MoveToIfNeeded(CCitadelPlayerPawn bot, CCitadelPlayerBotNPCBrain brain, CCitadelPlayerPawn target, BotState state, Vector3 targetPos, float dist)
    {
        string? reason = null;
        var brainStopped = false;

        if (!state.OrderIssued)
            reason = "no order yet";
        else if (state.FalseRun >= ArrivedFalseRun)
        {
            reason = $"brain has nowhere to go ({state.FalseRun} false in a row) but human is {dist:F0} away";
            brainStopped = true;
        }
        else if (Vector3.Distance(targetPos, state.LastGoal) > RepathGoalDrift)
            reason = "target moved";

        if (reason == null)
            return;

        var now = GlobalVars.CurTime;
        if (now - state.LastOrderTime < RepathMinInterval)
            return;
        state.LastOrderTime = now;

        // Snap the human's position onto the navmesh so the goal is a valid walkable point.
        var goal = Nav.GetNearestPoint(targetPos, bot);
        if (goal == null)
        {
            state.FailedOrderCount++;
            if (state.FailedOrderCount == 1 || state.FailedOrderCount % 20 == 0)
                Log($"{bot.Name}: MOVETO FAILED x{state.FailedOrderCount} - could not snap {Fmt(targetPos)} to navmesh ({reason})");
            return;
        }

        brain.MoveTo(goal.Value);

        state.OrderIssued = true;
        state.LastGoal = targetPos;
        state.FalseRun = 0;
        state.FailedOrderCount = 0;
        state.OrderCount++;

        // When the brain keeps stopping (e.g. unreachable human) this repeats every 0.5 s; log the first and every 10th.
        if (brainStopped)
            state.NoMoveReissues++;
        if (!brainStopped || state.NoMoveReissues == 1 || state.NoMoveReissues % 10 == 0)
            Log($"{bot.Name}: MOVETO #{state.OrderCount} ({reason}) goal={Fmt(goal.Value)} target={target.Name} dist={dist:F0}");
    }

    private void LogStatus(CCitadelPlayerPawn bot, CCitadelPlayerPawn target, BotState state, Vector3 botPos, float dist)
    {
        var now = GlobalVars.CurTime;
        if (now - state.LastStatusLogTime < StatusLogInterval)
            return;
        var dt = now - state.LastStatusLogTime;

        // speed = ground units/second, dz = height change; dz is how we check from numbers that a bot climbed.
        var motion = "speed=n/a dz=n/a";
        if (state.HasLastStatusPos && dt > 0f)
        {
            var dx = botPos.X - state.LastStatusPos.X;
            var dy = botPos.Y - state.LastStatusPos.Y;
            motion = $"speed={MathF.Sqrt(dx * dx + dy * dy) / dt:F0} dz={botPos.Z - state.LastStatusPos.Z:F0}";
        }

        state.LastStatusLogTime = now;
        state.LastStatusPos = botPos;
        state.HasLastStatusPos = true;

        Log($"STATUS {bot.Name} following={state.Following} dist={dist:F0} target={target.Name} pos={Fmt(botPos)} {motion} " +
            $"orders={state.OrderCount} falseRun={state.FalseRun} lastApply={state.LastApplyResult} failed={state.FailedOrderCount}",
            console: false);
    }

    private static string Fmt(Vector3 v) => $"({v.X:F0},{v.Y:F0},{v.Z:F0})";

    // Writes to the log file; also to the console unless console is false.
    private void Log(string message, bool console = true)
    {
        var line = $"[{GlobalVars.CurTime:F2}] {message}";
        if (console)
            Console.WriteLine($"[Deadlocked] {line}");

        try
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never crash the plugin.
        }
    }

    // ------------------| HOOKS |------------------ //
    [GameEventHandler("player_hero_changed")]
    public HookResult OnPlayerHeroChanged(PlayerHeroChangedEvent args)
    {
        var pawn = args.Userid?.As<CCitadelPlayerPawn>();

        Console.WriteLine($"new player {pawn?.Name}");

        if (pawn?.Controller?.PlayerSteamId == 0)
        {
            _botHandles.Add(pawn.EntityHandle);
            Console.WriteLine($"Found bot with handle: {pawn.EntityHandle}");

            var msg = new CCitadelUserMsg_HudGameAnnouncement
            {
                TitleLocstring = "BOT FOUND",
                DescriptionLocstring = $"Found bot named {pawn.Name}"
            };

            NetMessages.Send(msg, RecipientFilter.All);

            foreach (var ability in pawn.AbilityComponent.Abilities)
            {
                if (ability.AbilitySlot < EAbilitySlot.Signature1
                 || ability.AbilitySlot > EAbilitySlot.Signature4) continue;

                ability.UpgradeBits = ability.UpgradeBits | 0b11111;

                // disable ultimates/guns from bots for testing
                if (ability.AbilitySlot == EAbilitySlot.Signature4)
                    ability.CooldownEnd = GlobalVars.CurTime + 999;
            }
        }

        return HookResult.Continue;
    }

    [GameEventHandler("player_hurt")]
    public HookResult OnPlayerHurt(PlayerHurtEvent args)
    {
        var victim = args.UseridPawn?.As<CCitadelPlayerPawn>();
        var attacker = args.AttackerPawn?.As<CCitadelPlayerPawn>();

        if (victim == null || !victim.IsValid || _botHandles.Contains(victim.EntityHandle))
            return HookResult.Continue;

        return HookResult.Continue;
    }

    [GameEventHandler("player_death")]
    public HookResult OnPlayerDeath(PlayerDeathEvent args)
    {
        var victim = args.UseridPawn?.As<CCitadelPlayerPawn>();
        var killer = args.AttackerPawn?.As<CCitadelPlayerPawn>();

        if (victim == null || !victim.IsValid || killer == null || !killer.IsValid)
            return HookResult.Continue;

        if (_botHandles.Contains(victim.EntityHandle))
            OnBotDeath(victim.EntityHandle, killer.EntityHandle);

        return HookResult.Continue;
    }

    [GameEventHandler("player_respawned")]
    public HookResult OnPlayerRespawned(PlayerRespawnedEvent args)
    {
        var pawn = args.Userid?.As<CCitadelPlayerPawn>();
        if (pawn == null) return HookResult.Continue;

        // if is bot
        if (_botHandles.Contains(pawn.EntityHandle))
        {
        }

        return HookResult.Continue;
    }

    // ------------------| COMMANDS |------------------ //
    [Command("hi", Description = "Debug message")]
    public void CMD_Hi(Caller caller)
    {
        var pawn = caller.Player?.GetHeroPawn();

        if (pawn == null || !pawn.IsValid || caller.Player == null)
            return;

        Console.WriteLine($"{caller} tried using /hi");

        var msg = new CCitadelUserMsg_HudGameAnnouncement
        {
            TitleLocstring = "HI",
            DescriptionLocstring = "Yes, the plugin is working."
        };
        NetMessages.Send(msg, RecipientFilter.Single(caller.Player.Slot));
    }

    [Command("follow", Description = "Toggle bots following the human player")]
    public void CMD_Follow(Caller caller)
    {
        _followEnabled = !_followEnabled;
        Log($"/follow used by {caller}: follow is now {(_followEnabled ? "ON" : "OFF")}");
        caller.Reply($"Bot follow is now {(_followEnabled ? "ON" : "OFF")}.");
    }

    // Test tool: toggles a navmesh blocker centered on the caller so we can watch how bots route around it.
    // It deliberately does NOT re-issue MoveTo for bots, so we can see whether the brain replans by itself.
    [Command("blockhere", Description = "Toggle a navmesh blocker at your position (test)")]
    public void CMD_BlockHere(Caller caller)
    {
        // Second use: remove the existing blocker.
        if (_testBlocker != null && _testBlocker.IsValid)
        {
            _testBlocker.Remove();
            _testBlocker = null;
            Log($"/blockhere used by {caller}: blocker REMOVED");
            caller.Reply("Nav blocker removed.");
            return;
        }
        _testBlocker = null; // forget a blocker that no longer exists (e.g. map changed)

        var pawn = caller.Player?.GetHeroPawn();
        if (pawn == null || !pawn.IsValid)
            throw new CommandException("You need a hero to do that.");

        var center = pawn.Position;
        var halfExtents = new Vector3(BlockHalfWidth, BlockHalfWidth, BlockHalfHeight);

        // team 0 (default) = block everyone. The navmesh updates shortly after this call, not during it.
        var blocker = Nav.CreateBlocker(center, halfExtents);
        if (blocker == null)
        {
            Log($"/blockhere used by {caller}: BLOCKER FAILED (Nav.CreateBlocker returned null) center={Fmt(center)}");
            caller.Reply("Could not create a nav blocker (see log).");
            return;
        }

        _testBlocker = blocker;
        Log($"/blockhere used by {caller}: blocker PLACED center={Fmt(center)} " +
            $"halfExtents=({halfExtents.X:F0},{halfExtents.Y:F0},{halfExtents.Z:F0})");
        caller.Reply($"Nav blocker placed here ({BlockHalfWidth * 2:F0}x{BlockHalfWidth * 2:F0}). Step away; use /blockhere again to remove it.");
    }

    [Command("sethp", Description = "Sets HP to percentage for testing")]
    public void CMD_king(Caller caller, int percentage)
    {
        var pawn = caller.Player?.GetHeroPawn();

        if (pawn == null || !pawn.IsValid || percentage <= 0 || percentage > 100)
            return;

        var target = (int)pawn.GetMaxHealth() * percentage / 100;
        var delta = target - pawn.Health;

        Console.WriteLine($"{target} for {pawn.Name}");

        if (delta > 0)
            pawn.Heal(delta);
        else
            pawn.Hurt(-delta);
    }
}