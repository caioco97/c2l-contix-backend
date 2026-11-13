using Contix.Application.Common.Abstractions;
using Contix.Domain.Entities;
using Contix.Domain.Enums;
using FluentValidation;

namespace Contix.Application.Features.Certidoes.Emissao;

/// <summary>Configuração da emissão automática da empresa, com o que a integração do ambiente suporta.</summary>
/// <param name="IntegracaoHabilitada">false: o ambiente não tem integrador configurado (não emite nada).</param>
public sealed record ConfiguracaoEmissaoDto(
    bool IntegracaoHabilitada,
    string? Provedor,
    IReadOnlyList<TipoCertidao> TiposSuportados,
    bool Ativa,
    IReadOnlyList<TipoCertidao> Tipos,
    int DiasAntecedencia,
    Guid? ResponsavelId,
    string? ResponsavelNome);

public sealed record SalvarConfiguracaoEmissaoCommand(bool Ativa, IReadOnlyList<TipoCertidao> Tipos, int DiasAntecedencia, Guid? ResponsavelId);

public sealed class SalvarConfiguracaoEmissaoCommandValidator : AbstractValidator<SalvarConfiguracaoEmissaoCommand>
{
    public SalvarConfiguracaoEmissaoCommandValidator()
    {
        RuleFor(x => x.Tipos).NotNull();
        RuleForEach(x => x.Tipos).IsInEnum();
        RuleFor(x => x.Tipos).NotEmpty().When(x => x.Ativa).WithMessage("Escolha ao menos um tipo de certidão para emitir.");
        RuleFor(x => x.DiasAntecedencia).InclusiveBetween(0, ConfiguracaoEmissaoCertidao.AntecedenciaMaxima).WithName("Antecedência");
    }
}

/// <summary>Pedido de emissão imediata. Sem tipos: todos os configurados (ou, sem configuração, todos os suportados).</summary>
public sealed record EmitirCertidoesCommand(IReadOnlyList<TipoCertidao>? Tipos);

public sealed record EmissaoCertidaoDto(
    Guid Id, Guid EmpresaId, TipoCertidao Tipo, string TipoNome, OrigemEmissaoCertidao Origem, StatusEmissaoCertidao Status,
    int Tentativas, DateTime? ProximaTentativaEm, DateTime CreatedAt, DateTime? ConcluidaEm, string? Provedor, string? Mensagem,
    Guid? CertidaoId, string? SolicitadoPorNome);

/// <summary>Configuração ativa com a situação atual de cada tipo, para o agendamento decidir o que emitir.</summary>
public sealed record AgendamentoEmissao(
    Guid TenantId, Guid EmpresaId, ConfiguracaoEmissaoCertidao Configuracao,
    IReadOnlyDictionary<TipoCertidao, (DateOnly Validade, SituacaoCertidao Situacao)> Atuais,
    IReadOnlySet<TipoCertidao> ComEmissaoRecente);

public interface IEmissaoCertidaoRepository : IRepository<EmissaoCertidao>
{
    /* Na requisição (respeitam o tenant). */
    Task<ConfiguracaoEmissaoCertidao?> ObterConfiguracaoAsync(Guid empresaId, CancellationToken ct = default);
    Task AdicionarConfiguracaoAsync(ConfiguracaoEmissaoCertidao configuracao, CancellationToken ct = default);
    Task<IReadOnlyList<EmissaoCertidaoDto>> ListarAsync(Guid empresaId, int limite, CancellationToken ct = default);
    /// <summary>Tipos com emissão na fila para a empresa.</summary>
    Task<IReadOnlySet<TipoCertidao>> TiposEmAbertoAsync(Guid empresaId, CancellationToken ct = default);

    /* No job (entre tenants). */
    /// <summary>
    /// Configurações ativas de empresas e escritórios ativos, com a certidão atual de cada tipo e os tipos que já
    /// tiveram emissão desde <paramref name="recenteDesde"/> ou que estão na fila.
    /// </summary>
    Task<IReadOnlyList<AgendamentoEmissao>> ListarAgendamentosAsync(DateTime recenteDesde, CancellationToken ct = default);
    /// <summary>Emissões na fila cuja hora chegou, rastreadas e com a empresa carregada.</summary>
    Task<IReadOnlyList<EmissaoCertidao>> ListarProntasAsync(DateTime agora, int limite, CancellationToken ct = default);
    Task<ConfiguracaoEmissaoCertidao?> ObterConfiguracaoIgnorandoTenantAsync(Guid empresaId, CancellationToken ct = default);
}
