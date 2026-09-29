using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Gravitone.Interop;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

/// <summary>
/// The shape of the "genie": the window's rows run down a funnel whose sides bend from the window's edges to the
/// dock icon's (an S curve). First the funnel forms, then the rows slide down it into the icon, faster and faster.
/// Progress 0 is the window as it was, 1 is all of it in the icon.
/// </summary>
internal static class GenieMesh
{
    public const int Rows = 256;

    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public Vector2 Position; // clip space
        public Vector2 Uv;
    }

    static double Smooth(double x) => x * x * (3 - 2 * x);

    /// <summary>Two vertices per row (a triangle strip), in clip space of <paramref name="area"/>.</summary>
    public static void Compute(RECT win, RECT icon, DockEdge edge, double p, RECT area, Span<Vertex> vertices)
    {
        bool sideways = edge is DockEdge.Left or DockEdge.Right;
        int s = edge == DockEdge.Left ? -1 : 1;
        double far = edge switch { DockEdge.Left => win.Right, DockEdge.Right => win.Left, _ => win.Top };
        double length = sideways ? win.Width : win.Height;
        double lat0 = sideways ? win.Top : win.Left, lat1 = sideways ? win.Bottom : win.Right;
        double iconDepth = sideways ? (icon.Left + icon.Right) / 2.0 : (icon.Top + icon.Bottom) / 2.0;
        double iconSize = sideways ? icon.Height : icon.Width;
        double iconMid = sideways ? (icon.Top + icon.Bottom) / 2.0 : (icon.Left + icon.Right) / 2.0;
        double end0 = iconMid - iconSize * 0.4, end1 = iconMid + iconSize * 0.4;
        // Distance from the window's far edge to the icon, along the way to the dock.
        double reach = Math.Max(s * (iconDepth - far), length * 0.5 + 1);

        // The funnel forms in the first half; the rows start sliding a quarter in and speed up.
        double k = Smooth(Math.Clamp(p / 0.5, 0, 1));
        double x = Math.Clamp((p - 0.25) / 0.75, 0, 1);
        double slide = 0.55 * x * x + 0.45 * Smooth(x);

        double aw = area.Width, ah = area.Height;
        for (int i = 0; i < Rows; i++)
        {
            double v = i / (double)(Rows - 1);
            double d = Math.Min(v * length + slide * reach, reach);
            double bend = k * Smooth(Math.Clamp(d / reach, 0, 1));
            double a = lat0 + (end0 - lat0) * bend;
            double b = lat1 + (end1 - lat1) * bend;
            double depth = far + s * d;

            Vector2 pa, pb, ua, ub;
            if (sideways)
            {
                pa = new((float)depth, (float)a);
                pb = new((float)depth, (float)b);
                float u = (float)(s > 0 ? v : 1 - v);
                ua = new(u, 0);
                ub = new(u, 1);
            }
            else
            {
                pa = new((float)a, (float)depth);
                pb = new((float)b, (float)depth);
                ua = new(0, (float)v);
                ub = new(1, (float)v);
            }
            vertices[2 * i] = new Vertex { Position = ToClip(pa), Uv = ua };
            vertices[2 * i + 1] = new Vertex { Position = ToClip(pb), Uv = ub };
        }

        Vector2 ToClip(Vector2 px) => new((float)((px.X - area.Left) / aw * 2 - 1), (float)(1 - (px.Y - area.Top) / ah * 2));
    }
}

/// <summary>
/// Draws genie animations on the graphics card: the captured window is a mipmapped texture on a strip of
/// <see cref="GenieMesh.Rows"/> quads, rendered with Direct3D 11 into a swap chain that DirectComposition shows
/// in a transparent window. One frame per screen refresh (Present waits for it), and the only CPU work per
/// frame is a few hundred vertices. All graphics work happens on one thread of its own.
/// </summary>
internal sealed class GenieRenderer
{
    const string Shaders = """
        Texture2D tex : register(t0);
        SamplerState smp : register(s0);
        struct VSIn { float2 pos : POSITION; float2 uv : TEXCOORD0; };
        struct PSIn { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        PSIn VS(VSIn i) { PSIn o; o.pos = float4(i.pos, 0, 1); o.uv = i.uv; return o; }
        float4 PS(PSIn i) : SV_TARGET { return tex.Sample(smp, i.uv); }
        """;

