# Live Info Boards

`LiveInfoBoards.dll` is a server-side MelonLoader mod for A Township Tale. It
updates infoboard text from JSON while the server is running. It works on its
own or alongside the Prefab Editor mod. Players do not need to install anything.

## Install

Download `LiveInfoBoards.dll` from the [latest release](https://github.com/calebv2/Live-Info-Boards/releases/latest)
and copy it into the server's `Mods` directory under the active game root.
Depending on the server setup, that may be `/game-source/Mods` or `/game/Mods`.
Restart the server once to load the mod.

On first boot, the mod creates `InfoBoards.json` in MelonLoader's active
`UserData` directory. Common paths are:

```text
/game-source/UserData/InfoBoards.json
/game/UserData/InfoBoards.json
```

Some hosting setups mount the game directory directly, so the same file may
appear as `/UserData/InfoBoards.json`. Use the path belonging to the active
server; you do not need to create or maintain multiple copies. The mod reads
the file from MelonLoader's active `UserData` directory.

## Build from source

Build against the local game files:

```bash
bash build.sh /home/ATT/a-township-container/game-source
```

The build writes `bin/LiveInfoBoards.dll`.

## Board configuration

The file is a registry keyed by physical board position, rounded to 0.1 units:

```json
{
  "boards": {
    "-688.3,129.2,74.9": {
      "channel": "welcome",
      "text": "Welcome to ATT!\n\nTown meeting at 8 PM.",
      "rotation": {
        "enabled": true,
        "interval": 2,
        "unit": "minutes",
        "messages": ["Welcome to ATT!", "Town meeting at 8 PM."]
      }
    },
    "-762.7,128.2,62.3": {
      "channel": "board-6000",
      "text": "Mining event Friday."
    }
  }
}
```

Save the file. The mod notices it within roughly half a second and refreshes
only changed boards. A malformed file leaves the previous board text in place
and writes one warning to the server log. A legacy channel-to-text JSON file is
migrated automatically on the first run.

Configuration metadata is checked every half second; unchanged revisions reuse
their parsed definitions. Physical board discovery runs at roughly five-second
intervals. Cached board positions, rotations, and pending save checks continue
every half second.

On startup and as map chunks load, boards must stay at the same rounded
position for three seconds before registration. Missing positions receive a
unique channel and a visible placeholder. Edit the generated `text` field;
keep the position keys unchanged. Runtime entity IDs can change after reboot
and are not persistent board identities.

Moving a board transfers its entry only when the destination key is free.
If two boards share a rounded position, or a move would overwrite an existing
entry, the mod preserves the existing registrations and logs a warning.
Separate those boards so their rounded positions differ. Each registry entry
must have a unique channel, since channels identify the game's text files.
The mod also detaches loaded save data before changing a board's channel.

Old registries containing only numeric entity-ID keys are left intact with a
warning, because matching those IDs after reboot can assign text to the wrong
board. Back up the file and replace numeric keys with the correct positions
from the diagnostic log. Previously overwritten text must be recovered from
a backup; the mod cannot reconstruct it.

Rotating boards can be enabled before messages are added; once present, up
to 20 messages advance in order using a 1-86400 second or minute interval.
The first message is shown immediately and the list loops.

## Limits

- Channel names: 1-20 letters, digits, `_`, or `-`.
- Text: at most 5,000 characters per board/message section.
- Use `\n` inside JSON text for a line break.
- Removing a JSON property deliberately does not blank a board. Use the
  game's ordinary infoboard command if you want to clear a channel.

## Test

```bash
bash tests/run-tests.sh
bash build.sh /home/ATT/a-township-container/game-source
```
