using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PartyFarm
{
    internal static class ConfigLoader
    {
        public static object LoadConfig(string path, Type targetType, object def)
        {
            if (!File.Exists(path))
                File.WriteAllText(path, JObject.FromObject(def).ToString());
            var obj = JObject.Parse(File.ReadAllText(path)).ToObject(targetType);
            return obj;
        }

        public static void SaveConfig(string path, object def)
        {
            File.WriteAllText(path, JObject.FromObject(def).ToString());
        }
    }
}
