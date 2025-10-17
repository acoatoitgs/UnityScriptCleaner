using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

struct Node
{
    public HashSet<string> children;
    public string id;
    public string parent;
    public string name;
}

class Program
{
    static ConcurrentDictionary<string, Lazy<HashSet<string>>> scriptFieldCache = new();

    static ConcurrentDictionary<string, bool> usedScripts = new();
    
    static bool IsValidScriptCached(YamlMappingNode node, string scriptPath)
    {
        var fields = scriptFieldCache.GetOrAdd(scriptPath, path => 
            new Lazy<HashSet<string>>(() => {
                string code = File.ReadAllText(path);
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetRoot();
                return root.DescendantNodes()
                    .OfType<FieldDeclarationSyntax>()
                    .Where(f => f.AttributeLists.Any(a => a.Attributes.Any(attr => attr.Name.ToString().Contains("SerializeField"))) ||
                                f.Modifiers.Any(m => m.Text == "public"))
                    .SelectMany(f => f.Declaration.Variables)
                    .Select(v => v.Identifier.Text)
                    .ToHashSet();
            })
        ).Value;


        foreach (var property in node.Children.Keys)
        {
            string propertyString = property.ToString();
            if (propertyString.StartsWith("m_")) continue;
            if (!fields.Contains(propertyString))
            {
                return false;
            }
        }

        usedScripts.TryAdd(scriptPath, true);
        return true;
    }

    static Dictionary<string, Node> GetGameObjectStructure(IList<YamlDocument> documents, Dictionary<string, string> scripts)
    {
        var gameObjects = new Dictionary<string, (string name, string transformId)>();
        var transforms = new Dictionary<string, (string gameObjectId, string parentTransformId)>();
        var nodes = new Dictionary<string, Node>();

        foreach (var document in documents)
        {
            var root = (YamlMappingNode)document.RootNode;
            var type = ((YamlScalarNode)root.Children.Keys.First()).Value;
            var data = (YamlMappingNode)root.Children.Values.First();
            var anchor = root.Anchor.Value;
            if (string.IsNullOrEmpty(anchor)) continue;

            if (type == "GameObject")
            {
                var nameNode = data.FirstOrDefault(kv => ((YamlScalarNode)kv.Key).Value == "m_Name").Value as YamlScalarNode;
                var componentSeq = data.FirstOrDefault(kv => ((YamlScalarNode)kv.Key).Value == "m_Component").Value as YamlSequenceNode;

                var transformEntry = componentSeq?.Children
                    .OfType<YamlMappingNode>()
                    .Select(m => m.Children.Values.FirstOrDefault() as YamlMappingNode)
                    .FirstOrDefault();
                var transformId = ((YamlScalarNode)transformEntry?.Children.Values.FirstOrDefault())?.Value ?? "";
                gameObjects[anchor] = (nameNode?.Value ?? "(unnamed)", transformId);
            }
            else if (type == "Transform")
            {
                var gameObjectId = ((YamlScalarNode)((YamlMappingNode)data
                    .FirstOrDefault(kv => ((YamlScalarNode)kv.Key).Value == "m_GameObject").Value)?
                    .Children.Values.FirstOrDefault())?.Value ?? "";

                var parentTransformId = ((YamlScalarNode)((YamlMappingNode)data
                    .FirstOrDefault(kv => ((YamlScalarNode)kv.Key).Value == "m_Father").Value)?
                    .Children.Values.FirstOrDefault())?.Value ?? "";

                transforms[anchor] = (gameObjectId, parentTransformId);
            }
            else if (type == "MonoBehaviour")
            {
                var scriptNode = data?.Children
                    .FirstOrDefault(kv => ((YamlScalarNode)kv.Key).Value == "m_Script").Value as YamlMappingNode;

                if (scriptNode != null)
                {
                    var guidNode = scriptNode.Children.Values.ElementAt(1) as YamlScalarNode;
                    var guid = guidNode?.Value;
                    if (!string.IsNullOrEmpty(guid) && scripts.ContainsKey(guid))
                    {
                        IsValidScriptCached(data, scripts[guid]);
                    }
                }
            }
        }

        foreach (var (transformId, (gameObjectId, parentTransformId)) in transforms)
        {
            if (!gameObjects.TryGetValue(gameObjectId, out var gameObject)) continue;

            var parentGameObjectId = transforms.TryGetValue(parentTransformId, out var parentTransform)
                ? parentTransform.gameObjectId
                : "";

            nodes[gameObjectId] = new Node
            {
                id = gameObjectId,
                name = gameObject.name,
                parent = parentGameObjectId,
                children = new HashSet<string>(),
            };
        }

        foreach (var gameObject in nodes.Values)
        {
            if (!string.IsNullOrEmpty(gameObject.parent))
            {
                if (!nodes[gameObject.parent].children.Contains(gameObject.id))
                    nodes[gameObject.parent].children.Add(gameObject.id);
            }
        }

        return nodes;
    }

