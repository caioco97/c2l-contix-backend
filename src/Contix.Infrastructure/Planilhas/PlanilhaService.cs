using System.Globalization;
using System.Text;
using Contix.Application.Features.Solicitacoes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Contix.Infrastructure.Planilhas;

/// <summary>Planilhas simples: .xlsx (OpenXML, leitura em streaming) e CSV (";" ou ",", UTF-8 ou Latin-1).</summary>
public sealed class PlanilhaService : IPlanilhaService
{
    public IReadOnlyList<IReadOnlyList<string>> Ler(Stream conteudo, FormatoPlanilha formato, int maxLinhas)
    {
        // O OpenXML precisa de stream com seek; o arquivo já é pequeno (limite de tamanho no serviço).
        using var buffer = new MemoryStream();
        conteudo.CopyTo(buffer);
        buffer.Position = 0;
        return formato == FormatoPlanilha.Xlsx ? LerXlsx(buffer, maxLinhas) : LerCsv(buffer, maxLinhas);
    }

    public byte[] Gerar(IReadOnlyList<string> cabecalho, IEnumerable<IReadOnlyList<string>> linhas, FormatoPlanilha formato, string nomeAba) =>
        formato == FormatoPlanilha.Xlsx ? GerarXlsx(cabecalho, linhas, nomeAba) : GerarCsv(cabecalho, linhas);

    private static List<IReadOnlyList<string>> LerXlsx(Stream stream, int maxLinhas)
    {
        SpreadsheetDocument doc;
        try { doc = SpreadsheetDocument.Open(stream, false); }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FileFormatException)
        {
            throw new FormatException("Não foi possível ler o arquivo .xlsx. Salve a planilha novamente no Excel e tente de novo.", ex);
        }

