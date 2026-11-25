using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Lumo
{
    /// <summary>Přihrádka na soubory a rychlé spouštění – ukládá se do %APPDATA%\Lumo\nastroje.json.</summary>
    public class ToolsStore
    {
        public List<string> Shelf { get; set; } = new();
        public List<string> Launchers { get; set; } = new();

        private static string FilePath => Path.Combine(Settings.Folder, "nastroje.json");

        public static ToolsStore Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<ToolsStore>(File.ReadAllText(FilePath)) ?? new ToolsStore();
            }
            catch (Exception ex) { Log.Write("Nástroje nejde načíst: " + ex.Message); }
            return new ToolsStore();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Log.Write("Nástroje nejde uložit: " + ex.Message); }
        }
    }
}
