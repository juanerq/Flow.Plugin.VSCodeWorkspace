using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace Flow.Plugin.VSCodeWorkspaces.VSCodeHelper
{
    internal static class SvgIconHelper
    {
        private const double IconSize = 64;

        private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static ImageSource GetIcon(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return null;

            if (Cache.TryGetValue(relativePath, out var cachedIcon))
                return cachedIcon;

            var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (pluginDirectory == null)
                return null;

            var svgPath = Path.Combine(pluginDirectory, relativePath);
            if (!File.Exists(svgPath))
                return null;

            if (relativePath.Contains("node", StringComparison.OrdinalIgnoreCase))
                return CreateNodeIcon(relativePath);

            var document = XDocument.Load(svgPath);
            var svg = document.Root;
            if (svg == null)
                return null;

            var viewBox = ParseViewBox(svg.Attribute("viewBox")?.Value);
            var drawingGroup = new DrawingGroup
            {
                Transform = GetViewBoxTransform(viewBox)
            };

            var paths = svg.Descendants().Where(element => element.Name.LocalName == "path").ToArray();
            for (var i = 0; i < paths.Length; i++)
            {
                var data = paths[i].Attribute("d")?.Value;
                if (string.IsNullOrEmpty(data))
                    continue;

                var fill = GetFill(paths[i], relativePath, i);
                var geometry = Geometry.Parse(data);
                var transform = ParseTransform(paths[i].Attribute("transform")?.Value);
                if (transform != null)
                    geometry.Transform = transform;

                drawingGroup.Children.Add(new GeometryDrawing(fill, null, geometry));
            }

            var image = new DrawingImage(drawingGroup);
            image.Freeze();

            Cache[relativePath] = image;
            return image;
        }

        private static ImageSource CreateNodeIcon(string relativePath)
        {
            var drawingGroup = new DrawingGroup();

            drawingGroup.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(0x53, 0xA0, 0x48)),
                null,
                Geometry.Parse("M32 4 L56 18 L56 46 L32 60 L8 46 L8 18 Z")));

            drawingGroup.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(0x6C, 0xB8, 0x5A)),
                null,
                Geometry.Parse("M32 4 L56 18 L32 32 L8 18 Z")));

            drawingGroup.Children.Add(new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(0x3E, 0x86, 0x3D)),
                null,
                Geometry.Parse("M32 32 L56 46 L32 60 Z")));

            var image = new DrawingImage(drawingGroup);
            image.Freeze();

            Cache[relativePath] = image;
            return image;
        }

        private static Rect ParseViewBox(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new Rect(0, 0, IconSize, IconSize);

            var parts = value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
                .ToArray();

            return parts.Length == 4
                ? new Rect(parts[0], parts[1], parts[2], parts[3])
                : new Rect(0, 0, IconSize, IconSize);
        }

        private static Transform GetViewBoxTransform(Rect viewBox)
        {
            var scale = Math.Min(IconSize / viewBox.Width, IconSize / viewBox.Height) * 0.86;
            var offsetX = (IconSize - viewBox.Width * scale) / 2 - viewBox.X * scale;
            var offsetY = (IconSize - viewBox.Height * scale) / 2 - viewBox.Y * scale;

            var transforms = new TransformGroup();
            transforms.Children.Add(new ScaleTransform(scale, scale));
            transforms.Children.Add(new TranslateTransform(offsetX, offsetY));
            return transforms;
        }

        private static Brush GetFill(XElement path, string relativePath, int index)
        {
            var fill = path.Attribute("fill")?.Value ?? ReadStyleFill(path.Attribute("style")?.Value);

            if (!string.IsNullOrEmpty(fill) && fill.StartsWith("#", StringComparison.Ordinal))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill));

            if (relativePath.Contains("python", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(index == 0 ? Color.FromRgb(0x32, 0x7E, 0xBD) : Color.FromRgb(0xFF, 0xDA, 0x4B));

            if (relativePath.Contains("go-", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x00, 0xAC, 0xD7));

            if (relativePath.Contains("node", StringComparison.OrdinalIgnoreCase))
                return new SolidColorBrush(Color.FromRgb(0x53, 0xA0, 0x48));

            return new SolidColorBrush(Color.FromRgb(0x17, 0x93, 0xD1));
        }

        private static string ReadStyleFill(string style)
        {
            if (string.IsNullOrEmpty(style))
                return null;

            return style.Split(';')
                .Select(part => part.Split(':'))
                .Where(parts => parts.Length == 2)
                .FirstOrDefault(parts => string.Equals(parts[0].Trim(), "fill", StringComparison.OrdinalIgnoreCase))?[1]
                .Trim();
        }

        private static Transform ParseTransform(string transform)
        {
            if (string.IsNullOrEmpty(transform))
                return null;

            var transforms = new TransformGroup();
            foreach (var part in transform.Split(')', StringSplitOptions.RemoveEmptyEntries))
            {
                var item = part.Trim();
                var start = item.IndexOf('(');
                if (start < 0)
                    continue;

                var name = item.Substring(0, start);
                var values = item.Substring(start + 1)
                    .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
                    .ToArray();

                if (name == "translate" && values.Length >= 1)
                    transforms.Children.Add(new TranslateTransform(values[0], values.Length > 1 ? values[1] : 0));
                else if (name == "scale" && values.Length >= 1)
                    transforms.Children.Add(new ScaleTransform(values[0], values.Length > 1 ? values[1] : values[0]));
            }

            return transforms.Children.Count > 0 ? transforms : null;
        }
    }
}
