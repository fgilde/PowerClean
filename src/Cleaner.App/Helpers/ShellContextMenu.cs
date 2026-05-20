using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Cleaner.App.Helpers;

/// <summary>
/// Öffnet das native Windows-Shell-Kontextmenü für eine Datei oder einen Ordner.
/// Nutzt IContextMenu/IContextMenu2 via COM — bekommt also die echten Explorer-Einträge
/// inkl. Shell-Extensions (z.B. "In Editor öffnen", "TortoiseGit", etc.).
/// </summary>
public static class ShellContextMenu
{
    public static void ShowFor(Window owner, string path, bool extendedVerbs)
    {
        if (owner is null || string.IsNullOrWhiteSpace(path))
        {
            Cleaner.App.App.LogInfo("ShellContextMenu: null owner or empty path — abort");
            return;
        }

        if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
        {
            Cleaner.App.App.LogInfo($"ShellContextMenu: path does not exist — {path}");
            return;
        }

        // Defer auf den nächsten Dispatcher-Cycle, damit unser eigenes WPF-Kontextmenü
        // garantiert geschlossen ist und der Focus stimmt — sonst kann TrackPopupMenuEx
        // mit überlappenden Menüs in schief gehen.
        owner.Dispatcher.BeginInvoke(new Action(() => ShowInternal(owner, path, extendedVerbs)),
            DispatcherPriority.Background);
    }

