# Project Amethyst: Game Design

The initial design for the Project Amethyst MVP: one run of twelve connected
arenas against neon monsters, with a sword, a dash and a special, and an upgrade
system built so every pick changes what the player does and sees. It builds two
combat modes, focused Hades-style fights and Vampire Survivors-style hordes, and
compares them behind a flag ([Two combat modes](#two-combat-modes)).

[What Makes ARPG Combat Feel Great](arpg-combat-feel.md) is the companion
reference. This document says *what* the game is; the feel reference says how
every part of it must feel. Section references like "feel §3.1" point there.

## The game in one paragraph

A white knight fights neon monsters across twelve connected arenas, with no
loading between them. Each arena is a fight; when it's won, the knight picks one
of three upgrades and walks into the next. An ogre guards the midpoint and a
dragon waits at the end. Unlike Vampire Survivors or Brotato, nothing attacks on
its own: the knight fights with three buttons, a sword combo, a dash and a
leaping slam. Four upgrade suites (Voltage, Frost, Void, Gale) reshape those
actions until a strong build clears the screen. It looks like the Playlist
Survivors neon edge trailer: glowing wireframe figures on a black grid, every
enemy type in its own neon hue. Whether each arena holds a horde or a handful of
tougher enemies is the MVP's central experiment, and both are built.

## Pillars

1. **Feel decides what exists.** A mechanic ships only if it gets first-class
   animation, effects and sound. If the asset set can't support it, it waits.
   Every number here is a starting value for a testbed, not a promise.
2. **Explicit verbs.** Every kill comes from a button the player pressed,
   whether the arena holds eight enemies or three hundred.
3. **Every outcome is confirmed.** Feedback reports success (feel §1, §3), and
   in a horde it has to stay small per hit so it never hides the next threat
   (feel §3.4).
4. **Upgrades change actions, not numbers** (feel §8). No upgrade is only a
   number; any number an upgrade raises comes with something you can see or
   hear.
5. **Danger is readable.** Filled red shapes mean "this will hurt you". Anything
   that winds up an attack telegraphs it on two channels (feel §6).

## Influences

- **Vampire Survivors and Brotato**: horde density, timed waves, gems pulled in
  by a magnet, steering a build by spending currency.
- **Hades and Hades II**: the room-to-room run, the dash at the center of
  combat, slot-based boons. Hades II's dash that can't cancel attacks is the
  cautionary tale (feel §2.4).
- **Path of Exile**: status effects that feed each other through a crowd
  (chains, freezes, delayed bursts).
- **Playlist Survivors**: the neon edge look, the roster, the shatter on kill,
  and the lesson that rhythm is an accent that can't carry a game.

## Decisions at a glance

| Area             | Decision                                                                   |
| ---------------- | -------------------------------------------------------------------------- |
| Run              | 12 connected arenas, ~15 minutes, linear, no loading                       |
| Combat modes     | Focused (Hades) and horde (Vampire Survivors), compared behind a flag      |
| Controls         | WASD move, Space dash, LMB/F attack, RMB/Q special, E interact             |
| Aim              | Cursor aim and movement-direction aim, both behind a flag                  |
| Camera           | Fixed 3/4 top-down; pulled back for the horde, closer for focused fights   |
| Kit              | Cleave (cone), Lunge dash-strike (line), Leap Slam special (circle), Dodge |
| Health           | 100 HP, no regen, two full heals, one Second Wind, instant restart         |
| Upgrades         | 3 exclusive action slots + passives; 4 suites; 3 Levels each; 6 Synergies  |
| Offers           | 3 cards from 3 suites; rerolls and banish cost Shards; two shops           |
| Shards           | Pulled in continuously by a magnet radius; vacuumed at the clear           |
| Roster           | Playlist Survivors' monsters; the Fat Ogre (the Warden); the dragon        |
| Look             | The neon edge trailer: per-type enemy hues, the hero's world in white      |
| Music            | Bundled soundtrack or `--file <m3u> --shuffle`; nothing reads the beat     |
| Meta-progression | None                                                                       |

## Two combat modes

Is it more fun to fight a few tough enemies with focus, as in Hades, or to carve
through a horde, as in Vampire Survivors and Brotato? The MVP builds both and
lets play decide. One enum flag, `combat.mode`, selects:

- `focused`: Hades-style fights against fewer, tougher enemies that all
  telegraph.
- `horde`: hundreds of contact-damage fodder with a few telegraphing threats.
- `blind`: each run picks one of the two at random, so the comparison isn't
  shaped by which mode the player chose or played first (feel §11.3).

Everything else is shared: the roster, the look, the kit, the suites and their
effects, the arena modules, the run's structure and the bosses' attacks. Each
mode is a tuning table plus its own rule for ending a room:

| Aspect             | Focused                                              | Horde                                                     |
| ------------------ | ---------------------------------------------------- | --------------------------------------------------------- |
| Enemies per room   | 6–14 over 2–3 waves; at most ~6 alive early, ~9 late | Streaming; ~60–100 alive early, 200–300 late              |
| Who telegraphs     | Every enemy                                          | Special types, elites and bosses                          |
| Contact damage     | None                                                 | Fodder, 4–6 per touch                                     |
| Basic enemy HP     | 2–4 hits early (feel §7)                             | 1–2 hits                                                  |
| Stagger            | Every hit staggers basic enemies                     | Knockback only                                            |
| Aggression tokens  | 2 early, 3 late                                      | 3 early, 5 late, for telegraphing enemies                 |
| Room ends          | When every wave is dead                              | At the timer's buzzer; survivors shatter, elites must die |
| Camera             | Closer: the hero larger                              | Pulled back: the hero small, the crowd visible            |
| Hitstop            | Feel §3.1's values per target                        | 1–2 frames on fodder; global freezes only for big moments |
| Upgrade magnitudes | Counts halved (rounded up), ranges ×0.7              | As in the suite tables                                    |
| Shard drops        | 1–2 per kill                                         | About 1 in 3 fodder kills                                 |
| During boss fights | Small capped groups at phase changes                 | A thin horde that each phase change shatters              |

**Recording.** The run card names the mode. Each run also writes a summary to
its run folder: mode, time played, rooms reached, win or loss, kills, damage
taken, actions per second, and whether the player started another run straight
away. Free-choice playtime per mode is the measure that matters most (feel §1,
§11.3).

**Deciding.** The human plays both, ideally in `blind`, and picks. Until then
every milestone keeps both modes working and tuned. Then the winner becomes the
default, and the flag and the losing mode's code are deleted (AGENTS.md, Flags
and concepts).

## The run

### Structure

| Room | Name        | What happens                                               |
| ---- | ----------- | ---------------------------------------------------------- |
| 0    | Threshold   | A gentle fight (skeletons only); grants the first pick     |
| 1–4  | Early rooms | Combat arenas, escalating                                  |
| 5    | The Warden  | Miniboss                                                   |
| 6    | Respite     | Fountain (full heal) and shop; no fight                    |
| 7–10 | Late rooms  | Combat arenas with heavies and more elites                 |
| 11   | Antechamber | Fountain (full heal) and shop; a quiet beat before the end |
| 12   | Heartwyrm   | Final boss                                                 |

Nine combat rooms and two bosses give about eleven picks per run: enough to fill
all three slots, collect four or five passives, and find one or two Synergies.

### Arenas

Each combat room is a bounded arena of about 40×30 m: a black floor with the
trailer's faint grid, grey walls at its edges and a few pillars.

- **Entry.** Stepping in seals the gate behind the hero. Enemies arrive from the
  arena's edges, each group behind a 0.5–1 s spawn marker, never within a few
  meters of the hero (feel §7).
- **Horde mode** shows a room timer of 45–75 s, with spawn pressure building
  through it. At the buzzer, spawning stops and every surviving fodder enemy
  shatters in one outward wave from the hero, with global hitstop and a
  slow-motion tail (feel §3.1). Elites alive at the buzzer don't shatter; the
  room clears when they die.
- **Focused mode** sends 2–3 waves. The next wave's markers appear when the
  current wave is down to its last two enemies. The room clears with its last
  kill, which gets the global hitstop and slow-motion tail.
- **Clear.** The exit's laser gate dissolves, the remaining Shards rush to the
  hero, and a prism shard forms in front of the exit.
- **Corridor.** A 3–5 s walk to the next arena is the breather.

The chain is linear: one exit per room. All arenas sit in one continuous world,
so walking between them never loads. Boss arenas have no timer.

### Variety between runs

- **Combat arenas** are hand-built modules that share the same doorway sockets,
  so any module can follow any other. About ten modules split into two tiers
  (early for rooms 1–4, late for rooms 7–10), shuffled each run. Modules differ
  in shape, pillar layout and hazards.
- **Spawn schedules** are drawn from per-tier tables (which types, in what mix,
  from which edges), so the same module plays differently.
- **The floor grid's color** shifts through the run using Playlist Survivors'
  zone keys: cobalt early, teal late, ember for the Heartwyrm.
- **Fixed rooms**: the Threshold, the Warden's arena, the Respite, the
  Antechamber and the Heartwyrm's arena.

### Health and death

- The hero has 100 HP and doesn't regenerate, so damage carries from room to
  room.
- After any hit the hero has ~0.8 s of invulnerability. In horde mode, contact
  damage from fodder is small (4–6), so a crowd can't take a big bite at once;
  the danger is being surrounded with no dash ready. Focused mode has no contact
  damage: every hit comes from a telegraphed attack.
- The fountains in the Respite and the Antechamber heal fully.
- **Second Wind**, once per run: lethal damage revives the hero at 40% HP with a
  slow-motion shockwave that shatters the fodder around it.
- Some upgrades heal as a side effect. Healing is never one of the three
  choices.
- Death shatters the hero into white shards. A 5-second **run card** follows
  (the combat mode, time, rooms cleared, kills, the build's slots and Synergies,
  damage taken from contact and from telegraphed attacks, and the share of
  telegraphed hits that were avoidable, feel §6.5), then a key press starts the
  next run in the Threshold. Winning shows the same card.

There is no hub, no meta-progression and no story. The game launches straight
into the Threshold.

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

**Camera**: fixed 3/4 top-down. In horde mode it's pulled back as in the
trailer, so the hero is small and the crowd around it is visible on every side;
in focused mode it sits closer, so each enemy's telegraph reads. It follows the
hero, leans slightly toward the cursor, and never rotates (feel §5). It zooms
only for boss introductions.

## The hero's kit

The hero is the Synty Polygon knight (sword and shield) animated by Synty's
Sword Combat pack, the figure Playlist Survivors used. Three moves, three
shapes:

| Move      | Input                       | Clips (Sword Combat)              | Shape  | Role                                |
| --------- | --------------------------- | --------------------------------- | ------ | ----------------------------------- |
| Cleave    | Attack                      | `LightCombo01` A → B → C          | Cone   | Carves the crowd; C is the finisher |
| Lunge     | Attack during or after dash | `LightFencing01` or `HeavyStab01` | Line   | Cuts a lane through the crowd       |
| Leap Slam | Special                     | `LightLeaping01`                  | Circle | Clears a ring; gets you out         |
| Dash      | Space                       | `Dodge_F/B/L/R`                   | —      | Escape through bodies               |

- **Cleave**: three wide cuts at an even cadence (~250 ms apart), each hitting
  everything in its cone. The finisher C is wider, knocks back harder, has its
  own sound and marks the end of the phrase (feel §9).
- **Lunge**: attacking during the dash or just after it thrusts along the dash
  line, piercing everything in it. Dash invulnerability ends the moment the
  Lunge starts: the strong option costs safety (feel §4.3).
- **Leap Slam**: a leap toward the aim point (up to 4 m) that lands with a ring
  and radial knockback. It commits the hero: no invulnerability in the air and
  no dash-cancel until it lands. A ~2 s cooldown, shown as a ring under the
  hero.
- **Dash**: the short dodge, not the roll. Invulnerable from frame 1 through
  most of its length, and it passes through enemy bodies. One charge with a
  ~0.35 s cooldown. It cancels the recovery of every attack (feel §2.4).
- **Getting hit**: directional `Hit_*_React` for contact hits, `Hit_*_Stagger`
  for heavy telegraphed hits, then ~0.8 s of invulnerability. Player damage is
  the one place light screen shake is always on (feel §3.3).

### Clip timing

The Synty clips were made for weightier games. `LightCombo01A` contacts at 350
ms, `HeavyStab01` at 883 ms, `LightLeaping01` at 1.1 s, while the feel targets
are 50–100 ms for basic attacks and 130–250 ms for specials (feel §2.2). So
every move starts its clip just before the contact frame and skips most of the
windup. `HeavyFlourish01` (contact at 1.43 s) is nearly all windup, which is why
there's no spin in the base kit.

The test is the first rendered frame after the press (feel §2.1): it must
already read as the strike. If the trimmed clips look like a pose snap rather
than a swing, better clips (Mixamo, or a small purchase) are a follow-up for the
human.

### Starting values

| Parameter    | Value                                |
| ------------ | ------------------------------------ |
| Cleave       | 10 / 10 / 18 damage; 120° cone, ~3 m |
| Lunge        | 22 damage, pierces                   |
| Leap Slam    | 25 damage in a 3.5 m ring            |
| Dash         | ~3.5 m in ~10 frames                 |
| Run speed    | Faster than every enemy              |
| Input buffer | 6–12 frames, last input wins         |

## Upgrades

### Structure

- **Three action slots.** *Attack* (Cleave and Lunge, both on the attack
  button), *Special* (Leap Slam) and *Dash* (the dodge itself: its start, its
  path, its end). A slot holds one upgrade; taking another for the same slot
  replaces it, and the card says what it replaces.
- **Passives** have no slot and stack freely.
- **Synergies** need upgrades from two suites.

Exclusive slots keep each action wearing at most one suite's effects, so a swing
never stacks three layers over the hits it should be showing (feel §3.4).

### Suites

Suites stand in for Hades' gods: abstract powers with no characters or dialogue.
The crowd owns the colors, so **suites are told apart by shape and sound, and
are drawn in white** like everything else the hero makes. Each suite has one
shape language, one element folder from the RPG Magic Sound Effects Pack 3, and
one status or verb that all of its upgrades feed. Its color appears only on
cards and HUD icons.

| Suite   | Shape language                          | Status and verb                                                     | Sound                         | Effect references                               |
| ------- | --------------------------------------- | ------------------------------------------------------------------- | ----------------------------- | ----------------------------------------------- |
| Voltage | Jagged bolt segments, sparks            | **Charge**: 3 stacks discharge as a bolt that chains                | Electric Magic, ZAPS, THUNDER | Hovl Lightning strike/hit, Magic circle electro |
| Frost   | Faceted shells, shards                  | **Chill** slows; 5 stacks **Freeze**; frozen kills **Shatter**      | Ice Magic, glass SHATTER      | Hovl Ise attack, crystal flashes                |
| Void    | Dark discs with bright rims, pull lines | **Rift**: stores damage for 1 s, then bursts; pulls enemies in      | Dark Magic, SPACE_WARPS       | Hovl Black hole, Darkness signet                |
| Gale    | Crescents, streamlines                  | **Knockback**; **Impact** damage when a pushed enemy hits something | Wind Magic, WHOOSHES          | Hovl Slash wave                                 |

- **Voltage** chains through the crowd.
- **Frost** controls the crowd, then pays off by shattering it.
- **Void** gathers the crowd into piles, then bursts them.
- **Gale** turns the crowd and the walls into the weapon (feel §6.3), and holds
  the mobility upgrades.

**Statuses show as white overlays** in the suite's shape on the enemy, so they
read on any enemy hue: sparks for Charge, frost facets for Chill and a full
crystal shell for Freeze, a rim ring for Rift.

The stock effect prefabs are shape and timing references only; each effect is
redrawn as white line geometry, as Playlist Survivors redrew the Hovl slashes.

### Levels

There are no rarity tiers. Every upgrade has three Levels, and each Level adds a
visible and audible layer instead of only bigger numbers. Each tier adds one
layer to the last rather than replacing it.

Levels come from a *Refine* card in an offer ("Refine: Arc Cleave → II"), which
appears more often after the midpoint, or from a shop.

### Offers

- **The prism.** On a room clear, a prism shard forms in front of the exit.
  Pressing E freezes and dims the world.
- **Three cards from three different suites.** Once the player owns any upgrade,
  at least one card comes from an owned suite. An eligible Synergy takes one
  card, with a distinct frame.
- **Hover preview.** Hovering a card makes the frozen hero act out that move
  with the new effects.
- **Reroll**: new cards, costing Shards, more each time within the same offer
  (5, then 10, then 20).
- **Banish**: shatter a card instead of taking it, removing that upgrade for the
  rest of the run (5 Shards).

### Shards

The run's currency is Shards. They don't carry between runs.

- **Dropping**: in horde mode, fodder kills drop one now and then (about 1 in
  3); in focused mode, every kill drops 1–2. Elites drop 5.
- **Collecting**: Shards within a magnet radius (~3 m) fly to the hero all the
  time, Vampire Survivors-style; at the clear, every Shard left in the arena
  rushes in at once.
- **Clean bonus**: a room cleared without taking damage pays 10 extra.
- **Shops** in the Respite and the Antechamber sell three upgrade cards (~40),
  Refine on an owned upgrade of the player's choice (~30), and restoring a spent
  Second Wind (~60). The fountain in the same room already heals fully, so the
  shop's heal is the Second Wind.

### Status effects on elites and bosses

| Effect    | Empowered elite (while armored) | Boss                         |
| --------- | ------------------------------- | ---------------------------- |
| Charge    | Works                           | Works                        |
| Chill     | Works                           | Slows its attacks, up to 20% |
| Freeze    | Immune                          | Converted to Break damage    |
| Rift      | Works                           | Works                        |
| Pull      | Immune                          | Doesn't move it              |
| Knockback | Immune                          | Converted to Break damage    |
| Stagger   | Immune                          | Only through Break           |

## The suites

Each suite has three slot upgrades and three passives. The tables give horde
mode's numbers, sized for crowds; focused mode halves the counts (chain targets,
shards, Pinball links; rounded up) and scales ranges and radii by 0.7. Chain
reactions stop after two generations, which keeps them readable and cheap.
Levels read I / II / III.

