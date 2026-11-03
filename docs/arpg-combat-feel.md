# What Makes ARPG Combat Feel Great

A working reference for building action-RPG combat that feels as good as Hades.
It names concrete mechanisms, gives target numbers, and says how to measure each
one.

## How to read this document

Every claim carries one of three evidence tags:

- **[Study]**: backed by a published experiment. A citation is given.
- **[Practitioner]**: stated by an experienced developer about their own games.
  A citation is given.
- **[Hypothesis]**: my own reasoning or estimate. Treat these numbers as
  starting points to tune and test, not facts.

Frame counts assume 60 fps, so 1 frame ≈ 16.7 ms.

I do not have measured frame data for Hades. When this document says "Hades does
X," it describes observable behavior, not internal values.

______________________________________________________________________

## 1. The core model: an action loop that keeps confirming you caused something

Moment-to-moment combat is a loop, repeated several times per second:

1. **Intent**: the player decides to do something ("hit that enemy", "get out of
   that red circle").
2. **Input**: they press a button or move the stick.
3. **Response**: the character starts acting.
4. **Outcome**: the action succeeds or fails.
5. **Confirmation**: the game reports the outcome, clearly and through several
   senses.
6. Back to step 1, now with new information.

Great combat makes each step fast and clear, and makes the outcome *matter*: it
is not guaranteed, and it reflects what the player did.

### What the research says about motivation

- **Real-time control needs a short loop.** [Practitioner] Steve Swink's *Game
  Feel* defines "real-time response" as a game responding within a correction
  cycle of under about 100 ms. Above that, the sense of directly controlling the
  character starts to break down.
  ([Game feel, Wikipedia](https://en.wikipedia.org/wiki/Game_feel);
  [Game Feel: The Secret Ingredient](https://www.gamedeveloper.com/design/game-feel-the-secret-ingredient))

- **Feedback only motivates when it reports success.** [Study] Kao, Ballou,
  Gerling, Breitsohl and Deterding (CHI 2024) ran a pre-registered experiment
  with 1,699 players. They used a purpose-built action RPG with five feedback
  conditions. ([paper](https://dl.acm.org/doi/full/10.1145/3613904.3642656))

  - *Success-dependent* feedback (impact effects that fire only when you
    actually hit an enemy) raised effectance, competence and curiosity.
    Effectance is the basic pleasure of causing effects.
  - *Amplified* feedback that fired on every swing, hit or not, *lowered*
    effectance and competence compared with plain feedback. The authors traced
    this partly to occlusion: a large glow on the swing hid the hit and kill
    effects, so players could not see that they had succeeded.
  - **Curiosity**, meaning interest in resolving uncertainty over whether each
    action would succeed, was the strongest predictor of enjoyment and the
    *only* predictor of free-choice playtime.
  - Enjoyment did *not* predict playtime. If you test, measure both.

  | Condition (selected)            | n   | Effectance | Competence | Enjoyment | Free-choice playtime (min) |
  | ------------------------------- | --- | ---------- | ---------- | --------- | -------------------------- |
  | Standard feedback               | 333 | 5.57       | 5.19       | 5.23      | 13.31                      |
  | Amplified, fires on every swing | 345 | 5.35       | 4.80       | 5.12      | 13.65                      |
  | Amplified, fires only on hits   | 305 | 5.69       | 5.39       | 5.55      | 14.76                      |

  *Scores are on a 7-point scale. Values are the paper's Table 2 descriptives.*

- **More juice is not always better.** [Study] Kao (2020, *Entertainment
  Computing*) compared no, medium, high and extreme juiciness in one action RPG
  with 3,018 participants. Medium and high outperformed both none and extreme on
  competence, autonomy, immersion, enjoyment, playtime and performance.
  ([summary](https://www.goodreads.com/author_blog_posts/19932694-the-effects-of-juiciness-in-an-action-rpg-new-study);
  results as described in the CHI 2024 paper's related work)

**Design consequences** \[Hypothesis, built on the studies above\]:

- Spend your feedback budget on *outcomes* (hits, kills, crits, dodges), not on
  *actions* (swings, button presses). The swing itself should be crisp and
  readable, not loud.
- Feedback must never hide the information the player needs next: whether they
  hit, who they hit, and what is about to hit them.
- Some uncertainty is good: crits, procs, close calls, builds you are still
  figuring out. Uncertainty only produces curiosity if the result is *legible*
  afterward.

______________________________________________________________________

## 2. Responsiveness: the game does what you asked, immediately

Responsiveness comes first because no amount of polish fixes a game that feels
like it is ignoring you.

### 2.1 End-to-end latency

Latency is the time from button press to the first changed pixel on screen.

**Evidence:**

- [Study] People can detect much smaller delays than you might expect. Raaen &
  Eg (2015) had participants lower a button-to-screen delay until it felt
  instant, and they reached a median of about 40 ms. When the same people judged
  "instant vs. delayed," the 50/50 point was about 148 ms.
  ([Simula](https://www.simula.no/publications/instantaneous-human-computer-interactions-button-causes-and-screen-effects))
- [Study] In an earlier study by the same group, some gamers detected delays
  under 40 ms, but the median detection threshold sat around 51–90 ms.
  ([MMSys 2016 paper, related work](https://mmsys2016.itec.aau.at/papers/MMSYS/a28-westerdals.pdf))
- [Study] Local latency, which delays all of the player's input, appears to hurt
  performance more than the same amount of network latency.
  ([Liu, Claypool et al.](https://web.cs.wpi.edu/~claypool/papers/csgo-net-local-21/paper.pdf))
- [Study, low-confidence] A WPI student project on cloud gaming found
  quality-of-experience ratings dropped about 14% per 100 ms of added latency,
  and fell roughly linearly.
  ([WPI project](https://web.cs.wpi.edu/~claypool/iqp/onlive/))

**Targets** \[Hypothesis\]:

- Total press-to-photon latency under ~70 ms on a typical 60 Hz display. Under
  50 ms is excellent. At 100 ms, players start to call the game "floaty" or
  "heavy."
- The character's pose must change on the **first rendered frame** after the
  input is processed. Sakurai has a whole video on how important an attack's
  first frame is.
  ([Nintenderos summary](https://www.nintenderos.com/2022/12/sakurai-destaca-la-importancia-del-primer-frame-de-un-ataque-en-un-videojuego/))

**Rough budget at 60 fps** \[Hypothesis\]:

| Stage                                | Typical cost                        |
| ------------------------------------ | ----------------------------------- |
| Input sampled once per frame         | 0–17 ms (waiting for the next poll) |
| Game simulation                      | 17 ms (one frame)                   |
| Render, or render queue if pipelined | 17–33 ms                            |
| Display processing and scanout       | 5–20 ms (TV "game mode" matters)    |
| **Total**                            | **~40–90 ms**                       |

Every extra frame of buffering in the pipeline costs 17 ms. Triple buffering,
"smooth" input filtering, and animation blend-ins that delay the visible pose
change are the usual culprits.

### 2.2 Asymmetric startup: the player is fast, enemies are slow

|                          | Startup (press to hit)     | Why                                               |
| ------------------------ | -------------------------- | ------------------------------------------------- |
| Player basic attack      | 3–6 frames (50–100 ms)     | Feels instant; the player's intent becomes action |
| Player heavy or special  | 8–15 frames                | Commitment is a choice the player made            |
| Basic enemy attack       | 24–45 frames (400–750 ms)  | Long enough to see and react to                   |
| Elite or boss big attack | 45–90 frames (750–1500 ms) | Long, showy warning; huge payoff when dodged      |

*All values: [Hypothesis].*

The player's own attacks feel instant. Enemy attacks must be readable. The game
is not trying to be "realistic." It is trying to make the player feel fast and
the enemies feel dangerous but fair.

### 2.3 Input buffering

A *buffer* stores a press made slightly too early and runs it on the first frame
it becomes legal.

- [Practitioner] Celeste's "jump buffering" does this for jumps: press jump just
  before landing and the character jumps on the exact landing frame. Maddy
  Thorson presents this as one of ten "forgiveness" mechanics that make the game
  feel kind despite its difficulty.
  ([Celeste & Forgiveness](https://maddymakesgames.com/articles/celeste_and_forgiveness/))
- In combat, the same idea applies to presses made during attack recovery,
  during hitstop, or during the last frames of a dash.

**Targets** \[Hypothesis\]:

- Buffer window of about 6–12 frames (100–200 ms).
- Keep only the **most recent** buffered input. Players often mash, then change
  their mind, and the last press is the most current intent.
- Clear the buffer on hit-stun or knockdown, and when the player drops an action
  deliberately. Otherwise stale inputs fire later and feel like the game is
  "doing things on its own."
- **Don't make the buffer too long.** Above roughly 250 ms, players get trapped
  into actions they no longer want: a third swing queues up when they wanted to
  dodge. A buffer that is too long feels as bad as one that is too short, only
  in the opposite direction.

### 2.4 Cancels and lockout fraction

- **Cancels**: during the recovery of an attack, the player can interrupt it
  with another action, most importantly the dash. In Hades, the dash interrupts
  attack recovery, which is why you almost never feel stuck.
- **Lockout fraction** \[Hypothesis, a metric I'm proposing\]: the share of
  combat time during which a new input would be neither honored right away nor
  buffered. Lower is better. Basic attacks should leave the player locked out
  for at most ~200–300 ms before a dash cancel is available.
- **A natural experiment.** [Anecdotal] Some players on the Steam forums
  described Hades II's controls as less responsive than the first game's, and
  named the cause: the dash can't cancel out of attack animations.
  ([Steam thread](https://steamcommunity.com/app/1145350/discussions/0/4358998752031959500))
  These are forum opinions, not a study, and I haven't verified the frame-level
  differences between the two games. Still, it's the complaint the lockout model
  predicts.

### 2.5 Direction snapping

- At the moment of the press, the attack faces the stick direction (or the mouse
  cursor), not the direction the character was already facing. There is no
  turn-speed animation.
- Add mild aim assist: within roughly ±20–30° of the input direction, snap
  toward the nearest enemy. [Hypothesis]
- Without snapping, players hit empty air and blame themselves, or the game.

______________________________________________________________________

## 3. Hit confirmation: every hit reported through several senses at once

### 3.1 Hitstop (hit pause)

**What it is.** [Practitioner] Sakurai, in a Famitsu column, describes hitstop
as a short freeze of both the attacker and the target at the moment of the hit.
It is "a crucial effect" for conveying impact.
([Source Gaming translation](https://sourcegaming.info/2015/11/11/thoughts-on-hitstop-sakurais-famitsu-column-vol-490-1/),
fan translation) His details:

- Duration generally scales with damage, with a **hard cap** so huge hits don't
  stall the game.
- Each attack can have its own multiplier. A sword's tip gets more hitstop than
  its edge, and electric attacks get extra.
- Projectiles get comparatively little hitstop.
- The **defender vibrates** during the freeze, with an amplitude that starts
  large and decays to zero by the end. The size of the vibration scales with
  camera distance so it still reads from far away.
- The **attacker also vibrates slightly and drifts forward imperceptibly**, then
  snaps back. The freeze feels like resistance, not like the game froze.
- The defender's pose blends into the hurt animation over about 4 frames during
  the freeze.
- In free-for-all modes, hitstop is kept short because a third player could take
  advantage of the freeze. **This matters a lot for ARPGs.**

**Implications for ARPGs**, which hit many enemies at once:

- [Practitioner] Sakurai notes that games like Dynasty Warriors, built on mass
  hits, largely skip hitstop: freezing on every hit would slow everything down.

- [Hypothesis] Use **per-target hitstop**. The enemies that were hit freeze and
  shake. The player freezes only briefly (1–3 frames), or not at all, for light
  hits. Freeze the whole screen only for major moments: finisher hits, boss
  hits, last kill in a room.

- [Hypothesis] Starting values:

  | Event                                  | Hitstop                                               |
  | -------------------------------------- | ----------------------------------------------------- |
  | Light hit                              | 2–4 frames on the target, 0–2 on the player           |
  | Heavy or combo finisher                | 5–8 frames                                            |
  | Crit                                   | +2 frames                                             |
  | Kill                                   | 4–8 frames                                            |
  | Boss phase break or room-clearing kill | 10–20 frames global, often with slow motion afterward |
  | Cap                                    | ~12 frames for normal play                            |

- [Hypothesis] Input received during hitstop **must be buffered** (§2.3).
  Otherwise hitstop feels like lag.

### 3.2 The other feedback channels

| Channel                                  | Starting values [Hypothesis]                                                                   | What it tells the player             |
| ---------------------------------------- | ---------------------------------------------------------------------------------------------- | ------------------------------------ |
| Hit flash (target turns white or tinted) | 1–3 frames                                                                                     | Which enemy got hit                  |
| Hit reaction / knockback                 | Along the attack direction; light hits push a short distance, heavy hits launch                | Cause and effect, physically         |
| Directional particles                    | Spray outward along the attack vector                                                          | Where the force went                 |
| Impact sound                             | Sharp transient plus a body layer, triggered on the **contact frame**, pitch randomized ±5–10% | That the hit landed, and its weight  |
| Screen shake                             | Small, fast decay (~100–200 ms), stronger for big events                                       | How big the hit was                  |
| Damage number                            | Size and color change for crits                                                                | How much, and whether it was special |
| Rumble                                   | Short pulse, scaled with hit size                                                              | Physical confirmation                |
| Kill effect                              | Distinct sound, burst or dissolve, brief hitstop                                               | The enemy is done, move on           |

Practitioner evidence:

- [Practitioner] Sébastien Bénard (Dead Cells) said the team's feedback
  references were all fighting games (Street Fighter IV, BlazBlue, Garou: Mark
  of the Wolves). They used particles, freeze frames and slowdowns from that
  genre.
  ([PC Gamer](https://www.pcgamer.com/dead-cells-combat-evolved-from-tower-defense-games-and-team-fortress-2))
- [Practitioner] Jan Willem Nijman (Vlambeer) described the many small effects
  behind a single pistol shot, including kicking the camera back 6 pixels from
  the aim direction and adding 4 to a fast-decaying shake value.
  ([infovore summary](https://infovore.org/?p=5275))
- [Practitioner] Jason McDonald (God of War 2018) has said that if you
  freeze-frame an axe hit, there is a frame where the axe is actually inside the
  enemy, and players can feel it even though they never consciously see it.
  ([Game Informer](https://www.gameinformer.com/b/features/archive/2018/01/15/how-kratos-axe-changes-god-of-wars-combat.aspx))

### 3.3 Screen shake: the trauma model

[Practitioner] Squirrel Eiserloh's GDC 2016 talk "Juicing Your Cameras With
Math" introduced a widely copied model.
([GDC Vault](https://gdcvault.com/play/1023146/Math-for-Game-Programmers-Juicing);
[Bevy implementation](https://bevy.org/examples/camera/2d-screen-shake/))

- Keep a `trauma` value from 0 to 1. Events *add* to it, and it decays linearly
  over time.
- The actual shake amount is `trauma²` or `trauma³`. Small events barely
  register, and stacked big events become dramatic. That gives you the scaling
  from §3.2 for free.
- Drive the offset with smooth noise (Perlin) rather than random jitter. Random
  jitter looks like a rendering glitch.
- Shake rotation as well as translation, if the camera allows it.

[Hypothesis] In a top-down ARPG, shake is the most overused channel. Light hits
should usually add zero shake. Save it for heavy hits, explosions and player
damage. Many players find heavy shake tiring, so offer a slider.

### 3.4 Visual budget: don't hide the outcome

The CHI 2024 study (§1) found that big swing effects **hid the hit effects**,
which reduced players' sense of competence. Practical rules \[Hypothesis\]:

- Swing trails should be thin, short-lived and partially transparent.
- Hit effects take priority over swing effects. Kill effects take priority over
  hit effects.
- Enemy telegraphs take priority over everything (§5).
- Test with a screenshot taken mid-combat: can you point to every threat and
  tell which enemies were just hit? If not, there is too much going on.

______________________________________________________________________

## 4. Forgiveness: the game quietly favors the player

### 4.1 Evidence from platformers

- [Practitioner] Celeste includes ten forgiveness mechanics, among them coyote
  time, jump buffering, corner correction, and wall jumps allowed from 2 pixels
  away (5 pixels for the harder "super" wall jump). The harder move gets the
  *bigger* tolerance.
  ([Celeste & Forgiveness](https://maddymakesgames.com/articles/celeste_and_forgiveness/))
- [Practitioner] Bénard says Dead Cells has about 30–40 small hidden tricks like
  these to make the game feel fair.
  ([80.lv](https://80.lv/articles/dead-cells-hidden-tricks-that-make-the-game-feel-fair))

### 4.2 ARPG equivalents [Hypothesis]

| Mechanism                                             | Suggested tuning                                                                         | Effect                                            |
| ----------------------------------------------------- | ---------------------------------------------------------------------------------------- | ------------------------------------------------- |
| Player attack hitbox larger than its visual effect    | 10–20% larger                                                                            | Hits that look like they connected always do      |
| Player hurtbox smaller than the sprite                | ~60–80% of sprite size                                                                   | Near-misses feel like skill, not luck             |
| Enemy projectile hitbox smaller than its visual       | ~70–80%                                                                                  | Grazes don't count; visual threat stays full size |
| Invulnerability starts on the first frame of the dash | Frame 1                                                                                  | A dodge pressed "just in time" works              |
| Late-dodge grace                                      | Damage resolves 1–2 frames after contact, so a dodge pressed in that window still counts | Matches Celeste's coyote time                     |
| No random miss chance for basic hits                  | Always hit on overlap                                                                    | Visual and mechanical truth match                 |
| Last-hit-point grace                                  | Brief invulnerability at very low health, or survive a lethal hit once per room          | Optional; reduces "cheap" deaths                  |

### 4.3 Limits

- [Hypothesis] A good pattern for the dash: full protection during a plain dash,
  with that protection *traded away* when the player attacks out of it (a
  dash-strike). Powerful offensive options should cost some safety. Community
  guides describe Hades' dash as protective, but I could not verify its exact
  invulnerability window or how dash-strikes affect it.
- **Generosity has limits.** Too much forgiveness makes success feel unearned
  and removes the curiosity that the CHI 2024 study tied to uncertain outcomes.
  Forgiveness should cover *execution* (timing, positioning), not *decisions*
  (standing in the wrong place for two seconds).

______________________________________________________________________

## 5. Movement and the dash

The dash is the core of Hades' combat. Everything else hangs off it.

**Targets** \[Hypothesis\]:

| Property            | Target                                                                 |
| ------------------- | ---------------------------------------------------------------------- |
| Dash duration       | ~8–12 frames (130–200 ms)                                              |
| Distance            | 2–4 character widths                                                   |
| Invulnerability     | From frame 1 through most of the dash                                  |
| Cooldown or charges | Short cooldown (~0.3–0.5 s) or 2+ charges                              |
| Cancels             | Dash cancels attack recovery; dash into attack becomes a "dash-strike" |
| Speed ratio         | Dash speed ≥ 2× the fastest enemy projectile that targets the player   |
| Base run speed      | Faster than most enemies, so positioning is a choice                   |

**Speed ratio.** If you dash faster than projectiles travel, a dodge is always
possible given a timely reaction. Fairness then becomes a question of *reading
the warning*, not of physics.

**Fixed camera.** Hades uses an isometric camera that the player never manages.
All attention goes to the fight. [Hypothesis] Every second spent fixing the
camera is a second not spent on the action loop.

______________________________________________________________________

## 6. Readability: threats are slow and obvious

### 6.1 Telegraphs

- [Practitioner] Mike Stout frames each enemy attack as a question posed to the
  player ("can you dodge this?", "can you get out of range?"). The telegraph is
  how the question is asked.
  ([Enemy Attacks and Telegraphing](https://gamedeveloper.com/design/enemy-attacks-and-telegraphing))

- [Hypothesis] Telegraph length should scale with damage and with area:

  | Attack            | Telegraph                                           |
  | ----------------- | --------------------------------------------------- |
  | Basic melee swing | 400–600 ms (distinct pose and sound)                |
  | Ranged shot       | 300–500 ms aim pose, plus a visibly slow projectile |
  | Area attack       | 600–1000 ms ground marker that fills up             |
  | Boss big attack   | 1000–1500 ms with sound and voice cue               |

- Every warning should use **at least two channels** (a pose plus a sound, or a
  ground marker plus a glow), so it still reads when one channel is hidden or
  missed.

- Ground markers should show **when** the hit lands as well as where. A filling
  circle is better than a static one.

### 6.2 Color and visual hierarchy

- [Hypothesis] Reserve one color family (say, red and orange) for things that
  will hurt the player. The player's own effects must never use it.
- Enemy telegraphs are drawn above the player's effects in the render order, or
  the player's effects fade when they overlap a telegraph.

### 6.3 Interrupts and armor

- [Hypothesis] Basic enemies should be staggered by player hits, so attacking is
  also a defense. Armored enemies must be **visibly** marked (Hades uses an
  armor bar). An enemy that ignores your hits without warning feels like a bug.
- [Hypothesis] Wall slams and trap knockback that deal bonus damage reward the
  player for *aiming* knockback, which turns the room layout into a weapon.

### 6.4 Limiting simultaneous attackers

- [Practitioner, secondary source] God of War 2018 reportedly used **aggression
  tokens**: only a limited number of enemies may attack at once, and hit
  reactions give the player short windows of advantage.
  ([third-party notes](https://coconote.app/notes/d7ee8609-618e-4cad-a470-0fff6b470dc4))
- [Hypothesis] In ARPGs this keeps big crowds readable. Twenty enemies can be on
  screen, but only two or three are actively winding up at any moment.

### 6.5 Metric: share of damage that was avoidable

[Hypothesis, proposed metric] For each hit taken, check whether a dash pressed
when the warning first became visible would have avoided it. Target: over 95%
avoidable. Anything that hits the player without a visible, dodgeable warning
(off-screen shots, effects hidden by particles, warnings shorter than reaction
time) gets flagged.

______________________________________________________________________

## 7. Pacing and density

[Hypothesis throughout]

- **Peak action density**: about 3–6 meaningful inputs per second. Lower feels
  sluggish; higher becomes mashing.
- **Encounter length**: 30–90 seconds per room, in 2–3 waves, with a short
  breather between waves. Reward right after the room is cleared.
- **Time to kill basic enemies**: 2–5 hits early in a run. Kills are the biggest
  feedback events, so they have to come regularly.
- **Spawn warnings**: new enemies appear with a 0.5–1 s marker. Nothing appears
  and attacks in the same instant.
- **Failure cost**: fast restarts and persistent progress make risk-taking
  attractive. Risky plays are where the best moments come from.

______________________________________________________________________

## 8. Growth over a run

- [Hypothesis] Upgrades that change **actions** ("your dash leaves a lightning
  trail") feel bigger than upgrades that change **numbers** ("+10% damage"),
  because they change what the player does in the action loop.
- [Hypothesis] Synergies between upgrades (Hades' Duo boons) create discovery
  moments, which tie to the curiosity finding in §1.
- [Hypothesis] **Time-to-kill curve**: plot hits-to-kill for basic enemies over
  a run. A satisfying curve trends *down* overall (the player outscales the
  enemies) with occasional spikes back up (elites, bosses, new biomes). A flat
  curve means upgrades aren't felt. A curve that drops too fast removes the
  challenge.
- [Study-informed hypothesis] **Variance is good when it is legible.** Crits,
  procs and random bonus effects give each action an uncertain outcome, which
  feeds the curiosity motive that predicted playtime in the CHI 2024 study. The
  result must be clearly shown (§3), or the uncertainty only frustrates.

______________________________________________________________________

## 9. Rhythm and audio

[Hypothesis throughout]

- **Combo cadence**: basic combos follow a steady beat (for example 3 hits at
  ~250 ms intervals) and end with a finisher that has more hitstop, more
  knockback and a distinct sound. The finisher marks the end of the phrase.
- **Exact sync**: hit sounds fire on the contact frame, never on swing start
  plus a fixed delay. A 1–2 frame drift makes hits feel "mushy."
- **Variation**: randomize pitch (±5–10%) and choose between 3–5 sample
  variations, so repeated hits never sound like a machine gun.
- **Escalation**: pitch or intensity can rise with combo count or kill streak,
  so players hear momentum building.
- **Ducking**: briefly lower music and ambient sound under big impacts, so the
  impacts read clearly.

______________________________________________________________________

## 10. Common ways it goes wrong

| Symptom players report           | Likely cause                                            | Fix        |
| -------------------------------- | ------------------------------------------------------- | ---------- |
| "Floaty" or "heavy"              | High latency, slow attack startup, blended pose changes | §2.1, §2.2 |
| "It ate my input"                | No buffer, or buffer cleared during hitstop             | §2.3       |
| "It did things I didn't press"   | Buffer too long, or stale inputs                        | §2.3       |
| "I'm stuck in animations"        | No dash cancel, long recovery                           | §2.4       |
| "Hits feel weak"                 | No hitstop, sound not on contact frame, no hit reaction | §3         |
| "I can't tell what's happening"  | Player effects hiding telegraphs or outcomes            | §3.4, §6   |
| "Deaths feel cheap"              | Off-screen attacks, short telegraphs, strict hitboxes   | §4, §6     |
| "It gets boring halfway through" | Upgrades only change numbers; time-to-kill curve flat   | §8         |
| "It's exhausting"                | Too much shake or flashing; no breaks in density        | §3.3, §7   |

______________________________________________________________________

## 11. How to validate this

### 11.1 Build a test room

A single combat room with:

- Every mechanism above behind a runtime toggle or slider: latency injection,
  buffer length, cancel windows, hitstop scale, each feedback channel, hitbox
  scale, telegraph length.
- Instrumented logging (§11.2).
- A fixed enemy roster and spawn sequence, so sessions are comparable.

[Practitioner] Sébastien Bénard's interactive game-feel demo is built on this
idea: you switch individual features on and off to feel their effect.
([deepnight.net game feel demo](https://deepnight.net/games/game-feel/))

### 11.2 What to log

| Metric                     | Definition                                                                      |
| -------------------------- | ------------------------------------------------------------------------------- |
| Input honor rate           | Share of inputs that triggered an action right away                             |
| Buffer rate                | Share of inputs that were buffered and later executed                           |
| Drop rate                  | Share of inputs that did nothing at all (should approach 0)                     |
| Lockout fraction           | Share of combat time when a new input would be dropped (§2.4)                   |
| Avoidable-damage share     | Share of damage taken that could have been dodged given the warning (§6.5)      |
| Hits-to-kill by enemy type | Over time, per build (§8)                                                       |
| Actions per second         | Mean and peak (§7)                                                              |
| Free-choice playtime       | Minutes played after the required session, when the player can quit at any time |

### 11.3 Study design

- **Small teams (5–20 testers):** within-subject pairwise comparisons. Each
  tester plays two variants back to back for 2–3 minutes without knowing which
  settings are active, then says which felt better. Randomize the order.
- **Large-scale (hundreds or more):** between-subject designs like Kao's
  studies. Pre-register what you'll measure.
- **Measure both** a self-reported feel score (1–7) and free-choice playtime,
  because enjoyment and playtime separated in the CHI 2024 study.
- Use **latency injection** in steps of 0, 33, 66 and 100 ms to find the point
  where ratings drop.
- **Remove one feedback channel at a time** to see which ones carry the most
  weight.

### 11.4 Predicted results

[Hypothesis] The order in which I expect mechanisms to matter, from biggest
effect to smallest:

1. Latency, buffering and cancels (responsiveness)
2. Hitstop and impact sound timed to the contact frame
3. Dash quality and the share of damage that was avoidable
4. Telegraph readability and visual hierarchy
5. Hitbox generosity
6. Everything else: particles, shake, damage numbers, rumble

I also predict that removing screen shake from light hits *improves* ratings,
and that swing effects louder than hit effects *lower* them, as the CHI 2024
study found.

Hades doesn't do any one thing magically. It does every item above at a high
standard at once. Players notice the weakest element in a combat system, not the
average.

______________________________________________________________________

## 12. Parameter cheat sheet

All values at 60 fps. Confidence: **H** = from a study or practitioner, **M** =
common practice, **L** = my estimate.

| Parameter                           | Starting value                                   | Confidence                                    |
| ----------------------------------- | ------------------------------------------------ | --------------------------------------------- |
| Press-to-photon latency             | < 70 ms (< 50 ms excellent)                      | M (thresholds from studies, target is mine)   |
| Player basic attack startup         | 3–6 frames                                       | L                                             |
| Enemy basic attack telegraph        | 400–750 ms                                       | L                                             |
| Input buffer window                 | 6–12 frames, most recent input only              | M                                             |
| Lockout before dash cancel          | ≤ 200–300 ms                                     | L                                             |
| Hitstop light / heavy / kill / cap  | 2–4 / 5–8 / 4–8 / ~12 frames                     | M (practice); scaling and cap are H (Sakurai) |
| Hit flash                           | 1–3 frames                                       | M                                             |
| Screen shake                        | trauma model, shake = trauma², linear decay      | H (Eiserloh)                                  |
| Sound pitch randomization           | ±5–10%                                           | M                                             |
| Player hurtbox                      | 60–80% of sprite                                 | L                                             |
| Player attack hitbox                | 10–20% larger than visual                        | L                                             |
| Dash                                | 8–12 frames, invulnerable from frame 1           | L                                             |
| Avoidable-damage share              | > 95%                                            | L                                             |
| Peak actions per second             | 3–6                                              | L                                             |
| Room length                         | 30–90 s                                          | L                                             |
| Basic enemy hits-to-kill, early run | 2–5                                              | L                                             |
| Feedback amount                     | Medium–high; avoid none and extreme              | H (Kao 2020)                                  |
| Feedback trigger                    | On success (hits, kills), not on action (swings) | H (Kao et al. 2024)                           |

______________________________________________________________________

## Sources

Studies:

- Kao, Ballou, Gerling, Breitsohl, Deterding. "How does Juicy Game Feedback
  Motivate? Testing Curiosity, Competence, and Effectance." CHI 2024.
  <https://dl.acm.org/doi/full/10.1145/3613904.3642656>
- Kao. "The effects of juiciness in an action RPG." *Entertainment Computing*,
  2020\. Summary:
  <https://www.goodreads.com/author_blog_posts/19932694-the-effects-of-juiciness-in-an-action-rpg-new-study>
- Raaen & Eg. "Instantaneous human-computer interactions: Button causes and
  screen effects." 2015.
  <https://www.simula.no/publications/instantaneous-human-computer-interactions-button-causes-and-screen-effects>
- Raaen, Eg et al. "How to demonstrate delay? Let's play!" MMSys 2016.
  <https://mmsys2016.itec.aau.at/papers/MMSYS/a28-westerdals.pdf>
- Liu, Claypool et al. Local vs. network latency in CS:GO.
  <https://web.cs.wpi.edu/~claypool/papers/csgo-net-local-21/paper.pdf>
- WPI student project on cloud gaming latency (low confidence).
  <https://web.cs.wpi.edu/~claypool/iqp/onlive/>

Practitioners:

- Maddy Thorson. "Celeste & Forgiveness."
  <https://maddymakesgames.com/articles/celeste_and_forgiveness/>
- Masahiro Sakurai, Famitsu column on hitstop (fan translation).
  <https://sourcegaming.info/2015/11/11/thoughts-on-hitstop-sakurais-famitsu-column-vol-490-1/>
- Sakurai on an attack's first frame (summary).
  <https://www.nintenderos.com/2022/12/sakurai-destaca-la-importancia-del-primer-frame-de-un-ataque-en-un-videojuego/>
- Squirrel Eiserloh. "Math for Game Programmers: Juicing Your Cameras With
  Math." GDC 2016.
  <https://gdcvault.com/play/1023146/Math-for-Game-Programmers-Juicing>
- Bevy screen-shake example implementing the trauma model.
  <https://bevy.org/examples/camera/2d-screen-shake/>
- Jan Willem Nijman on game feel (summary). <https://infovore.org/?p=5275>
- Sébastien Bénard on Dead Cells feedback.
  <https://www.pcgamer.com/dead-cells-combat-evolved-from-tower-defense-games-and-team-fortress-2>
- Bénard on hidden fairness tricks.
  <https://80.lv/articles/dead-cells-hidden-tricks-that-make-the-game-feel-fair>
- Bénard's interactive game-feel demo. <https://deepnight.net/games/game-feel/>
- Jason McDonald on God of War's axe.
  <https://www.gameinformer.com/b/features/archive/2018/01/15/how-kratos-axe-changes-god-of-wars-combat.aspx>
- Mike Stout. "Enemy Attacks and Telegraphing."
  <https://gamedeveloper.com/design/enemy-attacks-and-telegraphing>
- Steve Swink, *Game Feel* (as summarized).
  <https://en.wikipedia.org/wiki/Game_feel>;
  <https://www.gamedeveloper.com/design/game-feel-the-secret-ingredient>

Community and secondary (low confidence):

- Steam discussion on Hades II dash canceling.
  <https://steamcommunity.com/app/1145350/discussions/0/4358998752031959500>
- Notes on God of War GDC talk (aggression tokens).
  <https://coconote.app/notes/d7ee8609-618e-4cad-a470-0fff6b470dc4>
