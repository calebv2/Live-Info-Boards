# Live Info Boards Design

## Goal

Provide a server-only MelonLoader mod that changes A Township Tale infoboard
text from a JSON file while the server is running. Clients continue to use the
game's built-in infoboard networking and need no additional mod.

## Configuration

The mod reads `UserData/InfoBoards.json` every 0.5 seconds. Its root is a JSON
object whose property names are infoboard channels and whose values are text.

```json
{
  "townnews": "Welcome to ATT!\n\nTown meeting at 8 PM."
}
```

The initial physical board is entity `54619` at approximately `(-688.3, 129.2,
74.9)`. It is configured once in the game to use the `townnews` channel. More
physical boards and JSON channels can be added later without modifying the mod.

## Runtime flow

1. The mod runs only in the headless server process.
2. On a new valid JSON revision, it validates every entry and compares each
   channel value with the last applied value.
3. For each changed channel, it writes the UTF-8 value to the game-native
   `Save/InfoBoard/<channel>` file and calls `Alta.Map.InfoBoard.ForceRefresh`.
4. The game reloads the file for every loaded matching board and sends the text
   through its own `MethodSync` to players in the board's loaded chunks.

## Constraints and failure behavior

- Channel names contain 1-20 ASCII letters, digits, `_`, or `-` only.
- Text is at most 500 characters. JSON `\n` escapes naturally become line
  breaks when parsed.
- The mod writes only changed entries, and a successful version is never
  repeatedly applied.
- Missing configuration is harmless. On first boot the mod creates an example
  configuration file.
- Invalid JSON or an invalid entry is logged once per file revision and does
  not alter any board. The last successfully applied text remains displayed.
- Deleting a property does not erase a board; removal is intentionally explicit
  through the game's normal command, avoiding accidental blank boards.

## Testing

Pure configuration parsing and validation are covered by an executable test
program. A build script compiles the server DLL against the local game files.
Manual verification updates `UserData/InfoBoards.json` while a player is near
the `townnews` board and confirms the text changes without a restart.
