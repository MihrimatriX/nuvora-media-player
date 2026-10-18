using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Player.Windows.Interop;

namespace Player.Windows;

public sealed record JumpListItem(string Path, string Title);

/// <summary>The "recent" category of the taskbar / Start jump list. Each entry is a shell link that starts
/// this exe with the file as its argument, so it works whether or not Nuvora owns the file type.</summary>
[SupportedOSPlatform("windows")]
public static class JumpList
{
    /// <summary>Replaces the category with <paramref name="items"/> (newest first). Entries the user removed
    /// from the jump list are skipped: Windows rejects the whole category if one of them is re-added.</summary>
    public static void SetRecent(string category, IReadOnlyList<JumpListItem> items, string exePath)
    {
        var list = ComObjects.Create<ICustomDestinationList>(ComObjects.ClsidDestinationList);
        list.BeginList(out var slots, typeof(IObjectArray).GUID, out var removedPtr);
        try
        {
            var removed = RemovedArguments(ComObjects.Wrap<IObjectArray>(removedPtr));
            var collection = ComObjects.Create<IObjectCollection>(ComObjects.ClsidObjectCollection);
            var count = 0;
            foreach (var item in items)
            {
                if (count >= slots) break;
                var args = QuoteArgument(item.Path);
                if (removed.Contains(args)) continue;
                collection.AddObject(CreateLink(exePath, args, item.Title, item.Path));
                count++;
            }
            if (count > 0) Marshal.ThrowExceptionForHR(list.AppendCategory(category, collection));
            list.CommitList();
        }
        catch
        {
            list.AbortList();
            throw;
        }
    }

    static IShellLinkW CreateLink(string exePath, string args, string title, string tooltip)
    {
        var link = ComObjects.Create<IShellLinkW>(ComObjects.ClsidShellLink);
        link.SetPath(exePath);
        link.SetArguments(args);
        link.SetIconLocation(exePath, 0);
        link.SetDescription(tooltip.Length < 260 ? tooltip : tooltip[..259]);
        var store = (IPropertyStore)link; // QueryInterface through the COM wrapper
        var value = new PropVariant { Type = PropVariant.VtLpwstr, Pointer = Marshal.StringToCoTaskMemUni(title) };
        try
        {
            store.SetValue(PropertyKey.Title, value);
            store.Commit();
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Pointer);
        }
        return link;
    }

    static unsafe HashSet<string> RemovedArguments(IObjectArray removed)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffer = stackalloc char[1024];
        for (uint i = 0, n = removed.GetCount(); i < n; i++)
        {
            removed.GetAt(i, typeof(IShellLinkW).GUID, out var p);
            ComObjects.Wrap<IShellLinkW>(p).GetArguments(buffer, 1024);
            result.Add(new string(buffer));
        }
        return result;
    }

    /// <summary>Quotes one argument the way CommandLineToArgvW reads it back: backslashes are literal except
    /// in front of a quote, so a run of them before a quote (or the closing quote) is doubled.</summary>
    public static string QuoteArgument(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
        var sb = new StringBuilder(arg.Length + 2).Append('"');
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }
        return sb.Append('\\', slashes * 2).Append('"').ToString();
    }
}
