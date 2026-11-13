namespace Contix.Domain.Enums;

/// <summary>Quem publicou o documento: o escritório (para o cliente) ou o cliente (para o escritório).</summary>
public enum OrigemDocumento
{
    Escritorio = 1,
    Cliente = 2
}

/// <summary>Ações registradas no histórico (auditoria) de um documento.</summary>
public enum TipoEventoDocumento
{
    Upload = 1,
    Visualizacao = 2,
    Download = 3,
    Envio = 4,
    Exclusao = 5
}