    private static void ShowInternal(Window owner, string path, bool extendedVerbs)
    {
        Cleaner.App.App.LogInfo($"ShellContextMenu.ShowInternal: {path} (extended={extendedVerbs})");

        IntPtr pidl = IntPtr.Zero;
        IntPtr pmenu = IntPtr.Zero;
        IShellFolder? parent = null;
        IContextMenu? ctxMenu = null;
        IContextMenu2? ctxMenu2 = null;
        IntPtr hMenu = IntPtr.Zero;
        HwndSource? source = null;
        HwndSourceHook? hook = null;

        try
        {
            // 1. Pfad → PIDL via SHCreateItemFromParsingName (Vista+ API, robuster als SHParseDisplayName)
            Cleaner.App.App.LogInfo("Step 1: SHCreateItemFromParsingName");
            object? shellItemObj = null;
            try
            {
                int hr0 = SHCreateItemFromParsingName(path, IntPtr.Zero, IID_IShellItem, out shellItemObj);
                Cleaner.App.App.LogInfo($"  hr=0x{hr0:X8}");
                if (hr0 != 0 || shellItemObj is null) return;
            }
            catch (Exception ex)
            {
                Cleaner.App.App.LogException("SHCreateItemFromParsingName", ex);
                return;
            }

            // PIDL aus IShellItem holen via SHGetIDListFromObject
            Cleaner.App.App.LogInfo("Step 1b: SHGetIDListFromObject");
            IntPtr pUnk = IntPtr.Zero;
            try
            {
                pUnk = Marshal.GetIUnknownForObject(shellItemObj);
                int hr1 = SHGetIDListFromObject(pUnk, out pidl);
                Cleaner.App.App.LogInfo($"  hr=0x{hr1:X8}, pidl=0x{pidl.ToInt64():X}");
            }
            catch (Exception ex)
            {
                Cleaner.App.App.LogException("SHGetIDListFromObject", ex);
                return;
            }
            finally
            {
                if (pUnk != IntPtr.Zero) Marshal.Release(pUnk);
                if (shellItemObj is not null) Marshal.ReleaseComObject(shellItemObj);
            }

            if (pidl == IntPtr.Zero) { Cleaner.App.App.LogInfo("ShellContextMenu: null PIDL"); return; }

            // 2. Parent + child-PIDL
            Cleaner.App.App.LogInfo("Step 2: SHBindToParent");
            int hr = SHBindToParent(pidl, IID_IShellFolder, out var parentObj, out var childPidl);
            Cleaner.App.App.LogInfo($"  hr=0x{hr:X8}, parent={parentObj is not null}");
            if (hr != 0 || parentObj is null)
            {
                Cleaner.App.App.LogInfo($"ShellContextMenu: SHBindToParent failed hr=0x{hr:X8}");
                return;
            }
            parent = parentObj;

            // 3. IContextMenu für das Kind
            Cleaner.App.App.LogInfo("Step 3: GetUIObjectOf");
            hr = parent.GetUIObjectOf(IntPtr.Zero, 1, new[] { childPidl }, IID_IContextMenu, IntPtr.Zero, out pmenu);
            Cleaner.App.App.LogInfo($"  hr=0x{hr:X8}, pmenu=0x{pmenu.ToInt64():X}");
            if (hr != 0 || pmenu == IntPtr.Zero) return;

            Cleaner.App.App.LogInfo("Step 4: GetObjectForIUnknown");
            ctxMenu = (IContextMenu)Marshal.GetObjectForIUnknown(pmenu);
            // ein Ref für das RCW, einer im pmenu — den raw-Ref geben wir frei
            Marshal.Release(pmenu);
            pmenu = IntPtr.Zero;
            Cleaner.App.App.LogInfo($"  ctxMenu OK = {ctxMenu is not null}");

            ctxMenu2 = ctxMenu as IContextMenu2;
            Cleaner.App.App.LogInfo($"ShellContextMenu: ctxMenu2 supported = {ctxMenu2 is not null}");

            // 4. HMENU bauen
            hMenu = CreatePopupMenu();
            if (hMenu == IntPtr.Zero) { Cleaner.App.App.LogInfo("CreatePopupMenu failed"); return; }

            uint flags = CMF_NORMAL | CMF_CANRENAME;
            if (extendedVerbs) flags |= CMF_EXTENDEDVERBS;
            hr = ctxMenu.QueryContextMenu(hMenu, 0, IdCmdFirst, IdCmdLast, flags);
            // QueryContextMenu returnt eine HRESULT mit dem höchsten ID-Wert+1 als SCODE
            // Negative HRESULTs sind Fehler
            if (hr < 0) { Cleaner.App.App.LogInfo($"QueryContextMenu failed hr=0x{hr:X8}"); return; }

            // 5. WndProc-Hook für IContextMenu2-Submenüs
            var hwnd = new WindowInteropHelper(owner).Handle;
            if (hwnd == IntPtr.Zero) { Cleaner.App.App.LogInfo("Owner HWND is null"); return; }

            source = HwndSource.FromHwnd(hwnd);
            if (source is not null && ctxMenu2 is not null)
            {
                hook = (IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg == WM_INITMENUPOPUP || msg == WM_DRAWITEM ||
                        msg == WM_MEASUREITEM || msg == WM_MENUCHAR)
                    {
                        try { ctxMenu2.HandleMenuMsg((uint)msg, w, l); handled = true; }
                        catch (Exception ex) { Cleaner.App.App.LogException("HandleMenuMsg", ex); }
                    }
                    return IntPtr.Zero;
                };
                source.AddHook(hook);
            }

            // 6. Menü anzeigen
            GetCursorPos(out var pt);
            Cleaner.App.App.LogInfo($"TrackPopupMenuEx at ({pt.X},{pt.Y})");
            int cmd = TrackPopupMenuEx(
                hMenu,
                TPM_RETURNCMD | TPM_RIGHTBUTTON,
                pt.X, pt.Y, hwnd, IntPtr.Zero);
            Cleaner.App.App.LogInfo($"TrackPopupMenuEx returned {cmd}");

            if (cmd > 0)
            {
                // 7. Befehl ausführen
                var ici = new CMINVOKECOMMANDINFO
                {
                    cbSize = (uint)Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                    hwnd = hwnd,
                    lpVerb = (IntPtr)(cmd - IdCmdFirst),
                    nShow = SW_SHOWNORMAL,
                };
                try
                {
                    hr = ctxMenu.InvokeCommand(ref ici);
                    Cleaner.App.App.LogInfo($"InvokeCommand hr=0x{hr:X8}");
                }
                catch (Exception ex)
                {
                    Cleaner.App.App.LogException("ShellContextMenu.InvokeCommand", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Cleaner.App.App.LogException("ShellContextMenu.ShowInternal", ex);
        }
        finally
        {
            try { if (source is not null && hook is not null) source.RemoveHook(hook); } catch { }
            try { if (hMenu != IntPtr.Zero) DestroyMenu(hMenu); } catch { }
            try
            {
                if (ctxMenu is not null) Marshal.ReleaseComObject(ctxMenu);
            }
            catch { }
            try { if (parent is not null) Marshal.ReleaseComObject(parent); } catch { }
            try { if (pmenu != IntPtr.Zero) Marshal.Release(pmenu); } catch { }
            try { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); } catch { }
            Cleaner.App.App.LogInfo("ShellContextMenu cleanup done");
        }
    }

    // ---- Win32 / COM ----

    const uint CMF_NORMAL = 0x00000000;
    const uint CMF_EXTENDEDVERBS = 0x00000100;
    const uint CMF_CANRENAME = 0x00000002;

    const uint TPM_RETURNCMD = 0x0100;
    const uint TPM_RIGHTBUTTON = 0x0002;

    const int SW_SHOWNORMAL = 1;

    const uint IdCmdFirst = 1;
    const uint IdCmdLast = 0x7FFF;

    const int WM_INITMENUPOPUP = 0x0117;
    const int WM_DRAWITEM = 0x002B;
    const int WM_MEASUREITEM = 0x002C;
    const int WM_MENUCHAR = 0x0120;

    static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    static readonly Guid IID_IContextMenu = new("000214e4-0000-0000-c000-000000000046");
    static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? ppv);

    [DllImport("shell32.dll", PreserveSig = true)]
    static extern int SHGetIDListFromObject(IntPtr punk, out IntPtr ppidl);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    struct CMINVOKECOMMANDINFO
    {
        public uint cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    static extern int SHBindToParent(
        IntPtr pidl,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IShellFolder ppv,
        out IntPtr ppidlLast);

    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr ptr);

    [Guid("000214E6-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComImport]
    interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, [In] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [In] IntPtr[] apidl, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [Guid("000214e4-0000-0000-c000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComImport]
    interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [Guid("000214f4-0000-0000-c000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown), ComImport]
    interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }
}
