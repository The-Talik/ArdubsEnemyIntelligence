# Ardub's Enemy Intelligence

Ardub's Enemy Intelligence is a mod for
**Pathfinder: Wrath of the Righteous** intended to increase enemy intelligence, and require a higher degree of tactics.
I built this after getting tired of being able to so easily lock-up my enemies with my summoned shadow.

In turn-based combat, it gives 
enemies a chance to reconsider their targets based on Intelligence and uses
learned or recognized information to compare danger, defenses, and distance.
In making this mod, the enemies basically seamrolled past my defenses, so I also implemented fixes for movement and body-blocking.

## Features

- Improved AI.  Mostly around target selection.
 - This mod implements a `Risk * Squishyness * Distance` calculation for picking a target  (Ok, it's actually way more complicated than that, but you get the idea).  The ai won't target your tank if it doesn't think they can hurt it, or that it poses a threat.
- Body-blocking.
 - Improved body blocking so non-flying enemies can't just run through your tank.
 - Flying units, and incorporeal units can bypass this.
 - You can still pass through your own characters, but can't stop in their squares.
 - ...Unless you're taking a 5-ft step, then you *can* stop in their squares.
- Extensive settings to turn various options on/off, and to tweak the enemy's choicemaking.  I'd love feedback on my defaults!
- This is not a replacement for the entire AI, or even the game's action-selection(at this time, anyway).  It's limited to targetting.

The mod is still experimental. Save before trying it in an important encounter, but it should be safe to add/remove from a save at any time.

## Requirements

- Pathfinder: Wrath of the Righteous
- Unity Mod Manager

There are no required gameplay-mod dependencies. I recommend playing my Ardub's QOL mod, as it adds a lot of features I found useful after making this mod.

## Installation

1. Install Unity Mod Manager for Pathfinder: Wrath of the Righteous
2. Install the mod using Unity Mod Manger, or Modfinder(I recommend this).
3. Start the game!

## Compatibility and limitations

- Targeting and body-blocking changes apply only in turn-based combat.
- It uses the game's pathfinder for eligible routes but adds clearance rules;
  cramped encounters, mounts, large creatures, and unusual movement deserve
  particular caution.  I'm still tweaking this, so if you run into any issues with specific encounters, please let me know.

## License

Ardub's Enemy Intelligence's original code and documentation are available
under the [MIT License](LICENSE), copyright 2026 The-Talik.
