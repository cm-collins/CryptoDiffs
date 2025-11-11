using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;

namespace CryptoDiffs;

/// <summary>
/// Service for generating Excel (.xlsx) reports from price difference calculations.
/// Creates formatted spreadsheets with metadata, headers, and period results.
/// </summary>
public class ExcelService
{
    private readonly ILogger<ExcelService> _logger;

    public ExcelService(ILogger<ExcelService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates an Excel workbook from PriceDiffResponse data.
    /// Creates a single sheet with metadata section and period results table.
    /// </summary>
    /// <param name="response">Price difference calculation results</param>
    /// <returns>Excel file as byte array (.xlsx format)</returns>
    public byte[] GenerateExcelReport(PriceDiffResponse response)
    {
        using var memoryStream = new MemoryStream();
        
        using (var spreadsheetDocument = SpreadsheetDocument.Create(memoryStream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = spreadsheetDocument.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);

            var sheets = spreadsheetDocument.WorkbookPart!.Workbook.AppendChild(new Sheets());
            var sheet = new Sheet
            {
                Id = spreadsheetDocument.WorkbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Price Differences"
            };
            sheets.Append(sheet);

            // Add styles
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();

            uint rowIndex = 1;

            // Metadata section
            rowIndex = AddMetadataSection(sheetData, response, rowIndex);

            // Empty row for spacing
            rowIndex++;

            // Header row
            rowIndex = AddHeaderRow(sheetData, rowIndex);

            // Data rows (one per period)
            foreach (var result in response.Results)
            {
                rowIndex = AddDataRow(sheetData, result, rowIndex);
            }

            // Auto-fit columns (approximate widths)
            var columns = new Columns();
            columns.Append(new Column { Min = 1, Max = 12, Width = 15, CustomWidth = true });
            worksheetPart.Worksheet.InsertBefore(columns, sheetData);
        }

        var excelBytes = memoryStream.ToArray();
        _logger.LogInformation("Generated Excel report with {RowCount} periods, size: {Size} bytes",
            response.Results.Count, excelBytes.Length);

        return excelBytes;
    }

    /// <summary>
    /// Adds metadata section at the top of the worksheet.
    /// </summary>
    private uint AddMetadataSection(SheetData sheetData, PriceDiffResponse response, uint startRow)
    {
        uint rowIndex = startRow;

        // Title row
        var titleRow = new Row { RowIndex = rowIndex };
        titleRow.AppendChild(CreateCell("A", rowIndex, "CryptoDiffs - Price Difference Report", CellValues.String, 2));
        sheetData.AppendChild(titleRow);
        rowIndex++;

        // Empty row
        rowIndex++;

        // Metadata rows
        var metadata = new[]
        {
            ("Symbol:", response.Symbol),
            ("As Of Date:", response.AsOf.ToString("yyyy-MM-dd HH:mm:ss UTC")),
            ("Interval:", response.Interval),
            ("Aggregate Method:", response.Aggregate),
            ("Generated:", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"))
        };

        foreach (var (label, value) in metadata)
        {
            var row = new Row { RowIndex = rowIndex };
            row.AppendChild(CreateCell("A", rowIndex, label, CellValues.String, 1));
            row.AppendChild(CreateCell("B", rowIndex, value, CellValues.String, 0));
            sheetData.AppendChild(row);
            rowIndex++;
        }

        // Notes section if available
        if (response.Notes != null && response.Notes.Count > 0)
        {
            rowIndex++;
            var notesTitleRow = new Row { RowIndex = rowIndex };
            notesTitleRow.AppendChild(CreateCell("A", rowIndex, "Notes:", CellValues.String, 1));
            sheetData.AppendChild(notesTitleRow);
            rowIndex++;

            foreach (var note in response.Notes)
            {
                var noteRow = new Row { RowIndex = rowIndex };
                noteRow.AppendChild(CreateCell("A", rowIndex, $"  • {note}", CellValues.String, 0));
                sheetData.AppendChild(noteRow);
                rowIndex++;
            }
        }

        return rowIndex;
    }

    /// <summary>
    /// Adds header row with column names.
    /// </summary>
    private uint AddHeaderRow(SheetData sheetData, uint rowIndex)
    {
        var headerRow = new Row { RowIndex = rowIndex };
        
        var headers = new[]
        {
            ("A", "Period (Days)"),
            ("B", "Start Date"),
            ("C", "End Date"),
            ("D", "Start Price"),
            ("E", "End Price"),
            ("F", "Absolute Change"),
            ("G", "Percentage Change (%)"),
            ("H", "High"),
            ("I", "Low"),
            ("J", "Volatility (%)")
        };

        foreach (var (column, header) in headers)
        {
            headerRow.AppendChild(CreateCell(column, rowIndex, header, CellValues.String, 3));
        }

        sheetData.AppendChild(headerRow);
        return rowIndex + 1;
    }

    /// <summary>
    /// Adds a data row for a single period result.
    /// </summary>
    private uint AddDataRow(SheetData sheetData, PeriodResult result, uint rowIndex)
    {
        var row = new Row { RowIndex = rowIndex };

        // Period (Days) - Integer
        row.AppendChild(CreateCell("A", rowIndex, result.Days.ToString(), CellValues.Number, 0));

        // Start Date - String
        row.AppendChild(CreateCell("B", rowIndex, result.StartCandle, CellValues.String, 0));

        // End Date - String
        row.AppendChild(CreateCell("C", rowIndex, result.EndCandle, CellValues.String, 0));

        // Start Price - Number with 2 decimals
        row.AppendChild(CreateCell("D", rowIndex, result.StartPrice.ToString("F2"), CellValues.Number, 4));

        // End Price - Number with 2 decimals
        row.AppendChild(CreateCell("E", rowIndex, result.EndPrice.ToString("F2"), CellValues.Number, 4));

        // Absolute Change - Number with 2 decimals, color-coded
        var absChangeStyle = result.AbsChange >= 0 ? 5U : 6U; // Green for positive, red for negative
        row.AppendChild(CreateCell("F", rowIndex, result.AbsChange.ToString("F2"), CellValues.Number, absChangeStyle));

        // Percentage Change - Number with 2 decimals, color-coded
        var pctChangeStyle = result.PctChange >= 0 ? 5U : 6U;
        row.AppendChild(CreateCell("G", rowIndex, result.PctChange.ToString("F4"), CellValues.Number, pctChangeStyle));

        // High - Number with 2 decimals
        row.AppendChild(CreateCell("H", rowIndex, result.High.ToString("F2"), CellValues.Number, 4));

        // Low - Number with 2 decimals
        row.AppendChild(CreateCell("I", rowIndex, result.Low.ToString("F2"), CellValues.Number, 4));

        // Volatility - Number with 2 decimals (if available)
        if (result.Volatility.HasValue)
        {
            row.AppendChild(CreateCell("J", rowIndex, result.Volatility.Value.ToString("F2"), CellValues.Number, 4));
        }
        else
        {
            row.AppendChild(CreateCell("J", rowIndex, "N/A", CellValues.String, 0));
        }

        sheetData.AppendChild(row);
        return rowIndex + 1;
    }

    /// <summary>
    /// Helper method to create a cell with proper reference.
    /// </summary>
    private Cell CreateCell(string columnName, uint rowIndex, string value, CellValues dataType, uint styleIndex)
    {
        return new Cell
        {
            CellReference = $"{columnName}{rowIndex}",
            CellValue = new CellValue(value),
            DataType = dataType,
            StyleIndex = styleIndex
        };
    }

    /// <summary>
    /// Creates stylesheet with formatting for titles, headers, and data cells.
    /// </summary>
    private Stylesheet CreateStylesheet()
    {
        var stylesheet = new Stylesheet();

        // Numbering formats
        var numberingFormats = new NumberingFormats();
        numberingFormats.AppendChild(new NumberingFormat
        {
            NumberFormatId = 164U,
            FormatCode = "#,##0.00"
        });
        numberingFormats.Count = 1;

        // Fonts
        var fonts = new Fonts();
        fonts.AppendChild(new Font()); // Default font (index 0)
        fonts.AppendChild(new Font(new Bold())); // Bold font (index 1)
        fonts.AppendChild(new Font(new Bold(), new FontSize { Val = 14 })); // Title font (index 2)
        fonts.AppendChild(new Font(new Bold(), new FontSize { Val = 11 })); // Header font (index 3)
        fonts.Count = 4;

        // Fills
        var fills = new Fills();
        fills.AppendChild(new Fill(new PatternFill { PatternType = PatternValues.None })); // Default (index 0)
        fills.AppendChild(new Fill(new PatternFill { PatternType = PatternValues.Gray125 })); // Gray (index 1)
        fills.AppendChild(new Fill(new PatternFill
        {
            PatternType = PatternValues.Solid,
            ForegroundColor = new ForegroundColor { Rgb = "FFE2EFDA" } // Light green
        })); // Light green (index 2)
        fills.AppendChild(new Fill(new PatternFill
        {
            PatternType = PatternValues.Solid,
            ForegroundColor = new ForegroundColor { Rgb = "FFFFE2E2" } // Light red
        })); // Light red (index 3)
        fills.Count = 4;

        // Borders
        var borders = new Borders();
        borders.AppendChild(new Border()); // Default (index 0)
        borders.AppendChild(new Border(
            new LeftBorder(),
            new RightBorder(),
            new TopBorder(),
            new BottomBorder(new Color { Auto = true })
        )); // Bottom border (index 1)
        borders.Count = 2;

        // Cell style formats
        var cellStyleFormats = new CellStyleFormats();
        cellStyleFormats.AppendChild(new CellFormat()); // Default (index 0)
        cellStyleFormats.Count = 1;

        // Cell formats
        var cellFormats = new CellFormats();
        cellFormats.AppendChild(new CellFormat()); // Default (index 0)
        cellFormats.AppendChild(new CellFormat { FontId = 1, ApplyFont = true }); // Bold (index 1)
        cellFormats.AppendChild(new CellFormat { FontId = 2, ApplyFont = true }); // Title (index 2)
        cellFormats.AppendChild(new CellFormat
        {
            FontId = 3,
            FillId = 1,
            BorderId = 1,
            ApplyFont = true,
            ApplyFill = true,
            ApplyBorder = true
        }); // Header (index 3)
        cellFormats.AppendChild(new CellFormat
        {
            NumberFormatId = 164U,
            ApplyNumberFormat = true
        }); // Number with 2 decimals (index 4)
        cellFormats.AppendChild(new CellFormat
        {
            NumberFormatId = 164U,
            FontId = 0,
            FillId = 2,
            ApplyNumberFormat = true,
            ApplyFill = true
        }); // Positive number with green background (index 5)
        cellFormats.AppendChild(new CellFormat
        {
            NumberFormatId = 164U,
            FontId = 0,
            FillId = 3,
            ApplyNumberFormat = true,
            ApplyFill = true
        }); // Negative number with red background (index 6)
        cellFormats.Count = 7;

        stylesheet.Append(numberingFormats);
        stylesheet.Append(fonts);
        stylesheet.Append(fills);
        stylesheet.Append(borders);
        stylesheet.Append(cellStyleFormats);
        stylesheet.Append(cellFormats);

        var cellStyles = new CellStyles();
        cellStyles.AppendChild(new CellStyle
        {
            Name = "Normal",
            FormatId = 0,
            BuiltinId = 0
        });
        cellStyles.Count = 1;
        stylesheet.Append(cellStyles);

        var differentialFormats = new DifferentialFormats();
        differentialFormats.Count = 0;
        stylesheet.Append(differentialFormats);

        var tableStyles = new TableStyles
        {
            Count = 0,
            DefaultTableStyle = "TableStyleMedium2",
            DefaultPivotStyle = "PivotStyleLight16"
        };
        stylesheet.Append(tableStyles);

        return stylesheet;
    }
}

