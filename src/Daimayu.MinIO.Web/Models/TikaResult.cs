using MongoDB.Bson;
using System;

namespace Daimayu.MinIO.Web.Models
{
    public class TikaResult
    {
        public ObjectId Id { get; set; }
        public string fileId { get; set; }
        public DateTime created { get; set; }
        public string mine { get; set; }
        public string length { get; set; }
        public long len
        {
            get
            {
                _ = long.TryParse(this.length, out var result);
                return result;
            }
        }
        public string embedded_depth { get; set; }
        public string resource_name { get; set; }
        public string content { get; set; }
    }
}
