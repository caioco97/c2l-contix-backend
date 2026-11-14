using System.Globalization;
using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empregados;
using Contix.Application.Features.Empresas;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;
using Contix.Domain.ValueObjects;

namespace Contix.Application.Features.Solicitacoes;

public enum FormatoPlanilha
{
    Xlsx = 1,
    Csv = 2
}

/// <summary>Linha com conteúdo e o número dela no arquivo (base 1, como o Excel mostra).</summary>
public sealed record LinhaPlanilha(int Numero, IReadOnlyList<string> Celulas);

/// <summary>Lê e gera planilhas simples (primeira aba; primeira linha com conteúdo = cabeçalho).</summary>
public interface IPlanilhaService
{
    /// <summary>
    /// Linhas com conteúdo (inclui o cabeçalho; as vazias são puladas e não contam no limite). Para de ler
    /// ao passar de <paramref name="maxLinhas"/> (devolve maxLinhas + 1). Lança <see cref="FormatException"/> se o arquivo não for legível.
    /// </summary>
    IReadOnlyList<LinhaPlanilha> Ler(Stream conteudo, FormatoPlanilha formato, int maxLinhas);
    byte[] Gerar(IReadOnlyList<string> cabecalho, IEnumerable<IReadOnlyList<string>> linhas, FormatoPlanilha formato, string nomeAba);
}

public sealed record ErroLinhaRubrica(int Linha, string Campo, string Mensagem);

/// <summary>Resultado da leitura: os lançamentos válidos (prévia) e os erros por linha. Nada é gravado.</summary>
public sealed record ImportacaoRubricasDto(int TotalLinhas, IReadOnlyList<LancamentoRubrica> Validos, IReadOnlyList<ErroLinhaRubrica> Erros);

/// <summary>Importação de rubricas da folha por planilha (CTX-77): modelo, leitura e validação linha a linha.</summary>
public interface IImportacaoRubricasService
{
    /// <summary>Planilha modelo já preenchida com os empregados ativos da empresa.</summary>
    Task<ArquivoParaDownload> ModeloAsync(Guid empresaId, FormatoPlanilha formato, CancellationToken ct = default);
    Task<ImportacaoRubricasDto> LerAsync(Guid empresaId, ArquivoUpload arquivo, CancellationToken ct = default);
}

