// Monitor Flipper — capture a single program window (UltraMon's "mirror one application").
//
// Uses Windows Graphics Capture, the system's own window-capture service (Windows 10 1903+).
// It keeps working when the window is covered by other windows or moved between monitors, and
// includes the mouse pointer. Frames arrive as GPU textures on the same Direct3D device the
// mirror engine draws with, so nothing touches the CPU.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace MonitorFlipper
{
    class WindowCapture : IDisposable
    {
        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow(IntPtr hwnd, ref Guid iid);
            IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid);
        }

        [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
        [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string s, int len, out IntPtr hstring);
        [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hstring);
        [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr inspectable);

        static readonly Guid IID_IDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        static readonly Guid IID_GraphicsCaptureItem = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        static readonly Guid IID_DxgiInterfaceAccess = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
        const DirectXPixelFormat Format = DirectXPixelFormat.B8G8R8A8UIntNormalized;

        readonly IDirect3DDevice device;
        readonly GraphicsCaptureItem item;
        readonly Direct3D11CaptureFramePool pool;
        readonly GraphicsCaptureSession session;
        readonly AutoResetEvent arrived = new AutoResetEvent(false);
        Direct3D11CaptureFrame current;
        SizeInt32 poolSize;
        bool recreate;
        public volatile bool Closed;

        public static bool Supported
        {
            get { try { return GraphicsCaptureSession.IsSupported(); } catch { return false; } }
        }

        public WindowCapture(IntPtr d3dDevice, IntPtr hwnd)
        {
            IntPtr dxgi = Com.QI(d3dDevice, IID_IDXGIDevice, "IDXGIDevice"), insp;
            try { Com.Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out insp), "CreateDirect3D11DeviceFromDXGIDevice"); }
            finally { Com.Release(ref dxgi); }
            device = (IDirect3DDevice)Marshal.GetObjectForIUnknown(insp);
            Marshal.Release(insp);

            item = CreateItem(hwnd);
            item.Closed += delegate { Closed = true; arrived.Set(); };
            poolSize = item.Size;
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, Format, 2, poolSize);
            pool.FrameArrived += delegate { arrived.Set(); };
            session = pool.CreateCaptureSession(item);
            try { session.IsCursorCaptureEnabled = true; } catch { }
            try { session.IsBorderRequired = false; } catch { }   // hide the yellow capture border (Windows 11)
            session.StartCapture();
        }

        static GraphicsCaptureItem CreateItem(IntPtr hwnd)
        {
            const string cls = "Windows.Graphics.Capture.GraphicsCaptureItem";
            IntPtr hs, factory;
            Com.Check(WindowsCreateString(cls, cls.Length, out hs), "WindowsCreateString");
            try
            {
                Guid iid = typeof(IGraphicsCaptureItemInterop).GUID;
                Com.Check(RoGetActivationFactory(hs, ref iid, out factory), "RoGetActivationFactory");
            }
            finally { WindowsDeleteString(hs); }
            IGraphicsCaptureItemInterop interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            Marshal.Release(factory);
            Guid itemIid = IID_GraphicsCaptureItem;
            IntPtr p = interop.CreateForWindow(hwnd, ref itemIid);
            GraphicsCaptureItem result = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(p);
            Marshal.Release(p);
            return result;
        }

        // Wait up to ms for a new frame (or for the window to close).
        public void Wait(int ms) { arrived.WaitOne(ms); }

        // Latest frame as an AddRef'd ID3D11Texture2D (caller releases), or Zero if none.
        // Call DoneWithFrame() after the copy has been issued.
        public IntPtr Next(out int contentW, out int contentH)
        {
            contentW = contentH = 0;
            Direct3D11CaptureFrame f = pool.TryGetNextFrame();
            if (f == null) return IntPtr.Zero;
            current = f;
            SizeInt32 cs = f.ContentSize;
            contentW = cs.Width; contentH = cs.Height;
            if (cs.Width != poolSize.Width || cs.Height != poolSize.Height) { poolSize = cs; recreate = true; }

            IntPtr unk = Marshal.GetIUnknownForObject(f.Surface);
            IntPtr access = Com.QI(unk, IID_DxgiInterfaceAccess, "IDirect3DDxgiInterfaceAccess");
            Marshal.Release(unk);
            try
            {
                Guid g = IID.D3D11Texture2D; IntPtr tex;
                Com.Check(Com.F<D.GetParent>(access, 3)(access, ref g, out tex), "GetInterface");   // IDirect3DDxgiInterfaceAccess::GetInterface
                return tex;
            }
            finally { Com.Release(ref access); }
        }

        public void DoneWithFrame()
        {
            if (current != null) { current.Dispose(); current = null; }
            if (recreate && poolSize.Width > 0 && poolSize.Height > 0)
            {
                recreate = false;
                pool.Recreate(device, Format, 2, poolSize);   // window was resized
            }
        }

        public void Dispose()
        {
            DoneWithFrame();
            try { session.Dispose(); } catch { }
            try { pool.Dispose(); } catch { }
            try { ((IDisposable)device).Dispose(); } catch { }
        }
    }
}
