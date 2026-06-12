using Netvan.Storage;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Netvan.Cli;

internal readonly record struct KeyValueLine(string Key, string Value, bool ValueIsMarkup = false);

internal static class ConsoleUi
{
  public static void WriteError(string message) =>
    AnsiConsole.MarkupLine($"[red]✗[/] {Escape(message)}");

  public static void WriteWarning(string message) =>
    AnsiConsole.MarkupLine($"[yellow]![/] {Escape(message)}");

  public static void WriteSuccess(string message) =>
    AnsiConsole.MarkupLine($"[green]✓[/] {Escape(message)}");

  public static void WriteNote(string message) =>
    AnsiConsole.MarkupLine($"[grey50]{Escape(message)}[/]");

  public static void WritePlain(string message) =>
    AnsiConsole.WriteLine(message);

  public static void WriteServiceResult(int exitCode, string message)
  {
    if (exitCode == 0)
      WriteSuccess(message);
    else
      WriteError(message);
  }

  public static void RenderSectionHeader(string title)
  {
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule($"[bold cyan]{Escape(title)}[/]").RuleStyle("grey37"));
    AnsiConsole.WriteLine();
  }

  public static void RenderKeyValues(IReadOnlyList<KeyValueLine> rows, string? caption = null)
  {
    AnsiConsole.Write(BuildKeyValueRenderable(rows, caption));
    AnsiConsole.WriteLine();
  }

  public static void RenderInfo(
    string version,
    DatabaseInfoRow info,
    string datetimeFormat)
  {
    RenderSectionHeader("Database");
    RenderKeyValues(
    [
      new("Version", version),
      new("Database", Path.GetFullPath(info.DatabasePath)),
      new("File size", ByteFormatter.FormatBytes(info.FileBytes)),
      new("Rows", info.RowCount.ToString("N0")),
      new("Apps", info.DistinctAppCount.ToString("N0")),
      new("First", CompactDateTime.FormatUtcAsLocal(info.FirstMinuteUtc)),
      new("Last", CompactDateTime.FormatUtcAsLocal(info.LastMinuteUtc)),
      new("Datetime input", $"{datetimeFormat} local (date-only yyMMdd → T0000)"),
    ]);
  }

  public static void RenderServiceStatus(
    bool installed,
    string? serviceName,
    string? displayName,
    string? status)
  {
    if (!installed)
    {
      RenderSectionHeader("Windows service");
      WriteWarning($"Service '{serviceName}' is not installed.");
      WriteNote("Install with: [cyan]netvan service install[/]");
      return;
    }

    RenderSectionHeader("Windows service");
    RenderKeyValues(
    [
      new("Service", serviceName ?? ""),
      new("Display", displayName ?? ""),
      new("Status", status ?? "unknown"),
    ]);
    WriteNote("Start: [cyan]netvan service start[/]  ·  Stop: [cyan]netvan service stop[/]");
  }

  public static void RenderUsageContext(string fromUtc, string toUtc, string targetLabel, bool includePrivate)
  {
    RenderSectionHeader("Query");
    RenderKeyValues(
    [
      new("Range", $"{CompactDateTime.FormatUtcAsLocal(fromUtc)} .. {CompactDateTime.FormatUtcAsLocal(toUtc)} [grey](inclusive, local)[/]", ValueIsMarkup: true),
      new("Target", targetLabel),
      new("Private IPs", includePrivate ? "[green]included[/]" : "[grey]excluded[/]", ValueIsMarkup: true),
    ]);
  }

  public static void RenderUsageTotals(long bytesSent, long bytesReceived)
  {
    RenderSectionHeader("Totals");
    var table = new Table().Border(TableBorder.Rounded).Expand();
    table.AddColumn(new TableColumn("[grey]Direction[/]").LeftAligned());
    table.AddColumn(new TableColumn("[grey]Bytes[/]").RightAligned());
    table.AddRow("Upload (sent)", $"[red]{ByteFormatter.FormatBytes(bytesSent)}[/]");
    table.AddRow("Download (recv)", $"[green]{ByteFormatter.FormatBytes(bytesReceived)}[/]");
    table.AddRow("[bold]Total[/]", $"[bold cyan]{ByteFormatter.FormatBytes(bytesSent + bytesReceived)}[/]");
    AnsiConsole.Write(table);
    AnsiConsole.WriteLine();
  }

  public static void RenderAppUsageTable(IReadOnlyList<AppUsageRow> rows)
  {
    RenderTrafficTable(
      "Applications",
      rows.Count,
      table =>
      {
        table.AddColumn(new TableColumn("[grey]Application[/]").LeftAligned());
        AddTrafficColumns(table);
        foreach (var row in rows)
          AddTrafficRow(table, row.AppName, row.BytesSent, row.BytesReceived);
      });
  }

  public static void RenderIpUsageTable(IReadOnlyList<IpUsageRow> rows)
  {
    RenderTrafficTable(
      "Remote IPs",
      rows.Count,
      table =>
      {
        table.AddColumn(new TableColumn("[grey]Remote IP[/]").LeftAligned());
        AddTrafficColumns(table);
        foreach (var row in rows)
          AddTrafficRow(table, row.RemoteIp, row.BytesSent, row.BytesReceived);
      });
  }

  public static void RenderHostUsageTable(IReadOnlyList<HostUsageRow> rows)
  {
    RenderTrafficTable(
      "Hosts",
      rows.Count,
      table =>
      {
        table.AddColumn(new TableColumn("[grey]Host[/]").LeftAligned());
        AddTrafficColumns(table);
        foreach (var row in rows)
          AddTrafficRow(table, row.HostName, row.BytesSent, row.BytesReceived);
      });
  }

  public static void RenderAppsList(IReadOnlyList<string> apps, string? filter)
  {
    RenderSectionHeader("Applications");
    if (filter is not null)
      WriteNote($"Filter: [cyan]{Escape(filter)}[/]");

    if (apps.Count == 0)
    {
      WriteNote("No applications found in the database.");
      return;
    }

    var table = CreateDataTable();
    table.AddColumn(new TableColumn("[grey]Name[/]").LeftAligned());
    foreach (var app in apps)
      table.AddRow(Escape(app));

    AnsiConsole.Write(table);
    WriteNote($"{apps.Count} application(s)");
    AnsiConsole.WriteLine();
  }

  public static void RunRealtimeLive(
    Func<RealtimeViewModel> loadSnapshot,
    TimeSpan refreshInterval,
    CancellationToken cancellationToken)
  {
    AnsiConsole.Live(new Markup(""))
      .AutoClear(false)
      .Overflow(VerticalOverflow.Ellipsis)
      .Cropping(VerticalOverflowCropping.Top)
      .Start(ctx =>
      {
        while (!cancellationToken.IsCancellationRequested)
        {
          ctx.UpdateTarget(BuildRealtimeView(loadSnapshot()));
          ctx.Refresh();

          try
          {
            Task.Delay(refreshInterval, cancellationToken).Wait(cancellationToken);
          }
          catch (OperationCanceledException)
          {
            break;
          }
        }
      });
  }

  public static IRenderable BuildRealtimeView(RealtimeViewModel model)
  {
    var sections = new List<IRenderable>
    {
      new Rule($"[bold cyan]{Escape("Windows")}[/]").RuleStyle("grey37"),
      BuildKeyValueRenderable(
      [
        new("Daily", $"{model.DailyStartLocal:yyyy-MM-dd HH:mm:ss} → {model.NowLocal:yyyy-MM-dd HH:mm:ss} local"),
        new("Weekly", $"{model.WeeklyStartLocal:yyyy-MM-dd HH:mm:ss} → {model.NowLocal:yyyy-MM-dd HH:mm:ss} local"),
        new("Monthly", $"{model.MonthlyStartLocal:yyyy-MM-dd HH:mm:ss} → {model.NowLocal:yyyy-MM-dd HH:mm:ss} local"),
      ]),
      new Rule($"[bold cyan]{Escape("Usage by app")}[/]").RuleStyle("grey37"),
    };

    if (model.Rows.Count == 0)
    {
      sections.Add(new Markup($"[grey50]{Escape("No traffic recorded for the current windows.")}[/]"));
    }
    else
    {
      var table = CreateDataTable();
      table.AddColumn(new TableColumn("[grey]App[/]").LeftAligned());
      table.AddColumn(new TableColumn("[grey]Download[/]").RightAligned());
      table.AddColumn(new TableColumn("[grey]Upload[/]").RightAligned());
      table.AddColumn(new TableColumn("[grey]Daily ↓/↑[/]").RightAligned());
      table.AddColumn(new TableColumn("[grey]Weekly ↓/↑[/]").RightAligned());
      table.AddColumn(new TableColumn("[grey]Monthly ↓/↑[/]").RightAligned());

      foreach (var row in model.Rows)
      {
        table.AddRow(
          Escape(row.AppName),
          $"[green]{ByteFormatter.FormatMegabytes(row.CurrentDownBytes)}[/]",
          $"[red]{ByteFormatter.FormatMegabytes(row.CurrentUpBytes)}[/]",
          ByteFormatter.FormatDownUpPair(row.DailyDownBytes, row.DailyUpBytes),
          ByteFormatter.FormatDownUpPair(row.WeeklyDownBytes, row.WeeklyUpBytes),
          ByteFormatter.FormatDownUpPair(row.MonthlyDownBytes, row.MonthlyUpBytes));
      }

      sections.Add(table);
    }

    sections.Add(new Markup(
      $"[grey50]Updated {model.NowLocal:yyyy-MM-dd HH:mm:ss} local · refresh {model.RefreshIntervalSeconds}s · Ctrl+C to exit[/]"));

    return new Rows(sections);
  }

  internal readonly record struct RealtimeViewModel(
    DateTime DailyStartLocal,
    DateTime WeeklyStartLocal,
    DateTime MonthlyStartLocal,
    DateTime NowLocal,
    int RefreshIntervalSeconds,
    IReadOnlyList<RealtimeUsageRow> Rows);

  internal readonly record struct RealtimeUsageRow(
    string AppName,
    long CurrentDownBytes,
    long CurrentUpBytes,
    long DailyDownBytes,
    long DailyUpBytes,
    long WeeklyDownBytes,
    long WeeklyUpBytes,
    long MonthlyDownBytes,
    long MonthlyUpBytes);

  private static void RenderTrafficTable(string title, int rowCount, Action<Table> configure)
  {
    RenderSectionHeader(title);
    if (rowCount == 0)
    {
      WriteNote("No matching traffic in this range.");
      return;
    }

    var table = CreateDataTable();
    configure(table);
    AnsiConsole.Write(table);
    WriteNote($"{rowCount} row(s)");
    AnsiConsole.WriteLine();
  }

  private static void AddTrafficColumns(Table table)
  {
    table.AddColumn(new TableColumn("[grey]Upload[/]").RightAligned());
    table.AddColumn(new TableColumn("[grey]Download[/]").RightAligned());
    table.AddColumn(new TableColumn("[grey]Total[/]").RightAligned());
  }

  private static void AddTrafficRow(Table table, string label, long bytesSent, long bytesReceived)
  {
    table.AddRow(
      Escape(label),
      $"[red]{ByteFormatter.FormatBytes(bytesSent)}[/]",
      $"[green]{ByteFormatter.FormatBytes(bytesReceived)}[/]",
      $"[cyan]{ByteFormatter.FormatBytes(bytesSent + bytesReceived)}[/]");
  }

  private static Table CreateKeyValueTable(string? caption)
  {
    var table = new Table().Border(TableBorder.None).Expand().HideHeaders();
    if (caption is not null)
      table.Title = new TableTitle($"[bold]{Escape(caption)}[/]");
    table.AddColumn(new TableColumn("").NoWrap().LeftAligned());
    table.AddColumn(new TableColumn("").LeftAligned());
    return table;
  }

  private static Table BuildKeyValueRenderable(IReadOnlyList<KeyValueLine> rows, string? caption = null)
  {
    var table = CreateKeyValueTable(caption);
    foreach (var row in rows)
    {
      var keyCell = new Markup($"[grey]{Escape(row.Key)}[/]");
      var valueCell = row.ValueIsMarkup
        ? new Markup(row.Value)
        : new Markup(Escape(row.Value));
      table.AddRow(keyCell, valueCell);
    }

    return table;
  }

  private static Table CreateDataTable() =>
    new Table()
      .Border(TableBorder.Rounded)
      .BorderColor(Spectre.Console.Color.Grey37)
      .Expand();

  private static string Escape(string? value) =>
    Markup.Escape(value ?? string.Empty);
}
