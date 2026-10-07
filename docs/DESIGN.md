# Aim all bots at the one human

Status: working. Tested 2026-10-06 with several bots, enemy and friendly. They looked at the human.

Aim is set in `OnProcessUsercmds` by writing `base.viewangles` and zeroing subtick pitch/yaw deltas. Schema angle writes do not stick and are not used.

## What failed

Writing `m_angEyeAngles` and `v_angle` every tick did not change aim. Log:

```
aim readback hero_inferno writtenYaw=155.9 eye=25.8 view=25.8
aim hero_inferno ... yaw=155.9
```

Possessing the bot still showed the old aim. `bot_mimic` did move aim, so the live aim is the user command, not those fields.

## Current attempt

`OnProcessUsercmds` runs for a tracked bot and sets `CBaseUserCmdPB.viewangles` to the human. Subtick pitch and yaw deltas are zeroed so they do not undo it. Schema writes are gone.

If the once-a-second log says `no bot usercmds seen yet`, this hook does not see bot commands and this attempt failed too.


## Goal

In a lobby with one real player, every detected bot should keep looking at that player's hero. "Looking" means the server aim angles, not the spectator camera.

## Assumption

Exactly one controller with `PlayerSteamId != 0`. Extra humans are out of scope. If that player is missing or dead as an entity, bots do nothing that tick.

## What to change in `DeadLockedPlugin.cs`

### 1. Track the human separately from bots

In `OnPlayerHeroChanged`:

- If `PlayerSteamId == 0`, keep the existing bot-handle path.
- Otherwise store that pawn's `EntityHandle` in a new `uint? _humanHandle`.
- Clear `_humanHandle` in `OnLoad` and `OnStartupServer`, next to `_botHandles.Clear()`.

Do not treat the human as a bot. The current `player_hurt` heal stays as it is.

### 2. Replace the dead aim accessor

Delete `_enemyAimTarget`. `m_hEnemyPlayerAimTarget` is not on `CCitadelPlayerPawn` in schema build 6759. Do not write `m_hEnemyPlayerPrimaryAimTarget` or `m_iEnemyPlayerAimTargetBitVec`. Those are outputs.

Leave `_guardianTarget` unused. `m_hLookTarget` is on `CAI_CitadelNPC`, not the hero pawn.

Add:

```csharp
private static readonly SchemaAccessor<Vector3> _eyeAngles =
    new("CCitadelPlayerPawn"u8, "m_angEyeAngles"u8);
private static readonly SchemaAccessor<Vector3> _viewAngles =
    new("CBasePlayerPawn"u8, "v_angle"u8);
```

Use the accessor, not `pawn.ViewAngles`. The current Deadworks getter reads a hardcoded offset that does not match `v_angle` at `0xC68` in this dump.

Do not call `SetCameraAngles` in this step. That usermessage is for a real client. A bot has no client, and the docs say it is not the crosshair.

### 3. Point every bot at the human inside `OnBotThink`

The timer is already every tick. After the existing invalid-bot check:

1. Resolve `_humanHandle` to a `CCitadelPlayerPawn`. If it is null or invalid, skip aiming this tick.
2. For each valid bot, skip if it is the human handle.
3. Compute pitch and yaw from `bot.EyePosition` to `human.EyePosition`. Source pitch is inverted (positive looks down):

```csharp
var delta = human.EyePosition - bot.EyePosition;
float horiz = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
var aim = new Vector3(
    -MathF.Atan2(delta.Z, horiz) * (180f / MathF.PI),
    MathF.Atan2(delta.Y, delta.X) * (180f / MathF.PI),
    0f);
_eyeAngles.Set(bot.Handle, aim);
_viewAngles.Set(bot.Handle, aim);
```

Write both every tick. The bot brain will likely overwrite them from its own command, so a one-shot set will not stick.

Do not use `m_angLockedEyeAngles` in this step.

### 4. Log so it can be checked without watching the game

Once per second, for the first valid bot only, log:

- bot name and handle
- human name and handle
- distance
- aim pitch and yaw just written
- `m_angEyeAngles` and `v_angle` read back on the next tick

That last pair is the check that the write survived the bot brain. If the read-back yaw is not the written yaw, this plan failed and we stop rather than guessing.

## Out of scope

- Shooting, movement, abilities.
- More than one human.
- Guardian or trooper look targets.
- Camera usermessage and locked eye angles, unless the log shows the two writes do not stick.

## How to test

Reload the plugin, start a bot match, and stand still.

Expect, about once a second: a log line with the human name, a distance, and a yaw. Next line should show read-back yaw within about 1 degree of the written yaw.

Then walk around the bot. Written yaw should change with your position. If read-back does not follow, the write is being clobbered.

Not tested. No game was run for this plan.
