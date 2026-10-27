using DirStudio.Config;
using DirStudio.Core.IManager;
using DirStudio.Core.Manager;
using DirStudio.Data.Files.Models;
using DirStudio.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serbenco.Helpers;

namespace DomicilioAutomationWorker;

internal static class BackendServices
{
    public static ServiceProvider Create(string mainConnectionString, string filesConnectionString,
        DbTransactionInterceptor? mainCommitGuard = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMemoryCache();
        services.AddOptions<DirStudioConfig>();
        services.AddDbContext<ADISU_SVILUPPOContext>(options =>
        {
            options.UseSqlServer(mainConnectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            if (mainCommitGuard != null) options.AddInterceptors(mainCommitGuard);
        });
        services.AddDbContext<ADISU_SVILUPPO_FILESContext>(options => options.UseSqlServer(filesConnectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddSingleton<IMemoryCacheManager, MemoryCacheManager>();
        services.AddScoped<IDomicilioIstanzaApplier, DomicilioIstanzaApplier>();
        services.AddScoped<DomicilioLavorazioneService>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
