using Daimayu.MinIO.Web.Models;
using Daimayu.MinIO.Web.Service;
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
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Daimayu.MinIO.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly MinioClient _client;
        private readonly string _bucket;
        private readonly string _region;
        private readonly string _endpoint;
        private readonly List<string> _indexedType;
        private readonly List<string> _previewType;
        private readonly IDataService _dataService;
        public HomeController(IDataService dataService,ILogger<HomeController> logger, MinioClient client,
            IConfiguration configuration)
        {
            _logger = logger;
            _client = client;
            _endpoint = configuration["MinIO:Endpoint"];
            _bucket = configuration["MinIO:Bucket"];
            _region = configuration["MinIO:Region"];
            _indexedType = configuration.GetSection("IndexedType").Get<string[]>().ToList();
            _previewType = configuration.GetSection("PreviewType").Get<string[]>().ToList();
            _dataService = dataService;
        }

        public async Task<IActionResult> IndexAsync(string keyword)
        {
            bool found = await _client.BucketExistsAsync(_bucket);
            if (!found)
            {
                await _client.MakeBucketAsync(_bucket);
                var jsonStr = "{\"Statement\":[{\"Action\":[\"s3:GetBucketLocation\",\"s3:ListBucket\"],\"Effect\":\"Allow\",\"Principal\":{\"AWS\":[\"*\"]},\"Resource\":[\"arn:aws:s3:::" + _bucket + "\"]},{\"Action\":[\"s3:GetObject\"],\"Effect\":\"Allow\",\"Principal\":{\"AWS\":[\"*\"]},\"Resource\":[\"arn:aws:s3:::" + _bucket + "/*\"]}],\"Version\":\"2012-10-17\"}";
                await _client.SetPolicyAsync(_bucket, jsonStr);
            }

            var fileIds = _dataService.SearchIndex(keyword);
            var data = new List<StoredItem>();
            if (string.IsNullOrEmpty(keyword) || fileIds.Any())
            {
                data = _dataService.List(fileIds);
                data.ForEach(async d => {
                    var url = await _client.PresignedGetObjectAsync(_bucket, d.FileId + d.FileType, 3600 * 24 * 7);
                    d.DownloadUrl = url;
                });
            }
            ViewBag.Keyword = keyword;
            return View(data);
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> UploadAsync([FromForm(Name = "file")] IFormFile file,string desc,string lang)
        {
            if (file != null)
            {
                var type = Path.GetExtension(file.FileName);
                var indexed = _indexedType.Contains(type);
                var previewed = _previewType.Contains(type);
                var fileId = Guid.NewGuid().ToString("N");
                FileStatus status = FileStatus.Uploading;
                var item = new StoredItem()
                {
                    Bucket = _bucket,
                    Region = _region,
                    CreateTime = DateTime.UtcNow,
                    FileContentType = MimeTypeMap.GetMimeType(type),
                    Chunk = 1,
                    FileId = fileId,
                    FileDesc = desc,
                    FileLength = file.Length,
                    FileName = file.FileName,
                    FileStatus = status,
                    IsDeleted = false,
                    IsIndexed = indexed,
                    Number = DateTime.UtcNow.Ticks,
                    IsPreview = previewed,
                    Lang = lang,
                    FileType = type
                };

                var saved = _dataService.Create(item);
                if (saved)
                {
                    await _client.PutObjectAsync(_bucket, fileId+item.FileType, file.OpenReadStream(), file.Length);
                    if (indexed)
                    {
                        _dataService.UpdateStatus(fileId, FileStatus.PendingExtract);
                        var content = await _dataService.CallTikaAsync(fileId);
                        _dataService.UpdateStatus(fileId, FileStatus.Indexed);
                        item.Content = content;                       
                    }
                    else
                    {
                        _dataService.UpdateStatus(fileId, FileStatus.Uploaded);
                    }
                    _dataService.BuildIndex(item);
                }
            }
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> DeleteAsync(string objectName)
        {
            await _client.RemoveObjectAsync(_bucket, objectName);
            return RedirectToAction("Index");
        }

        public IActionResult Preview(string type,string fileId)
        {
            var item = _dataService.Get(fileId);
            item.DownloadUrl = $"http://{_endpoint}/{_bucket}/{fileId}{item.FileType}";
            ViewBag.PreviewType = type;
            return View();
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
