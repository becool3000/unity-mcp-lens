using System.IO;
using UnityEngine;

namespace HFHubClient
{
    static class Paths
    {
        // Note: `persistentDataPath` points to: /Users/<username>/Library/Application Support/<Company>/<Project Name>
        static readonly string k_AppDataPath =
            Path.GetFullPath(Path.Combine(Application.persistentDataPath, "..", ".."));

        static readonly string k_ModelServerDataPath = Path.Combine(k_AppDataPath, "Unity", "UnityModelServer");
        public static readonly string ModelsPath = Path.Combine(k_ModelServerDataPath, "Models");
        public static string ModelPath(string id) => Path.Combine(ModelsPath, id);
    }
}
