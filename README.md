# nullroute

An endless, procedurally generated hacking game played entirely in a terminal. There's no story and no finish line. You take contracts, break into systems, earn money and reputation, and upgrade your rig and tools. Each rank unlocks harder targets, and the game keeps going until someone traces you. Getting caught ends your career for good.

> *null route (n.): a network route that goes nowhere — where packets go to die.*

## Playing

Open the project in **Unity 6000.6.9f1** and press Play, or build a standalone player with **Nullroute → Build Windows**. The output goes to `Builds/Windows/nullroute.exe`.

A new career starts with an interactive tutorial that walks you through your first job. You can skip it with `tutorial skip`. Type `help` at any point to see every command.

| Key | Action |
| --- | --- |
| Enter | run command |
| Tab | complete commands, IPs, file names, record ids |
| ↑ / ↓ | command history |
| PgUp / PgDn / mouse wheel | scroll |
| Esc | clear the input line |
| Ctrl + `=` / Ctrl + `-` | font size |
| Ctrl + L | clear the screen |

## How it works

**The loop:** `jobs` → `accept` → `connect` → break in → do the job → `logwipe` → `dc` → `submit`, then spend the money in the `shop`.

**Contracts** are generated for your rank and come in four types:
- **steal**: download a file and decrypt it
- **delete**: destroy a file on the target
- **plant**: upload a file the client gives you
- **alter**: change a database record

The board refreshes as jobs expire.

**Defenses.** Each target can run up to four security layers, each with a level:
- proxy, beaten by `pxbypass`
- firewall, beaten by `fwbypass`
- monitor, beaten by `monbypass`; monitors make traces faster
- password, beaten by `crack`

Each tool's version must be at least the layer's level. Tier and difficulty grow without limit.

**Getting caught.** There are two ways to lose:
- **Active trace.** The first hostile action on a system starts a trace. If it reaches 100% while you're still connected, you're caught.
- **Forensics.** If you disconnect and leave suspicious log entries behind, an investigation starts on a timer. When it resolves, it may trace you. You can reconnect and `logwipe` before then to kill it.

Quitting the game while you're connected counts as disconnecting, so it doesn't get you out of a trace.

**Getting better.** Rewards scale with tier. Your workstation and tools both improve:
- **cpu**: tools run faster
- **ram**: more tools can run at once
- **nic**: faster transfers and more bounce hops
- **hdd**: more storage space
- **cloak**: traces run slower and forensic risk drops
- **bounces**: route through public relays (`bounce auto`) or systems you've already breached. Each hop slows traces down.
- **Tool versions** are capped at your rank + 2, so ranking up keeps unlocking stronger tools.
- **New tools** unlock at higher ranks: Proxy Bypass, Decryptor, Monitor Bypass, and Origin Spoofer.

Hardware and tools improve your odds, but targets get tougher at a faster rate. In the long run, every career ends with a trace.

## Project layout

```
Assets/Scripts/Core/     Engine-agnostic game logic (pure C#, no UnityEngine)
  Game.cs                state, simulation tick, trace & forensics, HUD lines
  Commands.cs            every terminal command + tab completion
  WorldGen.cs, NameGen.cs procedural systems, files, records, contracts
  Balance.cs             all tunable numbers and formulas
  Tutorial.cs            guided first contract
Assets/Scripts/Unity/    IMGUI terminal front end, save store, bootstrap
Assets/Scripts/Editor/   scene setup and build menu items
tools/                   headless .NET harness that compiles the same Core code
```

### Headless harness

The core logic compiles outside Unity with Unity's language level (C# 9, netstandard2.1). That means you can test it and tune the balance without opening the editor:

```sh
dotnet run --project tools/Sim -- test      # scripted checks + bot careers
dotnet run --project tools/Sim -- bot 5     # balance report for 5 bot careers
dotnet run --project tools/Sim -- play      # play in a plain console
```

### Command-line build and screenshot test

```sh
Unity -batchmode -quit -projectPath . -executeMethod Nullroute.EditorTools.ProjectSetup.BuildWindows
Builds/Windows/nullroute.exe -autotest <dir>   # plays the tutorial, saves screenshots to <dir>, quits
```

Save files are stored in Unity's `Application.persistentDataPath` as `nullroute_career.json`.
