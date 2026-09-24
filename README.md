# STRAFTAT StatTracker

A BepInEx plugin for [STRAFTAT](https://store.steampowered.com/app/2386720/STRAFTAT/) that tracks kills, deaths, rounds won and matches won for everyone in your lobby, plus the damage you deal yourself. The stats are shown in a small overlay, for the current match and for the whole session.

The mod only reads game state. It doesn't change gameplay or send anything over the network, and it's marked as vanilla-compatible, so you can keep playing in normal public lobbies.

## Overlay

`F8` shows or hides the overlay. `F7` switches between the Match and Session tabs.

| Column | Meaning |
| --- | --- |
| K / D | Kills and deaths |
| K/D | Kill/death ratio |
| RW | Rounds won |
| MW | Matches won (Session tab only) |

Below the table is the damage you've dealt, in the same units as the game's health display (100 = full health). Your own row is highlighted, and gradient names keep their colors.

## Installing

1. Download BepInEx 5 for Windows x64 (`BepInEx_win_x64_5.4.23.x.zip`) from the [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) and extract it into the game folder, so that `winhttp.dll` ends up next to `STRAFTAT.exe`.
2. Start the game once and close it again. BepInEx creates its folders on the first start.
3. Download `StatTracker.dll` from the [releases](https://github.com/Toggsen/STRAFTAT_Stattracker/releases) of this repository and put it in `BepInEx\plugins\StatTracker\`.
4. Start the game. The overlay shows up in the top right corner.

To find the game folder, right-click STRAFTAT in Steam and choose Manage > Browse local files.

## Uninstalling

To remove only this mod, delete the `BepInEx\plugins\StatTracker` folder. You can also delete its config file `BepInEx\config\toggsen.straftat.stattracker.cfg` and the match history `BepInEx\StatTracker_history.txt`.

To remove BepInEx as well, delete `BepInEx`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `changelog.txt` from the game folder. If you just want to turn mods off for a while, set `enabled = false` in `doorstop_config.ini` instead.

## Settings

The config file `BepInEx\config\toggsen.straftat.stattracker.cfg` is created the first time the game starts with the mod installed.

| Setting | Default | Description |
| --- | --- | --- |
| `ToggleOverlay` | F8 | Show / hide the overlay |
| `CycleScope` | F7 | Switch between Match and Session |
| `VisibleOnStart` | true | Show the overlay when the game starts |
| `PositionX` | -12 | Horizontal position in pixels. Negative values are measured from the right edge |
| `PositionY` | 120 | Vertical position in pixels |
| `FontSize` | 16 | Font size at 1080p, scaled for other resolutions |
| `WriteMatchHistory` | true | Add a one-line summary of every match to `BepInEx\StatTracker_history.txt` |

## Vanilla compatibility

STRAFTAT scans all loaded mods on startup. Any mod that isn't marked as vanilla-compatible puts you into a separate matchmaking pool, so you'd only find lobbies of players with the same mods. StatTracker is marked as compatible (`[assembly: StraftatMod(isVanillaCompatible: true)]`), since it only shows stats and doesn't reveal anything like enemy health or positions.

You can check this yourself:

- If an incompatible mod is loaded, the game shows a "Non-vanilla friendly mods detected" popup at startup and adds `[MODDED]` to your Steam status. With StatTracker you get neither.
- `BepInEx\LogOutput.log` contains `Matchmaking: all loaded mods are vanilla-compatible.`
- The game's own log (`%USERPROFILE%\AppData\LocalLow\LemaitreBros\STRAFTAT\Player.log`) contains `No incompatible assemblies found!`

The game shares the names of your loaded mods with the other players in the lobby (they end up in their game log), so others can see that you're using StatTracker.

## How it works

The plugin uses Harmony to hook a few game methods. Every hook is a prefix or postfix that only reads values. The original methods always run as usual, and the plugin never writes to game objects, calls RPCs or sends network messages.

**Deaths.** Player health is synced to every client, so the plugin can see any player's health drop to zero, not just your own.

**Kills.** The game keeps a `killer` reference on each player, pointing to whoever damaged them last, and syncs it to everyone as well. When a player dies, that player gets the kill. Suicides and deaths without an attacker only count as a death, and teamkills don't count when teams are enabled.

**Damage.** Weapons deal damage through RPCs (`GiveDamage`, `KillServer` and `PlayerHealth.RemoveHealth`) that are called on the shooter's machine, so the calls made on your machine are exactly the hits you landed. Each hit is capped at the target's remaining health, so overkill doesn't inflate the number. Self-damage and friendly fire are ignored.

**Rounds.** The end-of-round screen is triggered on every client together with the winning team. Everyone on that team gets a round win.

**Matches.** When the victory screen opens, the team with the most rounds gets the match win. This is the same check the game uses to decide who sees "Victory".

A match starts when players spawn on the first map and ends at the victory screen, or when you go back to the main menu. The session covers everything since the game was started. Stats aren't saved between sessions, apart from the history file.

### Limitations

- Damage is only tracked for you. Other clients never tell yours who hit whom.
- If a player falls into the void after being hit, the last player who hit them gets the kill.
- A game update can rename the methods the plugin hooks. If a stat stops updating, look for "Could not find a method to hook" in `BepInEx\LogOutput.log`.

## Building

You need the .NET SDK (6.0 or newer) and STRAFTAT with BepInEx installed.

```
git clone https://github.com/Toggsen/STRAFTAT_Stattracker.git
cd STRAFTAT_Stattracker
dotnet build -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\STRAFTAT"
```

The project references the game assemblies in `STRAFTAT_Data\Managed` and BepInEx in `BepInEx\core`. If you clone the repository two folders below the game folder (for example `STRAFTAT\Mods\StatTracker`), you can leave out `GameDir`. After a successful build the DLL is copied to `BepInEx\plugins\StatTracker`.

Built against STRAFTAT 1.4.9f and BepInEx 5.4.23.5.
