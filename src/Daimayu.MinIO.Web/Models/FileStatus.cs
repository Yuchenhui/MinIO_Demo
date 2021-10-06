namespace Daimayu.MinIO.Web.Models
{
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
}
