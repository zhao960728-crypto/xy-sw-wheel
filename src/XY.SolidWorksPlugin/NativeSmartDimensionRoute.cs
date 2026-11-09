using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swcommands;

namespace XY.SolidWorksPlugin;

internal enum NativeMenuDispatchResult { NotHandled, Submitted, Rejected }

// Explicit reference API/symbol pairs qualify discovery only. Every UI ID and
// title still comes from this installed host's current-language XML resource.
internal static class NativeVerifiedMenuRoute
{
    private static readonly object CacheSync = new object();
    private static readonly Dictionary<string, IReadOnlyDictionary<int, Identity>> Resources = new Dictionary<string, IReadOnlyDictionary<int, Identity>>(StringComparer.OrdinalIgnoreCase);

    internal class Identity
    {
        internal readonly int ApiId, UiId;
        internal readonly string Symbol, Title;
        internal Identity(int apiId, string symbol, int uiId, string title) { ApiId = apiId; Symbol = symbol; UiId = uiId; Title = title; }
    }

    internal static NativeMenuDispatchResult TryExecute(ISldWorks host, int apiId, out string message)
    {
        message = "";
        if (host == null || !Marshal.IsComObject(host) || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            return NativeMenuDispatchResult.NotHandled;
        if (!NativeCommandRouteMappings.SymbolsByApi.TryGetValue(apiId, out string[] symbols) || symbols.Length != 1)
            return Unhandled(host, null, IntPtr.Zero, false, apiId, "unmapped-or-ambiguous-api", out message);

        IntPtr root = IntPtr.Zero;
        IModelDoc2 document = null;
        bool contextCaptured = false;
        Identity identity;
        NativeHostMenuRoute.Snapshot snapshot;
        try
        {
            IFrame frame = host.IFrameObject();
            root = new IntPtr(frame.GetHWndx64());
            if (!IsCurrentHostThread(root)) return Unhandled(host, null, root, false, apiId, "host-thread", out message);
            document = host.ActiveDoc as IModelDoc2;
            contextCaptured = true;
            var identities = ReadHostIdentities(host);
            if (identities == null || !identities.TryGetValue(apiId, out identity))
                return Unhandled(host, document, root, contextCaptured, apiId, "resource-unverified", out message);
            IntPtr menu = ExternalMenuCommandCatalog.ResolveMenuHandle(frame, GetMenu);
            snapshot = NativeHostMenuRoute.Capture(menu);
        }
        catch (Exception ex)
        {
            Logger.Trace("Verified native menu discovery unavailable: " + ex.Message);
            return Unhandled(host, document, root, contextCaptured, apiId, "discovery-failed", out message);
        }
        string command = NativeCommandReference.Encode(apiId);
        NativeMenuDispatchResult result = Dispatch(snapshot, identity,
            () => IsCurrentHostThread(root) && NativeMethods.IsWindowEnabled(root) && Equals(document, host.ActiveDoc as IModelDoc2),
            () => host.IsCommandEnabled(apiId),
            uiId =>
            {
                NativeCommandLaunchTrace.Mark("native-menu-dispatch", command);
                try { NativeMethods.SendMessage(root, 0x0111, new IntPtr(uiId), IntPtr.Zero); }
                finally { NativeCommandLaunchTrace.Mark("native-menu-return", command); }
            }, out message);
        if (result == NativeMenuDispatchResult.NotHandled)
        {
            var toolbar = NativeVerifiedToolbarRoute.TryExecute(root, identity,
                () => IsCurrentHostThread(root) && NativeMethods.IsWindowEnabled(root) && Equals(document, host.ActiveDoc as IModelDoc2),
                () => host.IsCommandEnabled(apiId), out message);
            if (toolbar != NativeMenuDispatchResult.NotHandled) return toolbar;
        }
        return result == NativeMenuDispatchResult.NotHandled ? Unhandled(host, document, root, contextCaptured, apiId,
            snapshot.Complete ? "menu-identity-unverified" : "menu-incomplete", out message) : result;
    }

    private static NativeMenuDispatchResult Unhandled(ISldWorks host, IModelDoc2 document, IntPtr root, bool captured,
        int apiId, string reason, out string message)
    {
        NativeMenuDispatchResult result = GuardUnverifiedContext(!captured ? (Func<bool>)null
            : () => IsCurrentHostThread(root) && Equals(document, host.ActiveDoc as IModelDoc2), out message);
        if (result == NativeMenuDispatchResult.NotHandled)
            NativeCommandLaunchTrace.Mark("native-menu-unverified:" + reason, NativeCommandReference.Encode(apiId));
        return result;
    }

    internal static NativeMenuDispatchResult GuardUnverifiedContext(Func<bool> sameContext, out string message)
    {
        message = "";
        try { if (sameContext == null || sameContext()) return NativeMenuDispatchResult.NotHandled; }
        catch (Exception ex)
        {
            message = "原生命令备用路径的活动文档无法确认，未激发：" + ex.Message;
            return NativeMenuDispatchResult.Rejected;
        }
        message = "原生命令发现菜单期间活动文档或宿主线程已变化，未改投新文档。";
        return NativeMenuDispatchResult.Rejected;
    }

    internal static NativeMenuDispatchResult Dispatch(NativeHostMenuRoute.Snapshot snapshot, Identity identity,
        Func<bool> sameContext, Func<bool> apiEnabled, Action<int> send, out string message)
    {
        message = "";
        if (identity == null || identity.UiId == identity.ApiId || snapshot == null
            || !NativeCommandRouteMappings.SymbolsByApi.TryGetValue(identity.ApiId, out string[] symbols)
            || symbols.Length != 1 || symbols[0] != identity.Symbol) return NativeMenuDispatchResult.NotHandled;
        NativeHostMenuRoute.Leaf leaf = snapshot.ResolveVerifiedNative(identity.UiId, identity.Title, identity.Symbol);
        if (leaf == null) return NativeMenuDispatchResult.NotHandled;
        bool attempted = false;
        try
        {
            if (!leaf.Enabled || !sameContext() || !apiEnabled() || !sameContext())
            {
                message = "原生命令当前未启用，或活动文档/宿主线程在调用前已变化；未重复激发。";
                return NativeMenuDispatchResult.Rejected;
            }
            attempted = true;
            send(leaf.Id);
            return NativeMenuDispatchResult.Submitted;
        }
        catch (Exception ex)
        {
            message = attempted ? "原生命令菜单调用已尝试提交，结果不确定；未重复激发：" + ex.Message
                : "原生命令菜单上下文无法确认，未激发：" + ex.Message;
            return NativeMenuDispatchResult.Rejected;
        }
    }

    private static bool IsCurrentHostThread(IntPtr root)
    {
        if (root == IntPtr.Zero) return false;
        uint thread = NativeMethods.GetWindowThreadProcessId(root, out uint process);
        return thread == NativeMethods.GetCurrentThreadId() && process == (uint)Process.GetCurrentProcess().Id;
    }

    private static IReadOnlyDictionary<int, Identity> ReadHostIdentities(ISldWorks host)
    {
        string executable = host.GetExecutablePath();
        string directory = Directory.Exists(executable) ? executable : Path.GetDirectoryName(executable);
        if (string.IsNullOrWhiteSpace(directory)) return null;
        string language = host.GetCurrentLanguage();
        if (string.IsNullOrWhiteSpace(language)) return null;
        string languageRoot = Path.GetFullPath(Path.Combine(directory, "lang")) + Path.DirectorySeparatorChar;
        string resourcePath = Path.GetFullPath(Path.Combine(languageRoot, language, "sldresu.dll"));
        if (!resourcePath.StartsWith(languageRoot, StringComparison.OrdinalIgnoreCase)) return null;
        var file = new FileInfo(resourcePath);
        if (!file.Exists) return null;
        string key = host.RevisionNumber() + "|" + language + "|" + resourcePath + "|" + file.LastWriteTimeUtc.Ticks + "|" + file.Length;
        lock (CacheSync) if (Resources.TryGetValue(key, out IReadOnlyDictionary<int, Identity> cached)) return cached;
        IntPtr module = LoadLibraryEx(resourcePath, IntPtr.Zero, 2); // data file, no DLL initialization
        if (module == IntPtr.Zero) return null;
        IReadOnlyDictionary<int, Identity> identities;
        try
        {
            IntPtr resource = FindResource(module, new IntPtr(8312), "XML");
            uint size = resource == IntPtr.Zero ? 0 : SizeofResource(module, resource);
            if (size == 0 || size > 8 * 1024 * 1024) return null;
            IntPtr data = LockResource(LoadResource(module, resource));
            if (data == IntPtr.Zero) return null;
            var bytes = new byte[size]; Marshal.Copy(data, bytes, 0, bytes.Length);
            using var stream = new MemoryStream(bytes, writable: false);
            identities = ReadIdentities(stream);
        }
        finally { FreeLibrary(module); }
        if (identities != null && identities.Count > 0)
        {
            lock (CacheSync)
            {
                if (Resources.Count >= 16) Resources.Clear();
                Resources[key] = identities;
            }
        }
        return identities;
    }

    internal static IReadOnlyDictionary<int, Identity> ReadIdentities(Stream stream)
    {
        var document = new XmlDocument { XmlResolver = null };
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 })) document.Load(reader);
        if (document.DocumentElement?.Name != "tooltips") return null;
        var nodes = document.SelectNodes("//toolbar/tooltip").Cast<XmlElement>().Select(node => new
        { Symbol = node.GetAttribute("name"), Id = int.TryParse(node.GetAttribute("id"), out int id) ? id : -1,
            Title = (node["title"]?.InnerText ?? "").Trim() }).ToArray();
        var bySymbol = nodes.GroupBy(node => node.Symbol, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var ambiguousIds = new HashSet<int>(nodes.GroupBy(node => node.Id)
            .Where(group => group.Select(node => node.Symbol).Distinct(StringComparer.Ordinal).Count() != 1).Select(group => group.Key));
        var result = new Dictionary<int, Identity>();
        foreach (var mapping in NativeCommandRouteMappings.SymbolsByApi)
        {
            // Multiple explicit symbols for one API identity have no implicit
            // preference. Retain RunCommand until that ambiguity is adapted.
            if (mapping.Value.Length != 1 || !bySymbol.TryGetValue(mapping.Value[0], out var targets)) continue;
            if (targets.Any(target => target.Id <= 0 || target.Id > ushort.MaxValue || target.Id == mapping.Key || target.Title.Length == 0)
                || targets.Select(target => target.Id).Distinct().Count() != 1
                || targets.Select(target => target.Title).Distinct(StringComparer.Ordinal).Count() != 1) continue;
            int uiId = targets[0].Id;
            if (ambiguousIds.Contains(uiId)) continue;
            result[mapping.Key] = new Identity(mapping.Key, mapping.Value[0], uiId, targets[0].Title);
        }
        return result;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr window);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW")] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindResourceW")] private static extern IntPtr FindResource(IntPtr module, IntPtr name, string type);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
}