### Voltage

Charge: each stack adds sparks to the enemy. At 3 stacks it discharges: a bolt
strikes it and chains to the 4 nearest enemies within 6 m.

| Upgrade                | I                                                                                      | II                                                | III                                                                                             |
| ---------------------- | -------------------------------------------------------------------------------------- | ------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| Arc Cleave (Attack)    | Cleave hits add 1 Charge, the finisher 2; the Lunge Charges all it pierces             | Chains reach 6 enemies; bolts fork visibly        | The finisher calls a vertical thunder strike that discharges everything within 3 m              |
| Thunderfall (Special)  | A lightning column on landing; 2 Charge to all in the ring                             | Everything already Charged in the ring discharges | Four more columns fall around the ring half a second later                                      |
| Static Step (Dash)     | The dash path stays a live wire for 1.5 s, Charging what touches it                    | The wire lasts 3 s and Charges every 0.5 s        | Dashing again while a wire is live links the two with a bolt that discharges everything between |
| Capacitor (Passive)    | Discharges chain to 2 more enemies                                                     | 4 more; bolts thicken                             | Chains can arc back through enemies already hit                                                 |
| Ground Fault (Passive) | Charged enemies discharge when they die                                                | That death discharge reaches 2 more               | It leaves a crackling floor patch for 1 s that Charges                                          |
| Overload (Passive)     | Hitting an enemy at full Charge staggers it, even an armored elite, with a spark burst | Overloaded hits chip armor twice as fast          | A discharge breaks an elite's armor outright                                                    |

