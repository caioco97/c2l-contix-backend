using System.Text;
using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Common.Models;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.NotasFiscais.Sefaz;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using Contix.Domain.Exceptions;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.NotasFiscais;

/// <summary>NF-e destinadas à empresa: lista, manifestação do destinatário, XML e importação manual.</summary>
public interface INotaFiscalService
{
    Task<PagedResult<NotaFiscalDto>> ListarAsync(Guid empresaId, NotaFiscalFiltro filtro, CancellationToken ct = default);
    Task<ResumoNotasFiscaisDto> ResumoAsync(Guid empresaId, CancellationToken ct = default);
    Task<NotaFiscalDto> ObterAsync(Guid empresaId, Guid notaId, CancellationToken ct = default);
    /// <summary>Registra a manifestação. Com certificado e integração, envia o evento à SEFAZ antes de gravar.</summary>
    Task<NotaFiscalDto> ManifestarAsync(Guid empresaId, Guid notaId, ManifestarNotaFiscalCommand command, CancellationToken ct = default);
    Task<ArquivoParaDownload> BaixarXmlAsync(Guid empresaId, Guid notaId, CancellationToken ct = default);
    /// <summary>Importação manual de XMLs (escritório).</summary>
    Task<ImportacaoNFeDto> ImportarAsync(Guid empresaId, IReadOnlyList<ArquivoImportacaoNFe> arquivos, CancellationToken ct = default);
}

