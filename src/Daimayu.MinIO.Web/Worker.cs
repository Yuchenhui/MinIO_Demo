using Daimayu.MinIO.Web.Service;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Daimayu.MinIO.Web
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IDataService _dataService;
        private readonly int _time;

        public Worker(ILogger<Worker> logger, IDataService dataService,
            IConfiguration configuration)
        {
            _logger = logger;
            _dataService = dataService;
            _time = configuration.GetValue<int>("Tika:StatusCheckInterval");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                try
                {
                    await Task.Delay(_time, stoppingToken);
                    _dataService.CheckExtract();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "ExecuteAsync error");
                }
            }
        }
    }
}