### Frost

Chill: each stack slows by 10% and frosts the enemy. At 5 stacks it Freezes for
2 s inside a white crystal shell. A Frozen enemy that dies Shatters, throwing
eight shards that deal damage and add 1 Chill.

| Upgrade                | I                                                                           | II                                                                          | III                                                                 |
| ---------------------- | --------------------------------------------------------------------------- | --------------------------------------------------------------------------- | ------------------------------------------------------------------- |
| Rime Edge (Attack)     | Cleave hits add 1 Chill, the finisher 2; the Lunge adds 2 to all it pierces | The finisher deals double to Frozen enemies and cracks them visibly         | The finisher shatters any Frozen enemy below 30% HP                 |
| Glacier Slam (Special) | A ring of crystal spikes on landing; 3 Chill to all in a 4 m ring           | The spikes stay 2 s as walls (they block enemies and count for Gale Impact) | The inner half of the ring Freezes instantly                        |
| Frostwake (Dash)       | The dash leaves a frost trail for 2 s that Chills enemies on it             | Dashing through enemies adds 2 Chill to each                                | The trail erupts into spikes (2 Chill) as it fades                  |
| Brittle (Passive)      | Frozen enemies take +25% damage; cracks show on them                        | +50%; deeper cracks                                                         | +75%; cracks glow                                                   |
| Cold Snap (Passive)    | Shatter shards add 2 Chill                                                  | Shards fly farther                                                          | A shard that kills a Frozen enemy shatters it too (two generations) |
| Permafrost (Passive)   | Freeze lasts 3 s                                                            | 4 s                                                                         | A thawing enemy releases a cold pulse (2 Chill nearby)              |

