using Daimayu.MinIO.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Daimayu.MinIO.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly MinioClient _client;
        private readonly string _bucket;

        public HomeController(ILogger<HomeController> logger, MinioClient client, IConfiguration configuration)
        {
            _logger = logger;
            _client = client;
            _bucket = configuration["MinIO:Bucket"];

        }

        public async Task<IActionResult> IndexAsync()
        {
            IObservable<Item> observable = _client.ListObjectsAsync(_bucket);
            List<Item> names = new List<Item>();
            List<DownloadItem> results = new List<DownloadItem>();
            bool found = await _client.BucketExistsAsync(_bucket);
            if (!found)
            {
                await _client.MakeBucketAsync(_bucket);
            }

            IDisposable subscription = observable.ToList().Subscribe(
              x => names.AddRange(x),
              ex => Console.WriteLine("OnError: {0}", ex),
              () => Console.WriteLine("Done" + "\n"));
            observable.Wait();
            Console.WriteLine("out of subscribe count:" + names.Count + "\n");
            subscription.Dispose();
            names.ForEach(async c =>
            {
                var url = await _client.PresignedGetObjectAsync(_bucket, c.Key, 3600 * 24 * 7);
                DownloadItem newx = new DownloadItem()
                {
                    DownloadUrl = url,
                    ETag = c.ETag,
                    IsDir = c.IsDir,
                    Key = c.Key,
                    LastModified = c.LastModified,
                    Size = c.Size,
                    ModifiedDateTime = c.LastModifiedDateTime
                };
                results.Add(newx);
            });
            results = results.OrderByDescending(c => c.ModifiedDateTime).ToList();
            return View(results);
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> UploadAsync([FromForm(Name = "file")] IFormFile file)
        {
            var type = Path.GetExtension(file.FileName);
            await _client.PutObjectAsync(_bucket, file.FileName, file.OpenReadStream(), file.Length);
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> DeleteAsync(string objectName)
        {
            await _client.RemoveObjectAsync(_bucket, objectName);
            return RedirectToAction("Index");
        }


        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
