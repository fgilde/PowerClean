namespace Cleaner.Core.Utils;

/// <summary>
/// Wrapper um Directory.Enumerate* der UnauthorizedAccess- und PathTooLong-Exceptions
/// schluckt statt das gesamte Scan-Ergebnis zu killen.
/// </summary>
public static class SafeEnumerator
{
    public static IEnumerable<string> EnumerateFiles(string path, string pattern = "*", bool recursive = false)
    {
        var stack = new Stack<string>();
        stack.Push(path);

        while (stack.Count > 0)
        {
            string current = stack.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, pattern, SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException) { continue; }
            catch (System.Security.SecurityException) { continue; }

            foreach (var f in files)
                yield return f;

            if (!recursive) continue;

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(current);
            }
            catch (UnauthorizedAccessException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            catch (IOException) { continue; }
            catch (System.Security.SecurityException) { continue; }

            foreach (var d in subdirs)
                stack.Push(d);
        }
    }

    public static IEnumerable<string> EnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        catch (DirectoryNotFoundException) { return Array.Empty<string>(); }
        catch (IOException) { return Array.Empty<string>(); }
    }

    public static long TryGetSize(string file)
    {
        try { return new FileInfo(file).Length; }
        catch { return 0; }
    }

    public static DateTime TryGetLastWrite(string file)
    {
        try { return File.GetLastWriteTimeUtc(file); }
        catch { return DateTime.MinValue; }
    }
}
