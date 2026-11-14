using Contix.Domain.Common;
using Contix.Domain.Enums;

namespace Contix.Domain.Entities;

/// <summary>Usuário da plataforma (escritório ou cliente).</summary>
public class Usuario : TenantEntity
{
    public const int MaxTentativasLogin = 5;
    public static readonly TimeSpan DuracaoBloqueio = TimeSpan.FromMinutes(15);

    public string Nome { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string SenhaHash { get; set; } = null!;
    public string? Telefone { get; set; }
    /// <summary>Foto de perfil no armazenamento de arquivos; nula quando o usuário não enviou foto.</summary>
    public string? FotoStorageKey { get; set; }
    public string? FotoContentType { get; set; }
    public DateTime? FotoAtualizadaEm { get; set; }
    public PerfilUsuario Perfil { get; set; }
    public bool Ativo { get; set; } = true;
    public int TentativasLoginFalhas { get; private set; }
    public DateTime? BloqueadoAte { get; private set; }
    public DateTime? UltimoLoginEm { get; private set; }
    /// <summary>Último convite de primeiro acesso enviado por e-mail.</summary>
    public DateTime? ConviteEnviadoEm { get; private set; }
    /// <summary>Quando o usuário definiu a própria senha pelo link do convite.</summary>
    public DateTime? ConviteAceitoEm { get; private set; }

    public Escritorio? Escritorio { get; set; }
    public ICollection<UsuarioEmpresa> Empresas { get; set; } = new List<UsuarioEmpresa>();

    /// <summary>Departamentos em que o colaborador atua (só para usuários do escritório).</summary>
    public List<Departamento> Departamentos { get; set; } = [];

    public bool EhDoEscritorio => Perfil is PerfilUsuario.Administrador or PerfilUsuario.Colaborador;

    /// <summary>Convidado por e-mail e ainda sem senha definida.</summary>
    public bool ConvitePendente => ConviteEnviadoEm is not null && ConviteAceitoEm is null;

    public void RegistrarConvite(DateTime agora)
    {
        if (ConviteAceitoEm is not null || UltimoLoginEm is not null)
            throw new Exceptions.DomainException("Este usuário já definiu a senha de acesso.");
        ConviteEnviadoEm = agora;
    }

    public bool EstaBloqueado(DateTime agora) => BloqueadoAte.HasValue && BloqueadoAte.Value > agora;

    public void RegistrarFalhaLogin(DateTime agora)
    {
        TentativasLoginFalhas++;
        if (TentativasLoginFalhas >= MaxTentativasLogin)
        {
            BloqueadoAte = agora.Add(DuracaoBloqueio);
            TentativasLoginFalhas = 0;
        }
    }

    /// <summary>Troca a senha (recuperação por e-mail) e libera um eventual bloqueio por tentativas.</summary>
    public void RedefinirSenha(string novoHash, DateTime agora)
    {
        SenhaHash = novoHash;
        if (ConviteEnviadoEm is not null) ConviteAceitoEm ??= agora;
        TentativasLoginFalhas = 0;
        BloqueadoAte = null;
    }

    /// <summary>Troca a senha a partir da tela de perfil (o usuário já informou a senha atual).</summary>
    public void TrocarSenha(string novoHash)
    {
        SenhaHash = novoHash;
        TentativasLoginFalhas = 0;
        BloqueadoAte = null;
    }

    public void RegistrarLoginComSucesso(DateTime agora)
    {
        TentativasLoginFalhas = 0;
        BloqueadoAte = null;
        UltimoLoginEm = agora;
    }
}
