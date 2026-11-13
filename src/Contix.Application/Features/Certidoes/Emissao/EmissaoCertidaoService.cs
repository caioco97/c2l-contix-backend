using Contix.Application.Common.Abstractions;
using Contix.Application.Common.Exceptions;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Usuarios;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;

namespace Contix.Application.Features.Certidoes.Emissao;

/// <summary>Configuração da emissão automática por empresa, pedido de emissão imediata e histórico (uso do escritório).</summary>
public interface IEmissaoCertidaoService
{
    Task<ConfiguracaoEmissaoDto> ObterConfiguracaoAsync(Guid empresaId, CancellationToken ct = default);
    Task<ConfiguracaoEmissaoDto> SalvarConfiguracaoAsync(Guid empresaId, SalvarConfiguracaoEmissaoCommand command, CancellationToken ct = default);
    /// <summary>Coloca na fila a emissão dos tipos pedidos. O job processa em seguida.</summary>
    Task<IReadOnlyList<EmissaoCertidaoDto>> EmitirAgoraAsync(Guid empresaId, EmitirCertidoesCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<EmissaoCertidaoDto>> ListarAsync(Guid empresaId, int limite, CancellationToken ct = default);
}

public sealed class EmissaoCertidaoService(
    IEmissaoCertidaoRepository emissoes,
    IUsuarioRepository usuarios,
    IAcessoEmpresaService acesso,
    IEmissorCertidao emissor,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IValidator<SalvarConfiguracaoEmissaoCommand> validator,
    ILogger<EmissaoCertidaoService> logger) : IEmissaoCertidaoService
{
    public async Task<ConfiguracaoEmissaoDto> ObterConfiguracaoAsync(Guid empresaId, CancellationToken ct = default)
    {
        await GarantirAcessoAsync(empresaId, ct);
        var config = await emissoes.ObterConfiguracaoAsync(empresaId, ct);
        string? responsavel = null;
        if (config?.ResponsavelId is { } id) responsavel = (await usuarios.GetByIdAsync(id, ct))?.Nome;
        return ToDto(config, responsavel);
    }

    public async Task<ConfiguracaoEmissaoDto> SalvarConfiguracaoAsync(Guid empresaId, SalvarConfiguracaoEmissaoCommand command, CancellationToken ct = default)
    {
        await validator.ValidateAndThrowAsync(command, ct);
        var empresa = await GarantirAcessoAsync(empresaId, ct);

        var tipos = command.Tipos.Distinct().Order().ToList();
        var naoSuportados = tipos.Where(t => !emissor.TiposSuportados.Contains(t)).ToList();
        if (naoSuportados.Count > 0)
            Invalido("tipos", $"A emissão automática ainda não cobre: {string.Join(", ", naoSuportados.Select(CatalogoCertidoes.Nome))}.");
        if (command.Ativa && !emissor.Habilitado)
            Invalido("ativa", "A emissão automática não está configurada neste ambiente. Fale com o administrador do sistema.");

        Usuario? responsavel = null;
        if (command.ResponsavelId is { } responsavelId)
        {
            responsavel = await usuarios.GetByIdAsync(responsavelId, ct);
            if (responsavel is null || !responsavel.EhDoEscritorio || !responsavel.Ativo)
                Invalido("responsavelId", "Selecione um colaborador ativo do escritório.");
        }

        var config = await emissoes.ObterConfiguracaoAsync(empresaId, ct);
        if (config is null)
        {
            config = new ConfiguracaoEmissaoCertidao { TenantId = empresa.TenantId, EmpresaId = empresaId };
            await emissoes.AdicionarConfiguracaoAsync(config, ct);
        }
        config.Ativa = command.Ativa;
        config.Tipos = tipos;
        config.DefinirAntecedencia(command.DiasAntecedencia);
        config.ResponsavelId = responsavel?.Id;
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Emissão automática de CND {Estado} na empresa {EmpresaId} ({Tipos}) pelo usuário {UsuarioId}",
            config.Ativa ? "ativada" : "desativada", empresaId, string.Join(",", tipos), currentUser.UserId);
        return ToDto(config, responsavel?.Nome);
    }

