using System.IO;
using Nullroute.Core;
using UnityEngine;

namespace Nullroute.UnityFrontend
{
    /// <summary>Saves the career as JSON under Application.persistentDataPath.</summary>
    public class UnitySaveStore : ISaveStore
    {
        readonly string path;

        public UnitySaveStore(string overridePath = null)
        {
            path = overridePath ?? Path.Combine(Application.persistentDataPath, "nullroute_career.json");
        }

        public bool Exists() { return File.Exists(path); }

        public SaveData Load() { return JsonUtility.FromJson<SaveData>(File.ReadAllText(path)); }

        public void Save(SaveData data)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(data));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public void Delete()
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