### Void

Rift: a white rim ring that marks an enemy and stores 30% of the damage dealt to
it for 1 s, then bursts for the stored amount in a 3 m radius.

| Upgrade                 | I                                                                                                                             | II                              | III                                                           |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------- | ------------------------------- | ------------------------------------------------------------- |
| Riftblade (Attack)      | The first hit opens a Rift; the Lunge tears a rift along its line that pulls enemies in for 0.5 s, then snaps shut for damage | Rifts store 45%                 | The finisher reopens a Rift on a target whose Rift just burst |
| Singularity (Special)   | Landing opens a black hole for 1.5 s that pulls in everything within 7 m, then collapses, Rifting all it caught               | 2.5 s, and 9 m                  | The collapse leaves a second, smaller singularity             |
| Phase Step (Dash)       | A void echo where the dash began pulls enemies within 4 m for 0.6 s, then bursts                                              | The echo Rifts what it hits     | Dashing back into the echo detonates it at once, for double   |
| Event Horizon (Passive) | Void pulls are 30% stronger and wider                                                                                         | 60%; the pull lines draw longer | 90%                                                           |
| Entropy (Passive)       | A burst opens Rifts on the enemies it hits (one generation)                                                                   | Bursts are 50% larger           | Two generations                                               |
| Hollow (Passive)        | A burst's size grows with the damage stored; the ring swells as it fills                                                      | The store cap rises             | The cap rises again; full Rifts hum before bursting           |

