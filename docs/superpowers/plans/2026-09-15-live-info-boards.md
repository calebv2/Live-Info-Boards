# Live Info Boards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a server-only MelonLoader mod that live-updates any configured A Township Tale infoboard channel from `UserData/InfoBoards.json`.

**Architecture:** A pure parser validates a JSON object and returns a sorted set of channel/text pairs. The server mod polls the file, persists changed values in the game-native InfoBoard save directory, and invokes the game's public `InfoBoard.ForceRefresh` to use existing synchronization.

**Tech Stack:** C# 7.3, .NET Framework 4.7.2, MelonLoader, A Township Tale `Root.Township.dll`, Mono `mcs`.

**Spec:** `docs/superpowers/specs/2026-09-15-live-info-boards-design.md`

## Global Constraints

- Server-only: do nothing unless `UnityEngine.Application.isBatchMode` is true.
- Read `UserData/InfoBoards.json` every 0.5 seconds.
- Accept channel names matching `^[A-Za-z0-9_-]{1,20}$`.
- Reject text longer than 500 characters.
- Do not erase a channel when it disappears from JSON.
- Clients require no DLL.

---

### Task 1: Configuration document parser

**Files:**
- Create: `InfoBoardDocument.cs`
- Create: `tests/Program.cs`

**Interfaces:**
- Produces: `InfoBoardDocument.TryParse(string json, out Dictionary<string,string> entries, out string error)`.

- [ ] **Step 1: Write the failing parser tests**

```csharp
Assert(InfoBoardDocument.TryParse("{\"townnews\":\"Hello\\nTown\"}", out var entries, out _));
Assert(entries["townnews"] == "Hello\nTown");
Assert(!InfoBoardDocument.TryParse("{\"../bad\":\"x\"}", out _, out _));
Assert(!InfoBoardDocument.TryParse("{\"townnews\":123}", out _, out _));
```

- [ ] **Step 2: Run the tests to verify they fail because `InfoBoardDocument` is missing.**

Run: `./tests/run-tests.sh`

- [ ] **Step 3: Implement the minimal parser and validation.**

```csharp
public static bool TryParse(string json, out Dictionary<string,string> entries, out string error)
```

Use `JavaScriptSerializer`, reject non-string values, validate every key and
text length, and return an ordinal dictionary.

- [ ] **Step 4: Run parser tests to verify they pass.**

Run: `./tests/run-tests.sh`

### Task 2: Polling authority and server integration

**Files:**
- Create: `InfoBoardAuthority.cs`
- Create: `Core.cs`
- Create: `Properties/AssemblyInfo.cs`
- Create: `LiveInfoBoards.csproj`
- Create: `build.sh`

**Interfaces:**
- Consumes: `InfoBoardDocument.TryParse`.
- Produces: `InfoBoardAuthority.TryReadChanges(string json, out IReadOnlyList<InfoBoardChange> changes, out string error)`.

- [ ] **Step 1: Write failing authority tests.**

```csharp
var authority = new InfoBoardAuthority();
Assert(authority.TryReadChanges("{\"townnews\":\"One\"}", out var first, out _));
Assert(first.Count == 1);
Assert(authority.TryReadChanges("{\"townnews\":\"One\"}", out var unchanged, out _));
Assert(unchanged.Count == 0);
```

- [ ] **Step 2: Run tests to verify the missing authority fails.**

Run: `./tests/run-tests.sh`

- [ ] **Step 3: Implement change tracking and the MelonMod shell.**

`Core.OnUpdate` reads the JSON file on a half-second interval. On valid changes
it writes each value to the canonical save folder and calls
`Alta.Map.InfoBoard.ForceRefresh(change.Channel)`.

- [ ] **Step 4: Run unit tests and compile the DLL.**

Run: `./tests/run-tests.sh && ./build.sh /home/ATT/a-township-container/game-source`

### Task 3: Operational documentation and live verification

**Files:**
- Create: `README.md`
- Modify: `build.sh` if deployment output requires adjustment.

- [ ] **Step 1: Document deployment and the initial board setup.**

Include the deploy destination, `townnews` JSON sample, 500-character limit,
and the one-time in-game channel assignment for entity `54619`.

- [ ] **Step 2: Verify the final build and all automated tests.**

Run: `./tests/run-tests.sh && ./build.sh /home/ATT/a-township-container/game-source`

- [ ] **Step 3: Copy the resulting DLL to the local server Mods directory only after the automated build is clean.**

Destination: `/home/ATT/a-township-container/game-source/Mods/LiveInfoBoards.dll`.
