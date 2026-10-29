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
	}

	private readonly HashSet<uint> _botHandles = new();
	private readonly EntityData<BotState> _botStates = new();

	private IHandle? _botThinkTimer;

	public override void OnLoad(bool isReload)
	{
		Console.WriteLine($"Deadlocked loaded (reload={isReload}");

		_botHandles.Clear();
		_botThinkTimer = Timer.Every(1.Ticks(), OnBotThink);
	}

	public override void OnStartupServer()
	{
		_botHandles.Clear();

		ConVar.Find("sv_cheats")?.SetInt(1);

		ConVar.Find("citadel_rapid_stamina_regen")?.SetBool(false);
		ConVar.Find("citadel_allow_purchasing_anywhere")?.SetInt(0);
	}

	public override void OnUnload()
	{
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