        using (doc)
        {
            var wb = doc.WorkbookPart ?? throw new FormatException("Planilha sem abas.");
            var primeira = wb.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw new FormatException("Planilha sem abas.");
            var aba = (WorksheetPart)wb.GetPartById(primeira.Id!);
            var compartilhadas = wb.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(i => i.InnerText).ToArray() ?? [];

            var resultado = new List<IReadOnlyList<string>>();
            using var reader = OpenXmlReader.Create(aba);
            while (reader.Read())
            {
                if (reader.ElementType != typeof(Row) || !reader.IsStartElement) continue;
                var row = (Row)reader.LoadCurrentElement()!;
                var celulas = new List<string>();
                foreach (var c in row.Elements<Cell>())
                {
                    var col = IndiceColuna(c.CellReference?.Value);
                    if (col < 0) col = celulas.Count;
                    while (celulas.Count < col) celulas.Add("");
                    celulas.Add(Valor(c, compartilhadas));
                }
                // Linhas vazias no meio contam para o número da linha exibido nos erros.
                var numero = (int)(row.RowIndex?.Value ?? (uint)(resultado.Count + 1));
                while (resultado.Count < numero - 1) resultado.Add([]);
                resultado.Add(celulas);
                if (resultado.Count > maxLinhas) break;
            }
            return resultado;
        }
    }

    private static string Valor(Cell c, string[] compartilhadas)
    {
        var bruto = c.CellValue?.Text ?? c.InlineString?.InnerText ?? "";
        if (c.DataType?.Value == CellValues.SharedString)
            return int.TryParse(bruto, out var i) && i >= 0 && i < compartilhadas.Length ? compartilhadas[i] : "";
        if (c.DataType?.Value == CellValues.Boolean) return bruto == "1" ? "VERDADEIRO" : "FALSO";
        return bruto;
    }

    /// <summary>"C12" → 2 (base zero).</summary>
    private static int IndiceColuna(string? referencia)
    {
        if (string.IsNullOrEmpty(referencia)) return -1;
        var n = 0;
        foreach (var ch in referencia)
        {
            if (!char.IsLetter(ch)) break;
            n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return n - 1;
    }

    private static List<IReadOnlyList<string>> LerCsv(MemoryStream stream, int maxLinhas)
    {
        var bytes = stream.ToArray();
        string texto;
        try { texto = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { texto = Encoding.Latin1.GetString(bytes); }
        texto = texto.TrimStart('﻿');

        var primeiraLinha = texto.Split('\n', 2)[0];
        var separador = primeiraLinha.Count(ch => ch == ';') >= primeiraLinha.Count(ch => ch == ',') ? ';' : ',';

        var linhas = new List<IReadOnlyList<string>>();
        var atual = new List<string>();
        var campo = new StringBuilder();
        var aspas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var ch = texto[i];
            if (aspas)
            {
                if (ch == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                else if (ch == '"') aspas = false;
                else campo.Append(ch);
            }
            else if (ch == '"') aspas = true;
            else if (ch == separador) { atual.Add(campo.ToString()); campo.Clear(); }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++;
                atual.Add(campo.ToString()); campo.Clear();
                linhas.Add(atual); atual = [];
                if (linhas.Count > maxLinhas) return linhas;
            }
            else campo.Append(ch);
        }
        if (campo.Length > 0 || atual.Count > 0) { atual.Add(campo.ToString()); linhas.Add(atual); }
        return linhas;
    }

    private static byte[] GerarCsv(IReadOnlyList<string> cabecalho, IEnumerable<IReadOnlyList<string>> linhas)
    {
        var sb = new StringBuilder("﻿");
        void Linha(IEnumerable<string> cels) => sb.Append(string.Join(';', cels.Select(Escapar))).Append("\r\n");
        Linha(cabecalho);
        foreach (var l in linhas) Linha(l);
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Escapar(string v)
    {
        // Evita injeção de fórmula ao abrir no Excel.
        if (v.Length > 0 && "=+-@".Contains(v[0]) && !decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) v = "'" + v;
        return v.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    private static byte[] GerarXlsx(IReadOnlyList<string> cabecalho, IEnumerable<IReadOnlyList<string>> linhas, string nomeAba)
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wb = doc.AddWorkbookPart();
            wb.Workbook = new Workbook();
            var estilos = wb.AddNewPart<WorkbookStylesPart>();
            estilos.Stylesheet = new Stylesheet(
                new Fonts(new Font(), new Font(new Bold())),
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })),
                new Borders(new Border()),
                new CellFormats(new CellFormat(), new CellFormat { FontId = 1, ApplyFont = true }));

            var aba = wb.AddNewPart<WorksheetPart>();
            var dados = new SheetData();
            var larguras = cabecalho.Select(h => Math.Max(12, h.Length + 4)).ToArray();
            dados.Append(Linha(1, cabecalho, negrito: true));
            uint n = 2;
            foreach (var l in linhas)
            {
                for (var i = 0; i < l.Count && i < larguras.Length; i++) larguras[i] = Math.Min(60, Math.Max(larguras[i], l[i].Length + 2));
                dados.Append(Linha(n++, l, negrito: false));
            }
            var colunas = new Columns(larguras.Select((w, i) => new Column { Min = (uint)i + 1, Max = (uint)i + 1, Width = w, CustomWidth = true }));
            aba.Worksheet = new Worksheet(colunas, dados);
            wb.Workbook.AppendChild(new Sheets(new Sheet { Id = wb.GetIdOfPart(aba), SheetId = 1, Name = nomeAba.Length > 31 ? nomeAba[..31] : nomeAba }));
            wb.Workbook.Save();
        }
        return ms.ToArray();
    }

    private static Row Linha(uint numero, IReadOnlyList<string> valores, bool negrito)
    {
        var row = new Row { RowIndex = numero };
        for (var i = 0; i < valores.Count; i++)
        {
            var cell = new Cell
            {
                CellReference = $"{Coluna(i)}{numero}",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(valores[i]) { Space = SpaceProcessingModeValues.Preserve })
            };
            if (negrito) cell.StyleIndex = 1;
            row.Append(cell);
        }
        return row;
    }

    private static string Coluna(int i)
    {
        var s = "";
        for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s;
        return s;
    }
}
