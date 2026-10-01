using System.Runtime.InteropServices;

namespace EasyChat.Infrastructure.MacOS.Native;

/// <summary>Uniform type identifiers EasyChat puts on, or reads from, the pasteboard.</summary>
internal static class PasteboardType
{
    internal const string Utf8Text = "public.utf8-plain-text";
    internal const string Png = "public.png";
}

/// <summary>
/// <c>NSPasteboard</c> operations on the general pasteboard. Every call must run inside an
/// <see cref="AutoreleasePool"/> opened by the caller, because the results are autoreleased.
/// </summary>
internal static class PasteboardNative
{
    private const string LibraryPath = "/System/Library/Frameworks/AppKit.framework/AppKit";

    private static readonly Lazy<IntPtr> General = new(
        LoadGeneralPasteboard,
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The pasteboard's <c>changeCount</c>, which macOS increments on every write by any process.
    /// This is the change token EasyChat compares before restoring a snapshot.
    /// </summary>
    internal static long ChangeCount() =>
        ObjectiveCNative.SendReturningNInt(
            General.Value,
            ObjectiveCNative.GetSelector("changeCount"));

    internal static void Clear() =>
        ObjectiveCNative.Send(General.Value, ObjectiveCNative.GetSelector("clearContents"));

    internal static string? ReadText()
    {
        var type = FoundationNative.CreateString(PasteboardType.Utf8Text);
        return FoundationNative.ReadString(ObjectiveCNative.SendReturningHandle(
            General.Value,
            ObjectiveCNative.GetSelector("stringForType:"),
            type));
    }

    internal static bool WriteData(ReadOnlySpan<byte> bytes, string type)
    {
        Clear();
        return ObjectiveCNative.SendReturningBool(
            General.Value,
            ObjectiveCNative.GetSelector("setData:forType:"),
            FoundationNative.CreateData(bytes),
            FoundationNative.CreateString(type));
    }

    internal static bool WriteText(string text)
    {
        Clear();
        return ObjectiveCNative.SendReturningBool(
            General.Value,
            ObjectiveCNative.GetSelector("setString:forType:"),
            FoundationNative.CreateString(text),
            FoundationNative.CreateString(PasteboardType.Utf8Text));
    }

    /// <summary>
    /// Copies every readable representation of every pasteboard item. Types whose data a lazy
    /// provider declines to supply answer null and are reported to the caller instead of being
    /// dropped silently.
    /// </summary>
    internal static IReadOnlyList<PasteboardItemData> ReadItems(out IReadOnlyList<string> unreadable)
    {
        var missing = new List<string>();
        var items = new List<PasteboardItemData>();
        var handles = ObjectiveCNative.SendReturningHandle(
            General.Value,
            ObjectiveCNative.GetSelector("pasteboardItems"));

        for (nint index = 0; index < FoundationNative.CountOf(handles); index++)
        {
            var item = FoundationNative.ItemAt(handles, index);
            var types = ObjectiveCNative.SendReturningHandle(
                item,
                ObjectiveCNative.GetSelector("types"));
            var representations = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            for (nint typeIndex = 0; typeIndex < FoundationNative.CountOf(types); typeIndex++)
            {
                var type = FoundationNative.ItemAt(types, typeIndex);
                var name = FoundationNative.ReadString(type);
                if (name is null)
                    continue;

                var data = FoundationNative.ReadData(ObjectiveCNative.SendReturningHandle(
                    item,
                    ObjectiveCNative.GetSelector("dataForType:"),
                    type));
                if (data is null)
                    missing.Add(name);
                else
                    representations[name] = data;
            }

            if (representations.Count > 0)
                items.Add(new PasteboardItemData(representations));
        }

        unreadable = missing;
        return items;
    }

    /// <summary>Replaces the pasteboard contents with previously captured items.</summary>
    internal static bool WriteItems(IReadOnlyList<PasteboardItemData> items)
    {
        Clear();
        if (items.Count == 0)
            return true;

        var objects = FoundationNative.CreateMutableArray();
        foreach (var item in items)
        {
            var handle = ObjectiveCNative.SendReturningHandle(
                ObjectiveCNative.SendReturningHandle(
                    ObjectiveCNative.GetClass("NSPasteboardItem"),
                    ObjectiveCNative.GetSelector("alloc")),
                ObjectiveCNative.GetSelector("init"));

            foreach (var (type, data) in item.Representations)
            {
                ObjectiveCNative.SendReturningBool(
                    handle,
                    ObjectiveCNative.GetSelector("setData:forType:"),
                    FoundationNative.CreateData(data),
                    FoundationNative.CreateString(type));
            }

            FoundationNative.Add(objects, handle);
            ObjectiveCNative.Send(handle, ObjectiveCNative.GetSelector("autorelease"));
        }

        return ObjectiveCNative.SendReturningBool(
            General.Value,
            ObjectiveCNative.GetSelector("writeObjects:"),
            objects);
    }

    private static IntPtr LoadGeneralPasteboard()
    {
        NativeLibrary.Load(LibraryPath);
        using var pool = AutoreleasePool.Push();
        var pasteboard = ObjectiveCNative.SendReturningHandle(
            ObjectiveCNative.GetClass("NSPasteboard"),
            ObjectiveCNative.GetSelector("generalPasteboard"));
        if (pasteboard == IntPtr.Zero)
            throw new PlatformNotSupportedException("The general pasteboard is unavailable.");

        // generalPasteboard is autoreleased, so the shared handle is retained for the process.
        return ObjectiveCNative.SendReturningHandle(
            pasteboard,
            ObjectiveCNative.GetSelector("retain"));
    }
}

/// <summary>One pasteboard item, as a copy of every representation macOS was able to hand over.</summary>
internal sealed record PasteboardItemData(IReadOnlyDictionary<string, byte[]> Representations);
