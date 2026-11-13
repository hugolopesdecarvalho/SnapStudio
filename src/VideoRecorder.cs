using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace SnapStudio
{
    /// <summary>
    /// Grava uma zona do ecrã para MP4 (H.264) com o Media Foundation incluído no Windows.
    /// Capturar o ecrã com BitBlt demora ~1–2 refrescamentos (espera pelo compositor), por isso
    /// várias threads capturam em paralelo, desfasadas, e uma thread codifica os frames por ordem.
    /// </summary>
    class VideoRecorder
    {
        public readonly string Path;
        public readonly Rectangle Area;      // coordenadas de ecrã
        public readonly int Fps;
        public readonly bool DrawCursor;
        public Exception Error { get; private set; }
        public int FramesWritten { get; private set; }
        public double GrabMs;            // tempo acumulado de captura (diagnóstico)
        const int Workers = 3;

        /// <summary>Chamado (na thread de gravação) quando termina: Error == null se correu bem.</summary>
        public event Action Finished;

        readonly int outW, outH;
        Thread thread;
        volatile bool stop, cancel, paused;
        readonly Stopwatch clock = new Stopwatch();
        long pauseStarted, pausedTotal;
        readonly object sync = new object();

        // fila entre as threads de captura e a de codificação
        readonly object q = new object();
        readonly SortedList<long, IMFSample> pending = new SortedList<long, IMFSample>();
        readonly long[] inProgress = new long[Workers];
        int workersAlive;
        Exception workerError;

        public VideoRecorder(string path, Rectangle area, int fps, bool drawCursor)
        {
            Path = path;
            Area = area;
            Fps = Math.Max(5, Math.Min(60, fps));
            DrawCursor = drawCursor;
            // H.264: dimensões pares e dentro dos limites do codificador
            double sc = Math.Min(1.0, Math.Min(3840.0 / area.Width, 2160.0 / area.Height));
            outW = Math.Max(2, (int)(area.Width * sc) & ~1);
            outH = Math.Max(2, (int)(area.Height * sc) & ~1);
        }

        public Size OutputSize { get { return new Size(outW, outH); } }

        /// <summary>Tempo gravado (sem as pausas).</summary>
        public TimeSpan Elapsed
        {
            get
            {
                lock (sync)
                {
                    long t = clock.Elapsed.Ticks - pausedTotal;
                    if (paused) t -= clock.Elapsed.Ticks - pauseStarted;
                    return TimeSpan.FromTicks(Math.Max(0, t));
                }
            }
        }

        public bool Paused
        {
            get { return paused; }
            set
            {
                lock (sync)
                {
                    if (paused == value) return;
                    if (value) pauseStarted = clock.Elapsed.Ticks;
                    else pausedTotal += clock.Elapsed.Ticks - pauseStarted;
                    paused = value;
                }
            }
        }

        public void Start()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "SnapStudio video" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public void Stop() { stop = true; }
        public void Wait(int ms) { if (thread != null) thread.Join(ms); }
        public void Cancel() { cancel = true; stop = true; }
        public bool Cancelled { get { return cancel; } }

        void Run()
        {
            MF.TimeBeginPeriod(1);
            bool mfStarted = false;
            IMFSinkWriter writer = null;
            var threads = new List<Thread>();
            try
            {
                MF.MFStartup(MF.MF_VERSION, 0);
                mfStarted = true;
                int stream;
                writer = CreateWriter(out stream);
                writer.BeginWriting();

                long frameTicks = 10000000L / Fps;
                for (int k = 0; k < Workers; k++) inProgress[k] = long.MaxValue;
                workersAlive = Workers;
                clock.Start();
                for (int k = 0; k < Workers; k++)
                {
                    int id = k;
                    var t = new Thread(() => CaptureLoop(id, frameTicks)) { IsBackground = true, Name = "SnapStudio capture " + k };
                    threads.Add(t);
                    t.Start();
                }

                long last = -1;
                var ready = new List<KeyValuePair<long, IMFSample>>();
                while (true)
                {
                    bool done;
                    lock (q)
                    {
                        while (true)
                        {
                            long minBusy = long.MaxValue;
                            foreach (long v in inProgress) minBusy = Math.Min(minBusy, v);
                            // só é seguro escrever frames mais antigos do que qualquer captura ainda em curso
                            while (pending.Count > 0 && pending.Keys[0] < minBusy)
                            {
                                ready.Add(new KeyValuePair<long, IMFSample>(pending.Keys[0], pending.Values[0]));
                                pending.RemoveAt(0);
                            }
                            done = workersAlive == 0 && pending.Count == 0;
                            if (ready.Count > 0 || done) break;
                            Monitor.Wait(q, 100);
                        }
                    }
                    foreach (var kv in ready)
                    {
                        try
                        {
                            if (kv.Key > last && !cancel)
                            {
                                kv.Value.SetSampleTime(kv.Key);
                                kv.Value.SetSampleDuration(frameTicks);
                                writer.WriteSample(stream, kv.Value);
                                FramesWritten++;
                                last = kv.Key;
                            }
                        }
                        finally { Marshal.ReleaseComObject(kv.Value); }
                    }
                    ready.Clear();
                    if (done) break;
                }
                if (workerError != null) throw workerError;
                if (!cancel && FramesWritten > 0) writer.DoFinalize();
            }
            catch (Exception ex) { Error = ex; stop = true; }
            finally
            {
                foreach (var t in threads) t.Join(5000);
                lock (q) { foreach (var smp in pending.Values) Marshal.ReleaseComObject(smp); pending.Clear(); }
                if (writer != null) Marshal.ReleaseComObject(writer);
                if (mfStarted) try { MF.MFShutdown(); } catch { }
                MF.TimeEndPeriod(1);
                clock.Stop();
            }
            if (cancel || Error != null || FramesWritten == 0) { try { File.Delete(Path); } catch { } }
            var h = Finished;
            if (h != null) h();
        }

        /// <summary>A thread k captura os frames k, k+N, k+2N… (N = número de threads).</summary>
        void CaptureLoop(int k, long frameTicks)
        {
            int size = outW * outH * 4;
            try
            {
                using (var frame = new DibFrame(outW, outH))
                {
                    long index = k;
                    while (!stop)
                    {
                        if (paused) { Thread.Sleep(10); continue; }
                        long now = Elapsed.Ticks;
                        long target = index * frameTicks;
                        if (now < target) { Thread.Sleep((int)Math.Max(1, Math.Min(20, (target - now) / 10000))); continue; }
                        // atrasado: saltar para o frame mais recente que pertence a esta thread
                        long cur = now / frameTicks;
                        if (cur - index >= Workers) index += (cur - index) / Workers * Workers;

                        lock (q) inProgress[k] = now;
                        IMFSample sample = null;
                        try
                        {
                            long t0 = Stopwatch.GetTimestamp();
                            Grab(frame);
                            sample = MakeSample(frame, size);
                            lock (q) GrabMs += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                        }
                        finally
                        {
                            lock (q)
                            {
                                if (sample != null)
                                {
                                    long ts = now;
                                    while (pending.ContainsKey(ts)) ts++;
                                    pending.Add(ts, sample);
                                }
                                inProgress[k] = long.MaxValue;
                                Monitor.PulseAll(q);
                            }
                        }
                        index += Workers;
                    }
                }
            }
            catch (Exception ex) { workerError = ex; stop = true; }
            finally
            {
                lock (q) { inProgress[k] = long.MaxValue; workersAlive--; Monitor.PulseAll(q); }
            }
        }

        IMFSinkWriter CreateWriter(out int stream)
        {
            Exception first = null;
            // 1.ª tentativa: perfil High com aceleração por hardware; 2.ª: valores por omissão
            for (int attempt = 0; attempt < 2; attempt++)
            {
                IMFAttributes attrs = null;
                IMFSinkWriter w = null;
                IMFMediaType outType = null, inType = null;
                try
                {
                    try { File.Delete(Path); } catch { }
                    MF.MFCreateAttributes(out attrs, 2);
                    if (attempt == 0) attrs.SetUINT32(ref MF.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
                    attrs.SetUINT32(ref MF.MF_SINK_WRITER_DISABLE_THROTTLING, 1);
                    MF.MFCreateSinkWriterFromURL(Path, IntPtr.Zero, attrs, out w);

                    MF.MFCreateMediaType(out outType);
                    outType.SetGUID(ref MF.MF_MT_MAJOR_TYPE, ref MF.MFMediaType_Video);
                    outType.SetGUID(ref MF.MF_MT_SUBTYPE, ref MF.MFVideoFormat_H264);
                    long bitrate = Math.Max(2000000L, Math.Min(24000000L, (long)outW * outH * Fps / 6));
                    outType.SetUINT32(ref MF.MF_MT_AVG_BITRATE, (int)bitrate);
                    outType.SetUINT32(ref MF.MF_MT_INTERLACE_MODE, 2); // progressivo
                    outType.SetUINT64(ref MF.MF_MT_FRAME_SIZE, Pack(outW, outH));
                    outType.SetUINT64(ref MF.MF_MT_FRAME_RATE, Pack(Fps, 1));
                    outType.SetUINT64(ref MF.MF_MT_PIXEL_ASPECT_RATIO, Pack(1, 1));
                    if (attempt == 0) outType.SetUINT32(ref MF.MF_MT_MPEG2_PROFILE, 100); // High
                    w.AddStream(outType, out stream);

                    MF.MFCreateMediaType(out inType);
                    inType.SetGUID(ref MF.MF_MT_MAJOR_TYPE, ref MF.MFMediaType_Video);
                    inType.SetGUID(ref MF.MF_MT_SUBTYPE, ref MF.MFVideoFormat_RGB32);
                    inType.SetUINT32(ref MF.MF_MT_INTERLACE_MODE, 2);
                    inType.SetUINT64(ref MF.MF_MT_FRAME_SIZE, Pack(outW, outH));
                    inType.SetUINT64(ref MF.MF_MT_FRAME_RATE, Pack(Fps, 1));
                    inType.SetUINT64(ref MF.MF_MT_PIXEL_ASPECT_RATIO, Pack(1, 1));
                    inType.SetUINT32(ref MF.MF_MT_DEFAULT_STRIDE, outW * 4); // de cima para baixo
                    w.SetInputMediaType(stream, inType, null);
                    var ok = w; w = null;
                    return ok;
                }
                catch (Exception ex) { if (first == null) first = ex; }
                finally
                {
                    if (w != null) Marshal.ReleaseComObject(w);
                    if (attrs != null) Marshal.ReleaseComObject(attrs);
                    if (outType != null) Marshal.ReleaseComObject(outType);
                    if (inType != null) Marshal.ReleaseComObject(inType);
                }
            }
            throw new InvalidOperationException(S.T("Não foi possível iniciar o codificador de vídeo do Windows. ", "Could not start the Windows video encoder. ") + first.Message, first);
        }

        static long Pack(int hi, int lo) { return ((long)hi << 32) | (uint)lo; }

        void Grab(DibFrame frame)
        {
            IntPtr src = Native.GetDC(IntPtr.Zero);
            try
            {
                // sem CAPTUREBLT: faz o cursor piscar e, com o compositor do Windows, as janelas em camadas já são incluídas
                const int SRCCOPY = 0x00CC0020;
                if (outW == Area.Width && outH == Area.Height)
                    Native.BitBlt(frame.Hdc, 0, 0, outW, outH, src, Area.X, Area.Y, SRCCOPY);
                else
                    MF.StretchBlt(frame.Hdc, 0, 0, outW, outH, src, Area.X, Area.Y, Area.Width, Area.Height, SRCCOPY);
                if (DrawCursor) DrawMouse(frame.Hdc);
                MF.GdiFlush();
            }
            finally { Native.ReleaseDC(IntPtr.Zero, src); }
        }

        static readonly Dictionary<IntPtr, Point> hotspots = new Dictionary<IntPtr, Point>();

        void DrawMouse(IntPtr hdc)
        {
            var ci = new MF.CURSORINFO { cbSize = Marshal.SizeOf(typeof(MF.CURSORINFO)) };
            if (!MF.GetCursorInfo(ref ci) || (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero) return;
            Point hot;
            lock (hotspots)
            {
                if (!hotspots.TryGetValue(ci.hCursor, out hot))
                {
                    MF.ICONINFO ii;
                    if (!MF.GetIconInfo(ci.hCursor, out ii)) return;
                    if (ii.hbmMask != IntPtr.Zero) MF.DeleteObject(ii.hbmMask);
                    if (ii.hbmColor != IntPtr.Zero) MF.DeleteObject(ii.hbmColor);
                    hot = new Point(ii.xHotspot, ii.yHotspot);
                    hotspots[ci.hCursor] = hot;
                }
            }
            double sx = outW / (double)Area.Width, sy = outH / (double)Area.Height;
            int x = (int)((ci.ptScreenPos.X - Area.X) * sx) - hot.X;
            int y = (int)((ci.ptScreenPos.Y - Area.Y) * sy) - hot.Y;
            if (x > outW || y > outH || x < -64 || y < -64) return;
            MF.DrawIconEx(hdc, x, y, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3 /*DI_NORMAL*/);
        }

        static IMFSample MakeSample(DibFrame frame, int size)
        {
            IMFMediaBuffer buf = null;
            IMFSample sample = null;
            try
            {
                MF.MFCreateMemoryBuffer(size, out buf);
                IntPtr dst; int max, cur;
                buf.Lock(out dst, out max, out cur);
                try { MF.CopyMemory(dst, frame.Bits, (uint)size); }
                finally { buf.Unlock(); }
                buf.SetCurrentLength(size);
                MF.MFCreateSample(out sample);
                sample.AddBuffer(buf);
                var ok = sample; sample = null;
                return ok;
            }
            finally
            {
                if (sample != null) Marshal.ReleaseComObject(sample);
                if (buf != null) Marshal.ReleaseComObject(buf);
            }
        }

        /// <summary>Bitmap GDI de 32 bits, de cima para baixo, com acesso direto aos pixels.</summary>
        sealed class DibFrame : IDisposable
        {
            public readonly IntPtr Hdc, Bits;
            readonly IntPtr bmp, old;

            public DibFrame(int w, int h)
            {
                var bi = new MF.BITMAPINFOHEADER { biSize = Marshal.SizeOf(typeof(MF.BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                Hdc = MF.CreateCompatibleDC(IntPtr.Zero);
                IntPtr bits;
                bmp = MF.CreateDIBSection(Hdc, ref bi, 0, out bits, IntPtr.Zero, 0);
                if (bmp == IntPtr.Zero) { MF.DeleteDC(Hdc); throw new OutOfMemoryException("CreateDIBSection"); }
                Bits = bits;
                old = MF.SelectObject(Hdc, bmp);
                MF.SetStretchBltMode(Hdc, 4 /*HALFTONE*/);
                MF.SetBrushOrgEx(Hdc, 0, 0, IntPtr.Zero);
            }

            public void Dispose()
            {
                MF.SelectObject(Hdc, old);
                MF.DeleteObject(bmp);
                MF.DeleteDC(Hdc);
            }
        }

        /// <summary>Exclui uma janela das capturas (Windows 10 2004+), para a barra e a moldura não aparecerem no vídeo.</summary>
        public static void ExcludeFromCapture(IntPtr hwnd)
        {
            try { MF.SetWindowDisplayAffinity(hwnd, 0x11 /*WDA_EXCLUDEFROMCAPTURE*/); } catch { }
        }
    }

    // ===================== interop: Media Foundation =====================

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09();
        void _10(); void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17();
        void SetUINT32(ref Guid key, int value);
        void SetUINT64(ref Guid key, long value);
        void _20();
        void SetGUID(ref Guid key, ref Guid value);
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09();
        void _10(); void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17();
        void SetUINT32(ref Guid key, int value);
        void SetUINT64(ref Guid key, long value);
        void _20();
        void SetGUID(ref Guid key, ref Guid value);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        void _00(); void _01(); void _02(); void _03(); void _04(); void _05(); void _06(); void _07(); void _08(); void _09();
        void _10(); void _11(); void _12(); void _13(); void _14(); void _15(); void _16(); void _17(); void _18(); void _19();
        void _20(); void _21(); void _22(); void _23(); void _24(); void _25(); void _26(); void _27(); void _28(); void _29();
        void _GetSampleFlags(); void _SetSampleFlags(); void _GetSampleTime();
        void SetSampleTime(long t);
        void _GetSampleDuration();
        void SetSampleDuration(long d);
        void _GetBufferCount(); void _GetBufferByIndex(); void _ConvertToContiguousBuffer();
        void AddBuffer(IMFMediaBuffer b);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        void Lock(out IntPtr p, out int max, out int cur);
        void Unlock();
        void GetCurrentLength(out int len);
        void SetCurrentLength(int len);
        void GetMaxLength(out int len);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSinkWriter
    {
        void AddStream(IMFMediaType type, out int stream);
        void SetInputMediaType(int stream, IMFMediaType type, IMFAttributes encodingParams);
        void BeginWriting();
        void WriteSample(int stream, IMFSample sample);
        void SendStreamTick(int stream, long t);
        void PlaceMarker(int stream, IntPtr ctx);
        void NotifyEndOfSegment(int stream);
        void Flush(int stream);
        void DoFinalize();
    }

    static class MF
    {
        public const int MF_VERSION = 0x00020070;

        public static Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static Guid MF_MT_AVG_BITRATE = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static Guid MF_MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static Guid MF_MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static Guid MF_MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static Guid MF_MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static Guid MF_MT_MPEG2_PROFILE = new Guid("ad76a80b-2d5c-4e0b-b375-64e520137036");
        public static Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static Guid MFVideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        public static Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");
        public static Guid MF_SINK_WRITER_DISABLE_THROTTLING = new Guid("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");

        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFStartup(int version, int flags);
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFShutdown();
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFCreateAttributes(out IMFAttributes a, int size);
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFCreateMediaType(out IMFMediaType t);
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFCreateMemoryBuffer(int max, out IMFMediaBuffer b);
        [DllImport("mfplat.dll", PreserveSig = false)] public static extern void MFCreateSample(out IMFSample s);
        [DllImport("mfreadwrite.dll", PreserveSig = false)]
        public static extern void MFCreateSinkWriterFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr byteStream, IMFAttributes attrs, out IMFSinkWriter w);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] public static extern void CopyMemory(IntPtr dst, IntPtr src, uint count);
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")] public static extern int TimeBeginPeriod(int ms);
        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")] public static extern int TimeEndPeriod(int ms);
        [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr prev);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr h);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bi, int usage, out IntPtr bits, IntPtr section, int offset);
        [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO ci);
        [DllImport("user32.dll")] public static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
        [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public Point ptScreenPos; }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }
    }

    /// <summary>Miniaturas de vídeo através do Explorador do Windows.</summary>
    static class VideoFile
    {
        public static readonly string[] Exts = { ".mp4" };

        public static bool Is(string path)
        {
            string e = (System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant();
            return Array.IndexOf(Exts, e) >= 0;
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { void GetImage(SIZE size, int flags, out IntPtr hbmp); }

        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr bc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory item);

        /// <summary>Imagem de pré-visualização do vídeo (ou null).</summary>
        public static Bitmap Thumbnail(string path, int size)
        {
            IShellItemImageFactory f = null;
            IntPtr hbmp = IntPtr.Zero;
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                // SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY
                f.GetImage(new SIZE { cx = size, cy = size }, 0x1 | 0x8, out hbmp);
                using (var b = Image.FromHbitmap(hbmp)) return new Bitmap(b);
            }
            catch { return null; }
            finally
            {
                if (hbmp != IntPtr.Zero) MF.DeleteObject(hbmp);
                if (f != null) Marshal.ReleaseComObject(f);
            }
        }

        /// <summary>Desenha o símbolo "play" ao centro de uma miniatura.</summary>
        public static void DrawPlayBadge(Graphics g, RectangleF r)
        {
            float d = Math.Max(22, Math.Min(48, Math.Min(r.Width, r.Height) * 0.32f));
            var c = new RectangleF(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
            var sm = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillEllipse(b, c);
            float t = d * 0.36f, cx = c.X + d / 2 + d * 0.05f, cy = c.Y + d / 2;
            g.FillPolygon(Brushes.White, new[] { new PointF(cx - t * 0.45f, cy - t * 0.6f), new PointF(cx - t * 0.45f, cy + t * 0.6f), new PointF(cx + t * 0.6f, cy) });
            g.SmoothingMode = sm;
        }
    }
}
