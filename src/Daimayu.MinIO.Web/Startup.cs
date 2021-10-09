using Daimayu.MinIO.Web.Models;
using Daimayu.MinIO.Web.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minio.AspNetCore;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Daimayu.MinIO.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            var ak = Configuration["MinIO:AccessKey"];
            var sk = Configuration["MinIO:SecretKey"];
            var en = Configuration["MinIO:Endpoint"];
            var reg = Configuration["MinIO:Region"];
            services.AddHttpClient();
            services.AddControllersWithViews().AddRazorRuntimeCompilation();

            var settings = Configuration.GetSection("Mongo").Get<MongoSettings>();
            services.AddSingleton(settings);

            services.AddMinio(options =>
            {
                options.Endpoint = "endpoint";
                // ...
                options.OnClientConfiguration = client =>
                {
                    //client
                };
            });

            // Url based configuration
            services.AddMinio(new Uri($"s3://{ak}:{sk}@{en}/{reg}"));

            services.Configure<FormOptions>(x =>
            {
                x.ValueLengthLimit = int.MaxValue;
                x.MultipartBodyLengthLimit = int.MaxValue;
                x.MultipartHeadersLengthLimit = int.MaxValue;
            });

            services.AddTransient<IDataService, DataService>();
            services.AddHostedService<Worker>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            //app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }
}
