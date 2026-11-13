using Contix.Domain.Common;

namespace Contix.Domain.Entities;

/// <summary>
/// Busca automática de NF-e da empresa na SEFAZ (Distribuição DF-e): se está ligada e até qual NSU já foi lida.
/// </summary>
public class SincronizacaoNFe : TenantEntity
{
    /// <summary>Sem documentos novos (cStat 137) ou consumo indevido (656): a SEFAZ exige 1 hora de espera.</summary>
    public static readonly TimeSpan EsperaSemDocumentos = TimeSpan.FromHours(1);
    /// <summary>Espera depois de uma falha de comunicação ou rejeição inesperada.</summary>
    public static readonly TimeSpan EsperaAposFalha = TimeSpan.FromMinutes(15);
    /// <summary>Intervalo normal entre buscas quando a fila da SEFAZ foi esvaziada.</summary>
    public static readonly TimeSpan IntervaloPadrao = TimeSpan.FromHours(1);

    public Guid EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    public bool Ativa { get; set; }
    /// <summary>Registra a Ciência da Operação automaticamente nas notas novas, para a SEFAZ liberar o XML completo.</summary>
    public bool CienciaAutomatica { get; set; } = true;

    public long UltimoNsu { get; private set; }
    public long MaxNsu { get; private set; }
    public DateTime? ProximaConsultaEm { get; set; }
    public DateTime? UltimaConsultaEm { get; private set; }
    /// <summary>Código de status (cStat) do último retorno da SEFAZ.</summary>
    public string? UltimoStatus { get; private set; }
    public string? UltimaMensagem { get; private set; }
    public bool UltimaConsultaFalhou { get; private set; }
    /// <summary>Total de documentos recebidos da SEFAZ.</summary>
    public int DocumentosRecebidos { get; private set; }

    /// <summary>A SEFAZ pede espera (sem documentos novos ou consumo indevido) e ela ainda não passou?</summary>
    public bool EmEsperaObrigatoria(DateTime agora) =>
        UltimoStatus is "137" or "656" && UltimaConsultaEm is { } u && agora < u + EsperaSemDocumentos;

    /// <summary>Registra o retorno de uma consulta e decide quando consultar de novo.</summary>
    public void RegistrarRetorno(string cStat, string? motivo, long ultimoNsu, long maxNsu, int documentos, DateTime agora)
    {
        UltimaConsultaEm = agora;
        UltimoStatus = cStat;
        UltimaMensagem = Limitar(motivo);
        UltimaConsultaFalhou = cStat is not ("137" or "138");
        if (ultimoNsu > UltimoNsu) UltimoNsu = ultimoNsu;
        if (maxNsu > 0) MaxNsu = maxNsu;
        DocumentosRecebidos += documentos;

        ProximaConsultaEm = cStat switch
        {
            "138" when UltimoNsu < MaxNsu => agora,
            "138" => agora + IntervaloPadrao,
            "137" or "656" => agora + EsperaSemDocumentos,
            _ => agora + EsperaAposFalha
        };
    }

    public void RegistrarFalha(string mensagem, DateTime agora)
    {
        UltimaConsultaEm = agora;
        UltimoStatus = null;
        UltimaMensagem = Limitar(mensagem);
        UltimaConsultaFalhou = true;
        ProximaConsultaEm = agora + EsperaAposFalha;
    }

    /// <summary>Volta a ler a SEFAZ do início (os últimos 3 meses que ela guarda). Notas já gravadas não duplicam.</summary>
    public void Reiniciar()
    {
        UltimoNsu = 0;
        MaxNsu = 0;
    }

    private static string? Limitar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim() is { Length: > 500 } t ? t[..500] : texto.Trim();
}
