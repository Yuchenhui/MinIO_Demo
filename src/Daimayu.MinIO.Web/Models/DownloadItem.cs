using Minio.DataModel;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Daimayu.MinIO.Web.Models
{
    public partial class DownloadItem: Item
    {
        public string DownloadUrl { get; set; }
        public DateTime? ModifiedDateTime { get; set; }
    }

    public class StoredItem
    {
        public ObjectId Id { get; set; }
        public long Number { get; set; }
        public string FileId { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime DeleteTime { get; set; }
        public int Chunk { get; set; }
        public string FileName { get; set; }
        public string FileDesc { get; set; }
        public string FileType { get; set; }
        public string FileContentType { get; set; }
        public FileStatus FileStatus { get; set; }
        public string Lang { get; set; }
        public long FileLength { get; set; }
        public string Bucket { get; set; }
        public string Region { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsIndexed { get; set; }
        public bool IsPreview { get; set; }
        public string Content { get; set; }
        public string DownloadUrl { get; set; }
    }

    public enum FileStatus
    {
        Init=0,
        Uploading=1,
        Uploaded=2,
        PendingExtract=3,
        Extracting=4,
        PendingIndex=5,
        Indexed=6
    }

    public class MongoSettings
    {
        //
        // 摘要:
        //     The connection string for the MongoDb server.
        public string Conn
        {
            get;
            set;
        }

        //
        // 摘要:
        //     The name of the MongoDb database where the identity data will be stored.
        public string Database
        {
            get;
            set;
        }
        public bool UseSsl
        {
            get;
            set;
        }
    }
}