    public static GenieRenderer Instance { get; } = new();

    readonly BlockingCollection<GenieJob> _jobs = [];
    readonly Thread _thread;
    ID3D11Device? _device;
    ID3D11DeviceContext? _context;
    IDXGIFactory2? _factory;
    IDCompositionDevice? _dcomp;
    ID3D11VertexShader? _vs;
    ID3D11PixelShader? _ps;
    ID3D11InputLayout? _layout;
    ID3D11SamplerState? _sampler;
    ID3D11Buffer? _vertexBuffer;
    readonly GenieMesh.Vertex[] _vertices = new GenieMesh.Vertex[GenieMesh.Rows * 2];

    GenieRenderer()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Gravitone genie", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>False once the graphics card could not be used (the genie is then skipped).</summary>
    public bool Available { get; private set; } = true;

    public void Enqueue(GenieJob job) => _jobs.Add(job);

    void Run()
    {
        foreach (var job in _jobs.GetConsumingEnumerable())
        {
            try
            {
                if (_device is null && !Initialize())
                {
                    Available = false;
                    job.Post(job.Started);
                    job.Post(job.Finished);
                    job.Post(job.Ended);
                    continue;
                }
                Play(job);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Genie effect");
                ReleaseDevice(); // device lost or broken: start over next time
                job.Post(job.Started);
                job.Post(job.Finished);
                job.Post(job.Ended);
            }
        }
    }

