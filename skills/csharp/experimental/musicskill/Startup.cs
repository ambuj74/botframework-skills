﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.ApplicationInsights;
using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.BotFramework;
using Microsoft.Bot.Builder.Integration.ApplicationInsights.Core;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Solutions;
using Microsoft.Bot.Solutions.Responses;
using Microsoft.Bot.Solutions.TaskExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MusicSkill.Bots;
using MusicSkill.Dialogs;
using MusicSkill.Services;
using SkillServiceLibrary.Utilities;

namespace MusicSkill
{
    public class Startup
    {
        public Startup(IWebHostEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddJsonFile("cognitivemodels.json", optional: true)
                .AddJsonFile($"cognitivemodels.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Configure MVC
            services.AddControllers().AddNewtonsoftJson();

            // Configure server options
            services.Configure<KestrelServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });

            services.Configure<IISServerOptions>(options =>
            {
                options.AllowSynchronousIO = true;
            });

            // Configure channel provider
            services.AddSingleton<IChannelProvider, ConfigurationChannelProvider>();

            // Load settings
            var settings = new BotSettings();
            Configuration.Bind(settings);
            services.AddSingleton(settings);
            services.AddSingleton<BotSettingsBase>(settings);

            // Configure credentials
            services.AddSingleton<ICredentialProvider, ConfigurationCredentialProvider>();
            services.AddSingleton(new MicrosoftAppCredentials(settings.MicrosoftAppId, settings.MicrosoftAppPassword));

            // Configure telemetry
            services.AddApplicationInsightsTelemetry();
            services.AddSingleton<IBotTelemetryClient, BotTelemetryClient>();
            services.AddSingleton<ITelemetryInitializer, OperationCorrelationTelemetryInitializer>();
            services.AddSingleton<ITelemetryInitializer, TelemetryBotIdInitializer>();
            services.AddSingleton<TelemetryInitializerMiddleware>();
            services.AddSingleton<TelemetryLoggerMiddleware>();

            // Configure bot services
            services.AddSingleton<BotServices>();

            // Configure storage
            // Uncomment the following line for local development without Cosmos Db
            // services.AddSingleton<IStorage, MemoryStorage>();
            services.AddSingleton<IStorage>(new CosmosDbPartitionedStorage(settings.CosmosDb));
            services.AddSingleton<UserState>();
            services.AddSingleton<ConversationState>();
            services.AddSingleton(sp =>
            {
                var userState = sp.GetService<UserState>();
                var conversationState = sp.GetService<ConversationState>();
                return new BotStateSet(userState, conversationState);
            });

            // Configure proactive
            services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
            services.AddHostedService<QueuedHostedService>();

            // Configure localized responses
            var localizedTemplates = new Dictionary<string, string>();
            var templateFile = "ResponsesAndTexts";
            var supportedLocales = new List<string>() { "en-us", "zh-cn" };

            foreach (var locale in supportedLocales)
            {
                // LG template for en-us does not include locale in file extension.
                var localeTemplateFile = locale.Equals("en-us")
                    ? Path.Combine(".", "Responses", "ResponsesAndTexts", $"{templateFile}.lg")
                    : Path.Combine(".", "Responses", "ResponsesAndTexts", $"{templateFile}.{locale}.lg");

                localizedTemplates.Add(locale, localeTemplateFile);
            }

            services.AddSingleton(new LocaleTemplateManager(localizedTemplates, settings.DefaultLocale ?? "en-us"));

            // Configure service manager
            services.AddSingleton<IServiceManager>(new ServiceManager(settings));

            // Register AuthConfiguration to enable custom claim validation.
            services.AddSingleton(sp => new AuthenticationConfiguration { ClaimsValidator = new AllowedCallersClaimsValidator(sp.GetService<IConfiguration>()) });

            // Register dialogs
            services.AddTransient<PlayMusicDialog>();
            services.AddTransient<ControlSettingsDialog>();
            services.AddTransient<MainDialog>();

            // Configure adapters
            services.AddSingleton<IBotFrameworkHttpAdapter, DefaultAdapter>();

            // Configure bot
            services.AddTransient<IBot, DefaultActivityHandler<MainDialog>>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseDefaultFiles()
                .UseStaticFiles()
                .UseWebSockets()
                .UseRouting()
                .UseEndpoints(endpoints => endpoints.MapControllers());

            // Uncomment this to support HTTPS.
            // app.UseHttpsRedirection();
        }
    }
}