### Gale

Knockback, and Impact: an enemy pushed into a wall, pillar, pylon or another
enemy takes damage scaled by its speed, with its own hitstop and sound. White
streamlines trail pushed enemies.

| Upgrade              | I                                                                                                                                     | II                                                      | III                                                       |
| -------------------- | ------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------- | --------------------------------------------------------- |
| Gust Cleave (Attack) | Cleave pushes 1 m farther; the finisher sends a wind crescent 8 m that pushes all it passes; the Lunge parts the crowd to either side | The crescent is wider and pierces                       | The finisher fires three crescents in a fan               |
| Downburst (Special)  | The landing ring pushes enemies 5 m; wall slams get 6 more frames of hitstop                                                          | Pushes 7 m                                              | An enemy that Impacts is launched again                   |
| Slipstream (Dash)    | The dash shoves enemies in its path aside                                                                                             | A second dash charge                                    | The dash ends in a gust that pushes everything within 4 m |
| Impact (Passive)     | +50% Impact damage                                                                                                                    | +100%; slam lines brighten                              | +150%                                                     |
| Pinball (Passive)    | An enemy knocked into another knocks it back too                                                                                      | The push keeps passing through the crowd, up to 6 links | Up to 12; each link pitches the impact sound up (feel §9) |
| Tailwind (Passive)   | +10% move speed and +1 m Shard magnet; a Lunge hit refunds 0.1 s of dash cooldown                                                     | +15%, +2 m, 0.2 s                                       | +20%, +3 m, 0.3 s                                         |

### Synergies

One Synergy per pair of suites, eligible once the player owns any upgrade from
each of its two suites. Each one adds an interaction, not a number.

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

In both modes:

- **Telegraphs** use two channels (feel §6.1): 400–600 ms for melee, 300–500 ms
  aim poses with slow projectiles, 600–1000 ms filling ground markers for areas.
- **Projectiles** travel at most half the dash's speed (feel §5).
- **Spacing**: enemies keep small gaps between bodies, so even a swarm reads as
  many figures with dark between them, as in the trailer.
