using System.Drawing.Drawing2D;
using Netvan.Storage;

namespace Netvan.Taskbar;

internal sealed class TaskbarOverlayForm : Form
{
  private static readonly Color UploadColor = Color.FromArgb(255, 220, 60);
  private static readonly Color DownloadColor = Color.FromArgb(255, 150, 40);
  private static readonly Color BackgroundColor = Color.FromArgb(28, 28, 30);

  private readonly System.Windows.Forms.Timer _refreshTimer;
  private readonly System.Windows.Forms.Timer _positionTimer;
  private readonly string _databasePath;
  private readonly Font _speedFont;
  private bool _attachedToTaskbar;
  private long _uploadBytesPerSecond;
  private long _downloadBytesPerSecond;

  public TaskbarOverlayForm()
  {
    _databasePath = NetvanConfig.Load().ResolvedDatabasePath;

    _speedFont = new Font("Segoe UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);

    FormBorderStyle = FormBorderStyle.None;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.Manual;
    TopMost = true;
    BackColor = BackgroundColor;
    ClientSize = new Size(86, 34);
    Text = "Netvan";
    DoubleBuffered = true;

    _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
    _refreshTimer.Tick += (_, _) => RefreshSpeeds();
    _refreshTimer.Start();

    _positionTimer = new System.Windows.Forms.Timer { Interval = 2000 };
    _positionTimer.Tick += (_, _) => Reposition();
    _positionTimer.Start();

    Load += (_, _) =>
    {
      _attachedToTaskbar = TaskbarNative.TryAttachToTaskbar(Handle);
      if (_attachedToTaskbar)
        TopMost = false;

      Reposition();
      RefreshSpeeds();
      TaskbarWidgetManager.WriteState(new TaskbarState(Environment.ProcessId, DateTime.UtcNow));
    };

    FormClosed += (_, _) =>
    {
      _refreshTimer.Stop();
      _positionTimer.Stop();
      _speedFont.Dispose();
    };
  }

  protected override CreateParams CreateParams
  {
    get
    {
      const int WsExToolWindow = 0x00000080;
      const int WsExNoActivate = 0x08000000;
      var cp = base.CreateParams;
      cp.ExStyle |= WsExToolWindow | WsExNoActivate;
      return cp;
    }
  }

  protected override void OnPaint(PaintEventArgs e)
  {
    base.OnPaint(e);

    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
    e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

    using var path = new GraphicsPath();
    var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
    var radius = 6;
    path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
    path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
    path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
    path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
    path.CloseFigure();

    using (var background = new SolidBrush(BackgroundColor))
      e.Graphics.FillPath(background, path);

    DrawSpeedRow(e.Graphics, y: 2, pointingUp: true, UploadColor, _uploadBytesPerSecond);
    DrawSpeedRow(e.Graphics, y: 17, pointingUp: false, DownloadColor, _downloadBytesPerSecond);
  }

  private void DrawSpeedRow(Graphics graphics, int y, bool pointingUp, Color color, long bytesPerSecond)
  {
    var triangle = new Point[]
    {
      new(6, y + (pointingUp ? 6 : 0)),
      new(12, y + (pointingUp ? 0 : 6)),
      new(18, y + (pointingUp ? 6 : 0)),
    };

    using var brush = new SolidBrush(color);
    graphics.FillPolygon(brush, triangle);

    var text = NetworkSpeedFormatter.FormatMegabitsPerSecond(bytesPerSecond);
    graphics.DrawString(text, _speedFont, brush, 22, y - 1);
  }

  private void RefreshSpeeds()
  {
    try
    {
      var nowUtc = TrafficStore.FormatBucketUtc(DateTime.UtcNow);
      using var store = new TrafficStore(_databasePath);
      var totals = store.UsageTotalsInRangeUtc(
        nowUtc,
        nowUtc,
        new UsageTarget(UsageTargetKind.All, null),
        includePrivate: true);

      _uploadBytesPerSecond = totals.BytesSent;
      _downloadBytesPerSecond = totals.BytesReceived;
    }
    catch
    {
      _uploadBytesPerSecond = 0;
      _downloadBytesPerSecond = 0;
    }

    Invalidate();
  }

  private void Reposition()
  {
    var location = _attachedToTaskbar
      ? TaskbarNative.GetWidgetLocationClient(Width, Height)
      : TaskbarNative.GetWidgetLocationScreen(Width, Height);

    if (Location != location)
      Location = location;

    if (!_attachedToTaskbar)
      TaskbarNative.KeepTopMost(Handle);
  }
}
