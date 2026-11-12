namespace Contix.Domain.Enums;

/// <summary>Quem recebe a notícia no portal.</summary>
public enum PublicoNoticia
{
    /// <summary>Todas as empresas do escritório, inclusive as cadastradas depois da publicação.</summary>
    Todos = 1,
    EmpresasSelecionadas = 2,
    /// <summary>Empresas dos regimes tributários escolhidos.</summary>
    RegimeTributario = 3
}

/// <summary>Situação derivada das datas de publicação e expiração.</summary>
public enum SituacaoNoticia
{
    Agendada = 1,
    Publicada = 2,
    Expirada = 3
}