- **Kills** shatter the enemy along outlined cracks in its own hue, thrown away
  from the blow, gone within half a second. A kill earns empty space.

In horde mode:

- **Density**: ~60–100 alive early in the run, 200–300 late.
- **Fodder** threatens by contact and by surrounding the hero. It doesn't wind
  up attacks; it walks into you. Fodder dies in one or two hits, a deliberate
  departure from feel §7's 2–5: here the crowd is the enemy, not the individual.
- **Special types, elites and bosses** telegraph every attack. Aggression tokens
  (feel §6.4) apply only to them: at most 3 winding up at once early, 5 late.
- **Hitstop at scale**: fodder freezes 1–2 frames when hit and the hero not at
  all. Global freezes are kept for elite kills, the buzzer and boss moments
  (feel §3.1).

In focused mode:

- **Density**: 6–14 enemies per room over 2–3 waves, at most ~6 alive early and
  ~9 late.
- **Every enemy telegraphs**, fodder included, using the attack clips in the
  roster table, and none deals contact damage. Aggression tokens: 2 early, 3
  late.
- **Basic enemies** die in 2–4 hits early (feel §7), and every hit staggers
  them, so attacking is also defense.
- **Hitstop** uses feel §3.1's values: 2–4 frames on the target for a light hit,
  5–8 for the finisher, 4–8 for a kill.

### Roster

Playlist Survivors' monsters, already proven in the look and baked in its
`monsters.json`. Colors follow its palette families (`palette.json`).

| Enemy    | Source                                 | Family                   | Role                                      | Key clips                               |
| -------- | -------------------------------------- | ------------------------ | ----------------------------------------- | --------------------------------------- |
| Skeleton | Magic Pig Skeleton Warrior             | Key (cyan, blue)         | Fodder walker                             | `Walk`, `Got Hit`, `Attack 1`           |
| Slime    | Magic Pig Slime                        | Key                      | Slow, tough fodder                        | `Walk Cycle`, `Been Hit`, `Spit Attack` |
| Demon    | Magic Pig Demons                       | Key                      | Fast fodder                               | `walk`, `hit reaction`, `attack1`       |
| Bat      | Magic Pig Bats                         | Key                      | Flying swarm stream                       | `Fly Forward`, `Got Hit`, `Attack 1`    |
| Goblin   | Magic Pig Goblin                       | Accent (magenta, violet) | Rusher: telegraphed lunge                 | `Attack1`, `Dodge`                      |
| Mushroom | Magic Pig Mushroom Monster             | Accent                   | Ranged: spore volleys                     | `MushAttack01`, `MushHit`               |
| Big Ork  | Synty Fantasy Rivals (with the Slayer) | Counter (orange, amber)  | Heavy: telegraphed lunge along a line     | Sword Combat `HeavyStab01`              |
| Troll    | Synty Fantasy Rivals                   | Counter                  | Heavy: telegraphed leap smash with a ring | Sword Combat `LightLeaping01`           |

- **Fodder** comes in streams and rings in horde mode, the trailer's shapes.
  Slimes soak hits; demons close fast; bats arrive in flying streams that ignore
  pillars. The attack clips are used in focused mode, and by Empowered fodder in
  both.
- **Goblins** crouch for 300 ms behind a red lane marker, then lunge along it.
- **Mushrooms** hang back and loft slow spore volleys at a marked spot.
- **Heavies** are always armored (an armor bar) and give the Lunge and the Leap
  Slam something to break. The big ork and the troll share the hero's humanoid
  rig, so they use Sword Combat clips.

### Elites: Empowered

Any type can appear Empowered: 1.5× its size, a doubled outline in its hue, and
an armor bar (feel §6.3). Empowered fodder gains a telegraphed attack (the
skeleton's `Attack 1`, the slime's `Spit Attack`).

- Hits chip the armor. Breaking it is a big moment: a white flash, the outer
  outline shattering, global hitstop.
- While armored, the elite ignores stagger, Freeze and knockback.
- Elites don't shatter at the buzzer; the room waits for them.

### Escalation

Focused mode:

| Rooms | Waves | Enemies | Alive at once | Types             | Elites      |
| ----- | ----- | ------- | ------------- | ----------------- | ----------- |
| 0     | 1     | 4       | 4             | Skeletons         | —           |
| 1     | 2     | 6–8     | 5             | + slimes, bats    | —           |
| 2     | 2     | 8       | 6             | + demons, goblins | —           |
| 3     | 2–3   | 10      | 6             | + mushrooms       | 1           |
| 4     | 3     | 12      | 7             | + big orks        | 1–2         |
| 7     | 3     | 12      | 8             | + trolls          | 2           |
| 8–10  | 3     | 14      | 9             | All               | ~1 per wave |

Horde mode:

