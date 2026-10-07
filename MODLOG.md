## 2026-10-07 Input watch from the think timer

**Changed:** `DeadLockedPlugin.cs`. Load line is `Deadlocked input watch loaded`. The command hook only stores `inForward` for a bot and `buttons` plus `inForward` for the human. The think timer prints those once a second. Aim still overwrites bot view angles after the read.
**Why:** the last input line never printed. The think timer did print bot velocity.
**Tested how:** not tested.
**Result:** not tested.
**Still broken / not tested:** whether mimic changes bot `inForward` or human `buttons`.
**Next:** reload. Expect `input bot inForward=none` until a bot command arrives. Walk, then `bot_mimic 1` and walk. A copied command changes bot `inForward`. `buttons=none` means the human command was not seen.

## 2026-10-07 Strip movement again

**Changed:** `DeadLockedPlugin.cs` restored to `44ca00b`. Removed Teleport strafe, leftmove, button bits, and the watch logs.
**Why:** start over. The watch build showed the bot velocity vector updating while mimicking the human.
**Tested how:** mimic result was from the watch build, not from this revert.
**Result:** movement code is gone. Aim is the last version that looked at the human.
**Still broken / not tested:** how mimic writes that velocity. Not a plugin move.
**Next:** a new movement attempt can use that fact. This file does not.

## 2026-10-07 Bot velocity prints, human line did not

**Changed:** `DeadLockedPlugin.cs`. Human watch no longer uses `_humanHandle`. A non-bot command logs `watch human` once a second with `inForward`, `inLeft`, and velocity.
**Why:** the think-timer bot line updated. The human line did not. That line depended on the hero-changed handle.
**Tested how:** `b770011` in game. Bot `vel` changed. No human line.
**Result:** the think timer runs. Bot velocity is readable.
**Still broken / not tested:** human command fields, and mimic versus puppet.
**Next:** reload, walk, and look for `watch human`. Then `bot_mimic 1` and compare `inForward` with the bot `vel`.

## 2026-10-07 Watch build still silent

**Changed:** `DeadLockedPlugin.cs`. Load line is now `Deadlocked watch build loaded`. Added the `watch` console command, which prints `watch command`.
**Why:** the think-timer lines never appeared. That is a load failure until this line shows.
**Tested how:** not tested.
**Result:** not tested.
**Still broken / not tested:** whether the new file is the one the game compiled.
**Next:** reload. If `Deadlocked watch build loaded` is missing, the game is not running this file. Then run `watch` in the console. `watch command` means the plugin is this build.

## 2026-10-07 Watch log was silent

**Changed:** `DeadLockedPlugin.cs`. The input line never printed. It sat after a button read and shared a timer with the old aim log. The think timer now prints `watch human` and `watch bot` with velocity every second. The usercmd hook prints `usercmd hook` once, before any field read.
**Why:** a silent log cannot tell mimic from puppet.
**Tested how:** not tested.
**Result:** not tested.
**Still broken / not tested:** whether the hook runs at all.
**Next:** reload. Expect `watch human` every second. If that is missing, the plugin did not load. `usercmd hook` once means the command hook runs.

## 2026-10-07 Watch mimic and puppet inputs

**Changed:** `DeadLockedPlugin.cs`. Teleport strafe is not called. Once a second, for the human and one bot, log the command before any write: `inForward`, `inLeft`, `buttons`, subtick count, and `AbsVelocity`.
**Why:** schema has no mimic or puppet field. `CCitadelPlayerBot` has no fields. The difference has to show up on the command or the velocity.
**Tested how:** not tested. Run this sequence and keep the lines.
**Result:** not tested.
**Still broken / not tested:** which value changes when mimic or puppet starts walking a bot.
**Next:** compare the four samples below.

What to run, in order. Stand still for one line between each step.

1. Reload. Do not move. Expect `inForward=0` on both human and bot.
2. Walk forward. Human `inForward` should be non-zero. Bot `inForward` is the question.
3. `bot_mimic 1`, then walk. If mimic copies the command, bot `inForward` and `buttons` match the human. If only velocity changes, the copy is elsewhere.
4. `bot_mimic 0`. Walk again. Bot line should return to the step 2 shape.
5. `bot_puppet 1`, then walk. Same comparison. Puppet may zero the human line and fill the bot line.
6. `bot_puppet 0`.

Ignore `inYaw` on the bot. The plugin still overwrites bot view angles after the read. `buttons` is `buttonstate1`. A change there is the control bit. No change in any field while the bot still walks means the mode is not on this command.

## 2026-10-07 Teleport strafe moves, but badly

