using Contix.Application.Features.Auth;
using Contix.Application.Features.Certidoes;
using Contix.Application.Features.Documentos;
using Contix.Application.Features.Empresas;
using Contix.Application.Features.Escritorios;
using Contix.Application.Features.Notificacoes;
using Contix.Application.Features.Noticias;
using Contix.Application.Features.Pastas;
using Contix.Application.Features.Solicitacoes;
using Contix.Application.Features.Usuarios;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Contix.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ValidatorOptions.Global.LanguageManager.Culture = new System.Globalization.CultureInfo("pt-BR");
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEscritorioService, EscritorioService>();
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<IConviteService, ConviteService>();
        services.AddScoped<IUsuarioPortalService, UsuarioPortalService>();
        services.AddScoped<IAcessoEmpresaService, AcessoEmpresaService>();
        services.AddScoped<IEmpresaService, EmpresaService>();
        services.AddScoped<IHistoricoEmpresaService, HistoricoEmpresaService>();
        services.AddScoped<IPastaService, PastaService>();
        services.AddScoped<IDocumentoService, DocumentoService>();
        services.AddScoped<ISolicitacaoService, SolicitacaoService>();
        services.AddScoped<INotificador, Notificador>();
        services.AddScoped<INotificacaoService, NotificacaoService>();
        services.AddScoped<ILembreteVencimentoService, LembreteVencimentoService>();
        services.AddScoped<IAvisoNoticiaService, AvisoNoticiaService>();
        services.AddScoped<IAvisoCertidaoService, AvisoCertidaoService>();
        services.AddScoped<INoticiaService, NoticiaService>();
        services.AddScoped<ICertidaoService, CertidaoService>();
        return services;
    }
}