| Rooms | Timer | Alive at once | Types             | Elites |
| ----- | ----- | ------------- | ----------------- | ------ |
| 0     | 30 s  | ~40           | Skeletons         | —      |
| 1     | 45 s  | ~60           | + slimes, bats    | —      |
| 2     | 50 s  | ~80           | + demons, goblins | 1      |
| 3     | 60 s  | ~100          | + mushrooms       | 1–2    |
| 4     | 60 s  | ~120          | + big orks        | 2      |
| 7     | 60 s  | ~180          | + trolls          | 2–3    |
| 8–10  | 75 s  | 200–300       | All               | 3–4    |

## Bosses

### Shared rules

- **Break bar.** A bar under the boss's health. Every hit fills it, and the
  finisher, the Lunge and the Leap Slam fill it more. When it fills, the boss is
  **Broken** for ~2.5 s: 10–20 frames of global hitstop on the break (feel
  §3.1), a stun pose, and +50% damage taken. Each Break makes the next one
  harder.
- **Suite effects convert** (see the table above).
- **Adds.** In horde mode, a thin horde streams in during boss fights (at most
  ~40 fodder for the Warden, ~60 for the Heartwyrm) to keep pressure and feed
  Shards and suites, and each phase change shatters it. In focused mode, a small
  capped group (3–4 skeletons or bats) joins at each phase change.
- **Phases**: two for the Warden, three for the Heartwyrm. Each change is a
  short invulnerable roar, never a cutscene.
- **No timers.** Strong builds end fights sooner; no phase waits on a clock.
- **Required dodges** never come closer together than the dash cooldown plus
  reaction time, and no two patterns overlap into an unavoidable hit.

### The Warden (room 5)

The Fat Ogre (Heroic Fantasy, Must Have Fantasy Villains Pack), Playlist
Survivors' mid-boss, already baked. In the amber counter family. A close-range
test: dodge the combo, punish the recovery.

| Attack          | Clips                                              | Telegraph                                                 | Answer                                              |
| --------------- | -------------------------------------------------- | --------------------------------------------------------- | --------------------------------------------------- |
| Advancing combo | `2HitComboAttackForward`, `3HitComboAttackForward` | 500–600 ms club raise and a grunt; steps forward each hit | Leave its lane sideways, punish the recovery        |
| Club slam       | `Attack3`                                          | 700 ms filling lane marker                                | Step off the lane                                   |
| Charge          | `Run`                                              | 900 ms lane marker the length of the arena                | Sidestep; it stumbles at the wall                   |
| Boulder         | `Attack2`                                          | A filling circle where it will land                       | Leave the circle                                    |
| Stomp           | `Attack1`                                          | 900 ms filling marker                                     | Its shockwave ring expands to 10 m: dash through it |

- **Phase 2 (50%)**: `RoarTaunt`. A surge of adds, and its patterns chain: a
  club slam into a stomp ring, a charge into a boulder, always with a recovery
  interval.
- **Broken**: it has no stun loop, so it holds `GetHit2`'s deepest frame.

### The Heartwyrm (room 12)

The Magic Pig dragon, drawn in the boss red as in the trailer, its breath a
filled red cone.

**Phase 1, grounded (100–66%)**

| Attack       | Clips                           | Telegraph                                                     | Answer               |
| ------------ | ------------------------------- | ------------------------------------------------------------- | -------------------- |
| Bite         | `Attack 1`                      | 500 ms head draw-back; short front cone                       | Stay off its nose    |
| Fireballs    | `Attack 2`                      | Filling circles where each lands                              | Leave the circles    |
| Tail whip    | `Tail Whip L/R`                 | Hero behind or beside it: 700 ms tail raise and a filling arc | Don't camp its back  |
| Breath sweep | `Fire Head 1/2`, `Breathe Fire` | 1–1.2 s throat glow and a filling cone; the cone sweeps 60°   | Get behind the sweep |

Broken: `StunnedStanding`.

**Phase 2, airborne (66–33%)**

- `Idle Takeoff`, then it circles the arena edge above the horde.
- **Strafing runs**: a lane marker fills, then it crosses the arena low,
  breathing fire along the lane (`Fly Fast`, `Fly Breathe Fire`). The breath
  burns the horde too. Low passes are in reach: dodge through the lane and
  strike its flank as it goes.
- **Dive**: `Fly Dive` onto a filling landing marker placed where the hero stood
  when it appeared. The landing ring is the dash-through test, and the ~2 s
  `Idle Landing` recovery is the punish window.
- **Out of the sky**: Breaking it in the air plays `StunnedFlying`, drops it
  crashing to the floor (shattering the fodder it lands on), and leaves it
  Broken for a long 3.5 s. The run's biggest moment.

**Phase 3, grounded (33–0%)**

- A roar calls its last and largest adds and arms every Spike Vent.
- The breath sweeps all the way around, with a safe gap that follows it.
- Concentric rings and earlier patterns combine, under the shared rule above.
- Its `Death` plays with the run's largest shatter, takes the horde with it, and
  gives way to the run card.

## Arenas and hazards

Each combat arena uses one or two hazard types besides its walls. The Threshold
uses none.

- **Walls and pillars** are grey line structures. Gale's Impact hits them, and
  pillars split the horde into lanes.
