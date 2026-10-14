using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using static CatSaver.NativeMethods;

namespace CatSaver.Overlay;

/// <summary>
/// Base for borderless, always-on-top, per-pixel-alpha windows that never take
/// keyboard focus. Subclasses draw into <see cref="Draw"/>; frames are rendered
/// into a DIB section and pushed with UpdateLayeredWindow.
/// </summary>
internal abstract class LayeredWindow : Form
{
    private readonly Rectangle _bounds;
    private readonly bool _clickThrough;

    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _oldBitmap;
    private Bitmap? _surface;
    private Graphics? _graphics;

    /// <param name="bounds">Screen rectangle in physical pixels.</param>
    /// <param name="clickThrough">true = mouse clicks go to the windows underneath.</param>
    protected LayeredWindow(Rectangle bounds, bool clickThrough)
    {
        _bounds = bounds;
        _clickThrough = clickThrough;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = true;
        Text = "CatSaver";
    }

    protected Rectangle ScreenBounds => _bounds;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            if (_clickThrough) cp.ExStyle |= WS_EX_TRANSPARENT;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            // Clicking must not steal focus from the app the user is typing in.
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        CreateSurface();
    }

    private void CreateSurface()
    {
        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = _bounds.Width,
            biHeight = -_bounds.Height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };

        IntPtr screenDc = GetDC(IntPtr.Zero);
        try
        {
            _memDc = CreateCompatibleDC(screenDc);
            _dib = CreateDIBSection(screenDc, ref bmi, DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            _oldBitmap = SelectObject(_memDc, _dib);
            // GDI+ draws straight into the DIB memory in premultiplied ARGB, which is
            // exactly what UpdateLayeredWindow expects.
            _surface = new Bitmap(_bounds.Width, _bounds.Height, _bounds.Width * 4, PixelFormat.Format32bppPArgb, bits);
            _graphics = Graphics.FromImage(_surface);
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            _graphics.CompositingQuality = CompositingQuality.HighQuality;
            _graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Draws the window contents. The surface is already cleared.</summary>
    protected abstract void Draw(Graphics g, in CatFrame frame);

    /// <summary>Draws one frame and pushes it to the screen.</summary>
    public void RenderFrame(in CatFrame frame, float windowAlpha)
    {
        if (_graphics is null || IsDisposed) return;

        _graphics.ResetTransform();
        _graphics.Clear(Color.Transparent);
        Draw(_graphics, frame);
        _graphics.Flush(FlushIntention.Sync);

        IntPtr screenDc = GetDC(IntPtr.Zero);
        try
        {
            var dst = new POINT(_bounds.Left, _bounds.Top);
            var size = new SIZE(_bounds.Width, _bounds.Height);
            var src = new POINT(0, 0);
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                SourceConstantAlpha = (byte)Math.Clamp((int)(windowAlpha * 255), 0, 255),
                AlphaFormat = AC_SRC_ALPHA,
            };
            UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, _memDc, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Re-assert topmost so newly opened windows don't cover us.</summary>
    public void BringToTop()
    {
        if (IsHandleCreated)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _graphics?.Dispose();
            _surface?.Dispose();
        }
        if (_memDc != IntPtr.Zero)
        {
            SelectObject(_memDc, _oldBitmap);
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        if (_dib != IntPtr.Zero)
        {
            DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }
        base.Dispose(disposing);
    }
}
