using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;
using FontFamily = System.Drawing.FontFamily;

namespace Flow.Plugin.VSCodeWorkspaces.VSCodeHelper
{
    internal static class WslDistributionIconHelper
    {
        private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static ImageSource GetIcon(string distributionName)
        {
            if (string.IsNullOrEmpty(distributionName))
                return null;

            if (Cache.TryGetValue(distributionName, out var cachedIcon))
                return cachedIcon;

            if (distributionName.Contains("arch", StringComparison.OrdinalIgnoreCase))
                return CreateArchLinuxIcon(distributionName);

            var executablePath = FindDistributionExecutable(distributionName);
            if (executablePath == null)
                return CreateDistributionIcon(distributionName);

            var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon == null)
                return CreateDistributionIcon(distributionName);

            var image = BitmapToImageSource(icon.ToBitmap());
            Cache[distributionName] = image;
            return image;
        }

        private static string FindDistributionExecutable(string distributionName)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
                return null;

            var windowsAppsPath = Path.Combine(localAppData, "Microsoft", "WindowsApps");
            if (!Directory.Exists(windowsAppsPath))
                return null;

            var exactPath = Path.Combine(windowsAppsPath, $"{distributionName}.exe");
            if (File.Exists(exactPath))
                return exactPath;

            return Directory.EnumerateFiles(windowsAppsPath, "*.exe")
                .FirstOrDefault(path =>
                    string.Equals(Path.GetFileNameWithoutExtension(path), distributionName, StringComparison.OrdinalIgnoreCase));
        }

        private static ImageSource CreateArchLinuxIcon(string distributionName)
        {
            const string archPath =
                "M15.188 0.807c-1.354 3.313-2.167 5.484-3.672 8.703 0.922 0.979 2.057 2.12 3.896 3.406-1.979-0.818-3.328-1.635-4.339-2.484-1.927 4.026-4.948 9.75-11.073 20.76 4.818-2.781 8.547-4.495 12.026-5.151-0.146-0.641-0.234-1.333-0.229-2.063l0.005-0.151c0.078-3.089 1.682-5.458 3.583-5.297s3.38 2.792 3.307 5.88c-0.016 0.578-0.083 1.135-0.198 1.656 3.443 0.672 7.135 2.38 11.885 5.125-0.938-1.724-1.771-3.281-2.573-4.76-1.255-0.974-2.568-2.245-5.24-3.62 1.839 0.479 3.151 1.031 4.177 1.646-8.12-15.109-8.771-17.12-11.557-23.651zM30.531 28.479v-0.828h-0.313v-0.115h0.75v0.115h-0.313v0.828h-0.125zM31.099 28.479v-0.943h0.188l0.224 0.667c0.021 0.063 0.036 0.109 0.042 0.141 0.010-0.031 0.031-0.083 0.052-0.151l0.224-0.656h0.172v0.943h-0.12v-0.792l-0.276 0.792h-0.115l-0.271-0.802v0.802h-0.12z";

            var drawing = new GeometryDrawing(
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x17, 0x93, 0xD1)),
                null,
                Geometry.Parse(archPath));
            var image = new DrawingImage(drawing);
            image.Freeze();

            Cache[distributionName] = image;
            return image;
        }

        private static ImageSource CreateDistributionIcon(string distributionName)
        {
            var initials = GetInitials(distributionName);
            using var bitmap = new Bitmap(64, 64);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using var backgroundBrush = new SolidBrush(Color.FromArgb(34, 139, 230));
            graphics.FillEllipse(backgroundBrush, 4, 4, 56, 56);

            using var font = new Font(FontFamily.GenericSansSerif, initials.Length > 1 ? 22 : 28, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var textSize = graphics.MeasureString(initials, font);
            graphics.DrawString(initials, font, textBrush, (64 - textSize.Width) / 2, (64 - textSize.Height) / 2);

            var image = BitmapToImageSource(bitmap);
            Cache[distributionName] = image;
            return image;
        }

        private static string GetInitials(string distributionName)
        {
            var letters = new string(distributionName.Where(char.IsLetterOrDigit).Take(2).ToArray());
            return string.IsNullOrEmpty(letters) ? "WSL" : letters.ToUpperInvariant();
        }

        private static BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            memory.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = memory;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            return bitmapImage;
        }
    }
}
