# Level gen tests without the Editor

```
dotnet test Tools/LevelGenTests
```

Builds `Assets/Scripts/LevelGen/Core` and `Assets/Scripts/LevelGen/Tests` straight from their folders (no Unity-generated
csprojs) against `UnityEngineStub.cs`, a managed stand-in for the UnityEngine math types, and runs every test except:

- `Tests/Unity/`: smoke tests that need real GameObjects, materials and shaders. Run them in the Unity Test Runner.
- Tests marked `[Category("UnityOnly")]`.

The Unity Test Runner still runs the whole suite. The Editor needn't be open, only installed.

## Why it runs on the Editor's Mono

`dotnet test` here builds against the Editor's `unityjit-macos` profile and runs NUnitLite on the Editor's own Mono
(`Unity.app/.../MonoBleedingEdge`, version from `ProjectSettings/ProjectVersion.txt`) with `-O=-float32`, as the Editor
runs it. The Editor's Mono keeps float intermediates in double precision; .NET's JIT rounds every float operation. Under
.NET every seed comes out a few ulps different, and so a different level, so tests would pass or fail on levels Unity
never makes. On the Editor's Mono the level hashes match the Editor's exactly.

The stand-in must match Unity bit for bit, too: write each member as Unity's managed source does (per component, the
same constants). `Quaternion.Euler` is native in Unity; the stand-in's matches it for yaw-only rotations, which is all
the generator uses.

Options: `-p:UnityEditorApp=/path/to/Unity.app` for an Editor elsewhere; `-p:TestArgs="--where 'class =~ Decal'"` passes
NUnitLite options.