    bool Initialize()
    {
        try
        {
            FeatureLevel[] levels = [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels,
                out _device, out _context).CheckError();
            using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            _factory = adapter.GetParent<IDXGIFactory2>();
            _dcomp = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);

            var vsCode = Compiler.Compile(Shaders, "VS", "genie", "vs_4_0");
            var psCode = Compiler.Compile(Shaders, "PS", "genie", "ps_4_0");
            _vs = _device.CreateVertexShader(vsCode.Span);
            _ps = _device.CreatePixelShader(psCode.Span);
            _layout = _device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
            ], vsCode.Span);
            _sampler = _device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.Anisotropic,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                MaxAnisotropy = 8,
                MaxLOD = float.MaxValue,
                ComparisonFunc = ComparisonFunction.Never,
            });
            _vertexBuffer = _device.CreateBuffer((uint)(_vertices.Length * Marshal.SizeOf<GenieMesh.Vertex>()),
                BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
            Log.Info($"Genie effect: Direct3D {_device.FeatureLevel}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Genie effect: the graphics card cannot be used");
            ReleaseDevice();
            return false;
        }
    }

    void ReleaseDevice()
    {
        _vertexBuffer?.Dispose(); _sampler?.Dispose(); _layout?.Dispose(); _ps?.Dispose(); _vs?.Dispose();
        _dcomp?.Dispose(); _factory?.Dispose(); _context?.Dispose(); _device?.Dispose();
        _vertexBuffer = null; _sampler = null; _layout = null; _ps = null; _vs = null;
        _dcomp = null; _factory = null; _context = null; _device = null;
    }

    void Play(GenieJob job)
    {
        var device = _device!;
        var context = _context!;
        var area = job.Area;
        var cap = job.Capture;

        using var swapChain = _factory!.CreateSwapChainForComposition(device, new SwapChainDescription1
        {
            Width = (uint)area.Width,
            Height = (uint)area.Height,
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Premultiplied,
            Scaling = Scaling.Stretch,
            Flags = SwapChainFlags.FrameLatencyWaitableObject,
        });
        // A composition swap chain does not make Present wait for the refresh: this handle does (one frame in flight).
        using var swapChain2 = swapChain.QueryInterface<IDXGISwapChain2>();
        swapChain2.MaximumFrameLatency = 1;
        var frameReady = swapChain2.FrameLatencyWaitableObject;
        using var backBuffer = swapChain.GetBuffer<ID3D11Texture2D>(0);
        using var renderTarget = device.CreateRenderTargetView(backBuffer);

        // The window, with mipmaps: squeezed into the neck it must stay smooth, not shimmer.
        using var texture = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)cap.Width,
            Height = (uint)cap.Height,
            MipLevels = 0,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            MiscFlags = ResourceOptionFlags.GenerateMips,
        });
        context.UpdateSubresource(cap.Pixels, texture, 0, (uint)cap.Width * 4, 0);
        using var view = device.CreateShaderResourceView(texture);
        context.GenerateMips(view);
        job.View = view;

        _dcomp!.CreateTargetForHwnd(job.Window, true, out var target).CheckError();
        using var visual = _dcomp.CreateVisual();
        try
        {
            visual.SetContent(swapChain);
            target!.SetRoot(visual);

            // First frame: the window exactly as it is, on screen before the real one goes.
            Draw(job, job.Expand ? 1 : 0, renderTarget, area);
            swapChain.Present(1, PresentFlags.None);
            _dcomp.Commit();
            _dcomp.WaitForCommitCompletion();
            job.Post(job.Started);

            var clock = Stopwatch.StartNew();
            long cpuStart = ThreadCpuTicks();
            int frames = 0;
            while (true)
            {
                WaitForSingleObject(frameReady, 100);
                frames++;
                double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / job.DurationMs);
                Draw(job, job.Expand ? 1 - t : t, renderTarget, area);
                swapChain.Present(1, PresentFlags.None); // waits for the next refresh
                if (t >= 1) break;
            }
            if (WindowTracker.Diagnostics)
                Log.Info($"Genie: {frames} frames in {clock.ElapsedMilliseconds} ms, thread CPU {(ThreadCpuTicks() - cpuStart) / 10000} ms");

            job.Post(job.Finished);
            if (job.LingerMs > 0)
            {
                // A restore: keep showing the window until the real one has painted.
                Thread.Sleep(job.LingerMs);
            }
            else
            {
                context.ClearRenderTargetView(renderTarget, new Color4(0, 0, 0, 0));
                swapChain.Present(1, PresentFlags.None);
            }
        }
        finally
        {
            target?.SetRoot(null);
            _dcomp.Commit();
            target?.Dispose();
            job.Post(job.Ended);
        }
    }

    [DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")]
    static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

    static long ThreadCpuTicks() => GetThreadTimes(GetCurrentThread(), out _, out _, out long k, out long u) ? k + u : 0;

    void Draw(GenieJob job, double p, ID3D11RenderTargetView renderTarget, RECT area)
    {
        var context = _context!;
        GenieMesh.Compute(job.Capture.Bounds, job.Target, job.Edge, p, area, _vertices);
        var mapped = context.Map(_vertexBuffer!, MapMode.WriteDiscard);
        _vertices.AsSpan().CopyTo(mapped.AsSpan<GenieMesh.Vertex>(_vertices.Length));
        context.Unmap(_vertexBuffer!, 0);

        context.OMSetRenderTargets(renderTarget);
        context.ClearRenderTargetView(renderTarget, new Color4(0, 0, 0, 0));
        context.RSSetViewport(0, 0, area.Width, area.Height);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.IASetInputLayout(_layout);
        context.IASetVertexBuffer(0, _vertexBuffer!, (uint)Marshal.SizeOf<GenieMesh.Vertex>());
        context.VSSetShader(_vs);
        context.PSSetShader(_ps);
        context.PSSetSampler(0, _sampler);
        context.PSSetShaderResource(0, job.View!);
        context.Draw((uint)_vertices.Length, 0);
    }
}

