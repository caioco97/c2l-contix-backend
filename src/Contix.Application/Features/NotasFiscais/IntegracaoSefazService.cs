using System.Text;
using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.NotasFiscais.Sefaz;
using Contix.Domain.Entities;
using Contix.Domain.Exceptions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.NotasFiscais;

/// <summary>Certificado A1 da empresa e busca automática de NF-e na SEFAZ (uso do escritório).</summary>
public interface IIntegracaoSefazService
{
    Task<IntegracaoSefazDto> ObterAsync(Guid empresaId, CancellationToken ct = default);
    /// <summary>Envia (ou substitui) o certificado A1. Valida senha, chave privada, validade e CNPJ.</summary>
    Task<IntegracaoSefazDto> EnviarCertificadoAsync(Guid empresaId, EnviarCertificadoCommand command, CancellationToken ct = default);
    /// <summary>Remove o certificado e desliga a busca automática.</summary>
    Task<IntegracaoSefazDto> RemoverCertificadoAsync(Guid empresaId, CancellationToken ct = default);
    Task<IntegracaoSefazDto> SalvarAsync(Guid empresaId, SalvarIntegracaoSefazCommand command, CancellationToken ct = default);
    /// <summary>Antecipa a próxima busca (o job processa em seguida), respeitando a espera exigida pela SEFAZ.</summary>
    Task<IntegracaoSefazDto> SincronizarAgoraAsync(Guid empresaId, CancellationToken ct = default);
}

