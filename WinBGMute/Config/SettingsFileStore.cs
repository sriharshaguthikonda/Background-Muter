using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WinBGMuter.Config
{
    internal static class SettingsFileStore
    {
        private const string SettingsFileName = "WinBGMuter_settings.json";
        private static string SettingsFilePath => Path.Combine(AppContext.BaseDirectory, SettingsFileName);

        public static void Load()
        {
            try
            {
                EnsureFileExists();

                var json = File.ReadAllText(SettingsFilePath);
                var map = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new Dictionary<string, string?>();

                var properties = Properties.Settings.Default.Properties.Cast<System.Configuration.SettingsProperty>()
                    .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in map)
                {
                    if (!properties.TryGetValue(kvp.Key, out var prop))
                    {
                        continue;
                    }

                    var converted = ConvertFromString(kvp.Value, prop.PropertyType);
                    if (converted != null)
                    {
                        Properties.Settings.Default[kvp.Key] = converted;
                    }
                }
            }
            catch
            {
                // Swallow read/parse errors to avoid blocking startup; user can still run with defaults.
            }
        }

        public static void Save()
        {
            try
            {
                var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                foreach (System.Configuration.SettingsProperty prop in Properties.Settings.Default.Properties)
                {
                    var value = Properties.Settings.Default[prop.Name];
                    map[prop.Name] = Convert.ToString(value, CultureInfo.InvariantCulture);
                }

                var json = JsonSerializer.Serialize(map, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Swallow write errors; app continues without blocking shutdown.
            }
        }

        private static void EnsureFileExists()
        {
            if (!File.Exists(SettingsFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
                Save();
            }
        }

        private static object? ConvertFromString(string? value, Type targetType)
        {
            if (value == null)
            {
                return null;
            }

            try
            {
                var converter = TypeDescriptor.GetConverter(targetType);
                if (converter.CanConvertFrom(typeof(string)))
                {
                    return converter.ConvertFromString(null, CultureInfo.InvariantCulture, value);
                }
            }
            catch
            {
                // ignore conversion failures
            }

            return null;
        }
    }
}
