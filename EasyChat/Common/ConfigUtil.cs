using System;
using System.IO;
using EasyChat.Constants;
using Newtonsoft.Json;

namespace EasyChat.Common;

public abstract class ConfigUtil
{
    static ConfigUtil()
    {
        if (!Directory.Exists(Constant.ConfigPath))
            Directory.CreateDirectory(Constant.ConfigPath);
    }

    public static T LoadConfig<T>(string confName)
    {
        var path = $"{Constant.ConfigPath}/{confName}.json";
        if (!File.Exists(path))
        {
            var config = Activator.CreateInstance<T>();
            File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
        }

        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<T>(json)!;
    }

    public static void SaveConfig<T>(string confName, T config)
    {
        var path = $"{Constant.ConfigPath}/{confName}.json";
        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        File.WriteAllText(path, json);
    }
}