public sealed class ImportacaoRubricasService(
    IAcessoEmpresaService acesso,
    IEmpregadoRepository empregados,
    IPlanilhaService planilhas) : IImportacaoRubricasService
{
    public const int MaxLinhas = 500;
    public const long TamanhoMaximo = 2 * 1024 * 1024;
    private static readonly string[] Cabecalho = ["CPF", "Matrícula", "Empregado", "Rubrica", "Quantidade", "Valor"];

    public async Task<ArquivoParaDownload> ModeloAsync(Guid empresaId, FormatoPlanilha formato, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
        var ativos = await empregados.ListarAtivosAsync(empresaId, ct);
        var linhas = ativos
            .Select(e => (IReadOnlyList<string>)[Cpf(e.Cpf), e.Matricula ?? "", e.Nome, "", "", ""])
            .DefaultIfEmpty(["000.000.000-00", "", "Nome do empregado", "Horas extras 50%", "10", ""]);
        var bytes = planilhas.Gerar(Cabecalho, linhas, formato, "Rubricas");
        return formato == FormatoPlanilha.Xlsx
            ? new ArquivoParaDownload(new MemoryStream(bytes), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "modelo-rubricas.xlsx")
            : new ArquivoParaDownload(new MemoryStream(bytes), "text/csv; charset=utf-8", "modelo-rubricas.csv");
    }

    public async Task<ImportacaoRubricasDto> LerAsync(Guid empresaId, ArquivoUpload arquivo, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.Solicitacoes, ct);
        if (arquivo.Tamanho > TamanhoMaximo) throw new DomainException("A planilha deve ter no máximo 2 MB.");
        var formato = Path.GetExtension(arquivo.NomeArquivo).ToLowerInvariant() switch
        {
            ".xlsx" => FormatoPlanilha.Xlsx,
            ".csv" or ".txt" => FormatoPlanilha.Csv,
            _ => throw new DomainException("Envie a planilha em .xlsx ou .csv (use o modelo).")
        };

        IReadOnlyList<LinhaPlanilha> linhas;
        try
        {
            await using var stream = arquivo.AbrirLeitura();
            // Cabeçalho + MaxLinhas lançamentos; o leitor para logo depois do limite (nunca uma prévia parcial).
            linhas = planilhas.Ler(stream, formato, MaxLinhas + 1);
        }
        catch (FormatException ex)
        {
            throw new DomainException(ex.Message);
        }
        if (linhas.Count == 0) throw new DomainException("A planilha está vazia.");

        var colunas = MapearColunas(linhas[0].Celulas);
        var dados = linhas.Skip(1).ToList();
        if (dados.Count > MaxLinhas) throw new DomainException($"A planilha pode ter no máximo {MaxLinhas} lançamentos.");

        // Só os empregados citados na planilha (no máximo 500 CPFs e 500 matrículas), sem teto de cadastro.
        string Coluna(LinhaPlanilha l, string nome) => colunas.TryGetValue(nome, out var i) && i < l.Celulas.Count ? l.Celulas[i].Trim() : "";
        var cpfs = dados.Select(l => DocumentoFiscal.SomenteDigitos(Coluna(l, "cpf"))).Where(c => c.Length > 0).Distinct().ToList();
        var matriculas = dados.Select(l => Coluna(l, "matricula")).Where(m => m.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var cadastro = await empregados.ListarPorCpfsOuMatriculasAsync(empresaId, cpfs, matriculas, ct);
        var porCpf = cadastro.Where(e => !string.IsNullOrEmpty(e.Cpf)).GroupBy(e => DocumentoFiscal.SomenteDigitos(e.Cpf)).ToDictionary(g => g.Key, g => g.First());
        var porMatricula = cadastro.Where(e => !string.IsNullOrWhiteSpace(e.Matricula))
            .GroupBy(e => e.Matricula!.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var validos = new List<LancamentoRubrica>();
        var erros = new List<ErroLinhaRubrica>();
        foreach (var linha in dados)
        {
            var numero = linha.Numero;
            string Celula(string nome) => Coluna(linha, nome);
            var errosDaLinha = new List<ErroLinhaRubrica>();

            var cpf = DocumentoFiscal.SomenteDigitos(Celula("cpf"));
            var matricula = Celula("matricula");
            Empregado? empregado = null;
            if (cpf.Length > 0) porCpf.TryGetValue(cpf, out empregado);
            if (empregado is null && matricula.Length > 0) porMatricula.TryGetValue(matricula, out empregado);
            if (empregado is null)
                errosDaLinha.Add(new(numero, "empregado", cpf.Length == 0 && matricula.Length == 0
                    ? "Informe o CPF ou a matrícula do empregado."
                    : "Empregado não encontrado nesta empresa (confira o CPF ou a matrícula)."));
            else if (empregado.EstaDesligado)
                errosDaLinha.Add(new(numero, "empregado", $"{empregado.Nome} está desligado."));

            var rubrica = Celula("rubrica");
            if (rubrica.Length == 0) errosDaLinha.Add(new(numero, "rubrica", "Informe a rubrica."));
            else if (rubrica.Length > 100) errosDaLinha.Add(new(numero, "rubrica", "A rubrica deve ter no máximo 100 caracteres."));

            var quantidade = Numero(Celula("quantidade"), numero, "quantidade", errosDaLinha);
            var valor = Numero(Celula("valor"), numero, "valor", errosDaLinha);
            if (quantidade is not > 0 && valor is not > 0 && !errosDaLinha.Any(e => e.Campo is "quantidade" or "valor"))
                errosDaLinha.Add(new(numero, "valor", "Informe a quantidade ou o valor (maior que zero)."));

            if (errosDaLinha.Count > 0) erros.AddRange(errosDaLinha);
            else validos.Add(new LancamentoRubrica(empregado!.Id, empregado.Nome, rubrica, quantidade is > 0 ? quantidade : null, valor is > 0 ? valor : null));
        }

        return new ImportacaoRubricasDto(dados.Count, validos, erros);
    }

    /// <summary>Localiza as colunas pelo cabeçalho (sem acento, sem diferenciar maiúsculas), na ordem do modelo como reserva.</summary>
    private static Dictionary<string, int> MapearColunas(IReadOnlyList<string> cabecalho)
    {
        var mapa = new Dictionary<string, int>();
        for (var i = 0; i < cabecalho.Count; i++)
        {
            var nome = Normalizar(cabecalho[i]);
            var chave = nome switch
            {
                "cpf" => "cpf",
                "matricula" => "matricula",
                "rubrica" or "evento" or "verba" => "rubrica",
                "quantidade" or "qtd" or "horas" or "referencia" => "quantidade",
                "valor" or "valor (r$)" or "valor r$" => "valor",
                _ => null
            };
            if (chave is not null) mapa.TryAdd(chave, i);
        }
        if (!mapa.ContainsKey("rubrica"))
            throw new DomainException("Cabeçalho não reconhecido. Use o modelo: CPF; Matrícula; Empregado; Rubrica; Quantidade; Valor.");
        return mapa;
    }

    private static decimal? Numero(string texto, int linha, string campo, List<ErroLinhaRubrica> erros)
    {
        if (texto.Length == 0) return null;
        var t = texto.Replace("R$", "").Replace(" ", "");
        // "1.234,56" (pt-BR) ou "1234.56" (Excel em inglês / número puro).
        var cultura = t.Contains(',') ? new CultureInfo("pt-BR") : CultureInfo.InvariantCulture;
        if (decimal.TryParse(t, NumberStyles.Number, cultura, out var v) && v >= 0) return Math.Round(v, 2);
        erros.Add(new(linha, campo, $"\"{texto}\" não é um número válido."));
        return null;
    }

    private static string Normalizar(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static string Cpf(string? cpf)
    {
        var d = DocumentoFiscal.SomenteDigitos(cpf);
        return d.Length == 11 ? $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}" : cpf ?? "";
    }
}
