# ExpandedStomach

## Overview

ExpandedStomach is a Vintage Story mod that enhances the game's food system by allowing players to continue eating even when their normal Satiety bar is full. This mod introduces a secondary "stomach" system that lets you store additional food beyond your character's normal saturation limit, creating more realistic and strategic eating mechanics.

## Features

- **Secondary Stomach System**: Continue eating after your normal Satiety bar is full
- **Drawback System**: Overeating can cause negative effects, such as weight gain, which can affect game play over time
- **Stomach Size**: Your stomach can expand over time based on eating habits
- **7-Day Average System**: Your stomach size is based on your average daily eating habits
- **Configurable Difficulty**: Multiple settings to adjust the mod to your preferred playstyle
- **Immersive Messages**: Optional in-game messages that provide feedback on your character's fullness
- **Mod Compatibility**: Designed to work with 'Xskills', 'Hydrate or Diedrate', and any mods that add food items by extending the BlockMeal and CollectibleObject classes

## Installation

1. Download the latest release from the mod releases page
2. Place the .zip file in your Vintage Story mods folder
3. Start the game and enable the mod in the mod menu

## Uninstallation

To properly uninstall the mod and remove all attributes that could affect gameplay:

1. Before removing the mod, run the following commands for each player:
   - `/es debug setStomachSize [player] 0.0` - Resets the stomach size
   - `/es debug setFatLevel [player] 0.001` - Sets fat level to near zero (this is to remove any possible calculated drawbacks)
   - `/es debug setFatLevel [player] 0.0` - Completely removes fat
2. Remove the mod file from your mods folder
3. Restart the server/game

These steps ensure all mod attributes are properly removed from player data, preventing any issues when playing without the mod.

## Configuration

The mod can be configured through the `expandedstomachServer.json` file located in your game's config folder. The following options are available:

| Setting | Description | Default |
|---------|-------------|--------|
| `hardcoreDeath` | If true, dying can severely reset your progress | `false` |
| `stomachSatLossMultiplier` | Multiplier for stomach saturation loss rate (minimum 1.0) | `1.0` |
| `drawbackSeverity` | Multiplier for negative effects from overeating | `0.4` |
| `fatGainRate` | Use to fine-tune the rate at which your character gains fat | `1.0` |
| `fatLossRate` | Use to fine-tune the rate at which your character loses fat | `1.0` |
| `strainGainRate` | Use to fine-tune the rate at which stomach strain increases | `1.0` |
| `strainLossRate` | Use to fine-tune the rate at which stomach strain decreases | `1.0` |
| `difficulty` | Overall difficulty setting (`easy`, `normal`, or `hard`) | `normal` |
| `immersiveMessages` | Enable/disable immersive fullness messages | `true` |
| `debugMode` | Enable/disable debug mode | `false` |

## Commands

The mod adds several commands that can be used to manage and monitor your expanded stomach:

### Main Command

- `/es` - Base command for all ExpandedStomach functionality

### Debug Subcommands
These commands are only available in single player or when run by a server op.

- `/es debug printInfo [player]` - Displays information about a player's stomach
- `/es debug setFatLevel [player] [level]` - Sets the fat level of a player (0.0-1.0)
- `/es debug setStomachLevel [player] [level]` - Sets the stomach fullness level of a player (0.0-1.0)
- `/es debug setStomachSize [player] [size]` - Sets the stomach size of a player (500-5500)
- `/es debug printConfig` - Displays the current configuration settings
- `/es debug setConfig [key] [value]` - Changes a configuration setting. A reboot is required once the value changes.

## Gameplay Tips

- Eating beyond your normal satiety will store food in your expanded stomach
- Your expanded stomach will slowly transfer nutrients to your normal satiety
- Overeating regularly will increase your stomach size over time
- Excessive eating leads to fat gain, which can slow your movement speed
- Your tolerance to cold weather will increase in proportion to your fat level
- Maintain a balanced diet to optimize nutrition and minimize negative effects
- Keep satiety below 1000 to diet and shed unwanted weight(may take some time)

## Known Issues

- None currently reported. If you find any issues, please report them!

## Credits

Coding: traugdor (Discord: @quadmoon)
Testing: traugdor, LadyWYT
Ideas: traugdor, LadyWYT
Artwork: LadyWYT, Flux Dev

Special thanks to Dana (Craluminium2413) for assistance with the configuration system.

Special thanks to Tyron (Vintage Story lead developer) for including Harmony in the base game, which made developing some of the mod systems significantly easier.

## Compatibility

ExpandedStomach was designed with compatibility in mind and works well with:

- **Xskills**: Designed to adapt to any changes Xskills makes to the satiety system
- **Hydrate or Diedrate**: Works alongside the hydration mechanics
- **Food Mods**: Compatible with any mods that add food items by extending the BlockMeal and CollectibleObject classes
- **Body Fat by Tasshroom**: Compatible, for those wanting some fat reserves to avoid starvation. Numbers may not be balanced, so you may need to tweak configs for best experience.

## License

All rights reserved.