- **Pylons** are breakable structures that burst in an area when struck or when
  an enemy is slammed into one. The burst hurts only enemies: they're the
  player's tool.
- **Spike Vents** erupt on a cycle after a filling red ground marker. They hurt
  everyone, so they also carve the horde.

No pits in the MVP: they would need fall states and edge rules for the hero.

**The Threshold** teaches by pacing: a few skeletons (a thin horde with a short
timer in horde mode), room to try the Cleave, the dash and the Leap Slam, then
the first prism. **The Respite** and **the Antechamber** hold a fountain (E to
drink, full heal) and a shop, and nothing attacks there.

## Look

The reference is the Playlist Survivors neon edge trailer (outside the tree:
`~/playlist_survivors/captures/trailer-action-edge.mp4`). It looks good; start
from it and change it only where the change looks better.

- Low-poly 3D figures drawn as glowing edges on black, hidden lines removed, one
  hue per figure.
- A black floor with a faint grid; light comes from things, never from the
  world.
- The camera pulled back far enough that the crowd forms visible shapes: rings,
  streams and arcs around a small white hero.
- Kills burst into shards of the enemy's hue.

### Color roles

| Role                                                               | Color                                                                               |
| ------------------------------------------------------------------ | ----------------------------------------------------------------------------------- |
| The hero and everything the hero makes (slashes, suites, statuses) | White                                                                               |
| Fodder (key family)                                                | Cyan and blue                                                                       |
| Special types (accent)                                             | Magenta and violet                                                                  |
| Heavies and the Warden (counter)                                   | Orange and amber                                                                    |
| The Heartwyrm                                                      | Boss red                                                                            |
| Hazards and enemy attacks                                          | Filled red and orange shapes                                                        |
| Walls                                                              | Grey                                                                                |
| Floor grid                                                         | The zone key: cobalt, teal, ember                                                   |
| Suites in the UI                                                   | Mint (Gale), cyan (Frost), indigo (Voltage), magenta (Void) on cards and icons only |

Playlist Survivors' `palette.json` and style guide (`docs/champions/style.md`)
hold the exact values and the value ladder: no monster outshines the hero.

### Crowds and meshes

Hundreds of skinned figures at once need Playlist Survivors' crowd renderer
(bone-texture instancing). Each figure draws through the edge look; where a
vendor mesh scribbles into a fill, a wireframe "monster mesh" replaces it: a
low-poly mesh skinned to the vendor rig, built locally by a committed recipe and
never committed.

### Visual priority

From highest to lowest (feel §3.4, §6.2): telegraphs, elite and boss moments,
kills, hits, statuses, swings. In horde mode, per-hit effects on fodder stay
small, because hundreds of them land each second. The white slash crescents can
be large but are thin and short-lived. Enemy telegraphs draw above the hero's
effects.

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
±5–10%, 3–5 variations per sound, music ducked under big impacts. At horde
scale, hit and kill sounds share a voice budget: a swing that kills twenty plays
one layered kill sound that grows with the count, not twenty.

**Music**: by default, tracks from the Ultimate Game Music Collection (its
Combat, Dungeons and Space folders). Launching with
`--file ~/Music/songs/songs.m3u --shuffle` plays the player's own playlist
instead. Nothing in the game reads the music's beat; the only coupling is the
ducking.

## HUD

Minimal, and out of the way of the fight:

- Health, with a pip for Second Wind.
- The room timer (horde mode).
- Dash charges and the special's cooldown ring under the hero.
- The Shard count.
- Three slot icons in their suites' colors, with Levels.
- The boss bar with its Break bar.

## Build order

What each step builds on. Each step makes its part feel right through testbed
variants (AGENTS.md), and every step builds and tunes both combat modes.

1. **The feel room**: one arena, the look with its crowd renderer, the hero's
   kit, `combat.mode`, and skeletons in both modes with every hit-feedback
   channel (feel §11).
2. **The chain**: the rest of the roster, Empowered elites, hazards, and four
   arenas connected without loading, each playable as timed horde rooms or
   wave-cleared focused rooms.
3. **The suites**, one at a time with all three Levels: Gale first (knockback
   needs only walls and the crowd), then Frost, Voltage and Void. Synergies
   arrive as their pairs exist.
4. **The Warden**, with prisms, Shards, offers and shops.
5. **The Heartwyrm** and the full 12-room run, in both modes and in `blind`.

## Out of scope

- **Rhythm.** The MVP keeps two things compatible with a later rhythm layer: the
  `--file`/`--shuffle` launch and the Cleave's even cadence.
- Other weapons, automatic attacks, a hub, meta-progression, story and voice.
- Room forks, pits, Legendary upgrades, rarity tiers and in-room level-ups.
- Controller support (the movement-aim flag keeps it open).

## Open questions

The big one is answered by playing: which combat mode is more fun (Two combat
modes). These are for testbeds:

- How far back the camera sits in each mode, and how many enemies read well at
  once.
- Whether damage numbers help or clutter (feel §3.2).
- Whether `LightFencing01` or `HeavyStab01` makes the better Lunge.
- Every number above.
