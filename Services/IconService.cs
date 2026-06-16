using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace RCMenuManager.Services;

/// <summary>
/// Extracts icons from "&lt;path&gt;,&lt;index-or-resource-id&gt;" expressions
/// commonly found in shell verbs, returning <see cref="BitmapSource"/> ready
/// for WPF binding. Results are cached.
/// </summary>
public class IconService
{
    private readonly ConcurrentDictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public BitmapSource? Resolve(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        return _cache.GetOrAdd(expression, ResolveCore);
    }

    /// <summary>
    /// Enumerates the first <paramref name="max"/> icons stored inside a .dll
    /// or .exe and returns them as bitmaps. Index of each entry maps to the
    /// icon-resource index used by ExtractIconExW (i.e. the same index used by
    /// "shell32.dll,42" verb expressions).
    /// </summary>
    public IReadOnlyList<(int index, BitmapSource bitmap)> EnumerateIconsFromFile(string path, int max = 64)
    {
        var list = new List<(int, BitmapSource)>();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return list;
        for (var i = 0; i < max; i++)
        {
            var bmp = ExtractIcon(path, i);
            if (bmp is null) break;
            list.Add((i, bmp));
        }
        return list;
    }

    private static BitmapSource? ResolveCore(string expression)
    {
        try
        {
            var (path, index) = ParseExpression(expression);
            if (string.IsNullOrEmpty(path))
                return null;
            var resolved = ResolvePath(path);
            if (resolved is null)
                return null;
            return ExtractIcon(resolved, index);
        }
        catch
        {
            return null;
        }
    }

    private static (string path, int index) ParseExpression(string raw)
    {
        var trimmed = raw.Trim().Trim('"');
        // Strip any leading "@" used by MUI strings.
        if (trimmed.StartsWith('@'))
            trimmed = trimmed.Substring(1);

        var commaIndex = trimmed.LastIndexOf(',');
        if (commaIndex < 0)
            return (trimmed, 0);
        var pathPart = trimmed.Substring(0, commaIndex).Trim().Trim('"');
        var idPart = trimmed.Substring(commaIndex + 1).Trim();
        if (!int.TryParse(idPart, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id))
            id = 0;
        return (pathPart, id);
    }

    private static string? ResolvePath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (File.Exists(expanded))
            return expanded;
        // Try System32.
        var inSys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), expanded);
        if (File.Exists(inSys32))
            return inSys32;
        // Try absolute already.
        return null;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int ExtractIconExW(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, int nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static BitmapSource? ExtractIcon(string path, int index)
    {
        // Negative ids reference resource IDs; ExtractIconEx accepts that form natively.
        var large = new IntPtr[1];
        var small = new IntPtr[1];
        var count = ExtractIconExW(path, index, large, small, 1);
        if (count <= 0)
            return null;

        try
        {
            var handle = large[0] != IntPtr.Zero ? large[0] : small[0];
            if (handle == IntPtr.Zero)
                return null;
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
            if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
        }
    }
}
