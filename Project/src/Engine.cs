// Monitor Flipper — the GPU mirror engine.
//
// Pipeline (all on the graphics card):
//   source frame -> our own copy -> drawn onto every output window as one textured rectangle
//   -> mouse pointer on top -> Present, synced to the first output's refresh.
// Sources: a whole monitor (Windows Desktop Duplication) or one program window
// (Windows Graphics Capture, WindowCapture.cs).
// Flipping = mirroring the rectangle's corners, so it costs nothing. A rotated (portrait)
// source monitor is turned upright in the shader. Nothing is redrawn when nothing changed.
//
// Direct3D 11 / DXGI are called through raw COM vtables (Com.F in Gpu.cs). Slot numbers come
// from the Windows SDK headers; comments name each one.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MonitorFlipper
{
    class GpuMirror
    {
        const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
        const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
        const int FORMAT_BGRA = 87;
        const int CURSOR_CANVAS = 256;
        static readonly Guid IID_IDXGIDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

        // opt.x = force opaque, opt.y = source rotation (1 none, 2 = 90, 3 = 180, 4 = 270),
        // opt.zw = used part of the texture (window captures can be smaller than their texture).
        const string Shader = @"
cbuffer C : register(b0) { float4 rect; float4 uv; float4 opt; };
Texture2D tex : register(t0);
SamplerState smp : register(s0);
struct V { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
V vs(uint id : SV_VertexID) {
    float2 t = float2(id & 1, id >> 1);
    V o;
    o.pos = float4(lerp(rect.x, rect.z, t.x), lerp(rect.y, rect.w, t.y), 0, 1);
    float2 a = lerp(uv.xy, uv.zw, t);
    float2 q = a;
    if (opt.y > 1.5 && opt.y < 2.5) q = float2(a.y, 1 - a.x);
    else if (opt.y > 2.5 && opt.y < 3.5) q = 1 - a;
    else if (opt.y > 3.5) q = float2(1 - a.y, a.x);
    o.uv = q * opt.zw;
    return o;
}
float4 ps(V i) : SV_Target {
    float4 c = tex.Sample(smp, i.uv);
    return opt.x > 0.5 ? float4(c.rgb, 1) : c;
}";

        class Target
        {
            public OutputForm Form; public IntPtr Hwnd, Swap, Rtv; public int W, H;
        }

        // Live settings, written by the UI thread.
        public volatile bool FlipH, FlipV, KeepAspect, ShowCursor;
        public volatile string Status = "Starting...";
        public volatile bool Finished;   // source window closed; UI should stop

        readonly string sourceDevice;    // monitor source, or null
        readonly IntPtr sourceWindow;    // window source, or Zero
        readonly List<Target> targets = new List<Target>();
        Thread thread;
        volatile bool running;

        // GPU objects
        IntPtr factory, adapter, output1, device, ctx, dup;
        WindowCapture wgc;
        IntPtr vs, ps, sampler, raster, blend, blendInvert, cbImage, cbCursor, cbInvert;
        IntPtr deskTex, deskSrv; int texW, texH;     // our copy of the latest frame
        int srcW, srcH, rotation = 1;               // picture size the way the user sees it
        float uMax = 1, vMax = 1;
        IntPtr curTex, curSrv, invTex, invSrv; IntPtr curHandle = IntPtr.Zero; int curHotX, curHotY; bool curHasInvert;
        Rectangle desktop;
        readonly IntPtr cbScratch = Marshal.AllocHGlobal(48);

        public GpuMirror(string sourceDeviceName, IntPtr sourceWindowHandle, IEnumerable<OutputForm> outputs)
        {
            sourceDevice = sourceDeviceName;
            sourceWindow = sourceWindowHandle;
            if (outputs != null)
                foreach (OutputForm f in outputs) { Target t = new Target(); t.Form = f; t.Hwnd = f.Handle; targets.Add(t); }
        }

        public void Start()
        {
            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "GpuMirror";
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            if (thread != null) thread.Join(3000);
        }

        void Loop()
        {
            while (running)
            {
                try { Session(); }
                catch (Exception ex) { Status = "Problem: " + ex.Message + " - retrying"; }
                finally { Cleanup(); }
                if (running) Thread.Sleep(500);
            }
            Marshal.FreeHGlobal(cbScratch);
        }

        // ---------------------------------------------------------------- setup
        void InitDevice()
        {
            int level;
            if (sourceWindow == IntPtr.Zero)
            {
                Guid g = IID.DXGIFactory1;
                Com.Check(Native.CreateDXGIFactory1(ref g, out factory), "CreateDXGIFactory1");
                // Find the graphics adapter + output that drives the source monitor.
                for (uint a = 0; output1 == IntPtr.Zero; a++)
                {
                    IntPtr ad;
                    if (Com.F<D.GetPtrIdx>(factory, 12)(factory, a, out ad) < 0) break;   // IDXGIFactory1::EnumAdapters1
                    for (uint o = 0; ; o++)
                    {
                        IntPtr op;
                        if (Com.F<D.GetPtrIdx>(ad, 7)(ad, o, out op) < 0) break;           // IDXGIAdapter::EnumOutputs
                        OUTPUT_DESC od;
                        Com.F<D.OutputGetDesc>(op, 7)(op, out od);                         // IDXGIOutput::GetDesc
                        if (string.Equals(od.DeviceName, sourceDevice, StringComparison.OrdinalIgnoreCase))
                        {
                            output1 = Com.QI(op, IID.DXGIOutput1, "IDXGIOutput1");
                            desktop = Rectangle.FromLTRB(od.Desktop.L, od.Desktop.T, od.Desktop.R, od.Desktop.B);
                            rotation = od.Rotation >= 1 && od.Rotation <= 4 ? od.Rotation : 1;
                            adapter = ad; ad = IntPtr.Zero;
                            Com.Release(ref op);
                            break;
                        }
                        Com.Release(ref op);
                    }
                    Com.Release(ref ad);
                }
                if (output1 == IntPtr.Zero) throw new Exception("source monitor not found on any graphics card");
                Com.Check(Native.D3D11CreateDevice(adapter, 0 /*UNKNOWN: use given adapter*/, IntPtr.Zero, 0x20 /*BGRA_SUPPORT*/,
                    IntPtr.Zero, 0, 7, out device, out level, out ctx), "D3D11CreateDevice");
            }
            else
            {
                Com.Check(Native.D3D11CreateDevice(IntPtr.Zero, 1 /*HARDWARE*/, IntPtr.Zero, 0x20,
                    IntPtr.Zero, 0, 7, out device, out level, out ctx), "D3D11CreateDevice");
                // The swap chains must come from the factory that owns this device.
                IntPtr dxgiDev = Com.QI(device, IID_IDXGIDevice, "IDXGIDevice");
                try
                {
                    Com.Check(Com.F<D.NoArgsOut>(dxgiDev, 7)(dxgiDev, out adapter), "GetAdapter");       // IDXGIDevice::GetAdapter
                    Guid g = IID.DXGIFactory1;
                    Com.Check(Com.F<D.GetParent>(adapter, 6)(adapter, ref g, out factory), "GetParent"); // IDXGIObject::GetParent
                }
                finally { Com.Release(ref dxgiDev); }
            }

            byte[] code = Encoding.ASCII.GetBytes(Shader);
            IntPtr blob = Compile(code, "vs", "vs_4_0");
            Com.Check(Com.F<D.CreateShader>(device, 12)(device, BlobPtr(blob), BlobSize(blob), IntPtr.Zero, out vs), "CreateVertexShader");
            Com.Release(ref blob);
            blob = Compile(code, "ps", "ps_4_0");
            Com.Check(Com.F<D.CreateShader>(device, 15)(device, BlobPtr(blob), BlobSize(blob), IntPtr.Zero, out ps), "CreatePixelShader");
            Com.Release(ref blob);

            SAMPLER_DESC sd = new SAMPLER_DESC();
            sd.Filter = 0x15; sd.U = sd.V = sd.W = 3; sd.MaxAniso = 1; sd.Compare = 1; sd.MaxLod = float.MaxValue;
            Com.Check(Com.F<D.CreateSampler>(device, 23)(device, ref sd, out sampler), "CreateSamplerState");

            RAST_DESC rd = new RAST_DESC();
            rd.Fill = 3; rd.Cull = 1; /*no culling: flipping reverses the corner order*/ rd.DepthClip = 1;
            Com.Check(Com.F<D.CreateRast>(device, 22)(device, ref rd, out raster), "CreateRasterizerState");

            BLEND_DESC bd = new BLEND_DESC();
            bd.RT = new RT_BLEND[8];
            bd.RT[0].Enable = 1; bd.RT[0].Src = 2; bd.RT[0].Dst = 6; bd.RT[0].Op = 1;   // premultiplied alpha
            bd.RT[0].SrcA = 2; bd.RT[0].DstA = 6; bd.RT[0].OpA = 1; bd.RT[0].Mask = 0xF;
            Com.Check(Com.F<D.CreateBlend>(device, 20)(device, ref bd, out blend), "CreateBlendState");
            // Inverting pointer pixels (e.g. the text I-beam): result = 1 - screen where the mask is white.
            bd.RT[0].Src = 10; /*INV_DEST_COLOR*/ bd.RT[0].Dst = 4; /*INV_SRC_COLOR*/ bd.RT[0].SrcA = 1; bd.RT[0].DstA = 2;
            Com.Check(Com.F<D.CreateBlend>(device, 20)(device, ref bd, out blendInvert), "CreateBlendState(invert)");

            BUFFER_DESC cb = new BUFFER_DESC();
            cb.ByteWidth = 48; cb.Bind = 4; /*CONSTANT_BUFFER*/
            Com.Check(Com.F<D.CreateBuffer>(device, 3)(device, ref cb, IntPtr.Zero, out cbImage), "CreateBuffer");
            Com.Check(Com.F<D.CreateBuffer>(device, 3)(device, ref cb, IntPtr.Zero, out cbCursor), "CreateBuffer");
            Com.Check(Com.F<D.CreateBuffer>(device, 3)(device, ref cb, IntPtr.Zero, out cbInvert), "CreateBuffer");

            MakeTexture(CURSOR_CANVAS, CURSOR_CANVAS, 8, out curTex, out curSrv);
            MakeTexture(CURSOR_CANVAS, CURSOR_CANVAS, 8, out invTex, out invSrv);
        }

        void MakeTexture(int w, int h, uint bind, out IntPtr tex, out IntPtr srv)
        {
            TEX2D_DESC td = new TEX2D_DESC();
            td.Width = (uint)w; td.Height = (uint)h; td.MipLevels = 1; td.ArraySize = 1; td.Format = FORMAT_BGRA;
            td.SampleCount = 1; td.Bind = bind;
            Com.Check(Com.F<D.CreateTexture2D>(device, 5)(device, ref td, IntPtr.Zero, out tex), "CreateTexture2D");
            srv = IntPtr.Zero;
            if ((bind & 8) != 0) Com.Check(Com.F<D.CreateView>(device, 7)(device, tex, IntPtr.Zero, out srv), "CreateShaderResourceView");
        }

        static IntPtr Compile(byte[] code, string entry, string target)
        {
            IntPtr blob, errors;
            int hr = Native.D3DCompile(code, new IntPtr(code.Length), "flip", IntPtr.Zero, IntPtr.Zero, entry, target, 0, 0, out blob, out errors);
            if (hr < 0)
            {
                string msg = errors != IntPtr.Zero ? Marshal.PtrToStringAnsi(BlobPtr(errors)) : "";
                Com.Release(ref errors);
                throw new Exception("shader compile failed: " + msg);
            }
            Com.Release(ref errors);
            return blob;
        }
        static IntPtr BlobPtr(IntPtr blob) { return Com.F<D.BlobPtr>(blob, 3)(blob); }
        static IntPtr BlobSize(IntPtr blob) { return Com.F<D.BlobPtr>(blob, 4)(blob); }

        void InitDup()
        {
            Com.Check(Com.F<D.DuplicateOutput>(output1, 22)(output1, device, out dup), "DuplicateOutput"); // IDXGIOutput1::DuplicateOutput
        }

        void InitSwap(Target t, int w, int h)
        {
            SWAP_DESC sd = new SWAP_DESC();
            sd.Width = (uint)w; sd.Height = (uint)h; sd.Format = FORMAT_BGRA; sd.SampleCount = 1;
            sd.Usage = 0x20; /*RENDER_TARGET_OUTPUT*/ sd.Window = t.Hwnd; sd.Windowed = 1;
            sd.BufferCount = 2; sd.SwapEffect = 4; /*FLIP_DISCARD*/
            int hr = Com.F<D.CreateSwapChain>(factory, 10)(factory, device, ref sd, out t.Swap);
            if (hr < 0)
            {
                // Older model, needed for click-through (layered) windows.
                sd.BufferCount = 1; sd.SwapEffect = 0; /*DISCARD*/
                hr = Com.F<D.CreateSwapChain>(factory, 10)(factory, device, ref sd, out t.Swap);
            }
            Com.Check(hr, "CreateSwapChain");
            Com.F<D.MakeWindowAssociation>(factory, 8)(factory, t.Hwnd, 2 /*NO_ALT_ENTER*/);
            t.W = w; t.H = h;
            MakeRtv(t);
        }

        void MakeRtv(Target t)
        {
            Guid g = IID.D3D11Texture2D; IntPtr buf;
            Com.Check(Com.F<D.GetBuffer>(t.Swap, 9)(t.Swap, 0, ref g, out buf), "GetBuffer");
            int hr = Com.F<D.CreateView>(device, 9)(device, buf, IntPtr.Zero, out t.Rtv);
            Com.Release(ref buf);
            Com.Check(hr, "CreateRenderTargetView");
        }

        void ResizeSwap(Target t, int w, int h)
        {
            Com.F<D.SetRenderTargets>(ctx, 33)(ctx, 0, null, IntPtr.Zero);
            Com.Release(ref t.Rtv);
            Com.Check(Com.F<D.ResizeBuffers>(t.Swap, 13)(t.Swap, 0, (uint)w, (uint)h, 0, 0), "ResizeBuffers");
            t.W = w; t.H = h;
            MakeRtv(t);
        }

        void Cleanup()
        {
            if (ctx != IntPtr.Zero) Com.F<D.SetRenderTargets>(ctx, 33)(ctx, 0, null, IntPtr.Zero);
            foreach (Target t in targets) { Com.Release(ref t.Rtv); Com.Release(ref t.Swap); }
            Com.Release(ref dup);
            if (wgc != null) { wgc.Dispose(); wgc = null; }
            Com.Release(ref deskSrv); Com.Release(ref deskTex); texW = texH = 0;
            Com.Release(ref curSrv); Com.Release(ref curTex); Com.Release(ref invSrv); Com.Release(ref invTex);
            curHandle = IntPtr.Zero;
            Com.Release(ref cbImage); Com.Release(ref cbCursor); Com.Release(ref cbInvert);
            Com.Release(ref blend); Com.Release(ref blendInvert); Com.Release(ref raster); Com.Release(ref sampler);
            Com.Release(ref ps); Com.Release(ref vs);
            Com.Release(ref ctx); Com.Release(ref device);
            Com.Release(ref output1); Com.Release(ref adapter); Com.Release(ref factory);
        }

        // ---------------------------------------------------------------- frames
        // Copy a source texture into our own (recreating ours if the size changed).
        void CopyFrame(IntPtr tex)
        {
            TEX2D_DESC d;
            Com.F<D.TexGetDesc>(tex, 10)(tex, out d);                                    // ID3D11Texture2D::GetDesc
            if (deskTex == IntPtr.Zero || d.Width != texW || d.Height != texH)
            {
                Com.Release(ref deskSrv); Com.Release(ref deskTex);
                TEX2D_DESC c = new TEX2D_DESC();
                c.Width = d.Width; c.Height = d.Height; c.MipLevels = 1; c.ArraySize = 1; c.Format = d.Format;
                c.SampleCount = 1; c.Bind = 8;
                Com.Check(Com.F<D.CreateTexture2D>(device, 5)(device, ref c, IntPtr.Zero, out deskTex), "CreateTexture2D(frame)");
                Com.Check(Com.F<D.CreateView>(device, 7)(device, deskTex, IntPtr.Zero, out deskSrv), "CreateShaderResourceView(frame)");
                texW = (int)d.Width; texH = (int)d.Height;
            }
            Com.F<D.SetArray>(ctx, 8)(ctx, 0, 1, new IntPtr[] { IntPtr.Zero });   // unbind before overwriting
            Com.F<D.CopyResource>(ctx, 47)(ctx, deskTex, tex);
        }

        // Monitor source. Returns true if a new picture was copied.
        bool AcquireMonitor(uint timeoutMs)
        {
            FRAME_INFO fi; IntPtr res;
            int hr = Com.F<D.AcquireNextFrame>(dup, 8)(dup, timeoutMs, out fi, out res);  // IDXGIOutputDuplication::AcquireNextFrame
            if (hr == DXGI_ERROR_WAIT_TIMEOUT) return false;
            Com.Check(hr, "AcquireNextFrame");
            bool copied = false;
            try
            {
                if (fi.LastPresentTime != 0 || deskTex == IntPtr.Zero)
                {
                    IntPtr tex = Com.QI(res, IID.D3D11Texture2D, "frame texture");
                    try { CopyFrame(tex); copied = true; }
                    finally { Com.Release(ref tex); }
                    // Duplication delivers the panel's native orientation; the user sees it rotated.
                    bool sideways = rotation == 2 || rotation == 4;
                    srcW = sideways ? texH : texW; srcH = sideways ? texW : texH;
                    uMax = vMax = 1;
                }
            }
            finally
            {
                Com.Release(ref res);
                Com.F<D.NoArgs>(dup, 14)(dup);                                               // ReleaseFrame
            }
            return copied;
        }

        // Window source. Returns true if a new picture was copied.
        bool AcquireWindow(int timeoutMs)
        {
            wgc.Wait(timeoutMs);
            bool copied = false;
            while (true)   // take the newest frame, drop older ones
            {
                int cw, ch;
                IntPtr tex = wgc.Next(out cw, out ch);
                if (tex == IntPtr.Zero) break;
                try
                {
                    CopyFrame(tex);
                    srcW = Math.Max(1, Math.Min(cw, texW)); srcH = Math.Max(1, Math.Min(ch, texH));
                    uMax = (float)srcW / texW; vMax = (float)srcH / texH;
                    copied = true;
                }
                finally { Com.Release(ref tex); wgc.DoneWithFrame(); }
            }
            return copied;
        }

        void Session()
        {
            InitDevice();
            if (sourceWindow != IntPtr.Zero) wgc = new WindowCapture(device, sourceWindow); else InitDup();
            foreach (Target t in targets) InitSwap(t, Math.Max(1, t.Form.ClientW), Math.Max(1, t.Form.ClientH));

            bool lastH = !FlipH, lastV = false, lastAspect = false, lastCursor = false;
            Native.POINT lastPt = new Native.POINT(); IntPtr lastCurHandle = IntPtr.Zero; bool lastCurVisible = false;
            int frames = 0; DateTime lastReport = DateTime.UtcNow;
            bool needDraw = true;
            Status = "Running";

            while (running)
            {
                foreach (Target t in targets)
                {
                    int cw = t.Form.ClientW, ch = t.Form.ClientH;
                    if (cw > 0 && ch > 0 && (cw != t.W || ch != t.H)) { ResizeSwap(t, cw, ch); needDraw = true; }
                }

                bool fh = FlipH, fv = FlipV, keep = KeepAspect, showCur = ShowCursor && wgc == null;
                if (fh != lastH || fv != lastV || keep != lastAspect || showCur != lastCursor) needDraw = true;
                lastH = fh; lastV = fv; lastAspect = keep; lastCursor = showCur;

                if (wgc != null)
                {
                    if (AcquireWindow(30)) needDraw = true;
                    if (wgc.Closed) { Status = "The mirrored window was closed."; Finished = true; running = false; break; }
                }
                else
                {
                    try
                    {
                        if (AcquireMonitor(30)) needDraw = true;
                    }
                    catch (GpuException ex)
                    {
                        if (ex.Code != DXGI_ERROR_ACCESS_LOST) throw;
                        // Display mode change, lock screen, UAC prompt... reconnect when possible.
                        Com.Release(ref dup);
                        Status = "Reconnecting...";
                        while (running && dup == IntPtr.Zero)
                        {
                            try { InitDup(); } catch (GpuException) { Thread.Sleep(200); }
                        }
                        Status = "Running";
                        needDraw = true;
                        continue;
                    }
                }

                // Pointer (monitor sources only; window captures include it already).
                bool curVisible = false; Native.POINT pt = new Native.POINT();
                if (showCur)
                {
                    Native.CURSORINFO ci = new Native.CURSORINFO();
                    ci.cbSize = Marshal.SizeOf(typeof(Native.CURSORINFO));
                    if (Native.GetCursorInfo(ref ci) && (ci.flags & Native.CURSOR_SHOWING) != 0 && desktop.Contains(ci.pt.X, ci.pt.Y))
                    {
                        curVisible = true; pt = ci.pt;
                        if (ci.hCursor != curHandle) UploadCursor(ci.hCursor);
                        if (ci.hCursor != lastCurHandle || pt.X != lastPt.X || pt.Y != lastPt.Y) needDraw = true;
                        lastCurHandle = ci.hCursor;
                    }
                    if (curVisible != lastCurVisible) needDraw = true;
                    lastCurVisible = curVisible; lastPt = pt;
                }

                if (needDraw && deskSrv != IntPtr.Zero)
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        Target t = targets[i];
                        Draw(t.Rtv, t.W, t.H, fh, fv, keep, curVisible, pt);
                        // Only the first output waits for its monitor's refresh, so extra outputs don't slow it down.
                        Com.Check(Com.F<D.Present>(t.Swap, 8)(t.Swap, i == 0 ? 1u : 0u, 0), "Present");
                    }
                    frames++;
                    needDraw = false;
                }

                double secs = (DateTime.UtcNow - lastReport).TotalSeconds;
                if (secs >= 1)
                {
                    Status = string.Format("Running on graphics card - {0} frames/sec", (int)Math.Round(frames / secs));
                    frames = 0; lastReport = DateTime.UtcNow;
                }
            }
        }

        // Draw the picture (and pointer) into a render target of size dw x dh.
        void Draw(IntPtr target, int dw, int dh, bool fh, bool fv, bool keep, bool drawCursor, Native.POINT pt)
        {
            Com.F<D.SetRenderTargets>(ctx, 33)(ctx, 1, new IntPtr[] { target }, IntPtr.Zero);
            VIEWPORT vp = new VIEWPORT(); vp.W = dw; vp.H = dh; vp.MaxDepth = 1;
            Com.F<D.SetViewports>(ctx, 44)(ctx, 1, ref vp);
            Com.F<D.ClearRtv>(ctx, 50)(ctx, target, new float[] { 0, 0, 0, 1 });
            Com.F<D.SetInt>(ctx, 24)(ctx, 5 /*TRIANGLESTRIP*/);
            Com.F<D.SetPtr>(ctx, 17)(ctx, IntPtr.Zero);                                      // no input layout
            Com.F<D.SetShader>(ctx, 11)(ctx, vs, IntPtr.Zero, 0);
            Com.F<D.SetShader>(ctx, 9)(ctx, ps, IntPtr.Zero, 0);
            Com.F<D.SetArray>(ctx, 10)(ctx, 0, 1, new IntPtr[] { sampler });
            Com.F<D.SetPtr>(ctx, 43)(ctx, raster);

            // Picture placement (letterboxed when keeping proportions).
            double rx = 0, ry = 0, rw = dw, rh = dh;
            if (keep)
            {
                double s = Math.Min((double)dw / srcW, (double)dh / srcH);
                rw = srcW * s; rh = srcH * s; rx = (dw - rw) / 2; ry = (dh - rh) / 2;
            }
            DrawQuad(cbImage, deskSrv, IntPtr.Zero, Ndc(rx, ry, rx + rw, ry + rh, dw, dh, fh, fv), true, rotation, uMax, vMax);

            if (drawCursor && curSrv != IntPtr.Zero)
            {
                double scaleX = rw / srcW, scaleY = rh / srcH;
                double cx = rx + (pt.X - desktop.X - curHotX) * scaleX;
                double cy = ry + (pt.Y - desktop.Y - curHotY) * scaleY;
                float[] r = Ndc(cx, cy, cx + CURSOR_CANVAS * scaleX, cy + CURSOR_CANVAS * scaleY, dw, dh, fh, fv);
                DrawQuad(cbCursor, curSrv, blend, r, false, 1, 1, 1);
                if (curHasInvert) DrawQuad(cbInvert, invSrv, blendInvert, r, false, 1, 1, 1);
            }
            Com.F<D.SetArray>(ctx, 8)(ctx, 0, 1, new IntPtr[] { IntPtr.Zero });
        }

        // Pixel rectangle -> clip-space corners. Flipping mirrors the corners around the center.
        static float[] Ndc(double x0, double y0, double x1, double y1, int dw, int dh, bool fh, bool fv)
        {
            float l = (float)(x0 / dw * 2 - 1), r = (float)(x1 / dw * 2 - 1);
            float t = (float)(1 - y0 / dh * 2), b = (float)(1 - y1 / dh * 2);
            if (fh) { l = -l; r = -r; }
            if (fv) { t = -t; b = -b; }
            return new float[] { l, t, r, b };
        }

        void DrawQuad(IntPtr cb, IntPtr srv, IntPtr blendState, float[] rect, bool opaque, int rot, float um, float vm)
        {
            float[] data = { rect[0], rect[1], rect[2], rect[3], 0, 0, 1, 1, opaque ? 1 : 0, rot, um, vm };
            Marshal.Copy(data, 0, cbScratch, 12);
            Com.F<D.UpdateSubresource>(ctx, 48)(ctx, cb, 0, IntPtr.Zero, cbScratch, 0, 0);
            IntPtr[] cbs = { cb };
            Com.F<D.SetArray>(ctx, 7)(ctx, 0, 1, cbs);    // VSSetConstantBuffers
            Com.F<D.SetArray>(ctx, 16)(ctx, 0, 1, cbs);   // PSSetConstantBuffers
            Com.F<D.SetArray>(ctx, 8)(ctx, 0, 1, new IntPtr[] { srv });
            Com.F<D.SetBlend>(ctx, 35)(ctx, blendState, null, 0xFFFFFFFF);
            Com.F<D.Draw>(ctx, 13)(ctx, 4, 0);
        }

        // Render the current Windows pointer into textures. Drawing it on black and on white
        // recovers its transparency exactly. Pixels that came out darker on white than on black
        // are "inverting" pixels (the text I-beam): they go into a separate mask that the GPU
        // uses to invert the picture underneath, just like the real pointer does.
        void UploadCursor(IntPtr hCursor)
        {
            curHandle = hCursor;
            Native.ICONINFO ii;
            curHotX = curHotY = 0;
            if (Native.GetIconInfo(hCursor, out ii))
            {
                curHotX = ii.xHotspot; curHotY = ii.yHotspot;
                if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
            }
            int n = CURSOR_CANVAS * CURSOR_CANVAS;
            int[] onBlack = RenderCursor(hCursor, Native.BLACKNESS, n);
            int[] onWhite = RenderCursor(hCursor, Native.WHITENESS, n);
            int[] px = new int[n], inv = new int[n];
            curHasInvert = false;
            for (int i = 0; i < n; i++)
            {
                int b = onBlack[i], w = onWhite[i];
                int bg = (b >> 8) & 255, wg = (w >> 8) & 255;
                if (wg < bg) { inv[i] = -1; curHasInvert = true; continue; }   // inverting pixel
                int a = Math.Max(0, Math.Min(255, 255 - (wg - bg)));
                int cb = Math.Min(a, b & 255), cg = Math.Min(a, bg), cr = Math.Min(a, (b >> 16) & 255);
                px[i] = (a << 24) | (cr << 16) | (cg << 8) | cb;
            }
            Upload(curTex, px);
            Upload(invTex, inv);
        }

        void Upload(IntPtr tex, int[] px)
        {
            GCHandle h = GCHandle.Alloc(px, GCHandleType.Pinned);
            try { Com.F<D.UpdateSubresource>(ctx, 48)(ctx, tex, 0, IntPtr.Zero, h.AddrOfPinnedObject(), CURSOR_CANVAS * 4, 0); }
            finally { h.Free(); }
        }

        static int[] RenderCursor(IntPtr hCursor, int fill, int n)
        {
            Native.BITMAPINFOHEADER bi = new Native.BITMAPINFOHEADER();
            bi.biSize = 40; bi.biWidth = CURSOR_CANVAS; bi.biHeight = -CURSOR_CANVAS; bi.biPlanes = 1; bi.biBitCount = 32;
            IntPtr bits;
            IntPtr dib = Native.CreateDIBSection(IntPtr.Zero, ref bi, 0, out bits, IntPtr.Zero, 0);
            IntPtr dc = Native.CreateCompatibleDC(IntPtr.Zero);
            IntPtr old = Native.SelectObject(dc, dib);
            Native.PatBlt(dc, 0, 0, CURSOR_CANVAS, CURSOR_CANVAS, fill);
            Native.DrawIconEx(dc, 0, 0, hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
            Native.GdiFlush();
            int[] r = new int[n];
            Marshal.Copy(bits, r, 0, n);
            Native.SelectObject(dc, old); Native.DeleteDC(dc); Native.DeleteObject(dib);
            return r;
        }

        // ---------------------------------------------------------------- self-tests
        // These render on the GPU with no window, show nothing and save no images; they report
        // only pixel-match counts.
        public static string SelfTest(string deviceName)
        {
            StringBuilder sb = new StringBuilder();
            GpuMirror m = new GpuMirror(deviceName, IntPtr.Zero, null);
            try
            {
                m.InitDevice();
                m.InitDup();
                for (int i = 0; i < 20 && m.deskTex == IntPtr.Zero; i++) m.AcquireMonitor(250);
                if (m.deskTex == IntPtr.Zero) return "FAIL: no frame from Desktop Duplication";
                int W = m.texW, H = m.texH;
                sb.AppendFormat("Source {0}: {1} x {2}, Windows rotation setting {3}\r\n", deviceName, W, H, m.rotation);
                int[] src = m.ReadBack(m.deskTex, W, H);
                bool allOk = true;

                // 1. Flips (identity rotation).
                IntPtr rt, rtv; m.MakeTexture(W, H, 0x20, out rt, out rtv);
                Com.Release(ref rtv);
                Com.Check(Com.F<D.CreateView>(m.device, 9)(m.device, rt, IntPtr.Zero, out rtv), "CreateRTV");
                m.rotation = 1; m.srcW = W; m.srcH = H;
                string[] names = { "no flip", "horizontal", "vertical", "both" };
                for (int k = 0; k < 4; k++)
                {
                    bool fh = (k & 1) != 0, fv = (k & 2) != 0;
                    m.Draw(rtv, W, H, fh, fv, false, false, new Native.POINT());
                    int[] dst = m.ReadBack(rt, W, H);
                    long bad = 0;
                    for (int y = 0; y < H; y++)
                        for (int x = 0; x < W; x++)
                        {
                            int sx = fh ? W - 1 - x : x, sy = fv ? H - 1 - y : y;
                            if (((dst[y * W + x] ^ src[sy * W + sx]) & 0xFFFFFF) != 0) bad++;
                        }
                    sb.AppendFormat("flip {0,-11} mismatched pixels: {1} of {2}\r\n", names[k], bad, (long)W * H);
                    if (bad != 0) allOk = false;
                }
                Com.Release(ref rtv); Com.Release(ref rt);

                // 2. Rotation (pretend the monitor is turned): output is H x W for 90/270.
                // Expected mapping, from Microsoft's Desktop Duplication sample:
                //   90:  screen(x,y) = texture(y, H-1-x)    180: texture(W-1-x, H-1-y)    270: texture(W-1-y, x)
                for (int rot = 2; rot <= 4; rot++)
                {
                    bool side = rot != 3;
                    int ow = side ? H : W, oh = side ? W : H;
                    m.MakeTexture(ow, oh, 0x20, out rt, out rtv);
                    Com.Release(ref rtv);
                    Com.Check(Com.F<D.CreateView>(m.device, 9)(m.device, rt, IntPtr.Zero, out rtv), "CreateRTV");
                    m.rotation = rot; m.srcW = ow; m.srcH = oh;
                    m.Draw(rtv, ow, oh, false, false, false, false, new Native.POINT());
                    int[] dst = m.ReadBack(rt, ow, oh);
                    long bad = 0;
                    for (int y = 0; y < oh; y++)
                        for (int x = 0; x < ow; x++)
                        {
                            int tx, ty;
                            if (rot == 2) { tx = y; ty = H - 1 - x; }
                            else if (rot == 3) { tx = W - 1 - x; ty = H - 1 - y; }
                            else { tx = W - 1 - y; ty = x; }
                            if (((dst[y * ow + x] ^ src[ty * W + tx]) & 0xFFFFFF) != 0) bad++;
                        }
                    sb.AppendFormat("rotation {0,-3}    mismatched pixels: {1} of {2}\r\n", (rot - 1) * 90, bad, (long)ow * oh);
                    if (bad != 0) allOk = false;
                    Com.Release(ref rtv); Com.Release(ref rt);
                }
                sb.Insert(0, allOk ? "PASS\r\n" : "FAIL\r\n");
            }
            catch (Exception ex) { sb.Insert(0, "FAIL: " + ex.Message + "\r\n"); }
            finally { m.Cleanup(); Marshal.FreeHGlobal(m.cbScratch); }
            return sb.ToString();
        }

        // Window-capture check: captures a window for a moment and reports only whether frames
        // arrived and their size.
        public static string WindowSelfTest(IntPtr hwnd)
        {
            GpuMirror m = new GpuMirror(null, hwnd, null);
            try
            {
                m.InitDevice();
                m.wgc = new WindowCapture(m.device, hwnd);
                int got = 0;
                for (int i = 0; i < 20; i++) if (m.AcquireWindow(100)) got++;
                return got > 0
                    ? string.Format("PASS: window frames received ({0} x {1})\r\n", m.srcW, m.srcH)
                    : "FAIL: no window frames in 2 seconds\r\n";
            }
            catch (Exception ex) { return "FAIL: " + ex.Message + "\r\n"; }
            finally { m.Cleanup(); Marshal.FreeHGlobal(m.cbScratch); }
        }

        int[] ReadBack(IntPtr tex, int W, int H)
        {
            TEX2D_DESC sd = new TEX2D_DESC();
            sd.Width = (uint)W; sd.Height = (uint)H; sd.MipLevels = 1; sd.ArraySize = 1; sd.Format = FORMAT_BGRA;
            sd.SampleCount = 1; sd.Usage = 3; /*STAGING*/ sd.CpuAccess = 0x20000; /*READ*/
            IntPtr staging;
            Com.Check(Com.F<D.CreateTexture2D>(device, 5)(device, ref sd, IntPtr.Zero, out staging), "CreateTexture2D(staging)");
            try
            {
                Com.F<D.CopyResource>(ctx, 47)(ctx, staging, tex);
                MAPPED mp;
                Com.Check(Com.F<D.Map>(ctx, 14)(ctx, staging, 0, 1 /*READ*/, 0, out mp), "Map");
                int[] r = new int[W * H];
                for (int y = 0; y < H; y++)
                    Marshal.Copy(new IntPtr(mp.Data.ToInt64() + (long)y * mp.RowPitch), r, y * W, W);
                Com.F<D.Unmap>(ctx, 15)(ctx, staging, 0);
                return r;
            }
            finally { Com.Release(ref staging); }
        }
    }
}