    static void PrintHierarchy(string scenePath, string outputPath, Dictionary<string, string> scripts)
    {
        var yaml = new YamlStream();
        using var tr = new StreamReader(scenePath);
        yaml.Load(tr);

        Dictionary<string, Node> nodes = GetGameObjectStructure(yaml.Documents, scripts);

        using var writer = new StreamWriter(outputPath);
        foreach (var node in nodes.Values.Where(n => string.IsNullOrEmpty(n.parent)))
            PrintNodeRecursively(node, nodes, 0, writer);
    }

    static void PrintNodeRecursively(Node node, Dictionary<string, Node> nodes, int depth, StreamWriter writer)
    {
        writer.WriteLine(new string('-', 2 * depth) + node.name);

        if (node.children == null || node.children.Count == 0) return;

        foreach (string child in node.children)
            PrintNodeRecursively(nodes[child], nodes, depth + 1, writer);
    }

    static List<string> GetListOfScenes(string unityProjectPath)
    {
        string assetsPath = Path.Combine(unityProjectPath, "Assets");
        if (!Directory.Exists(assetsPath))
        {
            Console.WriteLine("Project directory is not a Unity project.");
            Environment.Exit(1);
        }

        return Directory.EnumerateFiles(assetsPath, "*.unity", SearchOption.AllDirectories)
                        .Where(f => !f.Contains("Thirdparty") && !f.Contains("_Recovery"))
                        .ToList();
    }

    static Dictionary<string, string> GetListOfScripts(string unityProjectPath)
    {
        string assetsPath = Path.Combine(unityProjectPath, "Assets");
        if (!Directory.Exists(assetsPath))
        {
            Console.WriteLine("Project directory is not a Unity project.");
            Environment.Exit(1);
        }

        var csFiles = Directory.EnumerateFiles(assetsPath, "*.cs", SearchOption.AllDirectories)
                               .Where(f => !f.Contains("Thirdparty") && !f.Contains("_Recovery"));

        var guidToPath = new Dictionary<string, string>();

        foreach (var file in csFiles)
        {
            string metaFile = file + ".meta";
            if (!File.Exists(metaFile)) continue;

            string guidLine = File.ReadLines(metaFile).FirstOrDefault(line => line.StartsWith("guid:"));
            if (guidLine == null) continue;

            string guid = guidLine.Split(':')[1].Trim();
            guidToPath[guid] = file;
        }

        return guidToPath;
    }

    static void UnityProjectExplorer(string unityProjectPath, string outputFolderPath)
    {
        List<string> scenes = GetListOfScenes(unityProjectPath);
        Dictionary<string, string> scripts = GetListOfScripts(unityProjectPath);

        if (!Directory.Exists(outputFolderPath))
            Directory.CreateDirectory(outputFolderPath);

        Parallel.ForEach(scenes, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, scene =>
        {
            string scenePath = outputFolderPath + "/" + Path.GetFileNameWithoutExtension(scene) + ".txt";
            PrintHierarchy(scene, scenePath, scripts);
        });

        string unusedScriptsPath = Path.Combine(outputFolderPath, "UnusedScripts.csv");
        using var writer = new StreamWriter(unusedScriptsPath);
        writer.WriteLine("Relative Path, GUID");

        foreach (var script in scripts)
        {
            if (!usedScripts.ContainsKey(script.Value))
                writer.WriteLine($"{script.Value}, {script.Key}");
        }
    }

    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Please provide at least two arguments.");
            return;
        }

        UnityProjectExplorer(args[0], args[1]);
    }
}
