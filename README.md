# Ardub's Enemy Intelligence

Ardub's Enemy Intelligence is an experimental mod for
**Pathfinder: Wrath of the Righteous** intended to increase enemy intelligence, and require a higher degree of tactics.
I built this after getting tired of being able to so easily lock-up my enemies with my summoned shadow.

In turn-based combat, it gives 
enemies a chance to reconsider their targets based on Intelligence and uses
learned or recognized information to compare danger, defenses, and distance.
In making this mod, the enemies basically seamrolled past my defenses, so I also implemented fixes for movement and body-blocking.

Current version: **0.1.62**

## Features

- Intelligence-based target reassessment at the start of a unit's turn.
- Remembered opponents remain eligible for pursuit when out of sight, matching
  vanilla awareness. Unknown opponents are not discovered; native attack and
  spell targeting, range, line of sight, and route checks remain in effect.
  Targeting logs distinguish visible opponents from remembered ones out of sight.
- Attacks of opportunity recheck current allegiance, preventing former enemies
  from using stale engagements or queued reactions against their new allies.
- Ability targets are discounted when reaching them would leave no time to use
  the selected ability this turn, accounting for its action cost.
- `Risk * Squishyness * Distance` calculation for picking a target  (Ok, it's actually way more complicated than that, but you get the idea).  The ai won't target your tank if it doesn't think they can hurt it, or that it poses a threat.
- Improved body blocking so non-flying enemies can't just run through your tank.
- Attack previews continue past occupied in-range points to a clear stopping
  point on the searched route. Player attacks use the same stop-clearance rule
  as AI attacks; mounted attacks check the mount's footprint.
- Grid connections use exact body clearance instead of a blanket half-cell
  buffer, allowing closer legal stopping positions without cutting corners.
- Offensive abilities use complete body-aware approach routes when comparing
  targets. Flying units can cross enemy spaces but still need a clear place to
  stop within their movement budget. AI touch spells also carry their selected
  route into the follow-up touch attack. Player touch spells, including adjacent
  healing spells, also retain or recover a path for their delivery step.
 - You can still pass through your own characters, but can't stop in their squares.
 - ...Unless you're taking a 5-ft step, then you can stop in their squares.
- Extensive settings to turn various options on/off, and to tweak the enemy's choicemaking.  I'd love feedback on my defaults!
- (this is not a replacement for the entire AI, or even the game's action-selection(at this time, anyway).  It's limited to targetting.)

The mod is still experimental. Save before trying it in an important encounter, but it should be safe to add/remove from a save at any time.
For formulas, limitations, and the detailed version history, see the
[technical notes](TECHNICAL_NOTES.md) in the source repository.

## Requirements

- Pathfinder: Wrath of the Righteous
- Unity Mod Manager

There are no required gameplay-mod dependencies. I recommend playing my Ardub's QOL mod, as it adds a lot of features I found useful after making this mod.

## Installation

1. Install Unity Mod Manager for Pathfinder: Wrath of the Righteous
2. Install the mod using Unity Mod Manger, or Modfinder(I recommend this).
2. Extract the release into the game's `Mods` directory.
4. Start the game!

## Compatibility and limitations

- Targeting and body-blocking changes apply only in turn-based combat.
- It uses the game's pathfinder for eligible routes but adds clearance rules;
  cramped encounters, mounts, large creatures, and unusual movement deserve
  particular caution.  I'm still tweaking this, so if you run into any issues with specific encounters, please let me know.

## Troubleshooting

For any bugs, include the mod version, relevant settings,
steps to reproduce, and the relevant in-game or UMM log lines  If you are
at a specific encounter, a save file would be helpful, too.

For cursor stutters, enable **Profile cursor/pathfinding timings in UMM log**
under Logging. It writes one summary about every ten seconds to the UMM log,
not to the in-game combat log. Disable it again after comparing movement
previews with the mod's footprint and body-blocking options on and off.

## License

Ardub's Enemy Intelligence's original code and documentation are available
under the [MIT License](LICENSE), copyright 2026 The-Talik.

This license does not grant rights to Pathfinder: Wrath of the Righteous,
Owlcat Games assets or trademarks, Unity Mod Manager, or other third-party
material. Those remain subject to their respective owners and licenses. Blah blah blah.