**Changed:** no code. Test of `60fe44d`.
**Why:** see if `Teleport(velocity:)` is a usable move.
**Tested how:** in game, registered bots.
**Result:** they move. Motion is stiff, the model rotation bugs, and they float or clip through geometry.
**Still broken / not tested:** real locomotion. Teleport skips the movement controller, so it is not the path to build on.
**Next:** do not extend Teleport. The game commands `bot_mimic` and `bot_puppet` already feed player inputs to bots. That is the path that can walk.

## 2026-10-07 Strafe by Teleport velocity

**Changed:** `DeadLockedPlugin.cs`. Removed `leftmove` and move button bits. Each tick calls `Teleport(velocity:)` with a sideways vector of 250, flipped every second. Aim is unchanged.
**Why:** command move fields did not walk the bot. Deadworks documents velocity writes through `Teleport`.
**Tested how:** not tested.
**Result:** not tested.
**Still broken / not tested:** whether the bot motor clears the velocity next tick.
**Next:** reload and watch for a slide left, then right. Log is `strafe` with `dir`.

## 2026-10-07 Strafe left and right in place

**Changed:** `DeadLockedPlugin.cs`. Aim unchanged. Every second `leftmove` flips between `450` and `-450`, `forwardmove` stays `0`, and button bit `512` or `1024` is set.
**Why:** a smaller movement test than walking to a spot.
**Tested how:** not tested.
**Result:** not tested.
**Still broken / not tested:** whether `leftmove` or the button bit moves a bot.
**Next:** reload and watch if the bot slides side to side. The log `left` value flips each second.

## 2026-10-07 Revert movement

**Changed:** `DeadLockedPlugin.cs` restored to `44ca00b`. Removed the crouch spot, `forwardmove`, `leftmove`, and move button bits.
**Why:** three movement attempts did not walk the bot. Aim on the user command did work. Start movement over from that.
**Tested how:** not retested after the revert. The aim build was tested earlier and looked at the human.
**Result:** movement code is gone. Aim code is the last working version.
**Still broken / not tested:** movement. `forwardmove` and button bits were both ignored.
**Next:** a smaller movement test, not another combined crouch-and-walk feature.

## 2026-10-07 Move buttons as well as forwardmove

**Changed:** `DeadLockedPlugin.cs`. Aim stayed on the human. Added Source-style move bits on `buttons_pb.buttonstate1` and a subtick press: forward 8, back 16, left 512, right 1024. Log now includes incoming and written buttons.
**Why:** relative `forwardmove` was written and the bot still did not walk. Aim did stay on the human.
**Tested how:** not tested. Previous relative-move build was tested and did not walk.
**Result:** not tested.
**Still broken / not tested:** the bot brain may move the pawn itself and ignore the command's move fields. Mimic works because it replaces the command source.
**Next:** crouch and check `moveDist`. If it still does not fall, stop writing move fields and look at disabling the bot motor.

## 2026-10-07 Aim stays on the human while moving

**Changed:** `DeadLockedPlugin.cs`. Yaw is no longer pointed at the crouch spot. View stays on the human. The spot is turned into `forwardmove` and `leftmove` relative to that yaw. Subtick analog deltas are set to the same values instead of zero.
**Why:** the last test looked at the crouch spot and did not walk. `forward=450` was written and ignored. Aim and movement are separate command fields.
**Tested how:** not tested.
**Result:** not tested.
**Still broken / not tested:** whether the bot brain uses `forwardmove` at all. Log now includes `inForward`.
**Next:** crouch, move away, and check that the bot still looks at you. If `moveDist` does not fall, movement is not this field.

## 2026-10-06 Crouch sends bots to that spot

**Changed:** `DeadLockedPlugin.cs`. Rising `m_flCrouchFraction` on the human stores their position. Bot user commands set yaw toward that spot and `forwardmove` 450 until horizontal distance is 80 or less, then stop. Subtick move deltas are zeroed.
**Why:** walk bots to a marked spot without touching the navigator.
**Tested how:** not tested.
**Result:** not tested.
**Still broken / not tested:** whether 450 is enough speed, and whether the bot brain overwrites `forwardmove`.
**Next:** crouch once in the open and watch for `move target`, falling `moveDist`, then `arrived`.

## 2026-10-06 Plan: crouch sets a move spot

**Changed:** `docs/DESIGN.md` only. No plugin code.
**Why:** bots should walk to where the human crouches, using user commands.
**Tested how:** not tested. Plan only.
**Result:** detect crouch from `m_flCrouchFraction` rising, store the human position, set yaw at that spot and `forwardmove` until within 80 units.
**Still broken / not tested:** movement is not implemented.
**Next:** implement that section, one step at a time.

