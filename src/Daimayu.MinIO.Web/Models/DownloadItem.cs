using Minio.DataModel;
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
}
