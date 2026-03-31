using Coravel;
using Coravel.Scheduling.Schedule.Interfaces;

namespace CosmoApiServer.Core.Hosting;

public static class CosmoSchedulerExtensions
{
    public static CosmoWebApplicationBuilder AddScheduler(this CosmoWebApplicationBuilder builder)
    {
        builder.Services.AddScheduler();
        return builder;
    }

    public static CosmoWebApplication UseScheduler(this CosmoWebApplication app, Action<IScheduler> assignScheduledTasks)
    {
        app.Services.UseScheduler(assignScheduledTasks);
        return app;
    }
}
