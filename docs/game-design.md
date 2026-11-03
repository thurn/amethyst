# Project Amethyst: Game Design

The initial design for the Project Amethyst MVP: one run through one biome, with
a sword, a dash and a special, and an upgrade system built so every pick changes
what the player does and sees.

[What Makes ARPG Combat Feel Great](arpg-combat-feel.md) is the companion
reference. This document says *what* the game is; the feel reference says how
every part of it must feel. Section references like "feel §3.1" point there.

## The game in one paragraph

A knight descends into the Amethyst, a living geode that grows its monsters out
of crystal. Each run is twelve connected rooms with no loading between them:
fight, clear, choose one of three upgrades, walk into the next room. A crystal
golem guards the midpoint and a dragon sleeps at the heart. The knight fights
with three buttons: a sword combo, a dash, and a leaping slam. Four crystal
"suites" of upgrades (Voltage, Frost, Void, Gale) reshape those three actions,
and pairs of suites combine into Synergies. Everything is drawn as glowing neon
edges on black, in the synthwave wireframe look of Playlist Survivors.

## Pillars

1. **Feel decides what exists.** A mechanic ships only if it gets first-class
   animation, effects and sound. If the asset set can't support it, it waits.
   Every number in this document is a starting value for a testbed, not a
   promise.
2. **Every outcome is confirmed.** Feedback reports success: hits, kills,
   status, breaks (feel §1, §3). Swings stay thin; hits, kills and telegraphs
   win every contest for the screen (feel §3.4).
3. **Upgrades change actions, not numbers** (feel §8). No upgrade is only a
   number; any number an upgrade raises comes with something you can see or
   hear.
4. **Danger is readable.** Red and orange mean "this will hurt you", and only
   that. Every threat has a telegraph on two channels (feel §6).
