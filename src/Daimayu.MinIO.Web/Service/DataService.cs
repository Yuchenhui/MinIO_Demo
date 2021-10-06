using Daimayu.MinIO.Web.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Daimayu.MinIO.Web.Service
{
    public interface IDataService
    {
        List<StoredItem> List(List<string> fileIds = null);
        bool Create(StoredItem item);
        bool Delete(string fileId);
        bool UpdateStatus(string fileId, FileStatus status, string content = null);
        Task<string> CallTikaAsync(string fileId);
        bool BuildIndex(StoredItem item);
        bool DeleteIndex(string fileId);
        List<string> SearchIndex(string query);
        StoredItem Get(string fileId);
    }
    public class DataService : IDataService
    {
        private readonly ILogger<DataService> _logger;
        private readonly IMongoDatabase _mongoDatabase;
        private readonly IMongoCollection<StoredItem> _itemCollection;
        private readonly IHttpClientFactory _clientFactory;
        private readonly ElasticClient _esClient;
        private readonly string _minioHost;
        private readonly string _tikaHost;
        private readonly string _indexName;
        public DataService(ILogger<DataService> logger, MongoSettings mongoSettings,
            IHttpClientFactory clientFactory, IConfiguration configuration)
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
            _logger = logger;
            _minioHost = configuration["MinIO:Endpoint"];
            _tikaHost = configuration["Tika:Host"];
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

        public async Task<string> CallTikaAsync(string fileId)
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
                _logger.LogError(e, "CallTikaAsync error");
                return "";
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
                var result = _esClient.Delete<StoredItem>(fileId);
                return result.IsValid;
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
