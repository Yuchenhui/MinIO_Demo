namespace Daimayu.MinIO.Web.Models
{
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