## 2026-10-06 Schema notes for later movement and shooting

**Changed:** no code. Notes from schema build 6759 and the Deadworks API, for the next phase.
**Why:** avoid re-deriving aim-target and navigation fields.
**Tested how:** not tested, except the aim path already confirmed in game. These reads have not been logged from a live bot.
**Result:** findings below.
**Still broken / not tested:** reading the navigator and the aim-target fields from a running bot.
**Next:** when movement starts, log whether a walking bot has a non-null `m_pPath` and a changing goal position.

Crosshair and nearby heroes:

- No nearby-hero list on the pawn. A 12-hero scan is cheap. Line of sight is the expensive part, and Deadworks exposes that as `Trace`.
- `CCitadelPlayerPawn.m_hEnemyPlayerPrimaryAimTarget` (`0x1018`) is a `CPlayerSlot`, the main enemy under the aim. The old `m_hEnemyPlayerAimTarget` handle is gone.
- `m_iEnemyPlayerAimTargetBitVec` (`0x1020`) is a `uint64` bit set of enemy players being aimed at. Enemies only, not a distance list.
- `m_hEnemyHeroClientAimedAtAttackTime` (`0x1C48`) is the enemy hero handle aimed at when an attack happens.
- `CCitadelUserCmdPB.enemy_hero_aimed_at` is an `int32`, default `-1`. This is the command's own crosshair target. No friendly-at-crosshair field.
- `CPlayer_AutoaimServices` has no fields. `m_hLookTarget` is on `CAI_CitadelNPC`, not the hero pawn.

Navigation:

- `m_pBot` points at `CCitadelPlayerBot`, which has no fields. The pathfinder is `CAI_CitadelPlayerBotNavigator`, reached from `CCitadelPlayerBotNPCBrain` through `CAI_BaseNPC.m_pNavigator`.
- Readable: `m_pPath`, `m_queuedGoal`, `m_motorQueuedGoal`, `m_bBlocked`. The goal has `m_goalLocation` and `m_hGoalEntity`.
- Deadworks has no navigator or "move to point" call. Writing a goal is not a movement command. Schema writes already failed for aim.
- Movement that works is the user command: `forwardmove`, `leftmove`, and buttons in `OnProcessUsercmds`. `Teleport` skips the path.
- A goal is a destination, not a jump, slide, or crouch hint. `m_nNavGoalType` is `eDefault`, `eCover`, `eLOS`, or `eBackAway`. Stance, gait, strafing, facing, and speed are in `m_pathMotorSettings`. Jump or slide is on the path or a later button, not on the goal.

## 2026-10-06 User-command aim works

**Changed:** no code. Recorded the test of `44ca00b`.
**Why:** confirm the usercmd aim path.
**Tested how:** several bots at once, both enemy and friendly, in game.
**Result:** bots looked at the human. Works for both teams.
**Still broken / not tested:** shooting, movement, and more than one human are still out of scope.
**Next:** nothing on aim unless a new case fails.

## 2026-10-06 Aim through the user command

**Changed:** `DeadLockedPlugin.cs`. Stopped writing `m_angEyeAngles` and `v_angle`. On `OnProcessUsercmds`, for a tracked bot, set `base.viewangles` to the human and zero subtick pitch/yaw deltas.
**Why:** schema writes did not stick. Readback yaw stayed 25.8 while the written yaw was 155.9. Mimic, which copies a user command, did move aim.
**Tested how:** not tested. Previous schema write was tested and failed.
**Result:** not tested.
**Still broken / not tested:** whether bot commands pass through this hook. If the log says `no bot usercmds seen yet`, they do not.
**Next:** reload, stand near a bot, check for `cmd aim` and whether the body turns.

## 2026-10-06 Bots aim at the human

**Changed:** `DeadLockedPlugin.cs`. Tracks the one non-zero Steam id as the human. Each tick writes `m_angEyeAngles` and `v_angle` toward that hero's eye position. Removed the `m_hEnemyPlayerAimTarget` accessor. Logs written yaw and the next-tick readback once a second.
**Why:** make detected bots look at the only real player.
**Tested how:** not tested. No game was run.
**Result:** not tested.
**Still broken / not tested:** whether the bot brain overwrites the angles. Readback log is the check.
**Next:** reload the plugin in a bot match and compare written yaw to readback yaw.

## 2026-10-06 Aim plan only

**Changed:** added `docs/DESIGN.md`. No plugin code at that point.
**Why:** plan bots always looking at the single human before editing `DeadLockedPlugin.cs`.
**Tested how:** not tested. No game was run.
**Result:** plan written.
**Still broken / not tested:** was not implemented when this entry was first written.
**Next:** done. See the entry above.
