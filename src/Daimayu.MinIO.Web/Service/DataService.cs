using Daimayu.MinIO.Web.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Minio;
using MongoDB.Driver;
using Nest;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Daimayu.MinIO.Web.Service
{
    public interface IDataService
    {
        List<StoredItem> List(List<string> fileIds = null);
        bool Create(StoredItem item);
        bool Delete(string fileId);
        bool UpdateStatus(string fileId, FileStatus status, string content = null);
        Task<string> CallTika(string fileId);
        Task<bool> CallTikaAsync(string fileId);
        bool BuildIndex(StoredItem item);
        bool DeleteIndex(string fileId);
        List<string> SearchIndex(string query);
        StoredItem Get(string fileId);

        bool CheckExtract();
    }
    public class DataService : IDataService
    {
        private readonly ILogger<DataService> _logger;
        private readonly IMongoDatabase _mongoDatabase;
        private readonly IMongoCollection<StoredItem> _itemCollection;
        private readonly IMongoCollection<TikaResult> _indexCollection;
        private readonly IHttpClientFactory _clientFactory;
        private readonly ElasticClient _esClient;
        private readonly string _minioHost;
        private readonly string _tikaHost;
        private readonly string _indexName;
        private readonly string _tikaFilePath;
        private readonly MinioClient _minioClient;
        public DataService(ILogger<DataService> logger, MongoSettings mongoSettings,
            IHttpClientFactory clientFactory, IConfiguration configuration, MinioClient minioClient)
        {
            _clientFactory = clientFactory;
            MongoClient client;
            if (mongoSettings.UseSsl)
            {
                var clientWriteSettings = MongoClientSettings.FromUrl(new MongoUrl(mongoSettings.Conn));
                clientWriteSettings.UseTls = true;
                clientWriteSettings.AllowInsecureTls = true;
                clientWriteSettings.SslSettings = new SslSettings
                {
                    CheckCertificateRevocation = false
                };
                client = new MongoClient(clientWriteSettings);
            }
            else
            {
                client = new MongoClient(mongoSettings.Conn);
            }
            _mongoDatabase = client.GetDatabase(mongoSettings.Database);
            _itemCollection = _mongoDatabase.GetCollection<StoredItem>(Const.MongoCollectNameStoredItem);
            _indexCollection = _mongoDatabase.GetCollection<TikaResult>(Const.MongoCollectNameIndexes);
            _logger = logger;
            _minioHost = configuration["MinIO:Endpoint"];
            _minioClient = minioClient;
            _tikaHost = configuration["Tika:Host"];
            _tikaFilePath = configuration["Tika:SharedFilePath"];
            _indexName = configuration["ElasticSearch:IndexName"];
            var node = new Uri(configuration["ElasticSearch:Host"]);
            var settingsEs = new ConnectionSettings(node);
            _esClient = new ElasticClient(settingsEs);

        }

        public bool BuildIndex(StoredItem item)
        {
            try
            {
                var indexExist = _esClient.Indices.Exists(_indexName);
                if (!indexExist.Exists)
                {
                    _esClient.Indices.Create(_indexName,
                        x =>
                        x.Settings(se => se.Setting("analysis.analyzer.my_analyzer.type", "custom")
                        .Setting("analysis.analyzer.my_analyzer.tokenizer", "standard")
                        .Setting("analysis.analyzer.my_analyzer.filter", "lowercase")));
                }

                var indexResp = _esClient.Index(item, x => x.Index(_indexName).Id(item.FileId));
                return indexResp.IsValid;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "BuildIndex error");
                return false;
            }

        }

        public async Task<string> CallTika(string fileId)
        {
            try
            {
                var item = _itemCollection.AsQueryable().FirstOrDefault(c => c.FileId == fileId && c.IsDeleted == false);
                using var client = _clientFactory.CreateClient();
                var request = new HttpRequestMessage(HttpMethod.Put, $"{_tikaHost}/tika");
                request.Headers.Add("Accept", "text/plain");
                request.Headers.Add("X-Tika-OCRLanguage", item.Lang);
                request.Headers.Add("fetcherName", "http");
                request.Headers.Add("fetchKey", $"http://{_minioHost}/{item.Bucket}/{item.FileId}{item.FileType}");
                client.Timeout = TimeSpan.FromHours(3);
                var resp = await client.SendAsync(request);
                var result = await resp.Content.ReadAsStringAsync();
                var html = "";
                if (item.Lang == "chi_sim" || item.Lang == "chi_tra")
                {
                    html = result.Replace("\n", "").Replace("\r", "").Replace(" ", "");
                }
                else
                {
                    html = result.Replace("\n", " ").Replace("\r", " ");
                }
                var array = html.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                result = string.Join(" ", array);
                return result;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "CallTika error");
                return "";
            }

        }

        public async Task<bool> CallTikaAsync(string fileId)
        {
            try
            {
                using var client = _clientFactory.CreateClient();
                var item = _itemCollection.AsQueryable().FirstOrDefault(c => c.FileId == fileId && c.IsDeleted == false);
                string url = await _minioClient.PresignedGetObjectAsync(item.Bucket, $"{item.FileId}{item.FileType}", 3600 * 24 * 7);
                using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                using (Stream streamToReadFrom = await response.Content.ReadAsStreamAsync())
                {
                    string fileToWriteTo = $"{_tikaFilePath}/docs/{item.FileId}{item.FileType}";
                    if (File.Exists(fileToWriteTo))
                    {
                        File.Delete(fileToWriteTo);
                    }
                    using (Stream streamToWriteTo = File.Open(fileToWriteTo, FileMode.Create))
                    {
                        await streamToReadFrom.CopyToAsync(streamToWriteTo);
                    }
                } 
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_tikaHost}/async");
                request.Headers.Add("X-Tika-OCRLanguage", item.Lang);                
                request.Content = new StringContent("[{\"fetcher\":\"fsf\",\"fetchKey\":\"" + item.FileId + item.FileType
                + "\",\"emitter\":\"fse\",\"emitKey\":\""+ item.FileId + item.FileType + "\"}]",System.Text.Encoding.UTF8, "application/json");
                var resp = await client.SendAsync(request);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "CallTikaAsync error");
                return false;
            }

        }

        public bool CheckExtract()
        {
            try
            {
                var extFiles = Directory.EnumerateFiles(Path.Combine(_tikaFilePath, "extracts"));
                if(extFiles!=null && extFiles.Any())
                {
                    var deserializeOptions = new JsonSerializerOptions();
                    
                    foreach (var n in extFiles)
                    {
                        var fileId = Path.GetFileName(n).Split(".")[0];
                        var item = Get(fileId);
                        if(item == null)
                        {
                            if (File.Exists(n))
                            {
                                File.Delete(n);
                            }
                            var docPath = n.Replace("/extracts/", "/docs/").Replace(".json", "");
                            if (File.Exists(docPath))
                            {
                                File.Delete(docPath);
                            }
                        }
                        else
                        {
                            var file = File.ReadAllText(n);
                            var jArray = JsonSerializer.Deserialize<List<TikaResult>>(file, deserializeOptions);
                            var content = "";
                            jArray.ForEach(async x => {
                                content += x.content;
                                await _indexCollection.InsertOneAsync(x);
                            });
                            if (item.Lang == "chi_sim" || item.Lang == "chi_tra")
                            {
                                content = content.Replace("\n", "").Replace("\r", "").Replace(" ", "");
                            }
                            else
                            {
                                content = content.Replace("\n", " ").Replace("\r", " ");
                            }
                            var array = content.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
                            content = string.Join(" ", array);
                            item.Content = content;
                            var result = BuildIndex(item);
                            UpdateStatus(fileId, FileStatus.Indexed);
                            if (result)
                            {
                                if (File.Exists(n))
                                {
                                    File.Delete(n);
                                }
                                var docPath = n.Replace("extracts", "docs").Replace(".json", "");
                                if (File.Exists(docPath))
                                {
                                    File.Delete(docPath);
                                }
                            }
                        }
                        
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError("CheckExtract error", e);
                return false;
            }
        }

        public bool Create(StoredItem item)
        {
            try
            {
                _itemCollection.InsertOneAsync(item);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Create error");
                return false;
            }
        }

        public bool Delete(string fileId)
        {
            try
            {
                var update = Builders<StoredItem>.Update.Set(c => c.DeleteTime, DateTime.UtcNow).Set(c => c.IsDeleted, true);
                _itemCollection.UpdateOneAsync(c => c.FileId == fileId, update);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Update error");
                return false;
            }
        }

        public bool DeleteIndex(string fileId)
        {
            try
            {
                var doc = _esClient.DocumentExists(new DocumentExistsRequest(_indexName, fileId));
                if (doc != null)
                {
                    var del = new DeleteRequest(_indexName, fileId);
                    var result = _esClient.Delete(del);
                    return result.IsValid;
                }
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "DeleteIndex error");
                return false;
            }
        }

        public StoredItem Get(string fileId)
        {
            try
            {
                return _itemCollection.AsQueryable().FirstOrDefault(c => c.IsDeleted == false && c.FileId == fileId);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Get error");
                return null;
            }
        }

        public List<StoredItem> List(List<string> fileIds = null)
        {
            try
            {
                var q = _itemCollection.AsQueryable().Where(c => c.IsDeleted == false);
                if (fileIds != null)
                {
                    if (fileIds.Any())
                    {
                        q = q.Where(c => fileIds.Contains(c.FileId));
                    }
                    else
                    {
                        return new List<StoredItem>();
                    }
                }
                return q.ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "List error");
                return new List<StoredItem>();
            }
        }

        public List<string> SearchIndex(string query)
        {
            try
            {
                ISearchResponse<StoredItem> results;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    results = _esClient.Search<StoredItem>(s => s.Index(_indexName)
                        .Query(q => q.QueryString(
                            qs => qs.Query(query)
                            .AllowLeadingWildcard(true)
                            )
                        )
                    );
                }
                else
                {
                    results = _esClient.Search<StoredItem>(s => s.Index(_indexName)
                        .Query(q => q
                            .MatchAll()
                        )
                    );
                }
                return results.Documents.Select(c => c.FileId).ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "List error");
                return new List<string>();
            }
        }

        public bool UpdateStatus(string fileId, FileStatus status, string content = null)
        {
            try
            {
                var update = Builders<StoredItem>.Update.Set(c => c.FileStatus, status);
                if (!string.IsNullOrEmpty(content))
                {
                    update.Set(c => c.Content, content);
                }
                _itemCollection.UpdateOneAsync(c => c.FileId == fileId, update);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "UpdateStatus error");
                return false;
            }

        }
    }
}
