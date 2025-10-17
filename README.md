# Unity Unused Script Finder

This tool analyzes a Unity project, scans all scenes and C# scripts, and identifies scripts that are not used (i.e., no serialized fields or public variables are referenced in any scene). It works with `.unity` scene files and `.cs` scripts that inherit from `MonoBehaviour`.

---

## Features

- Parses all scenes in the Unity project.
- Checks all C# scripts for usage of public fields and `[SerializeField]` fields.
- Supports parallel processing for faster analysis.
- Outputs a CSV file listing all unused scripts.
- Generates a simple text hierarchy for each scene.

---

## Requirements

- .NET 6 or later
- YamlDotNet (`dotnet add package YamlDotNet`)
- Microsoft.CodeAnalysis.CSharp (`dotnet add package Microsoft.CodeAnalysis.CSharp`)

---

## Usage

1. Build the project:

```
dotnet build -c Release
```

2. Run the tool:

```
dotnet run --project <path-to-this-project> <path-to-unity-project> <output-folder>
```

- <path-to-unity-project>: Root folder of your Unity project.
- <output-folder>: Folder where the results will be saved.

3. Output:

- For each scene, a text file with the hierarchy of GameObjects will be saved in <output-folder>.
- A CSV file UnusedScripts.csv will be generated containing all scripts that are not used:

```
Relative Path, GUID
Assets/Scripts/UnusedScript.cs, <GUID>
...
```

---

## Notes

- Scripts are considered "used" if they contain public fields or `[SerializeField]` fields referenced by a scene.
- The tool caches parsed scripts and runs scene analysis in parallel for faster performance.
- Only scripts directly inherited from `MonoBehaviour` are supported.

---

## Example

```
dotnet run --project ./UnityUnusedScriptsFinder ./SampleUnityProject ./Output
```

This will generate:

```
- Output/UnusedScripts.csv
- Output/SceneName.txt for each scene in the project
```