public sealed class NotaFiscalService(
    INotaFiscalRepository notas,
    IIntegracaoSefazRepository integracao,
    RegistroNotasFiscais registro,
    ISefazNFeGateway sefaz,
    ICriptografiaSegredos cripto,
    IFileStorage storage,
    IAcessoEmpresaService acesso,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IValidator<ManifestarNotaFiscalCommand> validator,
    ILogger<NotaFiscalService> logger) : INotaFiscalService
{
    public const int MaximoArquivosImportacao = 100;
    public const long TamanhoMaximoXml = 5 * 1024 * 1024;

    public async Task<PagedResult<NotaFiscalDto>> ListarAsync(Guid empresaId, NotaFiscalFiltro filtro, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.NFe, ct);
        return await notas.ListarAsync(empresaId, filtro, ct);
    }

    public async Task<ResumoNotasFiscaisDto> ResumoAsync(Guid empresaId, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.NFe, ct);
        return await notas.ResumoAsync(empresaId, ct);
    }

    public async Task<NotaFiscalDto> ObterAsync(Guid empresaId, Guid notaId, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.NFe, ct);
        return await notas.ObterDtoAsync(empresaId, notaId, ct) ?? throw new NotFoundException("NF-e", notaId);
    }

    public async Task<NotaFiscalDto> ManifestarAsync(Guid empresaId, Guid notaId, ManifestarNotaFiscalCommand command, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var (empresa, _) = await acesso.GarantirModuloAsync(empresaId, ModuloPortal.NFe, ct);
        var nota = await notas.ObterAsync(empresaId, notaId, ct) ?? throw new NotFoundException("NF-e", notaId);
        nota.GarantirPodeManifestar(command.Tipo, command.Justificativa);

        string? protocolo = null;
        var certificado = sefaz.Habilitado ? await integracao.ObterCertificadoAsync(empresaId, ignorarTenant: false, ct) : null;
        if (certificado is not null && !certificado.VencidoEm(clock.UtcNow) && empresa.Documento.Length == 14)
        {
            RetornoEvento retorno;
            try
            {
                retorno = await sefaz.ManifestarAsync(Abrir(certificado, cripto), empresa.Documento, nota.ChaveAcesso,
                    command.Tipo, command.Justificativa?.Trim(), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Falha de comunicação com a SEFAZ ao manifestar a NF-e {NotaId}", nota.Id);
                throw new DomainException("Não foi possível falar com a SEFAZ agora. Tente novamente em alguns minutos.");
            }
            if (!retorno.Registrado)
                throw new DomainException($"A SEFAZ recusou a manifestação ({retorno.CStat}): {retorno.Motivo}");
            protocolo = retorno.Protocolo ?? retorno.CStat;
        }

        nota.Manifestar(command.Tipo, command.Justificativa, currentUser.RequireUserId(), protocolo, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("NF-e {NotaId} manifestada como {Tipo} pelo usuário {UsuarioId} (SEFAZ: {Protocolo})",
            nota.Id, command.Tipo, currentUser.UserId, protocolo ?? "não enviada");
        return (await notas.ObterDtoAsync(empresaId, notaId, ct))!;
    }

    public async Task<ArquivoParaDownload> BaixarXmlAsync(Guid empresaId, Guid notaId, CancellationToken ct = default)
    {
        await acesso.GarantirModuloAsync(empresaId, ModuloPortal.NFe, ct);
        var nota = await notas.ObterAsync(empresaId, notaId, ct) ?? throw new NotFoundException("NF-e", notaId);
        if (nota.XmlStorageKey is null) throw new NotFoundException("XML da NF-e");
        var stream = await storage.OpenReadAsync(nota.XmlStorageKey, ct);
        var sufixo = nota.XmlCompleto ? "-nfe" : "-resumo";
        return new ArquivoParaDownload(stream, "application/xml", $"{nota.ChaveAcesso}{sufixo}.xml");
    }

    public async Task<ImportacaoNFeDto> ImportarAsync(Guid empresaId, IReadOnlyList<ArquivoImportacaoNFe> arquivos, CancellationToken ct = default)
    {
        if (currentUser.EhCliente) throw new ForbiddenException("Somente o escritório importa XML de NF-e.");
        var empresa = await acesso.GarantirAcessoAsync(empresaId, ct);
        if (arquivos.Count == 0)
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("arquivos", "Envie ao menos um arquivo XML.")]);
        if (arquivos.Count > MaximoArquivosImportacao)
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("arquivos", $"Envie no máximo {MaximoArquivosImportacao} arquivos por vez.")]);

        var resultados = new List<ArquivoImportadoDto>();
        var chavesNoLote = new HashSet<string>();
        foreach (var arquivo in arquivos)
        {
            if (!arquivo.NomeArquivo.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                resultados.Add(new(arquivo.NomeArquivo, ResultadoArquivoNFe.Erro, null, "Envie arquivos .xml."));
                continue;
            }
            if (arquivo.Tamanho > TamanhoMaximoXml)
            {
                resultados.Add(new(arquivo.NomeArquivo, ResultadoArquivoNFe.Erro, null, "Arquivo maior que 5 MB."));
                continue;
            }

            string xml;
            await using (var stream = arquivo.AbrirLeitura())
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                xml = await reader.ReadToEndAsync(ct);

            DocumentoNFe documento;
            try { documento = LeitorXmlNFe.Ler(xml); }
            catch (FormatException ex)
            {
                resultados.Add(new(arquivo.NomeArquivo, ResultadoArquivoNFe.Erro, null, ex.Message));
                continue;
            }
            if (documento is not NotaLida { Completa: true } lida)
            {
                resultados.Add(new(arquivo.NomeArquivo, ResultadoArquivoNFe.Erro, null, "Importe o XML completo da NF-e (nfeProc)."));
                continue;
            }
            if (!chavesNoLote.Add(lida.Dados.ChaveAcesso))
            {
                resultados.Add(new(arquivo.NomeArquivo, ResultadoArquivoNFe.Ignorada, lida.Dados.ChaveAcesso, "Arquivo repetido neste envio."));
                continue;
            }

            var r = await registro.RegistrarAsync(empresa, documento, xml, OrigemNotaFiscal.ImportacaoXml, null, ignorarTenant: false, ct);
            resultados.Add(new(arquivo.NomeArquivo, r.Resultado switch
            {
                ResultadoRegistroNFe.Criada => ResultadoArquivoNFe.Importada,
                ResultadoRegistroNFe.Atualizada => ResultadoArquivoNFe.Atualizada,
                _ => ResultadoArquivoNFe.Ignorada
            }, lida.Dados.ChaveAcesso, r.Motivo));
        }

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await registro.DescartarArquivosAsync();
            throw;
        }
        await registro.ConfirmarAsync();

        var dto = new ImportacaoNFeDto(
            resultados.Count(r => r.Resultado == ResultadoArquivoNFe.Importada),
            resultados.Count(r => r.Resultado == ResultadoArquivoNFe.Atualizada),
            resultados.Count(r => r.Resultado == ResultadoArquivoNFe.Ignorada),
            resultados.Count(r => r.Resultado == ResultadoArquivoNFe.Erro),
            resultados);
        logger.LogInformation("Importação de XML de NF-e na empresa {EmpresaId} pelo usuário {UsuarioId}: {Importadas} novas, {Atualizadas} atualizadas, {Ignoradas} ignoradas, {Erros} com erro",
            empresaId, currentUser.UserId, dto.Importadas, dto.Atualizadas, dto.Ignoradas, dto.Erros);
        return dto;
    }

    /// <summary>Descriptografa o certificado A1 guardado.</summary>
    public static CertificadoA1 Abrir(CertificadoDigital certificado, ICriptografiaSegredos cripto) => new(
        cripto.Desproteger(certificado.ArquivoCriptografado),
        Encoding.UTF8.GetString(cripto.Desproteger(certificado.SenhaCriptografada)));
}