5. **The base game must be fun on its own.** Rhythm is out of scope (see
   [Out of scope](#out-of-scope)).

## Influences

- **Hades and Hades II**: the room-to-room run, the dash as the center of
  combat, slot-based boons from themed sources. Hades II's dash that can't
  cancel attacks is the cautionary tale (feel §2.4).
- **Brotato**: the player steers a build by spending currency on rerolls and
  rejects, which matters here because rooms have no reward previews.
- **Path of Exile**: status effects that feed each other (chains, freezes,
  delayed bursts) are where build discovery lives.
- **Playlist Survivors**: the neon wireframe look, the shatter on kill, the
  asset set, and the lesson that rhythm is an accent that can't carry a game.

## Decisions at a glance

| Area             | Decision                                                                   |
| ---------------- | -------------------------------------------------------------------------- |
| Run              | 12 rooms, ~15 minutes, linear, no loading, one biome (the Geode)           |
| Variety          | Shuffled pool of combat room modules; randomized waves; fixed boss rooms   |
| Controls         | WASD move, Space dash, LMB/F attack, RMB/Q special, E interact             |
| Aim              | Cursor aim and movement-direction aim, both behind a flag                  |
| Camera           | Fixed 3/4 top-down, never rotates                                          |
| Kit              | Cleave (cone), Lunge dash-strike (line), Leap Slam special (circle), Dodge |
| Economy          | Dash: 1 charge, ~0.35 s cooldown. Special: ~2 s cooldown                   |
| Health           | 100 HP, no regen, two full heals, one Second Wind, instant restart         |
| Upgrades         | 3 exclusive action slots + passives; 4 suites; 3 Levels each; 6 Synergies  |
| Offers           | 3 cards from 3 suites; rerolls and banish cost Shards; two shops           |
| Enemies          | 6 types, Crystallized elites, aggression tokens                            |
| Bosses           | The Warden (midpoint), the Heartwyrm (end); Break bar                      |
| Look             | Neon wireframe; enemies dim lavender, tinted by status                     |
| Music            | Bundled soundtrack or `--file <m3u> --shuffle`; nothing reads the beat     |
| Meta-progression | None                                                                       |

## The run

### Structure

| Room | Name        | What happens                                                        |
| ---- | ----------- | ------------------------------------------------------------------- |
| 0    | Threshold   | A tutorial-paced fight against a few Kobolds; grants the first pick |
| 1–4  | Early rooms | Combat, 45–90 s each, 1–3 waves, escalating                         |
| 5    | The Warden  | Miniboss                                                            |
| 6    | Respite     | Fountain (full heal) and shop; no fight                             |
| 7–10 | Late rooms  | Combat with elites and the Golem                                    |
| 11   | Antechamber | Fountain (full heal) and shop; a quiet beat before the end          |
| 12   | Heartwyrm   | Final boss                                                          |

Nine combat rooms and two bosses give about eleven picks per run: enough to fill
all three slots, collect four or five passives, and find one or two Synergies.

### Connected rooms

All rooms are laid out in one continuous world, so walking from one to the next
never loads.

- **Clear.** The last kill gets the run's standard big moment: 10–20 frames of
  global hitstop and a short slow-motion tail (feel §3.1). The exit's laser gate
  dissolves, the room's Shards stream to the hero, and a prism shard forms where
  the last enemy died.
- **Corridor.** A 3–5 s walk to the next room is the breather between fights.
- **Entry.** Stepping into the next room seals the gate behind the hero. The
  first wave's spawn markers appear, and nothing attacks until they finish
  (0.5–1 s, feel §7).

The chain is linear: one exit per room. Forks are deferred; without reward
previews they'd be blind picks.

### Variety between runs

- **Combat rooms** are hand-built modules that all share the same doorway
  sockets, so any module can follow any other. About ten modules split into two
  tiers (early for rooms 1–4, late for rooms 7–10), shuffled each run.
- **Waves** are drawn from per-tier encounter tables, so the same module plays
  differently.
- **Fixed rooms**: the Threshold, the Warden's arena, the Respite, the
  Antechamber and the Heartwyrm's arena.

### Health and death

- The hero has 100 HP and doesn't regenerate, so damage carries from room to
  room. That carry is what makes avoidable hits matter.
- The fountains in the Respite and the Antechamber heal fully.
- **Second Wind**, once per run: lethal damage revives the hero at 40% HP with a
  slow-motion shockwave that knocks back everything nearby.
- Some upgrades heal as a side effect. Healing is never one of the three
  choices.
- Death shatters the hero into white shards. A 5-second **run card** follows
  (time, rooms cleared, the build's slots and Synergies, damage taken, and the
  share of hits that were avoidable, feel §6.5), then a key press starts the
  next run in the Threshold. Winning shows the same card.

There is no hub, no meta-progression, and no story beyond the paragraph above.
The game launches straight into the Threshold.

## Controls and camera

| Input            | Action                                   |
| ---------------- | ---------------------------------------- |
| WASD             | Move                                     |
| Space            | Dash                                     |
| Left click or F  | Attack (Cleave; Lunge out of a dash)     |
| Right click or Q | Special (Leap Slam)                      |
| E                | Interact: prism shards, shops, fountains |

**Aim** is a flag with two settings, to be compared by feel:

- **Cursor**: attack and special aim at the mouse cursor, snapping to the
  nearest enemy within ±25° (feel §2.5). The dash follows WASD, or the cursor
  when no key is held. The Leap Slam lands at the cursor, at most 4 m away.
- **Movement direction**: attacks go where the hero is moving or last faced, as
  Hades plays on a controller. This also keeps controller support open.

In both, the Lunge follows the dash direction, so a dash is also an aim.

**Camera**: fixed 3/4 top-down at about 55° pitch. It follows the hero, leans
slightly toward the cursor, and never rotates (feel §5). It zooms only for boss
introductions.

## The hero's kit

The hero is the Synty Polygon knight (sword and shield) animated by Synty's
Sword Combat pack, the same figure Playlist Survivors used. Three moves, three
shapes, so each upgrade suite has an obvious canvas:

| Move      | Input                       | Clips (Sword Combat)              | Shape  | Role                                |
| --------- | --------------------------- | --------------------------------- | ------ | ----------------------------------- |
| Cleave    | Attack                      | `LightCombo01` A → B → C          | Cone   | Main damage; C is the finisher      |
| Lunge     | Attack during or after dash | `LightFencing01` or `HeavyStab01` | Line   | Dash-strike; pierces along the dash |
| Leap Slam | Special                     | `LightLeaping01`                  | Circle | Gap-close and crowd control         |
| Dash      | Space                       | `Dodge_F/B/L/R`                   | —      | Defense and movement                |

- **Cleave**: three cuts at an even cadence (~250 ms apart). The finisher C gets
  more hitstop, more knockback and its own sound, and marks the end of the
  phrase (feel §9). Hits stagger basic enemies, so attacking is also defense.
- **Lunge**: attacking during the dash or just after it thrusts along the dash
  line, piercing everything in it, for more damage than a Cleave hit. Dash
  invulnerability ends the moment the Lunge starts: the strong option costs
  safety (feel §4.3).
- **Leap Slam**: a leap toward the aim point (up to 4 m) that lands with a ring
  and radial knockback. It commits the hero: no invulnerability in the air and
  no dash-cancel until it lands. A ~2 s cooldown, shown as a ring under the
  hero.
- **Dash**: the short dodge, not the roll, which reads as slow. Invulnerable
  from frame 1 through most of its length; one charge with a ~0.35 s cooldown.
  It cancels the recovery of every attack (feel §2.4).
- **Getting hit**: directional `Hit_*_React` for light hits, `Hit_*_Stagger` for
  heavy ones, then about 0.8 s of invulnerability. Player damage is the one
  place light screen shake is always on (feel §3.3).

### Clip timing

The Synty clips were made for weightier games. `LightCombo01A` contacts at 350
ms, `HeavyStab01` at 883 ms, `LightLeaping01` at 1.1 s, while the feel targets
are 50–100 ms for basic attacks and 130–250 ms for specials (feel §2.2). So
every move starts its clip just before the contact frame and skips most of the
windup. `HeavyFlourish01` (contact at 1.43 s) is nearly all windup, which is why
there's no spin in the base kit.

The test is the first rendered frame after the press (feel §2.1): it must
already read as the strike. If the trimmed clips look like a pose snap rather
than a swing, the fallback is a variant with Mixamo's free sword sets,
retargeted to the same humanoid rig, and then a small purchase of game-speed
sword clips.

### Starting values

| Parameter     | Value                        |
| ------------- | ---------------------------- |
| Cleave damage | 10 / 10 / 18 (finisher)      |
| Lunge damage  | 22, pierces                  |
| Leap Slam     | 25 in a 2.5 m ring           |
| Dash          | ~3.5 m in ~10 frames         |
| Run speed     | Faster than every enemy      |
| Input buffer  | 6–12 frames, last input wins |

## Upgrades

### Structure

- **Three action slots.** *Attack* (Cleave and Lunge, both on the attack
  button), *Special* (Leap Slam) and *Dash* (the dodge itself: its start, its
  path, its end). A slot holds one upgrade; taking another for the same slot
  replaces it, and the card says what it replaces.
- **Passives** have no slot and stack freely.
- **Synergies** need upgrades from two suites.

Exclusive slots are a feel rule as much as a build rule: each action wears at
most one suite's effects, so a swing never stacks three glows over the hit it
should be showing (feel §3.4, the occlusion finding).

### Suites

Suites stand in for Hades' gods. They're abstract crystal resonances, with no
characters or dialogue. Each one is a complete package: one hue, one element
folder from the RPG Magic Sound Effects Pack 3, one effects family, and one
status effect that all of its upgrades feed.

| Suite   | Hue     | Status and verb                                                     | Sound                         | Effect references                               |
| ------- | ------- | ------------------------------------------------------------------- | ----------------------------- | ----------------------------------------------- |
| Voltage | Indigo  | **Charge**: 3 stacks discharge as a bolt that chains                | Electric Magic, ZAPS, THUNDER | Hovl Lightning strike/hit, Magic circle electro |
| Frost   | Cyan    | **Chill** slows; 5 stacks **Freeze**; frozen kills **Shatter**      | Ice Magic, glass SHATTER      | Hovl Ise attack, blue crystal flashes           |
| Void    | Magenta | **Rift**: stores damage for 1 s, then bursts; pulls enemies in      | Dark Magic, SPACE_WARPS       | Hovl Black hole, Darkness signet                |
| Gale    | Mint    | **Knockback**; **Impact** damage when a pushed enemy hits something | Wind Magic, WHOOSHES          | Hovl Slash wave                                 |

- **Voltage** chains: bolts are glowing segments, the look's native shape.
- **Frost** controls, then pays off: frozen enemies become faceted crystal, and
  shattering them throws shards that hit others.
- **Void** groups and delays: it pulls enemies together, then bursts them.
- **Gale** turns the room into the weapon (feel §6.3): walls, pillars, crystals
  and other enemies deal Impact damage. It also holds the mobility upgrades.

**Fire** is left out because red and orange belong to hazards. **Gold** stays
unused.

The stock effect prefabs are shape and timing references. Playlist Survivors
found they don't read in the wireframe look, so each effect is redrawn as line
geometry in its suite's hue, as the Hovl slashes were redrawn in white there.

### Levels

There are no rarity tiers. Every upgrade has three Levels, and each Level adds a
visible and audible layer instead of only bigger numbers. The art cost is three
intensities per upgrade, but each tier adds one layer to the last rather than
replacing it.

Levels come from a *Refine* card in an offer ("Refine: Arc Cleave → II"), which
appears more often after the midpoint, or from a shop.

### Offers

- **The prism.** On a room clear, a prism shard forms where the last enemy died,
  showing the hues of the three suites on offer. Pressing E freezes and dims the
  world.
- **Three cards from three different suites.** Once the player owns any upgrade,
  at least one card comes from an owned suite. An eligible Synergy takes one
  card, with a distinct frame.
- **Hover preview.** Hovering a card makes the frozen hero act out that move
  with the new effects, so the player picks an effect they've seen, not just a
  line of text.
- **Reroll**: the shard re-forms with three new cards. It costs Shards, and
  costs more each time within the same offer (5, then 10, then 20).
- **Banish**: shatter a card instead of taking it, removing that upgrade for the
  rest of the run (5 Shards). It narrows the pool toward the build the player
  wants.

### Shards

The run's currency is Shards, crystal fragments. They don't carry between runs.

- **Earning**: every kill drops a few (fodder 1–2, elites 5). They hover where
  the enemy died and stream to the hero when the room clears, so nobody chases
  pickups mid-fight and the clear gets a payout moment.
- **Clean bonus**: a room cleared without taking damage pays 10 extra, and says
  so on the payout. It rewards dodging well (feel §6.5).
- **Shops** in the Respite and the Antechamber sell three upgrade cards (~40),
  Refine on an owned upgrade of the player's choice (~30), and restoring a spent
  Second Wind (~60). The fountain in the same room already heals fully, so the
  shop's heal is the Second Wind.

### Status effects on elites and bosses

| Effect    | Crystallized elite (while shelled) | Boss                         |
| --------- | ---------------------------------- | ---------------------------- |
| Charge    | Works                              | Works                        |
| Chill     | Works                              | Slows its attacks, up to 20% |
| Freeze    | Immune                             | Converted to Break damage    |
| Rift      | Works                              | Works                        |
| Pull      | Immune                             | Doesn't move it              |
| Knockback | Immune                             | Converted to Break damage    |
| Stagger   | Immune                             | Only through Break           |

## The suites

Each suite has three slot upgrades and three passives. The levels read I / II /
III.

### Voltage

Charge: each stack shows as a flicker of indigo on the enemy's lines. At 3
stacks the enemy discharges: a bolt strikes it and chains to the 2 nearest
enemies within 5 m.

| Upgrade                | I                                                                             | II                                                | III                                                                                             |
| ---------------------- | ----------------------------------------------------------------------------- | ------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| Arc Cleave (Attack)    | Cleave hits add 1 Charge, the finisher 2; the Lunge Charges all it pierces    | Chains reach 1 more enemy; bolts fork visibly     | The finisher calls a vertical thunder strike that discharges its target at once                 |
| Thunderfall (Special)  | A lightning column on landing; 2 Charge to all in the ring                    | Everything already Charged in the ring discharges | Two more columns fall on the ring's edge half a second later                                    |
| Static Step (Dash)     | The dash path stays a live wire for 1.5 s, Charging what touches it           | The wire lasts 3 s and Charges every 0.5 s        | Dashing again while a wire is live links the two with a bolt that discharges everything between |
| Capacitor (Passive)    | Discharges chain to 1 more enemy                                              | 2 more; bolts thicken                             | Chains can arc back to their first target                                                       |
| Ground Fault (Passive) | Charged enemies discharge when they die                                       | That death discharge chains once more             | It leaves a crackling floor patch for 1 s that Charges                                          |
| Overload (Passive)     | Hitting an enemy at full Charge staggers it, even shelled, with a spark burst | Overloaded hits chip shells twice as fast         | A discharge breaks a Crystallized shell outright                                                |

### Frost

Chill: each stack slows by 10% and frosts the enemy's lines cyan. At 5 stacks
the enemy Freezes for 2 s inside a faceted crystal. A Frozen enemy that dies
Shatters, throwing six shards that deal damage and add 1 Chill.

| Upgrade                | I                                                                           | II                                                                          | III                                                                         |
| ---------------------- | --------------------------------------------------------------------------- | --------------------------------------------------------------------------- | --------------------------------------------------------------------------- |
| Rime Edge (Attack)     | Cleave hits add 1 Chill, the finisher 2; the Lunge adds 2 to all it pierces | The finisher deals double to Frozen enemies and cracks them visibly         | The finisher shatters any Frozen enemy below 30% HP                         |
| Glacier Slam (Special) | A ring of crystal spikes on landing; 3 Chill to all in the ring             | The spikes stay 2 s as walls (they block enemies and count for Gale Impact) | The inner half of the ring Freezes instantly                                |
| Frostwake (Dash)       | The dash leaves a frost trail for 2 s that Chills enemies on it             | Dashing through an enemy adds 2 Chill                                       | The trail erupts into small spikes (2 Chill) as it fades                    |
| Brittle (Passive)      | Frozen enemies take +25% damage; cracks show on them                        | +50%; deeper cracks                                                         | +75%; cracks glow                                                           |
| Cold Snap (Passive)    | Shatter shards add 2 Chill                                                  | Shards fly farther                                                          | A shard that kills a Frozen enemy shatters it too (two generations at most) |
| Permafrost (Passive)   | Freeze lasts 3 s                                                            | 4 s                                                                         | A thawing enemy releases a cold pulse (2 Chill nearby)                      |

### Void

Rift: a magenta ring that marks an enemy and stores 30% of the damage dealt to
it for 1 s, then bursts for the stored amount in a 2 m radius.

| Upgrade                 | I                                                                                                                             | II                              | III                                                           |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------- | ------------------------------- | ------------------------------------------------------------- |
| Riftblade (Attack)      | The first hit opens a Rift; the Lunge tears a rift along its line that pulls enemies in for 0.5 s, then snaps shut for damage | Rifts store 45%                 | The finisher reopens a Rift on a target whose Rift just burst |
| Singularity (Special)   | Landing opens a black hole for 1.5 s that pulls enemies in, then collapses, Rifting all it caught                             | 2.5 s, and wider                | The collapse leaves a second, smaller singularity             |
| Phase Step (Dash)       | A void echo where the dash began pulls nearby enemies for 0.6 s, then bursts                                                  | The echo Rifts what it hits     | Dashing back into the echo detonates it at once, for double   |
| Event Horizon (Passive) | Void pulls are 30% stronger and wider                                                                                         | 60%; the pull lines draw longer | 90%                                                           |
| Entropy (Passive)       | A burst opens Rifts on the enemies it hits (one generation)                                                                   | Bursts are 50% larger           | Two generations                                               |
| Hollow (Passive)        | A burst's size grows with the damage stored; the ring swells as it fills                                                      | The store cap rises             | The cap rises again; full Rifts hum before bursting           |

### Gale

Knockback, and Impact: an enemy pushed into a wall, pillar, crystal or other
enemy takes damage scaled by its speed, with its own hitstop and sound. Mint
lines trail pushed enemies.

| Upgrade              | I                                                                                                                                   | II                                    | III                                                       |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------- | --------------------------------------------------------- |
| Gust Cleave (Attack) | Cleave pushes 1 m farther; the finisher sends a wind crescent 6 m that pushes all it passes; the Lunge parts enemies to either side | The crescent is wider and pierces     | The finisher fires three crescents in a fan               |
| Downburst (Special)  | The landing ring pushes enemies 4 m; wall slams get 6 more frames of hitstop                                                        | Pushes 6 m                            | An enemy that Impacts is launched again                   |
| Slipstream (Dash)    | The dash shoves enemies in its path aside                                                                                           | A second dash charge                  | The dash ends in a gust that pushes everything within 3 m |
| Impact (Passive)     | +50% Impact damage                                                                                                                  | +100%; slam lines brighten            | +150%                                                     |
| Pinball (Passive)    | An enemy knocked into another knocks it back too                                                                                    | The chain continues to a second enemy | A third; each link pitches the impact sound up (feel §9)  |
| Tailwind (Passive)   | +10% move speed; a Lunge hit refunds 0.1 s of dash cooldown; mint speed lines                                                       | +15%, 0.2 s                           | +20%, 0.3 s                                               |

### Synergies

One Synergy per pair of suites. A Synergy becomes eligible once the player owns
any upgrade from each of its two suites. Each one adds an interaction, not a
number.

| Synergy        | Suites         | Effect                                                                                                         |
| -------------- | -------------- | -------------------------------------------------------------------------------------------------------------- |
| Superconductor | Voltage, Frost | Bolts jump freely between Frozen enemies and shatter them                                                      |
| Ion Rift       | Voltage, Void  | A Rift burst fires a bolt at every Charged enemy in range                                                      |
| Thunderhead    | Voltage, Gale  | An enemy that Impacts discharges a bolt                                                                        |
| Absolute Zero  | Frost, Void    | Enemies pulled together by Void freeze as one crystal; shattering it shatters all of them                      |
| Hailstone      | Frost, Gale    | A Frozen enemy you knock back becomes a projectile that shatters on whatever it hits                           |
| Maelstrom      | Void, Gale     | The Leap Slam ring pulls inward instead of pushing out, and enemies colliding in the vortex take Impact damage |

## Enemies

### Rules

- **Density**: 6–14 enemies per room over 2–3 waves; at most ~6 alive at once
  early in the run and ~9 late.
- **Aggression tokens** (feel §6.4): only 2 enemies may wind up an attack at the
  same time early, 3 late.
- **Time to kill**: basic enemies die in 2–4 hits early in the run (feel §7).
- **Telegraphs** follow feel §6.1: 400–600 ms for melee swings, 300–500 ms aim
  poses with slow projectiles, 600–1000 ms filling ground markers for areas.
  Every warning uses two channels, a pose plus a sound or a marker plus a glow.
- **Speed**: enemy projectiles travel at most half the dash's speed (feel §5).
- **Spawns** show a 0.5–1 s marker first.
- **Kills** shatter the enemy along outlined cracks, thrown away from the blow,
  gone within half a second. A kill earns empty space.

### Roster

| Enemy        | Source                                | Role     | The question it asks (feel §6.1)                   | Key clips                                                  |
| ------------ | ------------------------------------- | -------- | -------------------------------------------------- | ---------------------------------------------------------- |
| Kobold       | Heroic Fantasy, Must Have Villains    | Fodder   | Can you interrupt or step out of a combo?          | `Attack1–4`; `3HitCombo` and `4HitCombo` late              |
| Spiderling   | Polygonal Creatures, Spiderling Venom | Swarm    | Can you handle many fast, weak threats?            | `Jump`, `Bite Attack`, `Claw Attack`                       |
| One-Eyed Bat | Polygonal Creatures                   | Ranged   | Can you dodge shots while fighting, then close in? | `Projectile Attack`, `Dodge`, `Spawn`                      |
| Bomber Bug   | Magic Pig                             | Kamikaze | Can you get clear, or turn the blast on others?    | `Run`, `Explode`, `Fly Forward`                            |
| Rock Monster | Magic Pig                             | Ambusher | Did you read the room?                             | `RubblePose`, `RubbleToIdle`, `Attack01a/b`, `Jump`        |
| Golem        | Polygonal Creatures                   | Armored  | Can you break armor and avoid the slam?            | `Punch Attack`, `Double Punch Attack`, `Hit Ground Attack` |

- **Kobolds** are the most numerous. Early ones throw single attacks; late ones
  chain their 3- and 4-hit combos, each hit a new chance to stagger them.
- **Spiderlings** arrive in groups of 4–6 and die in one or two hits: the Leap
  Slam's payoff.
- **One-Eyed Bats** hover at range and fire slow orbs, sidestepping with `Dodge`
  when the hero lunges.
- **Bomber Bugs** run at the hero and explode after a filling red ring. The
  blast hurts enemies too, so Gale builds can knock them into crowds.
- **Rock Monsters** sit among the room's rocks as rubble and rise when the hero
  comes near. Fairness: their rubble carries a faint lavender glint the real
  rocks don't.
- **Golems** are always armored: a crystal shell with an armor bar. Their ground
  slam is a filling ring.

The Kobold comes from a denser, non-low-poly pack (the same pack as Playlist
Survivors' Fat Ogre). If its wireframe mesh can't be made to read cleanly, the
Toon Skeleton Knight or Dark Creatures Wererat Soldier fill the same role.

### Elites: Crystallized

Any enemy except the Golem can appear Crystallized: a faceted crystal shell
drawn as bright facets over its body, with a visible armor bar (feel §6.3).

- Hits chip the shell. Breaking it is a big moment: hitstop, the glass SHATTER
  sounds, shards flying.
- While shelled, the elite ignores stagger, Freeze and knockback, so each suite
  shows what it's good against.
- Elites start in room 3 and reach about one per wave by rooms 7–10.

### Escalation

| Rooms | Waves | Alive at once | Tokens | New enemies   | Elites      |
| ----- | ----- | ------------- | ------ | ------------- | ----------- |
| 0     | 1–2   | 4             | 1      | Kobolds       | —           |
| 1     | 2     | 5             | 2      | Spiderlings   | —           |
| 2     | 2     | 6             | 2      | One-Eyed Bats | —           |
| 3     | 2–3   | 6             | 2      | Bomber Bugs   | 1           |
| 4     | 3     | 7             | 2      | Rock Monsters | 1–2         |
| 7     | 2–3   | 8             | 3      | Golem         | 2           |
| 8–10  | 3     | 9             | 3      | All           | ~1 per wave |

## Bosses

### Shared rules

- **Break bar.** A bar under the boss's health. Every hit fills it, and the
  finisher, the Lunge and the Leap Slam fill it more. When it fills, the boss is
  **Broken** for ~2.5 s: 10–20 frames of global hitstop on the break (feel
  §3.1), a stun pose, and +50% damage taken. Each Break makes the next one
  harder. Hitting a boss never feels like hitting a wall, and the dash-strike is
  the way in.
- **Suite effects convert** (see the table above).
- **Phases**: two for the Warden, three for the Heartwyrm. Each change is a
  short invulnerable roar, never a cutscene.
- **Adds** come in small, capped groups.
- **No timers.** Strong builds end fights sooner; no phase waits on a clock.
- **Required dodges** never come closer together than the dash cooldown plus
  reaction time, and no two patterns overlap into an unavoidable hit.

### The Warden (room 5)

A crystal golem built on the Heroic Fantasy Demonic Golem (39 clips). The
contrast to the dragon: a close-range test of fundamentals. Dodge the combo,
punish the recovery.

| Attack          | Clips                                     | Telegraph                                                               | Answer                                             |
| --------------- | ----------------------------------------- | ----------------------------------------------------------------------- | -------------------------------------------------- |
| Advancing combo | `2HitCombo_A_Forward`, `3HitComboForward` | 500–600 ms raised-arm pose and a grinding sound; steps forward each hit | Leave its lane sideways, punish the recovery       |
| Flank sweep     | `Turn90AttackLeft/Right`                  | Hero on its flank for over 1 s: shoulder turn and a filling arc         | Don't circle forever; move or dash through         |
| Slam            | One of `Attack1–5`, chosen by eye         | 900 ms filling marker                                                   | Its shockwave ring expands to 8 m: dash through it |

- **Phase 2 (50%)**: `Roar`. It grows a crystal shell with an armor bar. Its
  slams now also send three traveling spike lines across the floor, each shown
  by a filling lane. Once, it calls 3 Kobolds.
- **Broken**: it has no stun loop, so it holds `GetHitHeavy`'s deepest frame.
  Breaking it in phase 2 shatters the shell off.

### The Heartwyrm (room 12)

The Magic Pig dragon (48 clips, already used by Playlist Survivors). Its breath
is fire, so its attacks are naturally in the hazard color.

**Phase 1, grounded (100–66%)**

| Attack       | Clips                           | Telegraph                                                     | Answer               |
| ------------ | ------------------------------- | ------------------------------------------------------------- | -------------------- |
| Bite         | `Attack 1`, `Attack 2`          | 500 ms head draw-back; short front cone                       | Stay off its nose    |
| Tail whip    | `Tail Whip L/R`                 | Hero behind or beside it: 700 ms tail raise and a filling arc | Don't camp its back  |
| Breath sweep | `Fire Head 1/2`, `Breathe Fire` | 1–1.2 s throat glow and a filling cone; the cone sweeps 60°   | Get behind the sweep |

Broken: `StunnedStanding`.

**Phase 2, airborne (66–33%)**

- `Idle Takeoff`, then it circles the arena edge.
- **Strafing runs**: a lane marker fills, then it crosses the arena low,
  breathing fire along the lane (`Fly Fast`, `Fly Breathe Fire`). Low passes are
  in reach: dodge through the lane and strike its flank as it goes.
- **Dive**: `Fly Dive` onto a filling landing marker placed where the hero stood
  when it appeared. The landing ring is the dash-through test, and the ~2 s
  `Idle Landing` recovery is the punish window.
- **Crystal bats** (One-Eyed Bats) join, at most 3 alive.
- **Out of the sky**: Breaking it in the air plays `StunnedFlying`, drops it
  crashing to the floor, and leaves it Broken for a long 3.5 s. The run's
  biggest moment.

**Phase 3, grounded (33–0%)**

- A roar lights every Resonant Crystal and Spike Vent in the arena.
- The breath sweeps all the way around, with a safe gap that follows it.
- Concentric rings and earlier patterns combine, under the shared rule above:
  never an unavoidable overlap, always a recovery interval.
- Its `Death` plays with the run's largest shatter and slow motion, then the run
  card.

## Rooms and hazards

Each combat room uses one or two hazard types. The Threshold uses none.

- **Walls and pillars** are solid, drawn in grey, and they're what Gale's Impact
  hits.
- **Resonant Crystals** are destructible clusters that shatter in an area when
  struck or when an enemy is slammed into one. They hurt only enemies: they're
  the player's tool. They're also the room's light sources.
- **Spike Vents** erupt crystal spikes on a cycle after a filling red ground
  marker. They hurt everyone, and enemies pushed into them take the hit.

No pits in the MVP: they would need fall states and edge rules for the hero.

**The Threshold** teaches by pacing: a few Kobolds with long telegraphs, room to
try the Cleave, the dash and the Leap Slam, then the first prism. **The
Respite** and **the Antechamber** hold a fountain (E to drink, full heal) and a
shop, and nothing attacks there.

## Look

The look is Playlist Survivors' retro 3D neon wireframe: low-poly models drawn
as glowing edges on black, hidden lines removed, one hue per object, and light
that comes from things, never from the world.

### Wireframe meshes

Every model gets a wireframe "monster mesh": a low-poly mesh designed so its
edges draw a clean neon figure, skinned to the vendor rig so the vendor clips
still drive it. Meshes are built locally from the vendor files and never
committed. The MVP needs nine: the knight, the six enemies, the Warden and the
Heartwyrm. Dense vendor topology is the risk: it scribbles into a fill (Playlist
Survivors' goblins).

### Color roles

| Role                      | Color                                               |
| ------------------------- | --------------------------------------------------- |
| The hero and attacks      | White                                               |
| Walls                     | Grey                                                |
| Hazards and enemy attacks | Red and orange (the dragon's fire too)              |
| Enemy bodies              | One dim, low-chroma amethyst lavender               |
| Status                    | The enemy's lines take the suite's hue              |
| Crystallized shells       | Bright facets over the body                         |
| Suites                    | Gale mint, Frost cyan, Voltage indigo, Void magenta |
| Gold                      | Unused                                              |

Enemy types are told apart by silhouette and size, not hue. Status takes over
the body color, so the player reads every Charge, Chill and Rift at a glance,
and the color reports success. The exact hues get settled in a look testbed: two
or three variants, judged by eye.

### Visual priority

From highest to lowest (feel §3.4, §6.2): telegraphs, kills, hits, status,
swings. Swing trails are thin and short-lived. Enemy telegraphs draw above the
hero's effects.

## Sound and music

| Role      | Source                                                 |
| --------- | ------------------------------------------------------ |
| Swings    | Universal Sound FX sword whooshes                      |
| Hits      | RPG3 generic impacts layered with a blade chop         |
| Shatters  | RPG3 Ice Break with glass SHATTER fragments            |
| Dash      | Universal Sound FX fast bright air whooshes            |
| Hero hurt | Universal Sound FX medieval armor hits                 |
| Suites    | Their RPG3 element folders (Electric, Ice, Dark, Wind) |

The rules come from feel §9: hit sounds on the contact frame, pitch varied
±5–10%, 3–5 variations per sound, and music ducked under big impacts.

**Music**: by default, tracks from the Ultimate Game Music Collection (its
Combat, Dungeons and Space folders). Launching with
`--file ~/Music/songs/songs.m3u --shuffle` plays the player's own playlist
instead. Nothing in the game reads the music's beat; the only coupling is the
ducking.

## HUD

Minimal, and out of the way of the fight:

- Health, with a pip for Second Wind.
- Dash charges and the special's cooldown ring under the hero.
- The Shard count.
- Three slot icons in their suites' hues, with Levels.
- The boss bar with its Break bar.

## Build order

What each step builds on. Each step makes its part feel right before moving on,
through testbed variants (AGENTS.md).

1. **The feel room**: one room, the hero's kit and Kobolds, with the feel
   reference's toggles (feel §11). The clip timing gets settled here.
2. **The chain**: the six enemies, elites, hazards, and four seamless rooms.
3. **The suites**, one at a time with all three Levels: Gale first (knockback
   needs only walls), then Frost, Voltage and Void. Synergies arrive as their
   pairs exist.
4. **The Warden**, with prisms, Shards, offers and shops.
5. **The Heartwyrm** and the full 12-room run.

## Out of scope

- **Rhythm.** The MVP keeps three things compatible with a later rhythm layer:
  the `--file`/`--shuffle` launch, the Cleave's even cadence, and unused gold.
  Playlist Survivors' lesson stands: the beat can only ever be a bonus, never
  required, never punishing off-beat play.
- Other weapons, a hub, meta-progression, story and voice.
- Room forks, pits, Legendary upgrades and rarity tiers.
- Controller support (the movement-aim flag keeps it open).

## Open questions

These are for testbeds to answer, not for this document:

- The exact suite and enemy hues.
- Whether damage numbers help or clutter (feel §3.2).
- Whether `LightFencing01` or `HeavyStab01` makes the better Lunge.
- Every number above.