public sealed class IntegracaoSefazService(
    IIntegracaoSefazRepository repositorio,
    ISefazNFeGateway sefaz,
    ICriptografiaSegredos cripto,
    IAcessoEmpresaService acesso,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<IntegracaoSefazService> logger) : IIntegracaoSefazService
{
    public const long TamanhoMaximoCertificado = 64 * 1024;

    public async Task<IntegracaoSefazDto> ObterAsync(Guid empresaId, CancellationToken ct = default)
    {
        var empresa = await GarantirAcessoAsync(empresaId, ct);
        return await MontarAsync(empresa, ct);
    }

    public async Task<IntegracaoSefazDto> EnviarCertificadoAsync(Guid empresaId, EnviarCertificadoCommand command, CancellationToken ct = default)
    {
        var empresa = await GarantirAcessoAsync(empresaId, ct);
        if (empresa.Documento.Length != 14)
            Invalido("arquivo", "A busca de NF-e na SEFAZ usa certificado e-CNPJ: a empresa precisa ter CNPJ.");
        if (!command.NomeArquivo.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            && !command.NomeArquivo.EndsWith(".p12", StringComparison.OrdinalIgnoreCase))
            Invalido("arquivo", "Envie o certificado A1 no formato .pfx ou .p12.");
        if (command.Tamanho is <= 0 or > TamanhoMaximoCertificado)
            Invalido("arquivo", "Arquivo de certificado inválido (máximo de 64 KB).");
        if (string.IsNullOrEmpty(command.Senha))
            Invalido("senha", "Informe a senha do certificado.");

        byte[] pfx;
        await using (var stream = command.AbrirLeitura())
        using (var ms = new MemoryStream())
        {
            await stream.CopyToAsync(ms, ct);
            pfx = ms.ToArray();
        }

        var info = LeitorCertificadoA1.Ler(pfx, command.Senha!);
        var agora = clock.UtcNow;
        if (info.ValidoAte <= agora) throw new DomainException($"O certificado venceu em {info.ValidoAte:dd/MM/yyyy}.");
        if (info.ValidoDe > agora) throw new DomainException($"O certificado só passa a valer em {info.ValidoDe:dd/MM/yyyy}.");
        if (info.Cnpj is null)
            throw new DomainException("Não encontramos um CNPJ no certificado. Use um certificado e-CNPJ (A1) da empresa.");
        // A SEFAZ aceita o certificado da matriz para consultar as filiais: basta a mesma raiz de CNPJ.
        if (info.Cnpj[..8] != empresa.Documento[..8])
            throw new DomainException($"O certificado é do CNPJ {FormatarCnpj(info.Cnpj)}, que não pertence a esta empresa ({FormatarCnpj(empresa.Documento)}).");

        var anterior = await repositorio.ObterCertificadoAsync(empresaId, ignorarTenant: false, ct);
        if (anterior is not null) repositorio.RemoverCertificado(anterior);

        await repositorio.AdicionarCertificadoAsync(new CertificadoDigital
        {
            TenantId = empresa.TenantId, EmpresaId = empresaId,
            NomeArquivo = Limitar(Path.GetFileName(command.NomeArquivo), 255),
            Titular = Limitar(info.Titular, 300), Documento = info.Cnpj, Emissor = Limitar(info.Emissor, 300),
            NumeroSerie = Limitar(info.NumeroSerie, 100), Thumbprint = info.Thumbprint,
            ValidoDe = info.ValidoDe, ValidoAte = info.ValidoAte,
            ArquivoCriptografado = cripto.Proteger(pfx),
            SenhaCriptografada = cripto.Proteger(Encoding.UTF8.GetBytes(command.Senha!)),
            EnviadoPorId = currentUser.RequireUserId()
        }, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Certificado A1 {Thumbprint} (validade {Validade:yyyy-MM-dd}) enviado para a empresa {EmpresaId} pelo usuário {UsuarioId}",
            info.Thumbprint, info.ValidoAte, empresaId, currentUser.UserId);
        return await MontarAsync(empresa, ct);
    }

    public async Task<IntegracaoSefazDto> RemoverCertificadoAsync(Guid empresaId, CancellationToken ct = default)
    {
        var empresa = await GarantirAcessoAsync(empresaId, ct);
        var certificado = await repositorio.ObterCertificadoAsync(empresaId, ignorarTenant: false, ct)
                          ?? throw new NotFoundException("Certificado digital");
        repositorio.RemoverCertificado(certificado);
        if (await repositorio.ObterSincronizacaoAsync(empresaId, ct) is { } sinc) sinc.Ativa = false;
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Certificado A1 removido da empresa {EmpresaId} pelo usuário {UsuarioId}", empresaId, currentUser.UserId);
        return await MontarAsync(empresa, ct);
    }

    public async Task<IntegracaoSefazDto> SalvarAsync(Guid empresaId, SalvarIntegracaoSefazCommand command, CancellationToken ct = default)
    {
        var empresa = await GarantirAcessoAsync(empresaId, ct);
        if (command.Ativa)
        {
            var pendencias = await PendenciasAsync(empresa, ct);
            if (pendencias.Count > 0) Invalido("ativa", pendencias[0]);
        }

        var sinc = await repositorio.ObterSincronizacaoAsync(empresaId, ct);
        if (sinc is null)
        {
            sinc = new SincronizacaoNFe { TenantId = empresa.TenantId, EmpresaId = empresaId };
            await repositorio.AdicionarSincronizacaoAsync(sinc, ct);
        }
        var ligando = command.Ativa && !sinc.Ativa;
        sinc.Ativa = command.Ativa;
        sinc.CienciaAutomatica = command.CienciaAutomatica;
        if (ligando && !sinc.EmEsperaObrigatoria(clock.UtcNow)) sinc.ProximaConsultaEm = clock.UtcNow;
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Busca automática de NF-e {Estado} na empresa {EmpresaId} pelo usuário {UsuarioId}",
            sinc.Ativa ? "ligada" : "desligada", empresaId, currentUser.UserId);
        return await MontarAsync(empresa, ct);
    }

    public async Task<IntegracaoSefazDto> SincronizarAgoraAsync(Guid empresaId, CancellationToken ct = default)
    {
        var empresa = await GarantirAcessoAsync(empresaId, ct);
        var sinc = await repositorio.ObterSincronizacaoAsync(empresaId, ct);
        if (sinc is not { Ativa: true }) throw new DomainException("Ligue a busca automática de NF-e antes de buscar.");
        var pendencias = await PendenciasAsync(empresa, ct);
        if (pendencias.Count > 0) throw new DomainException(pendencias[0]);

        var agora = clock.UtcNow;
        if (sinc.EmEsperaObrigatoria(agora))
        {
            var liberada = sinc.UltimaConsultaEm!.Value + SincronizacaoNFe.EsperaSemDocumentos;
            var minutos = (int)Math.Ceiling((liberada - agora).TotalMinutes);
            throw new DomainException(
                $"A SEFAZ não tinha notas novas na última busca e exige 1 hora de espera entre consultas. Tente de novo em {minutos} min.");
        }
        sinc.ProximaConsultaEm = agora;
        await unitOfWork.SaveChangesAsync(ct);
        return await MontarAsync(empresa, ct);
    }

    private async Task<List<string>> PendenciasAsync(Empresa empresa, CancellationToken ct)
    {
        var pendencias = new List<string>();
        if (!sefaz.Habilitado)
            pendencias.Add("A integração com a SEFAZ não está configurada neste ambiente. Fale com o administrador do sistema.");
        if (empresa.Documento.Length != 14)
            pendencias.Add("A busca de NF-e na SEFAZ é feita por CNPJ: cadastre o CNPJ da empresa.");
        if (CodigosUf.Codigo(empresa.Endereco?.Uf) is null)
            pendencias.Add("Cadastre a UF do endereço da empresa (a SEFAZ pede a UF de quem consulta).");
        var cert = await repositorio.ObterCertificadoAsync(empresa.Id, ignorarTenant: false, ct);
        if (cert is null) pendencias.Add("Envie o certificado digital A1 da empresa.");
        else if (cert.VencidoEm(clock.UtcNow)) pendencias.Add($"O certificado digital venceu em {cert.ValidoAte:dd/MM/yyyy}. Envie um novo.");
        return pendencias;
    }

    private async Task<IntegracaoSefazDto> MontarAsync(Empresa empresa, CancellationToken ct)
    {
        var certificado = await repositorio.ObterCertificadoDtoAsync(empresa.Id, clock.UtcNow, ct);
        var sinc = await repositorio.ObterSincronizacaoAsync(empresa.Id, ct);
        var pendencias = await PendenciasAsync(empresa, ct);
        return new IntegracaoSefazDto(
            sefaz.Habilitado, sefaz.Habilitado ? sefaz.Nome : null, sefaz.Habilitado ? sefaz.Ambiente : null,
            certificado,
            sinc?.Ativa ?? false, sinc?.CienciaAutomatica ?? true,
            sinc?.UltimoNsu ?? 0, sinc?.MaxNsu ?? 0, sinc?.UltimaConsultaEm, sinc is { Ativa: true } ? sinc.ProximaConsultaEm : null,
            sinc?.UltimoStatus, sinc?.UltimaMensagem, sinc?.UltimaConsultaFalhou ?? false, sinc?.DocumentosRecebidos ?? 0,
            pendencias);
    }

    private async Task<Empresa> GarantirAcessoAsync(Guid empresaId, CancellationToken ct)
    {
        if (currentUser.EhCliente) throw new ForbiddenException("Somente o escritório configura a integração com a SEFAZ.");
        return await acesso.GarantirAcessoAsync(empresaId, ct);
    }

    private static string FormatarCnpj(string c) => c.Length == 14 ? $"{c[..2]}.{c[2..5]}.{c[5..8]}/{c[8..12]}-{c[12..]}" : c;
    private static string Limitar(string v, int max) => v.Length > max ? v[..max] : v;

    private static void Invalido(string campo, string mensagem) =>
        throw new ValidationException([new ValidationFailure(campo, mensagem)]);
}
