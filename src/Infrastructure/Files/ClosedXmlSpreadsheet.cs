using ClosedXML.Excel;
using PharmaTrack.Application.Common;

namespace PharmaTrack.Infrastructure.Files;

public sealed class ClosedXmlSpreadsheet : ISpreadsheet
{
    public byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<object?[]> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(sheetName);

        for (var column = 0; column < headers.Count; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
        sheet.Row(1).Style.Font.Bold = true;

        var rowNumber = 2;
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
                sheet.Cell(rowNumber, column + 1).Value = XLCellValue.FromObject(ToCellValue(row[column]));
            rowNumber++;
        }

        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public IReadOnlyList<string?[]> Read(Stream file, int columnCount)
    {
        using var workbook = new XLWorkbook(file);
        var rows = new List<string?[]>();
        foreach (var row in workbook.Worksheet(1).RowsUsed().Skip(1))
        {
            rows.Add(Enumerable.Range(1, columnCount)
                .Select(column => CellText(row.Cell(column)))
                .ToArray());
        }

        return rows;
    }

    private static object? ToCellValue(object? value) => value switch
    {
        DateOnly date => date.ToDateTime(TimeOnly.MinValue),
        DateTimeOffset moment => moment.UtcDateTime,
        Enum e => e.ToString(),
        Guid id => id.ToString(),
        _ => value
    };

    private static string? CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        return cell.DataType switch
        {
            XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd"),
            XLDataType.Number => cell.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => cell.GetString().Trim()
        };
    }
}
