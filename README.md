# Live Info Boards

`LiveInfoBoards.dll` is a server-side MelonLoader mod for A Township Tale. It
updates infoboard text from JSON while the server is running. It works on its
own or alongside the Prefab Editor mod. Players do not need to install anything.

## Install

Download `LiveInfoBoards.dll` from the [latest release](https://github.com/calebv2/Live-Info-Boards/releases/latest),
copy it to the server's `game-source/Mods` directory, and restart the server
once to load the mod. On its first server boot it creates:

```text
game-source/UserData/InfoBoards.json
```

## Build from source

Build against the local game files:

```bash
./build.sh /home/ATT/a-township-container/game-source
```

The build writes `bin/LiveInfoBoards.dll`.

## Board configuration

The file is a registry keyed by physical infoboard entity ID:

```json
{
  "boards": {
    "4772": {
      "channel": "welcome",
      "text": "Welcome to ATT!\n\nTown meeting at 8 PM.",
      "rotation": {
        "enabled": true,
        "interval": 2,
        "unit": "minutes",
        "messages": ["Welcome to ATT!", "Town meeting at 8 PM."]
      }
    },
    "6000": {
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

On startup and as map chunks load, the mod discovers physical boards and adds
missing entity IDs to the JSON with a generated `board-<entityId>` channel and
a visible placeholder. Edit the generated `text` field; do not alter an entity
ID. Rotating boards can be enabled before messages are added; once present, up
to 20 messages advance in order using a 1-86400 second or minute interval. The
first message is shown immediately and the list loops. The existing board near
`(-688.3, 129.2, 74.9)` currently has
entity ID `4772` and channel `welcome`.

## Limits

- Channel names: 1-20 letters, digits, `_`, or `-`.
- Text: at most 5,000 characters per board/message section.
- Use `\n` inside JSON text for a line break.
- Removing a JSON property deliberately does not blank a board. Use the game's
  ordinary infoboard command if you want to clear a channel.

## Tests

```bash
./tests/run-tests.sh
./build.sh /home/ATT/a-township-container/game-source
```
