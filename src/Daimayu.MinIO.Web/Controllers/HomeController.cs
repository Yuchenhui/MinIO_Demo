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
        private readonly Dictionary<string, string> _mediaType;
        private readonly IDataService _dataService;
        private readonly int _tikaMaxSize;
        private readonly int _minioDownloadExp;
        private readonly int _minioUploadExp;
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
            _mediaType = configuration.GetSection("MediaType").Get<Dictionary<string,string>>();
            _tikaMaxSize = configuration.GetValue<int>("Tika:MaxSize");
            _minioDownloadExp = configuration.GetValue<int>("MinIO:DownloadExp");
            _minioUploadExp = configuration.GetValue<int>("MinIO:UploadExp");
            _dataService = dataService;
        }

        public IActionResult Index(string keyword)
        {
            var data = new List<StoredItem>();

            if (string.IsNullOrEmpty(keyword))
            {
                data = _dataService.List();
            }
            else
            {
                var fileIds = _dataService.SearchIndex(keyword);
                data = _dataService.List(fileIds);
            }
            if(data!=null && data.Any())
            {
                data.ForEach(async d =>
                {
                    var url = await _client.PresignedGetObjectAsync(_bucket, d.FileId + d.FileType, _minioDownloadExp);
                    d.DownloadUrl = url;
                });
            }
            ViewBag.Keyword = keyword;
            return View(data);
        }

        //[HttpPost]
        //[DisableRequestSizeLimit]
        //public async Task<IActionResult> UploadAsync([FromForm(Name = "file")] IFormFile file, string lang)
        //{
        //    if (file != null)
        //    {
        //        bool found = await _client.BucketExistsAsync(_bucket);
        //        if (!found)
        //        {
        //            await _client.MakeBucketAsync(_bucket);
        //            var jsonStr = "{\"Statement\":[{\"Action\":[\"s3:GetBucketLocation\",\"s3:ListBucket\"],\"Effect\":\"Allow\",\"Principal\":{\"AWS\":[\"*\"]},\"Resource\":[\"arn:aws:s3:::" + _bucket + "\"]},{\"Action\":[\"s3:GetObject\"],\"Effect\":\"Allow\",\"Principal\":{\"AWS\":[\"*\"]},\"Resource\":[\"arn:aws:s3:::" + _bucket + "/*\"]}],\"Version\":\"2012-10-17\"}";
        //            await _client.SetPolicyAsync(_bucket, jsonStr);
        //        }
        //        var type = Path.GetExtension(file.FileName);
        //        var indexed = _indexedType.Contains(type);
        //        var previewed = _previewType.Contains(type);
        //        var fileId = Guid.NewGuid().ToString("N");
        //        var previewType = "";
        //        if (previewed && _mediaType.ContainsKey(type))
        //        {
        //            previewType = _mediaType[type];
        //        }
        //        FileStatus status = FileStatus.Uploading;
        //        var item = new StoredItem()
        //        {
        //            Bucket = _bucket,
        //            Region = _region,
        //            CreateTime = DateTime.UtcNow,
        //            FileContentType = MimeTypeMap.GetMimeType(type),
        //            Chunk = 1,
        //            FileId = fileId,
        //            FileDesc = file.FileName.Replace(".", " "),
        //            FileLength = file.Length,
        //            FileName = file.FileName,
        //            FileStatus = status,
        //            IsDeleted = false,
        //            IsIndexed = indexed,
        //            Number = DateTime.UtcNow.Ticks,
        //            IsPreview = previewed,
        //            Lang = lang,
        //            FileType = type,
        //            PreviewType = previewType
        //        };

        //        var saved = _dataService.Create(item);
        //        if (saved)
        //        {
        //            await _client.PutObjectAsync(_bucket, fileId + item.FileType, file.OpenReadStream(), file.Length);
        //            var size = file.Length / 1024d;
        //            if (size > _tikaMaxSize)
        //            {
        //                indexed = false;
        //            }
        //            if (indexed)
        //            {
        //                _dataService.UpdateStatus(item.FileId, FileStatus.PendingExtract);
        //                _ = CallTika(item);
        //            }
        //            else
        //            {
        //                _dataService.UpdateStatus(item.FileId, FileStatus.Uploaded);
        //                _dataService.BuildIndex(item);
        //            }
        //        }else
        //        {
        //            _logger.LogError("Upload file failed");
        //        }
        //    }
        //    return RedirectToAction("Index", new { _t = DateTime.UtcNow.Ticks });
        //}

        private async Task CallTika(StoredItem item)
        {
            _dataService.UpdateStatus(item.FileId, FileStatus.Extracting);
            item.Content = await _dataService.CallTika(item.FileId);
            var length = item.Content.Length;
            string content;
            if (length > 0)
            {
                if (length > 10000)
                {
                    content = item.Content.Substring(0, 10000);
                }
                else
                {
                    content = item.Content;
                }
            }
            else
            {
                content = "{empty_content}";
            }
            
            _dataService.UpdateStatus(item.FileId, FileStatus.Indexed, content);
            _dataService.BuildIndex(item);
        }
        public IActionResult Extracting(string fileId)
        {
            _dataService.UpdateStatus(fileId, FileStatus.Extracting);
            _ = _dataService.CallTikaAsync(fileId);
            return RedirectToAction("Index", new { _t = DateTime.UtcNow.Ticks });
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> UploadAsync(string file, long size, string lang)
        {
            bool found = await _client.BucketExistsAsync(_bucket);
            if (!found)
            {
                await _client.MakeBucketAsync(_bucket);
                var jsonStr = "{\"Statement\":[{\"Action\":[\"s3:GetBucketLocation\",\"s3:ListBucket\"],\"Effect\":\"Allow\",\"Principal\":{\"AWS\":[\"*\"]},\"Resource\":[\"arn:aws:s3:::" + _bucket + "\"]},{\"Action\":[\"s3:GetObject\"],\"Effect\":\"Allow\",\"Principal\":{\"AWS\":[\"*\"]},\"Resource\":[\"arn:aws:s3:::" + _bucket + "/*\"]}],\"Version\":\"2012-10-17\"}";
                await _client.SetPolicyAsync(_bucket, jsonStr);
            }
            var type = Path.GetExtension(file);
            var indexed = _indexedType.Contains(type);
            var previewed = _previewType.Contains(type);
            var fileId = Guid.NewGuid().ToString("N");
            var previewType = "";
            if (previewed && _mediaType.ContainsKey(type))
            {
                previewType = _mediaType[type];
            }
            FileStatus status = FileStatus.Uploading;
            var item = new StoredItem()
            {
                Bucket = _bucket,
                Region = _region,
                CreateTime = DateTime.UtcNow,
                FileContentType = MimeTypeMap.GetMimeType(type),
                Chunk = 1,
                FileId = fileId,
                FileDesc = file.Replace(".", " "),
                FileLength = file.Length,
                FileName = file,
                FileStatus = status,
                IsDeleted = false,
                IsIndexed = indexed,
                Number = DateTime.UtcNow.Ticks,
                IsPreview = previewed,
                Lang = lang,
                FileType = type,
                PreviewType = previewType
            };

            var saved = _dataService.Create(item);
            if (saved)
            {
                var url = await _client.PresignedPutObjectAsync(_bucket, fileId + type, _minioUploadExp);
                return Json(new { method = "put", url = url, fileId = fileId });
            }
            else
            {
                return Json(new { method = "put", url = "" });
            }

        }

        [HttpPost]
        public IActionResult UploadCompleted(string fileId)
        {
            var item = _dataService.Get(fileId);
            var size = item.FileLength / 1024d;
            var indexed = item.IsIndexed;
            if (size > _tikaMaxSize)
            {
                indexed = false;
            }
            if (indexed)
            {
                _dataService.UpdateStatus(item.FileId, FileStatus.PendingExtract);
                _ = CallTika(item);
            }
            else
            {
                _dataService.UpdateStatus(item.FileId, FileStatus.Uploaded);
                _dataService.BuildIndex(item);
            }
            return Json("OK");
        }

        public async Task<IActionResult> DeleteAsync(string fileId)
        {
            var item = _dataService.Get(fileId);
            _dataService.Delete(fileId);
            _dataService.DeleteIndex(fileId);
            await _client.RemoveObjectAsync(_bucket, $"{item.FileId}{item.FileType}");
            return RedirectToAction("Index", new { _t = DateTime.UtcNow.Ticks });
        }

        public IActionResult Preview(string type,string fileId,string keyword)
        {
            var item = _dataService.Get(fileId);
            if (item == null)
            {
                return RedirectToAction("Index", new { _t = DateTime.UtcNow.Ticks });
            }
            item.DownloadUrl = $"http://{_endpoint}/{_bucket}/{fileId}{item.FileType}";
            ViewBag.PreviewType = type;
            ViewBag.Keyword = keyword;
            return View(item);
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
