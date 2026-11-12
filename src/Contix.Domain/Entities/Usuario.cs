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
    public PerfilUsuario Perfil { get; set; }
    public bool Ativo { get; set; } = true;
    public int TentativasLoginFalhas { get; private set; }
    public DateTime? BloqueadoAte { get; private set; }
    public DateTime? UltimoLoginEm { get; private set; }

    public Escritorio? Escritorio { get; set; }
    public ICollection<UsuarioEmpresa> Empresas { get; set; } = new List<UsuarioEmpresa>();

    public bool EhDoEscritorio => Perfil is PerfilUsuario.Administrador or PerfilUsuario.Colaborador;

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
    public void RedefinirSenha(string novoHash)
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
