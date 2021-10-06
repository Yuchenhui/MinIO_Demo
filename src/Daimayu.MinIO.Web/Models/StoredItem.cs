using MongoDB.Bson;
using System;

namespace Daimayu.MinIO.Web.Models
{
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
        public string PreviewType { get; set; }
        public string Content { get; set; }
        public string DownloadUrl { get; set; }
    }
}
