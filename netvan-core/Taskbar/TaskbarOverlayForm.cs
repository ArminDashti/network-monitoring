using Netvan.Cli;
using Netvan.Storage;

namespace Netvan.Taskbar;

internal sealed class TaskbarOverlayForm : Form
{
  private static readonly Color UploadColor = Color.FromArgb(255, 220, 60);
  private static readonly Color DownloadColor = Color.FromArgb(255, 150, 40);
  private static readonly Color TransparentKey = Color.FromArgb(255, 0, 255);

  private readonly System.Windows.Forms.Timer _refreshTimer;
  private readonly System.Windows.Forms.Timer _positionTimer;
  private readonly string _databasePath;
  private readonly Font _speedFont;
  private long _uploadBytesPerSecond;
  private long _downloadBytesPerSecond;

  public TaskbarOverlayForm()
  {
    _databasePath = NetmConfig.Load().ResolvedDatabasePath;
    var refreshSeconds = Math.Max(1, NetmConfig.Load().SamplingIntervalSeconds);

    _speedFont = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);

    FormBorderStyle = FormBorderStyle.None;
    ShowInTaskbar = false;
    StartPosition = FormStartPosition.Manual;
    TopMost = true;
    BackColor = TransparentKey;
    TransparencyKey = TransparentKey;
    ClientSize = new Size(72, 36);
    Text = "NetM";

    _refreshTimer = new System.Windows.Forms.Timer { Interval = refreshSeconds * 1000 };
    _refreshTimer.Tick += (_, _) => RefreshSpeeds();
    _refreshTimer.Start();

    _positionTimer = new System.Windows.Forms.Timer { Interval = 2000 };
    _positionTimer.Tick += (_, _) => Reposition();
    _positionTimer.Start();

    Load += (_, _) =>
    {
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

    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

    DrawSpeedRow(e.Graphics, y: 2, pointingUp: true, UploadColor, _uploadBytesPerSecond);
    DrawSpeedRow(e.Graphics, y: 18, pointingUp: false, DownloadColor, _downloadBytesPerSecond);
  }

  private void DrawSpeedRow(Graphics graphics, int y, bool pointingUp, Color color, long bytesPerSecond)
  {
    var triangle = new Point[]
    {
      new(2, y + (pointingUp ? 6 : 0)),
      new(8, y + (pointingUp ? 0 : 6)),
      new(14, y + (pointingUp ? 6 : 0)),
    };

    using var brush = new SolidBrush(color);
    graphics.FillPolygon(brush, triangle);

    var text = NetworkSpeedFormatter.FormatMegabitsPerSecond(bytesPerSecond);
    graphics.DrawString(text, _speedFont, brush, 18, y - 1);
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
    var location = TaskbarNative.GetWidgetLocation(Width, Height);
    if (Location != location)
      Location = location;

    TaskbarNative.KeepTopMost(Handle);
  }
}
