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