    public async Task<IReadOnlyList<EmissaoCertidaoDto>> EmitirAgoraAsync(Guid empresaId, EmitirCertidoesCommand command, CancellationToken ct = default)
    {
        var empresa = await GarantirAcessoAsync(empresaId, ct);
        if (!emissor.Habilitado)
            Invalido("tipos", "A emissão automática não está configurada neste ambiente. Fale com o administrador do sistema.");

        IReadOnlyList<TipoCertidao> tipos = command.Tipos is { Count: > 0 } pedidos
            ? pedidos.Distinct().ToList()
            : (await emissoes.ObterConfiguracaoAsync(empresaId, ct)) is { Tipos.Count: > 0 } config
                ? config.Tipos
                : emissor.TiposSuportados.ToList();
        if (tipos.Any(t => !Enum.IsDefined(t))) Invalido("tipos", "Tipo de certidão inválido.");
        var naoSuportados = tipos.Where(t => !emissor.TiposSuportados.Contains(t)).ToList();
        if (naoSuportados.Count > 0)
            Invalido("tipos", $"A emissão automática ainda não cobre: {string.Join(", ", naoSuportados.Select(CatalogoCertidoes.Nome))}.");

        // Um tipo que já está na fila não é duplicado.
        var emAberto = await emissoes.TiposEmAbertoAsync(empresaId, ct);
        var agora = clock.UtcNow;
        var usuarioId = currentUser.RequireUserId();
        foreach (var tipo in tipos.Where(t => !emAberto.Contains(t)).Order())
        {
            await emissoes.AddAsync(new EmissaoCertidao
            {
                TenantId = empresa.TenantId, EmpresaId = empresaId, Tipo = tipo, Origem = OrigemEmissaoCertidao.Manual,
                ProximaTentativaEm = agora, SolicitadoPorId = usuarioId
            }, ct);
        }
        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Emissão de CND pedida na empresa {EmpresaId} ({Tipos}) pelo usuário {UsuarioId}",
            empresaId, string.Join(",", tipos), usuarioId);

        var recentes = await emissoes.ListarAsync(empresaId, 50, ct);
        return recentes.Where(e => tipos.Contains(e.Tipo) && e.Status == StatusEmissaoCertidao.Pendente).ToList();
    }

    public async Task<IReadOnlyList<EmissaoCertidaoDto>> ListarAsync(Guid empresaId, int limite, CancellationToken ct = default)
    {
        await GarantirAcessoAsync(empresaId, ct);
        return await emissoes.ListarAsync(empresaId, Math.Clamp(limite, 1, 200), ct);
    }

    private ConfiguracaoEmissaoDto ToDto(ConfiguracaoEmissaoCertidao? c, string? responsavelNome) => new(
        emissor.Habilitado,
        emissor.Habilitado ? emissor.Nome : null,
        emissor.TiposSuportados.Order().ToList(),
        c?.Ativa ?? false,
        c?.Tipos.Order().ToList() ?? emissor.TiposSuportados.Order().ToList(),
        c?.DiasAntecedencia ?? ConfiguracaoEmissaoCertidao.AntecedenciaPadrao,
        c?.ResponsavelId,
        responsavelNome);

    private async Task<Empresa> GarantirAcessoAsync(Guid empresaId, CancellationToken ct)
    {
        if (currentUser.EhCliente) throw new ForbiddenException("Somente o escritório emite certidões.");
        return await acesso.GarantirAcessoAsync(empresaId, ct);
    }

    private static void Invalido(string campo, string mensagem) =>
        throw new ValidationException([new ValidationFailure(campo, mensagem)]);
}
