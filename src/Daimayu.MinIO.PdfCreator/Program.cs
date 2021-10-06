using Aspose.Pdf;
using Aspose.Pdf.Facades;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Daimayu.MinIO.PdfCreator
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var licenseWords = new License();
            licenseWords.SetLicense("Aspose.Total.lic");
            await TikaAsync();
            //Merge();
            //Document doc = new();
            //var files = Directory.GetFiles("docs");
            //for (var n = 0; n < files.Length; n++)
            //{
            //    var path = files[n];
            //    DocumentBuilder db = new();
            //    var fileName = Path.GetFileName(path);
            //    db.InsertHtml($"<h1>{fileName}</h1>");
            //    if (fileName.EndsWith(".txt"))
            //    {

            //        var content = File.ReadAllText(path);
            //        db.InsertHtml($"<p>{content}</p>");
            //    }
            //    else
            //    {
            //        db.InsertImage(files[n]);
            //    }

            //    doc.AppendDocument(db.Document, ImportFormatMode.UseDestinationStyles);
            //}
            //doc.Save($"{Guid.NewGuid():N}.pdf", SaveFormat.Pdf);
        }
        public static void Merge()
        {
            PdfFileEditor pdfEditor = new PdfFileEditor();
            var list = new List<string>();
            for(var n = 0; n < 7; n++)
            {
                list.Add("docs/136.pdf");
            }
            // merge files
            pdfEditor.Concatenate(list.ToArray(), "merged.pdf");
        }

        public static Task TikaAsync()
        {
            using (HttpClient client = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Put, "http://localhost:9003/tika");
                request.Headers.Add("Accept", "text/plain");
                request.Headers.Add("X-Tika-OCRLanguage", "chi_sim");
                request.Headers.Add("fetcherName", "http");
                request.Headers.Add("fetchKey", "http://minio:9000/test/223243.png");
                var resp = client.SendAsync(request).Result;
                var result = resp.Content.ReadAsStringAsync().Result;
                Console.WriteLine(result);

                //client.DefaultRequestHeaders.Add("Accept", "text/plain");
                //client.DefaultRequestHeaders.Add("X-Tika-OCRLanguage", "chi_sim");
                //client.DefaultRequestHeaders.Add("fetcherName", "http");
                //client.DefaultRequestHeaders.Add("fetchKey", "http://minio:9000/test/db.pdf");
                //var requestContent = new StringContent("", Encoding.UTF8, "application/json");
                //var result = await client.PutAsync("http://localhost:9003/tika", requestContent);

                //var result = await client.GetAsync("http://192.168.182.1");
            }

            return Task.CompletedTask;
        }
    }


}