// Compatibility seam for the focused Smart Dimension regression tests/tools.
// Production dispatch uses NativeVerifiedMenuRoute for every eligible API ID.
internal static class NativeSmartDimensionRoute
{
    internal sealed class Identity : NativeVerifiedMenuRoute.Identity
    {
        internal Identity(int uiId, string title) : base(38, "ID_INSERT_DIMENSION", uiId, title) { }
    }
    internal static Identity ReadIdentity(Stream stream)
    {
        var index = NativeVerifiedMenuRoute.ReadIdentities(stream);
        return index != null && index.TryGetValue(38, out var identity) ? new Identity(identity.UiId, identity.Title) : null;
    }
    internal static NativeMenuDispatchResult TryExecute(ISldWorks host, int apiId, out string message)
    {
        message = "";
        return apiId == 38 ? NativeVerifiedMenuRoute.TryExecute(host, apiId, out message) : NativeMenuDispatchResult.NotHandled;
    }
    internal static NativeMenuDispatchResult GuardUnverifiedContext(Func<bool> context, out string message)
        => NativeVerifiedMenuRoute.GuardUnverifiedContext(context, out message);
    internal static NativeMenuDispatchResult Dispatch(NativeHostMenuRoute.Snapshot snapshot, Identity identity,
        Func<bool> sameContext, Func<bool> enabled, Action<int> send, out string message)
        => NativeVerifiedMenuRoute.Dispatch(snapshot, identity, sameContext, enabled, send, out message);
}
