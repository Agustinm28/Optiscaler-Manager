using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OptiscalerClient.Models.Help;

namespace OptiscalerClient.Services
{
    public class HelpPageService
    {
        private HelpPageConfig? _config;

        public List<HelpPage> LoadHelpPages()
        {
            if (_config != null)
                return _config.Pages;

            try
            {
                var assetsPath = Path.Combine(AppContext.BaseDirectory, "assets", "help-pages.json");
                if (!File.Exists(assetsPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[HelpPageService] File not found: {assetsPath}");
                    return GetDefaultPages();
                }

                var json = File.ReadAllText(assetsPath);
                _config = JsonSerializer.Deserialize<HelpPageConfig>(json);
                
                if (_config?.Pages != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[HelpPageService] Loaded {_config.Pages.Count} pages from JSON");
                }
                
                return _config?.Pages ?? GetDefaultPages();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HelpPageService] Error loading help pages: {ex.Message}");
                return GetDefaultPages();
            }
        }

        private static List<HelpPage> GetDefaultPages()
        {
            return new List<HelpPage>
            {
                new HelpPage
                {
                    Id = "about",
                    Title = "About & Feedback",
                    Icon = "&#xE946;",
                    Sections = new List<HelpSection>
                    {
                        new HelpSection { Type = "app-info" },
                        new HelpSection { Type = "external-resources" },
                        new HelpSection { Type = "feedback" }
                    }
                }
            };
        }
    }
}
