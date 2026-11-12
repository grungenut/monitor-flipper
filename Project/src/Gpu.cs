// Monitor Flipper — GPU mirror engine.
//
// Pipeline (all on the graphics card):
//   Windows Desktop Duplication hands us each new frame of the source monitor as a GPU texture
//   -> we copy it into our own texture
//   -> draw it onto the output window's swap chain as one textured rectangle; flipping is just
//      mirroring that rectangle's corners, so it costs nothing extra
//   -> draw the mouse pointer on top (Duplication leaves it out), flipped with the picture
//   -> Present, synced to the output monitor's refresh.
// Nothing is redrawn when nothing changed, so an idle screen costs almost nothing.
//
// Direct3D 11 / DXGI are called through raw COM vtables (Com.F) so the program builds with the
// C# 5 compiler that ships in Windows and needs no extra libraries. Vtable slot numbers are
// from the Windows SDK headers (d3d11.h, dxgi.h, dxgi1_2.h); comments name each one.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MonitorFlipper
{
    static class Native
    {
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
        public const int CURSOR_SHOWING = 1;
        public const int DI_NORMAL = 3;
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
        public const int BLACKNESS = 0x00000042, WHITENESS = 0x00FF0062;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT pt; }
        [StructLayout(LayoutKind.Sequential)] public struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
        [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
        [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int w, int h, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool PatBlt(IntPtr hdc, int x, int y, int w, int h, int rop);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        [DllImport("dxgi.dll")] public static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
        [DllImport("d3d11.dll")]
        public static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags,
            IntPtr featureLevels, uint numLevels, uint sdkVersion, out IntPtr device, out int level, out IntPtr context);
        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
        public static extern int D3DCompile(byte[] src, IntPtr len, string name, IntPtr defines, IntPtr include,
            string entry, string target, uint flags1, uint flags2, out IntPtr code, out IntPtr errors);
    }

    // ---- D3D11 / DXGI structs -------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)] struct RECT32 { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OUTPUT_DESC { [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; public RECT32 Desktop; public int Attached; public int Rotation; public IntPtr Monitor; }
    [StructLayout(LayoutKind.Sequential)]
    struct FRAME_INFO { public long LastPresentTime, LastMouseUpdateTime; public uint AccumulatedFrames; public int RectsCoalesced, ProtectedMasked, PtrX, PtrY, PtrVisible; public uint TotalMeta, PtrShapeSize; }
    [StructLayout(LayoutKind.Sequential)]
    struct TEX2D_DESC { public uint Width, Height, MipLevels, ArraySize; public int Format; public uint SampleCount, SampleQuality; public int Usage; public uint Bind, CpuAccess, Misc; }
    [StructLayout(LayoutKind.Sequential)]
    struct BUFFER_DESC { public uint ByteWidth; public int Usage; public uint Bind, CpuAccess, Misc, Stride; }
    [StructLayout(LayoutKind.Sequential)]
    struct RAST_DESC { public int Fill, Cull, FrontCCW, DepthBias; public float BiasClamp, SlopeBias; public int DepthClip, Scissor, Msaa, LineAA; }
    [StructLayout(LayoutKind.Sequential)]
    struct SAMPLER_DESC { public int Filter, U, V, W; public float MipBias; public uint MaxAniso; public int Compare; public float B0, B1, B2, B3, MinLod, MaxLod; }
    [StructLayout(LayoutKind.Sequential)]
    struct RT_BLEND { public int Enable, Src, Dst, Op, SrcA, DstA, OpA; public byte Mask; byte p1, p2, p3; }
    [StructLayout(LayoutKind.Sequential)]
    struct BLEND_DESC { public int AlphaToCoverage, Independent; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public RT_BLEND[] RT; }
    [StructLayout(LayoutKind.Sequential)]
    struct VIEWPORT { public float X, Y, W, H, MinDepth, MaxDepth; }
    [StructLayout(LayoutKind.Sequential)]
    struct SWAP_DESC
    {
        public uint Width, Height, RefreshNum, RefreshDen; public int Format, ScanlineOrder, Scaling;
        public uint SampleCount, SampleQuality, Usage, BufferCount; public IntPtr Window; public int Windowed, SwapEffect; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] struct MAPPED { public IntPtr Data; public uint RowPitch, DepthPitch; }

    // ---- COM vtable calling ---------------------------------------------------------------
    static class D
    {
        public delegate int QI(IntPtr self, ref Guid iid, out IntPtr obj);
        public delegate uint Release(IntPtr self);
        public delegate int GetPtrIdx(IntPtr self, uint index, out IntPtr obj);
        public delegate int GetParent(IntPtr self, ref Guid iid, out IntPtr obj);
        public delegate int OutputGetDesc(IntPtr self, out OUTPUT_DESC desc);
        public delegate int DuplicateOutput(IntPtr self, IntPtr device, out IntPtr dup);
        public delegate int AcquireNextFrame(IntPtr self, uint timeoutMs, out FRAME_INFO info, out IntPtr resource);
        public delegate int NoArgs(IntPtr self);
        public delegate int NoArgsOut(IntPtr self, out IntPtr obj);
        public delegate void TexGetDesc(IntPtr self, out TEX2D_DESC desc);
        public delegate int CreateTexture2D(IntPtr self, ref TEX2D_DESC desc, IntPtr init, out IntPtr tex);
        public delegate int CreateView(IntPtr self, IntPtr resource, IntPtr desc, out IntPtr view);
        public delegate int CreateShader(IntPtr self, IntPtr code, IntPtr len, IntPtr linkage, out IntPtr shader);
        public delegate int CreateBuffer(IntPtr self, ref BUFFER_DESC desc, IntPtr init, out IntPtr buf);
        public delegate int CreateBlend(IntPtr self, ref BLEND_DESC desc, out IntPtr state);
        public delegate int CreateRast(IntPtr self, ref RAST_DESC desc, out IntPtr state);
        public delegate int CreateSampler(IntPtr self, ref SAMPLER_DESC desc, out IntPtr state);
        public delegate void SetArray(IntPtr self, uint start, uint count, IntPtr[] items);
        public delegate void SetShader(IntPtr self, IntPtr shader, IntPtr classes, uint numClasses);
        public delegate void Draw(IntPtr self, uint count, uint start);
        public delegate int Map(IntPtr self, IntPtr res, uint sub, int type, uint flags, out MAPPED mapped);
        public delegate void Unmap(IntPtr self, IntPtr res, uint sub);
        public delegate void SetInt(IntPtr self, int value);
        public delegate void SetPtr(IntPtr self, IntPtr value);
        public delegate void SetRenderTargets(IntPtr self, uint count, IntPtr[] rtvs, IntPtr dsv);
        public delegate void SetBlend(IntPtr self, IntPtr state, float[] factor, uint mask);
        public delegate void SetViewports(IntPtr self, uint count, ref VIEWPORT vp);
        public delegate void CopyResource(IntPtr self, IntPtr dst, IntPtr src);
        public delegate void UpdateSubresource(IntPtr self, IntPtr res, uint sub, IntPtr box, IntPtr data, uint rowPitch, uint depthPitch);
        public delegate void ClearRtv(IntPtr self, IntPtr rtv, float[] color);
        public delegate int CreateSwapChain(IntPtr self, IntPtr device, ref SWAP_DESC desc, out IntPtr swap);
        public delegate int MakeWindowAssociation(IntPtr self, IntPtr hwnd, uint flags);
        public delegate int Present(IntPtr self, uint sync, uint flags);
        public delegate int GetBuffer(IntPtr self, uint index, ref Guid iid, out IntPtr surface);
        public delegate int ResizeBuffers(IntPtr self, uint count, uint w, uint h, int format, uint flags);
        public delegate IntPtr BlobPtr(IntPtr self);
    }

    class GpuException : Exception
    {
        public readonly int Code;
        public GpuException(string what, int hr) : base(string.Format("{0} failed (0x{1:X8})", what, hr)) { Code = hr; }
    }

    static class Com
    {
        static readonly Dictionary<KeyValuePair<IntPtr, Type>, Delegate> cache = new Dictionary<KeyValuePair<IntPtr, Type>, Delegate>();

        public static T F<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size);
            KeyValuePair<IntPtr, Type> key = new KeyValuePair<IntPtr, Type>(fn, typeof(T));
            lock (cache)
            {
                Delegate d;
                if (!cache.TryGetValue(key, out d)) { d = Marshal.GetDelegateForFunctionPointer(fn, typeof(T)); cache[key] = d; }
                return (T)(object)d;
            }
        }

        public static void Check(int hr, string what) { if (hr < 0) throw new GpuException(what, hr); }

        public static void Release(ref IntPtr p)
        {
            if (p != IntPtr.Zero) { F<D.Release>(p, 2)(p); p = IntPtr.Zero; }
        }

        public static IntPtr QI(IntPtr p, Guid iid, string what)
        {
            IntPtr r; Check(F<D.QI>(p, 0)(p, ref iid, out r), what); return r;
        }
    }

    static class IID
    {
        public static readonly Guid DXGIFactory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        public static readonly Guid DXGIOutput1 = new Guid("00cddea8-939b-4b83-a340-a685226666cc");
        public static readonly Guid D3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    }
}
