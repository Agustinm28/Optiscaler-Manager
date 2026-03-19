using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OptiscalerClient
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// Applies Windows 11 dark title-bar to all windows via DwmSetWindowAttribute.
    /// </summary>
    public partial class App : Application
    {
        // DWM attribute that enables dark mode on the caption / title bar (Windows 11+)
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                System.IO.File.WriteAllText("crash.log", args.ExceptionObject.ToString());
            };
            this.DispatcherUnhandledException += (s, args) =>
            {
                System.IO.File.WriteAllText("crash.log", args.Exception.ToString());
            };

            base.OnStartup(e);

            // Hook into every window activation so we can apply the dark title bar
            EventManager.RegisterClassHandler(
                typeof(Window),
                Window.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window)
            {
                ApplyDarkTitleBar(window);
            }
        }

        private static void ApplyDarkTitleBar(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                int darkMode = 1; // 1 = dark
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
            catch
            {
                // Ignore on older Windows where DWM attribute is unsupported
            }
        }
        public static string CurrentLanguage = "en";

        public static void ChangeLanguage(string langCode)
        {
            try
            {
                var newLanguageDict = new ResourceDictionary()
                {
                    Source = new Uri($"pack://application:,,,/Languages/Strings.{langCode}.xaml")
                };

                // Replace the old language dictionary
                var oldDict = Application.Current.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("/Languages/Strings."));

                if (oldDict != null)
                {
                    Application.Current.Resources.MergedDictionaries.Remove(oldDict);
                }

                Application.Current.Resources.MergedDictionaries.Add(newLanguageDict);
                CurrentLanguage = langCode;
            }
            catch
            {
                // Fallback or ignore if dictionary not found
            }
        }
    }
}