/// <summary>One animation for the render thread; the callbacks are run on the UI thread.</summary>
internal sealed class GenieJob
{
    public required GenieCapture Capture;
    public required RECT Target;
    public required DockEdge Edge;
    public required bool Expand;
    public required int DurationMs;
    public required int LingerMs;
    public required IntPtr Window;
    public required RECT Area;
    public required Dispatcher Dispatcher;
    public Action? Started, Finished, Ended;
    public ID3D11ShaderResourceView? View;

    public void Post(Action? action)
    {
        if (action is not null) Dispatcher.BeginInvoke(action);
    }
}

/// <summary>
/// One minimize or restore, as the dock sees it: a transparent window (no redirection surface: DirectComposition
/// draws it) just below the dock, and a job for <see cref="GenieRenderer"/>.
/// </summary>
internal sealed class GenieAnimation
{
    const string ClassName = "GravitoneGenie";
    static bool _registered;

    readonly GenieCapture _capture;
    readonly RECT _target;
    readonly DockEdge _edge;
    readonly bool _expand;
    readonly Action? _started, _finished;
    readonly int _durationMs, _lingerMs;
    IntPtr _hwnd;

    /// <param name="expand">The window grows out of the icon (restore) instead of flowing into it (minimize).</param>
    /// <param name="started">Called once the first frame (the window as it is) is on screen: minimize the real window here.</param>
    /// <param name="finished">Called when the last frame has been drawn: restore the real window here.</param>
    /// <param name="lingerMs">How long the last frame stays after <paramref name="finished"/> while the real window paints.</param>
    public GenieAnimation(GenieCapture capture, RECT target, DockEdge edge, bool expand, Action? started, Action? finished,
        int durationMs, int lingerMs)
    {
        _capture = capture; _target = target; _edge = edge; _expand = expand;
        _started = started; _finished = finished; _durationMs = durationMs; _lingerMs = lingerMs;
    }

    public event Action? Ended;

    public bool Start(IntPtr insertAfter)
    {
        if (!GenieRenderer.Instance.Available) return false;
        var b = _capture.Bounds;
        var area = new RECT(Math.Min(b.Left, _target.Left), Math.Min(b.Top, _target.Top),
            Math.Max(b.Right, _target.Right), Math.Max(b.Bottom, _target.Bottom));
        if (area.Width < 1 || area.Height < 1 || area.Width > 16384 || area.Height > 16384) return false;

        _hwnd = CreateOverlay(area, insertAfter);
        if (_hwnd == IntPtr.Zero) return false;

        GenieRenderer.Instance.Enqueue(new GenieJob
        {
            Capture = _capture,
            Target = _target,
            Edge = _edge,
            Expand = _expand,
            DurationMs = _durationMs,
            LingerMs = _lingerMs,
            Window = _hwnd,
            Area = area,
            Dispatcher = Dispatcher.CurrentDispatcher,
            Started = _started,
            Finished = _finished,
            Ended = () =>
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
                Ended?.Invoke();
            },
        });
        return true;
    }

    static IntPtr CreateOverlay(RECT area, IntPtr insertAfter)
    {
        if (!_registered)
        {
            var cls = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = GetProcAddress(GetModuleHandleW("user32.dll"), "DefWindowProcW"),
                hInstance = GetModuleHandleW(null),
                lpszClassName = ClassName,
            };
            if (RegisterClassEx(ref cls) == 0 && Marshal.GetLastWin32Error() != 1410 /* already registered */) return IntPtr.Zero;
            _registered = true;
        }
        const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        var hwnd = CreateWindowEx(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT | (uint)WS_EX_TOOLWINDOW | (uint)WS_EX_NOACTIVATE | (uint)WS_EX_TOPMOST,
            ClassName, "Gravitone genie", WS_POPUP, area.Left, area.Top, area.Width, area.Height,
            IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (hwnd == IntPtr.Zero) return IntPtr.Zero;
        // Just below the dock, so its icons stay in front of the neck running into them.
        SetWindowPos(hwnd, insertAfter != IntPtr.Zero ? insertAfter : HWND_TOPMOST, area.Left, area.Top, area.Width, area.Height, SWP_NOACTIVATE);
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        return hwnd;
    }
}

