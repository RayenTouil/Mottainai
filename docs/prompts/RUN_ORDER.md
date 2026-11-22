# How to run the v4 update with Claude Code (for the team)

Files:
- `docs/prompts/UPDATE_V4.md`: the spec (phases A–F)
- `docs/prompts/VOICE.md`: details of phase E
- this file: what to type, in what order

Setup:
- Open a terminal in `greenhack/Mottainai` and run `claude`.
- Close the Unity editor before any build.
- Send **one message per phase**, wait for the phase report, check it, then send the next.
- Between phases type `/clear`. Claude Code then rereads the files and HANDOFF.md, which keeps it fast and cheap.
- To stop: `Esc`. A message you type while it's working is queued for after its current step.

Effort:
- Messages marked **MAX** start with the word `ultrathink`, which makes Claude Code think as hard as it can.
- **FAST** messages tell it to be quick.

---

## 1. Setup [FAST]
```
Read docs/prompts/UPDATE_V4.md section 0 only. Do the setup: branch feature/v4 from main + push -u, wrangler env "v4" deployed as a separate Worker, this branch's server_config.json pointing to it, PROTOCOL_VERSION 3 stub. Be fast, no analysis. Report the v4 URL.
```

## 2. Phase A [FAST]
```
Do Phase A of docs/prompts/UPDATE_V4.md (onboarding cards, collect/avoid pop-up, bottom legend, score top-left, leave button). Be fast and simple, no deep analysis. Follow section 0 rules. Build, test on 1 phone, report.
```

## 3. Phase B [high]
```
Do Phase B of docs/prompts/UPDATE_V4.md (READY/START rules, host, server-authoritative). Think carefully about the server state machine. Write the fakeclient tests first, then implement, deploy to v4, report.
```

## 4. Phase C [MAX]
```
ultrathink. Do Phase C of docs/prompts/UPDATE_V4.md (disconnection/reconnection). This is the most important fix: find and prove the root cause before changing code, write tests T1-T7 first, then implement pause/resume, fast dead-socket detection and the grace period. Deploy to v4, run the tests, then guide us through the phone tests and write docs/v4/reconnect.md.
```

## 5. Phase D [high]
```
Do Phase D of docs/prompts/UPDATE_V4.md (teams + leaderboard, TeamsDO, server-authoritative scores, lobby/results flows, TEAM and LEADERBOARD screens). Think carefully about the data model and double-counting. Tests first, deploy to v4, report.
```

## 6. Phase E [MAX]
```
ultrathink. Do Phase E: follow docs/prompts/VOICE.md exactly (mic ON/OFF per team, lowest possible latency and jitter). Start with E0 and show me the report before fixing. Then E1-E3, then the E4 tests on our phones.
```

## 7. Phase F [high]
```
Do Phase F of docs/prompts/UPDATE_V4.md: full integration test on our 3 phones with docs/v4/TEST_PLAN.md, fix what fails (no engine/graphics changes), update HANDOFF.md with the merge instructions. Do not merge into main.
```

## If something goes wrong
- It looks stuck → `Esc`, then: `What are you waiting on?` (it's often waiting for a phone USB prompt or for your answer).
- A phase broke the game → `git log --oneline -10`, then tell it: `revert the last phase's commits on feature/v4`.
- The demo APK on `main` and the deployed `mottainai` Worker are never touched by this branch.
