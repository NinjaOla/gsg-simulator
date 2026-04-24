# gsg-simulator

My test game/engine/stuff?

I want to create my own gsg game, i dont have experience in game development. Im a huge fan of the PDX gsgs like HOI, EU, VIC and CK so this will be inspired by that.

All tutorials show game logic tightly coupled into the game engine(unity etc) Which seems odd to me as a systems developer.
The idea is to create a simulation engine/logic as a library / seperate process and the UI/gameengine just interactis with it.

The first interaction is a console to manually verify that things work.
Second will be a globe with the stride game engine.
Most likely pure C# for everything as that is what I enjoy.


## Solution

Open the repository with `SimEngine.slnx`.

## Build

```powershell
dotnet build .\SimEngine.slnx
```

## Test

```powershell
dotnet test .\SimEngine.slnx
```
